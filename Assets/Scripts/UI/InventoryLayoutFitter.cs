using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 가방 칸 수에 맞춰 인벤토리 창 크기 자동 조정.
///  15칸 = 6열×3줄, 24칸 = 6열×4줄(원래 디자인), 40칸 = 8열×5줄 + 칸 88% (6열로 하면 화면을 넘어서 8열)
/// 창은 오른쪽 위 모서리를 고정한 채 커지고, 슬롯 그리드 위 요소는 위로, 아래 요소는 아래로 밀림.
/// 원래(24칸) 배치를 처음 한 번 저장해두고 매번 그 기준으로 다시 계산 → 여러 번 바꿔도 안 어긋남.
/// </summary>
public static class InventoryLayoutFitter
{
    class Original
    {
        public Vector2 windowSize, windowPos, gridSize;
        public int gridColumns;
        public Vector2 cellSize;
        public readonly Dictionary<RectTransform, (Vector2 pos, Vector2 size)> children = new Dictionary<RectTransform, (Vector2, Vector2)>();
    }

    static readonly Dictionary<int, Original> originals = new Dictionary<int, Original>();

    public static void Apply(InventoryUI ui, int capacity)
    {
        if (ui == null || ui.inventoryWindow == null || ui.slotGrid == null) return;
        var window = (RectTransform)ui.inventoryWindow.transform;
        var grid = (RectTransform)ui.slotGrid;
        var glg = grid.GetComponent<GridLayoutGroup>();
        if (glg == null) return;

        if (!originals.TryGetValue(window.GetInstanceID(), out var o))
        {
            o = new Original { windowSize = window.sizeDelta, windowPos = window.anchoredPosition, gridSize = grid.sizeDelta, gridColumns = glg.constraintCount, cellSize = glg.cellSize };
            foreach (RectTransform c in window) o.children[c] = (c.anchoredPosition, c.sizeDelta);
            originals[window.GetInstanceID()] = o;
        }

        int cols = capacity > 24 ? 8 : o.gridColumns;
        int rows = Mathf.CeilToInt(capacity / (float)cols);
        // 40칸은 칸을 88%로 줄임 → 창이 하단 손전등/탄약 패널, 상단 날짜 표시와 안 겹치게
        glg.cellSize = capacity > 24 ? o.cellSize * 0.88f : o.cellSize;
        Vector2 newGrid = new Vector2(cols * glg.cellSize.x + (cols - 1) * glg.spacing.x, rows * glg.cellSize.y + (rows - 1) * glg.spacing.y);
        float dW = newGrid.x - o.gridSize.x, dH = newGrid.y - o.gridSize.y;

        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = cols;
        window.sizeDelta = o.windowSize + new Vector2(dW, dH);
        window.anchoredPosition = o.windowPos + new Vector2(-dW / 2f, -dH / 2f); // 오른쪽 위 모서리 고정

        float gridY = o.children.TryGetValue(grid, out var g) ? g.pos.y : 0f;
        foreach (var kv in o.children)
        {
            var c = kv.Key; if (c == null) continue;
            var (pos, size) = kv.Value;
            if (c == grid) { c.sizeDelta = newGrid; c.anchoredPosition = pos; continue; }
            if (c.anchorMin != c.anchorMax) continue; // 늘어나는(stretch) 요소는 알아서 맞춰짐

            Vector2 p = pos, sz = size;
            if (size.x >= 400f) sz.x += dW;            // 구분선/탄약 패널처럼 넓은 요소는 같이 넓힘
            else if (pos.x < -50f) p.x -= dW / 2f;     // 왼쪽 요소 (제목 등)
            else if (pos.x > 50f) p.x += dW / 2f;      // 오른쪽 요소 (닫기, 칸 수)
            p.y += pos.y > gridY ? dH / 2f : -dH / 2f; // 그리드 위/아래
            c.anchoredPosition = p; c.sizeDelta = sz;
        }
    }
}
