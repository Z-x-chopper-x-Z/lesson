using UnityEngine;

/// <summary>
/// Камера в стиле Getting Over It: плавно следует за midpoint между
/// игроком и Malleus, при этом всегда оставляя Malleus в кадре.
/// </summary>
public class GOICamera : MonoBehaviour
{
    public Transform playerRoot;
    public Transform malleusRoot;
    [Tooltip("Базовый размер ортографической камеры (половина высоты экрана в мировых единицах)")]
    public float baseSize = 5f;
    [Tooltip("Дополнительный запас, чтобы оба шара помещались в кадр")]
    public float padding = 1.2f;
    public float followLerp = 4f;
    public float sizeLerp = 3f;

    new Camera camera;

    void Awake()
    {
        camera = GetComponent<Camera>();
        if (camera == null) camera = Camera.main;
        if (playerRoot == null)  playerRoot  = FindDeep("Player");
        if (malleusRoot == null) malleusRoot = FindDeep("Malleus");
    }

    static Transform FindDeep(string name)
    {
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            if (t.name == name && t.gameObject.scene.IsValid()) return t;
        return null;
    }

    void LateUpdate()
    {
        if (playerRoot == null || malleusRoot == null || camera == null) return;

        Vector2 a = playerRoot.position;
        Vector2 b = malleusRoot.position;
        Vector2 mid = (a + b) * 0.5f;
        float dist = Vector2.Distance(a, b);

        // Орто-размер: чтобы влезли оба шара + радиусы + запас
        float need = dist * 0.5f + ballDiameter() + padding;
        float targetSize = Mathf.Max(baseSize, need);

        Vector3 pos = transform.position;
        pos.x = Mathf.Lerp(pos.x, mid.x, Time.deltaTime * followLerp);
        pos.y = Mathf.Lerp(pos.y, mid.y, Time.deltaTime * followLerp);
        transform.position = pos;

        if (camera.orthographic)
            camera.orthographicSize = Mathf.Lerp(camera.orthographicSize, targetSize, Time.deltaTime * sizeLerp);
    }

    float ballDiameter()
    {
        var ctrl = FindObjectOfType<MaleusController>();
        return ctrl != null ? ctrl.ballRadius * 2f : 1f;
    }
}
