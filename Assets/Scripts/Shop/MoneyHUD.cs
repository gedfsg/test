using TMPro;
using UnityEngine;

/// <summary>
/// 돈 표시: 인벤토리 창의 탄약 패널 바로 아래 줄 (왼쪽).   소지금  $ 125
///  - 인벤토리를 열었을 때만 보인다 (인벤토리 창의 자식).
///  - 가방을 바꿔 창 크기가 바뀌어도(InventoryLayoutFitter) 탄약 패널 왼쪽 아래를 따라간다.
///  - 라벨 글꼴·색은 '탄약' 라벨(HUD_AmmoLabel)과 맞춘다.
///  - PlayerMoney가 만든다 (씬 수정 없음).
/// </summary>
public class MoneyHUD : MonoBehaviour
{
    private const float RowW = 260f, RowH = 26f;
    private const float GapBelowAmmo = 22.5f;   // 탄약 패널 아래 끝 → 이 줄 가운데 (원래 배치에서 '닫기 안내'와 같은 높이)
    private const string GoldHex = "#FFD700";

    private PlayerMoney money;
    private RectTransform row, ammoPanel;
    private TextMeshProUGUI amountText;
    private int shownAmount = int.MinValue;

    /// <summary>인벤토리 창을 찾아 탄약 패널 아래에 돈 줄을 만든다. 못 찾으면 false</summary>
    public static bool Create(PlayerMoney money)
    {
        var ui = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (ui == null || ui.inventoryWindow == null) return false;
        var window = ui.inventoryWindow.transform;

        var rowRt = HudStyle.NewRect(window, "MoneyRow", Vector2.zero, new Vector2(RowW, RowH));
        var hud = rowRt.gameObject.AddComponent<MoneyHUD>();
        hud.Build(money, rowRt, ui.ammoPanelContainer as RectTransform, window);
        return true;
    }

    private void Build(PlayerMoney m, RectTransform rowRt, RectTransform ammo, Transform window)
    {
        money = m;
        row = rowRt;
        ammoPanel = ammo;

        // '탄약' 라벨과 같은 글꼴·크기·색
        var refLabel = window.Find("HUD_AmmoLabel")?.GetComponent<TextMeshProUGUI>();
        TMP_FontAsset labelFont = refLabel != null ? refLabel.font : HudStyle.SemiBold;
        float labelSize = refLabel != null ? refLabel.fontSize : 18f;
        Color labelColor = refLabel != null ? refLabel.color : HudStyle.TextDim;

        HudStyle.NewText(row, "Label", "소지금", labelFont, labelSize, labelColor,
            TextAlignmentOptions.Left, new Vector2(-RowW * 0.5f + 40f, 0f), new Vector2(80f, RowH));
        amountText = HudStyle.NewText(row, "Amount", "", HudStyle.Bold, labelSize + 2f, Color.white,
            TextAlignmentOptions.Left, new Vector2(-RowW * 0.5f + 80f + 80f, 0f), new Vector2(160f, RowH));
        amountText.richText = true;

        Follow();
        Refresh();
    }

    private void LateUpdate()   // 인벤토리 창이 열려 있을 때만 돈다
    {
        Follow();
        Refresh();
    }

    /// <summary>탄약 패널의 왼쪽 아래를 따라간다 (가방에 따라 창·패널 크기가 바뀜)</summary>
    private void Follow()
    {
        if (row == null) return;
        if (ammoPanel != null && ammoPanel.parent == row.parent)
        {
            float left = ammoPanel.anchoredPosition.x - ammoPanel.sizeDelta.x * ammoPanel.pivot.x;
            float bottom = ammoPanel.anchoredPosition.y - ammoPanel.sizeDelta.y * ammoPanel.pivot.y;
            row.anchoredPosition = new Vector2(left + RowW * 0.5f, bottom - GapBelowAmmo);
        }
        else
        {
            // 탄약 패널을 못 찾으면 창 왼쪽 아래 구석
            var w = row.parent as RectTransform;
            if (w != null) row.anchoredPosition = new Vector2(-w.sizeDelta.x * 0.5f + 24f + RowW * 0.5f, -w.sizeDelta.y * 0.5f + 30f);
        }
    }

    private void Refresh()
    {
        if (money == null || amountText == null || money.Amount == shownAmount) return;
        shownAmount = money.Amount;
        amountText.text = $"<color={GoldHex}>$</color> {shownAmount:N0}";
    }
}
