using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Image나 Text에 그라데이션을 입히는 컴포넌트.
/// 별도 이미지 없이 꼭짓점 색을 섞어서 그린다.
///
/// 사용법: 그라데이션을 넣고 싶은 UI 오브젝트에 Add Component로 붙이고
/// 색 두 개와 방향만 정하면 된다. Image의 Color와 곱해지므로,
/// Image Color를 흰색으로 두면 여기 지정한 색이 그대로 나온다.
///
/// 주의: 사각형 하나(Simple Image)에서만 의도대로 동작한다.
/// Sliced나 Tiled처럼 조각이 여러 개로 나뉘는 경우엔 조각마다 그라데이션이 반복된다.
/// </summary>
[AddComponentMenu("UI/Effects/UI Gradient")]
public class UIGradient : BaseMeshEffect
{
    public enum Direction { Vertical, Horizontal, DiagonalDown, DiagonalUp }

    [SerializeField] private Color topColor = Color.white;
    [SerializeField] private Color bottomColor = new Color(0.6f, 0.6f, 0.6f, 1f);
    [SerializeField] private Direction direction = Direction.Vertical;

    /// <summary>코드에서 색을 바꿀 때 사용. 바로 다시 그린다.</summary>
    public void SetColors(Color top, Color bottom)
    {
        topColor = top;
        bottomColor = bottom;
        if (graphic != null) graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0) return;

        var verts = new List<UIVertex>();
        vh.GetUIVertexStream(verts);

        // 전체 영역의 경계를 구한다. 꼭짓점 위치를 0~1로 정규화하기 위함.
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        for (int i = 0; i < verts.Count; i++)
        {
            Vector3 p = verts[i].position;
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }

        float width = Mathf.Max(maxX - minX, 0.0001f);
        float height = Mathf.Max(maxY - minY, 0.0001f);

        for (int i = 0; i < verts.Count; i++)
        {
            UIVertex v = verts[i];

            float nx = (v.position.x - minX) / width;   // 0(왼쪽) ~ 1(오른쪽)
            float ny = (v.position.y - minY) / height;  // 0(아래) ~ 1(위)
            float t = GetFactor(nx, ny);

            // 기존 색과 곱해서 Image의 Color / 알파를 그대로 존중한다.
            v.color = Multiply(Color.Lerp(bottomColor, topColor, t), v.color);
            verts[i] = v;
        }

        vh.Clear();
        vh.AddUIVertexTriangleStream(verts);
    }

    /// <summary>방향에 따라 0~1 보간 계수를 만든다. 1이면 topColor 쪽.</summary>
    private float GetFactor(float nx, float ny)
    {
        switch (direction)
        {
            case Direction.Horizontal: return nx;
            case Direction.DiagonalDown: return Mathf.Clamp01((nx + (1f - ny)) * 0.5f);
            case Direction.DiagonalUp: return Mathf.Clamp01((nx + ny) * 0.5f);
            default: return ny;
        }
    }

    private static Color32 Multiply(Color a, Color32 b)
    {
        Color bc = b;
        return new Color(a.r * bc.r, a.g * bc.g, a.b * bc.b, a.a * bc.a);
    }
}