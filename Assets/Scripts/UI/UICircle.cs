using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 원 (2026-11-16, 미니맵 서브 미션 범위): 안쪽 채움(fillColor) + 테두리(color, thickness px).
/// 텍스처 없이 메시로 그려서 크기가 바뀌어도(미니맵 줌) 테두리 두께가 항상 같음.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class UICircle : MaskableGraphic
{
    public Color fillColor = new Color(1f, 0.6f, 0f, 0.1f);
    public float thickness = 2f;
    public int segments = 64;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float outer = Mathf.Min(r.width, r.height) * 0.5f;
        if (outer <= 0.5f) return;
        float inner = Mathf.Max(0f, outer - thickness);
        Vector2 c = r.center;

        // 채움 (부채꼴 삼각형)
        vh.AddVert(c, fillColor, Vector2.zero);
        for (int i = 0; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            vh.AddVert(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * inner, fillColor, Vector2.zero);
        }
        for (int i = 1; i <= segments; i++) vh.AddTriangle(0, i, i + 1);

        // 테두리 (안쪽~바깥 띠)
        int start = vh.currentVertCount;
        for (int i = 0; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            vh.AddVert(c + d * inner, color, Vector2.zero);
            vh.AddVert(c + d * outer, color, Vector2.zero);
        }
        for (int i = 0; i < segments; i++)
        {
            int k = start + i * 2;
            vh.AddTriangle(k, k + 1, k + 3);
            vh.AddTriangle(k, k + 3, k + 2);
        }
    }

    /// <summary>색 바꾸기 (같으면 메시 다시 안 만듦)</summary>
    public void SetColors(Color border, Color fill)
    {
        if (color == border && fillColor == fill) return;
        color = border; fillColor = fill;
        SetVerticesDirty();
    }
}
