using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Строит тестовую сцену: неровный "холм" из полигональных коллайдеров,
/// чтобы можно было сразу проверить отталкивание Malleus от поверхностей.
/// </summary>
public class TestTerrain : MonoBehaviour
{
    public float width = 40f;
    public int segments = 60;
    public float amplitude = 2.5f;

    void Start()
    {
        if (GetComponent<EdgeCollider2D>() != null) return;

        var ec = gameObject.AddComponent<EdgeCollider2D>();
        var pts = new List<Vector2>();
        for (int i = 0; i <= segments; i++)
        {
            float x = -width * 0.5f + width * i / segments;
            float y = Mathf.Sin(x * 0.55f) * amplitude * 0.5f
                    + Mathf.Sin(x * 1.3f + 1f) * amplitude * 0.3f
                    + Mathf.Cos(x * 0.2f) * amplitude * 0.4f - 2f;
            pts.Add(new Vector2(x, y));
        }
        ec.points = pts.ToArray();
    }
}
