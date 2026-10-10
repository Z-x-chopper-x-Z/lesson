using UnityEngine;

/// <summary>
/// Управление в стиле Getting Over It, но и игрок, и "молот" (Malleus) —
/// это шары одного и того же размера, а рукоятка отсутствует.
///
/// Идея:
///  - Malleus — кинематический шар, который следует за курсором мыши
///    (или точкой касания / стиком) на расстоянии не дальше радиуса захвата (ArmLength).
///  - Когда Malleus упирается в поверхность, он уже не может двигаться за курсором —
///    вместо этого он "упирается" и толкает игрока: между центрами шаров
///    действует пружина (Force), которая расталкивает их. Так происходит отталкивание
///    от стен, пола и любых коллайдеров (LayerMask "Ground").
///  - Игрок (PlayerBody) — динамическое Rigidbody2D-тело, которое катится и падает.
///
/// Расстояния задаются В МЕТРАСАХ сцены (не в масштабе трансформов), поэтому
/// Player и Malleus можно визуально масштабировать как угодно.
/// </summary>
public class MaleusController : MonoBehaviour
{
    [Header("Цели (пустые объекты Player / Malleus)")]
    [Tooltip("Пустой объект-родитель шара игрока (на нём должен быть PlayerBody)")]
    public Transform playerRoot;
    [Tooltip("Пустой объект-родитель шара Malleus")]
    public Transform malleusRoot;
    [Tooltip("Rigidbody2D игрока, который толкается при упоре молота в поверхность")]
    public Rigidbody2D playerRb;
    [Tooltip("Если включено и ссылки не назначены в инспекторе — найти пустые объекты 'Player' и 'Malleus' по иерархии автоматически")]
    public bool autoFindTargets = true;
    [Tooltip("Трансформ, вокруг которого размещается Malleus (по умолчанию — сам контроллер). Нужен, чтобы координаты курсора корректно считывались при смещенном родителе.")]
    public Transform referenceTransform;

    [Header("Геометрия (в метрах сцены)")]
    [Tooltip("Радиус обоих шаров (игрок и Malleus одинаковые)")]
    public float ballRadius = 0.5f;
    [Tooltip("Максимальная дальность 'руки' — на сколько далеко можно увести Malleus от игрока")]
    public float armLength = 2.6f;

    [Header("Следование за курсором")]
    [Tooltip("Насколько быстро Malleus догоняет курсор (больше = жёстче)")]
    public float followStrength = 45f;
    [Tooltip("Максимальная скорость самого Malleus, м/с")]
    public float maxMaleusSpeed = 30f;

    [Header("Отталкивание (пружина упора)")]
    [Tooltip("Жёсткость пружины между центрами шаров, когда Malleus уткнулся в поверхность")]
    public float pushSpringK = 60f;
    [Tooltip("Демпфирование пружины")]
    public float pushDamperC = 9f;
    [Tooltip("Масса, приписываемая Malleus для расчёта силы упора")]
    public float maleusMass = 1.2f;
    [Tooltip("Куда уходит сила, если уперлись в подвижный объект без Rigidbody2D")]
    public LayerMask groundLayer = ~0;
    [Tooltip("Толщина слоя 'пол', проверяемого под игроком (защита от липких стыков)")]
    public float skinWidth = 0.02f;

    [Header("Ввод")]
    [Tooltip("Скорость управления Malleus с клавиатуры/стока, м/с")]
    public float keyboardSpeed = 6f;
    [Tooltip("Мёртвая зона оси стика")]
    public float stickDeadzone = 0.1f;

    [Header("Служебное")]
    [Tooltip("Пустой объект-родитель спрайта связи (визуальная перемычка между шарами, НЕ рукоятка). Оставьте пустым, чтобы не рисовать ничего.")]
    public Transform connectorPivot;

    // Состояние
    Vector2 _vel;            // виртуальная скорость Malleus (используется как демпфер пружины)
    bool   _pushing;         // Malleus сейчас упирается в поверхность
    Vector2 _lastGoal;       // куда хотели двигать Malleus на этом шаге

    public bool IsPushing => _pushing;
    public Vector2 PushDirection { get; private set; } = Vector2.up;

    void Awake()
    {
        if (autoFindTargets)
        {
            if (playerRoot == null)  playerRoot  = FindDeep("Player");
            if (malleusRoot == null) malleusRoot = FindDeep("Malleus");
        }
        if (playerRb == null && playerRoot != null)
            playerRb = playerRoot.GetComponent<Rigidbody2D>();

        int def = LayerMask.NameToLayer("Default");
        int gr  = LayerMask.NameToLayer("Ground");
        int tr  = LayerMask.NameToLayer("Terrain");
        groundLayer = (1 << def)
                    | (gr  >= 0 ? 1 << gr  : 0)
                    | (tr  >= 0 ? 1 << tr  : 0);
    }

    static Transform FindDeep(string name)
    {
        var objs = Resources.FindObjectsOfTypeAll<Transform>();
        foreach (var t in objs)
            if (t.name == name && t.gameObject.scene.IsValid()) return t;
        return null;
    }

    void FixedUpdate()
    {
        if (playerRoot == null || malleusRoot == null || playerRb == null) return;

        Vector2 center = playerRoot.position;          // центр шара игрока
        Vector2 pos    = malleusRoot.position;         // текущий центр шара Malleus

        // --- 1. Цель движения (курсор / тач / стик / клавиатура) ---
        Vector2 goal = GetInputGoal(center, pos);

        // Ограничение "рукой": Malleus не может улететь дальше armLength от игрока
        Vector2 toGoal = goal - pos;
        float over = (goal - center).magnitude - armLength;
        if (over > 0f)
            goal = goal - (goal - center).normalized * Mathf.Min(over, toGoal.magnitude);

        // --- 2. Пружина следования за целью ---
        Vector2 desiredVel = toGoal * followStrength;
        desiredVel = Vector2.ClampMagnitude(desiredVel, maxMaleusSpeed);

        Vector2 stepVel = desiredVel;
        Vector2 newPos  = pos + desiredVel * Time.fixedDeltaTime;

        // --- 3. Коллизии Malleus со средой (CC2D кинематического шара) ---
        _pushing = false;
        for (int i = 0; i < 4 && (newPos - pos).sqrMagnitude > 1e-8f; i++)
        {
            ContactPoint2D[] contacts = new ContactPoint2D[8];
            int n = Physics2D.CircleCastNonAlloc(pos, ballRadius, newPos - pos,
                                                 contacts, Vector2.Distance(pos, newPos), groundLayer);
            if (n == 0) break;

            float minTOF = 1.1f;
            Vector2 normal = Vector2.zero;
            for (int c = 0; c < n; c++)
            {
                var cpl = contacts[c].pointLight;
                float depth = cpl.depth0;
                float dirDot = Vector2.Dot(cpl.normalOther, newPos - pos);
                float tof = (dirDot >= 0f) ? 0f : (-depth / dirDot); // момент, когда шар коснулся
                if (tof < minTOF) { minTOF = tof; normal = cpl.normalOther; }
            }

            if (minTOF <= 0f)
            {
                // Уже вплотную вжаты в поверхность — скользим вдоль неё
                Vector2 move = newPos - pos;
                move -= normal * Vector2.Dot(move, normal);
                newPos = pos + move;
                stepVel -= normal * Vector2.Dot(stepVel, normal); // гасим скорость в стену
                PushOffDynamic(contacts, n, normal);
                _pushing = true;
                PushPlayer(pos, normal, stepVel);
            }
            else
            {
                newPos = pos + (newPos - pos) * Mathf.Clamp01(minTOF);
                stepVel *= minTOF;
                PushOffDynamic(contacts, n, normal);
                _pushing = true;
                PushPlayer(newPos, normal, stepVel);
            }
        }

        // Если курсор тянет Malleus ВНИЗ сквозь пол, а упора нет — мягкая страховка
        if (!_pushing)
        {
            RaycastHit2D below = Physics2D.CircleCast(pos, ballRadius, Vector2.down, skinWidth, groundLayer);
            if (below) { newPos.y = Mathf.Max(newPos.y, below.point.y + ballRadius); }
        }

        // --- 4. Применяем позицию и силу упора к игроку ---
        malleusRoot.position = newPos;
        _vel = stepVel / Time.fixedDeltaTime;
        _lastGoal = goal;

        UpdateConnector();
    }

    /// <summary>Сила, расталкивающая центры двух шаров при упоре Malleus в поверхность.</summary>
    void PushPlayer(Vector2 maleusPos, Vector2 surfaceNormal, Vector2 steppedVel)
    {
        Vector2 center = playerRoot.position;
        Vector2 d = center - maleusPos;              // вектор Malleus -> Игрок
        float dist = d.magnitude;
        Vector2 dir = dist > 1e-5f ? d / dist : surfaceNormal;
        float overlap = (ballRadius * 2f) - dist;    // насколько центры "ближе" суммы радиусов
        if (overlap <= 0f) return;                   // шары ещё не соприкоснулись

        // Относительная скорость игрока вдоль линии центров (для демпфера пружины)
        Vector2 playerVel = playerRb.linearVelocity;
        float closing = -Vector2.Dot(playerVel, dir); // >0 когда игрок движется к Malleus

        float forceMag = pushSpringK * overlap + pushDamperC * Mathf.Max(0f, closing);

        playerRb.AddForce(dir * forceMag, ForceMode2D.Force);

        PushDirection = dir;
        _pushing = true;
    }

    /// <summary>Если Malleus толкает подвижный объект (ящик и т.п.) — отдаём ему реакцию.</summary>
    void PushOffDynamic(ContactPoint2D[] contacts, int n, Vector2 normal)
    {
        foreach (var c in contacts)
        {
            var rb = c.collider.attachedRigidbody;
            if (rb != null && !rb.isKinematic)
                rb.AddForceAtPosition(-normal * pushSpringK * 0.5f, c.point);
        }
    }

    Vector2 GetInputGoal(Vector2 center, Vector2 pos)
    {
        Transform refT = referenceTransform != null ? referenceTransform : transform;

        // Мышь (включая эмуляцию мыши через тач в редакторе)
        if (Input.GetMouseButton(0) || Input.mousePresent)
        {
            Vector3 sp = Input.mousePosition;
            sp.z = -refT.position.z;
            return Camera.main != null ? (Vector2)Camera.main.ScreenToWorldPoint(sp) : sp;
        }

        // Тач: относительное ведение пальца вокруг контроллера
        if (Input.touchCount > 0)
        {
            Touch t = Input.GetTouch(0);
            Vector3 sp = t.position; sp.z = -refT.position.z;
            Vector2 world = Camera.main != null ? (Vector2)Camera.main.ScreenToWorldPoint(sp) : sp;
            if (t.phase == TouchPhase.Began) _touchRef = world;
            return pos + (world - _touchRef);
        }

        // Клавиатура / геймпад
        Vector2 axis = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (axis.sqrMagnitude > stickDeadzone * stickDeadzone)
            return pos + axis.normalized * keyboardSpeed * Time.fixedDeltaTime;

        return pos;
    }
    Vector2 _touchRef;

    void UpdateConnector()
    {
        if (connectorPivot == null) return;
        Vector2 a = playerRoot.position;
        Vector2 b = malleusRoot.position;
        connectorPivot.position = a;
        Vector2 d = b - a;
        if (d.sqrMagnitude > 1e-6f)
            connectorPivot.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
    }

    void OnDrawGizmosSelected()
    {
        if (playerRoot == null) return;
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(playerRoot.position, ballRadius);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(playerRoot.position, armLength);
        if (malleusRoot != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(malleusRoot.position, ballRadius);
        }
    }
}
