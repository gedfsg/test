using UnityEngine;

/// <summary>
/// 인벤토리 '보유 탄약' 줄의 칸별 폭/배치를 레퍼런스대로 맞춤 (AmmoPanel에 붙임).
///
/// InventoryUI는 같은 탄약칸 프리팹을 P → AR → SG → SR 순서로 4번 만들기 때문에 칸 폭이 전부 같은데,
/// 레퍼런스는 아이콘 크기에 따라 칸 폭이 달라서(구분선 x 1237.5 / 1370 / 1515) 최대 20px 어긋났음.
/// 자식이 바뀔 때마다 순번대로 폭과 아이콘/라벨 위치를 다시 잡음 → InventoryUI 코드는 건드리지 않음.
/// (UIUtils.ClearChildren이 옛 칸을 즉시 떼어내므로 순번 = 탄약 종류 순서)
/// </summary>
public class HudAmmoRowLayout : MonoBehaviour
{
    [Header("칸별 값 (P, AR, SG, SR 순서, 레퍼런스 px)")]
    [Tooltip("칸 폭. 합계 = 줄 폭 494")]
    public float[] cellWidths = { 99.5f, 132.5f, 145f, 117f };

    [Tooltip("칸 왼쪽 끝에서 아이콘 중심까지")]
    public float[] iconCenterX = { 17.5f, 42.5f, 48.5f, 47f };

    [Tooltip("아이콘 Image 크기 (스프라이트 여백 포함 → 보이는 크기 P 10x41, AR 19x38, SG 16x46, SR 11x46)")]
    public Vector2[] iconSizes = { new Vector2(36f, 55f), new Vector2(36f, 44f), new Vector2(43f, 51f), new Vector2(50f, 49f) };

    [Tooltip("칸 왼쪽 끝에서 종류 라벨/수치 왼쪽 끝까지")]
    public float[] labelLeftX = { 45f, 70.5f, 80f, 77f };

    void OnTransformChildrenChanged() => Apply();

    void OnEnable() => Apply();

    public void Apply()
    {
        int n = Mathf.Min(transform.childCount, cellWidths.Length);
        for (int i = 0; i < n; i++)
        {
            var cell = (RectTransform)transform.GetChild(i);
            float w = cellWidths[i];
            cell.sizeDelta = new Vector2(w, cell.sizeDelta.y);
            float left = -w / 2f; // 자식들은 칸 중심 기준 좌표

            SetX(cell, "Icon", left + iconCenterX[i]);
            var icon = cell.Find("Icon") as RectTransform;
            if (icon != null && i < iconSizes.Length) icon.sizeDelta = iconSizes[i];
            SetLeft(cell, "TypeLabel", left + labelLeftX[i]);
            SetLeft(cell, "CountLabel", left + labelLeftX[i]);
            SetX(cell, "Divider", w / 2f - 0.5f);
        }
    }

    static void SetX(RectTransform cell, string child, float x)
    {
        var rt = cell.Find(child) as RectTransform;
        if (rt != null) rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
    }

    // 왼쪽 정렬 텍스트: 사각형 왼쪽 끝이 x에 오도록 (가운데 피벗 기준)
    static void SetLeft(RectTransform cell, string child, float x)
    {
        var rt = cell.Find(child) as RectTransform;
        if (rt != null) rt.anchoredPosition = new Vector2(x + rt.sizeDelta.x / 2f, rt.anchoredPosition.y);
    }
}
