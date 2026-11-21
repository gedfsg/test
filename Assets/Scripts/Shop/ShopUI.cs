using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 상점 창 (상점 2단계). 코드로 만드는 화면 —— 씬·프리팹 수정 없음.
///  - 사기: 분류(전체·탄약·음식·물·치료·총기) → 줄마다 아이콘·이름·설명·재고·가격·[구매]
///  - 팔기: 상점을 열면 인벤토리도 같이 열린다 → 인벤토리에서 아이템을 클릭하면 메뉴 맨 위에 '팔기 (+$n)' / '전부 팔기'
///          (이 메뉴는 상점이 열려 있을 때만 나옴 —— ItemActionProvider, InventoryUI)
///  - 게임 시간은 계속 흐른다. 창이 열린 동안 사격·조준·카메라 휠만 막고 이동은 됨 (인벤토리와 같은 방식)
///  - 인벤토리 창(오른쪽 위)과 겹치지 않게 화면 왼쪽 가장자리에 붙어 뜬다 (LeftMargin)
///  - 닫기: Esc / X 버튼 / 여는 키를 다시 / 상인에게서 멀어지면 (3단계). 상점이 열어 준 인벤토리는 같이 닫는다
///  - Open(stock, trader): 3단계 상인이 부른다. 지금은 촬영 패널(F8) '상점 열기 (시험)'으로 연다
/// </summary>
public class ShopUI : MonoBehaviour
{
    public static ShopUI Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.root != null && Instance.root.activeSelf;
    /// <summary>지금 열린 진열대 (닫혀 있으면 null)</summary>
    public static ShopStock CurrentStock => IsOpen ? Instance.stock : null;

    [Tooltip("상인에게서 이만큼(m) 멀어지면 창이 닫힌다")]
    public float closeDistance = 5f;

    // 색
    private static readonly Color WindowBg = new Color(0.07f, 0.075f, 0.085f, 0.96f);
    private static readonly Color WindowBorder = new Color32(100, 98, 96, 255);
    private static readonly Color RowBg = new Color(1f, 1f, 1f, 0.05f);
    private static readonly Color RowBgAlt = new Color(1f, 1f, 1f, 0.025f);
    private static readonly Color Gold = new Color32(0xFF, 0xD7, 0x00, 0xFF);
    private static readonly Color Accent = new Color(1f, 0.65f, 0.1f);
    private static readonly Color ChipOff = new Color(1f, 1f, 1f, 0.08f);
    private static readonly Color ChipOn = new Color(1f, 0.65f, 0.1f, 0.9f);
    private static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color Bad = new Color32(0xFC, 0x62, 0x60, 0xFF);

    // 배치 (창 가운데 기준, 위가 +)
    private const float W = 720f, H = 640f;
    private const float LeftMargin = 40f;   // 화면 왼쪽 끝에서 띄우는 거리
    private const float TitleY = H / 2f - 42f, SubtitleY = H / 2f - 72f, SepY = H / 2f - 92f;
    private const float ChipY = H / 2f - 122f, ChipH = 34f;                 // 분류 칩 줄
    private const float ListTop = ChipY - ChipH / 2f - 14f;                 // 칩 아래 14px 띄우고 목록 시작
    private const float ListBottom = -H / 2f + 52f;                         // 아래 안내 줄 위
    private const float ListW = 680f, ListH = ListTop - ListBottom, ListY = (ListTop + ListBottom) / 2f;
    private const float RowH = 58f, RowGap = 6f;

    private GameObject root;
    private RectTransform content;
    private TextMeshProUGUI titleText, moneyText, messageText;
    private readonly List<KeyValuePair<int, Image>> chips = new List<KeyValuePair<int, Image>>();   // -1 = 전체

    private ShopStock stock;
    private Transform trader;
    private int filter = -1;
    private float messageTimer;
    private bool dirty;
    private bool openedInventory;   // 상점이 인벤토리를 열어 줬으면 닫을 때 같이 닫음

    // ── 열기/닫기 ──────────────────────────────────
    public static void Open(ShopStock stock, Transform trader = null)
    {
        if (stock == null) return;
        if (Instance == null) new GameObject("ShopUI").AddComponent<ShopUI>();
        Instance.Show(stock, trader);
    }

    public static void Close()
    {
        if (Instance != null) Instance.Hide();
    }

    /// <summary>열려 있으면 닫고, 닫혀 있으면 연다</summary>
    public static void Toggle(ShopStock stock, Transform trader = null)
    {
        if (IsOpen && Instance.stock == stock) Close();
        else Open(stock, trader);
    }

    /// <summary>인벤토리 메뉴의 '팔기' / '전부 팔기' (InventoryUI가 부름)</summary>
    public static void SellFromInventory(InventorySlot slot, int count)
    {
        if (!IsOpen || slot == null || slot.item == null) return;
        string name = ShopPricing.DisplayName(slot.item);
        int sold = ShopStock.Sell(slot, count, out int earned);
        if (sold > 0) Instance.ShowMessage(sold > 1 ? $"{name} {sold}개 판매 (+${earned})" : $"{name} 판매 (+${earned})", Gold);
        else Instance.ShowMessage(ShopStock.Message(ShopStock.Result.NoInventory), Bad);
        Instance.dirty = true;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
        root.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (PlayerMoney.Instance != null) PlayerMoney.Instance.Changed -= OnMoneyChanged;
    }

    private void Show(ShopStock s, Transform t)
    {
        stock = s;
        trader = t;
        filter = -1;
        titleText.text = string.IsNullOrEmpty(s.traderName) ? "상인" : s.traderName;
        root.SetActive(true);
        if (PlayerMoney.Instance != null)
        {
            PlayerMoney.Instance.Changed -= OnMoneyChanged;
            PlayerMoney.Instance.Changed += OnMoneyChanged;
        }

        // 인벤토리도 같이 연다 (팔기는 인벤토리에서)
        openedInventory = false;
        if (!MenuUI.InventoryOpen)
        {
            var inv = FindFirstObjectByType<InventoryUI>();
            if (inv != null) { inv.ToggleInventory(); openedInventory = true; }
        }

        ShowMessage("", Color.white);
        Rebuild();
    }

    private void Hide()
    {
        if (root == null || !root.activeSelf) return;
        root.SetActive(false);
        if (PlayerMoney.Instance != null) PlayerMoney.Instance.Changed -= OnMoneyChanged;
        ContextMenuUI.Instance?.Hide();   // '팔기' 메뉴가 떠 있으면 같이 닫기

        if (openedInventory && MenuUI.InventoryOpen)
        {
            var inv = FindFirstObjectByType<InventoryUI>();
            if (inv != null) inv.ToggleInventory();   // 안에서 RefreshGameplayInput까지 함
        }
        openedInventory = false;
        MenuUI.RefreshGameplayInput();   // 사격·카메라 원래대로 (인벤토리·일시정지 상태에 맞게)
    }

    private void OnMoneyChanged(int amount, int delta) => dirty = true;

    private void Update()
    {
        if (root == null || !root.activeSelf) return;

        // 열려 있는 동안 휠 줌 막기 (인벤토리를 열고 닫아도 다시 막음). 이동은 그대로
        // (2026-11-21) 사격·조준은 막지 않음 — 인벤토리처럼 상점을 연 채로도 싸울 수 있게 (창 위 클릭은 PlayerController가 걸러냄)
        var player = GameObject.FindWithTag("Player");
        var cam = Camera.main;
        if (cam != null)
        {
            var zoom = cam.GetComponent<CameraZoom>();
            if (zoom != null) zoom.enabled = false;
        }

        // 상인에게서 멀어지면 닫기
        if (trader != null && player != null)
        {
            Vector3 d = player.transform.position - trader.position; d.y = 0f;
            if (d.sqrMagnitude > closeDistance * closeDistance) { Hide(); return; }
        }
        if (trader == null && stock != null && stock.boundToTrader) { Hide(); return; }   // 상인이 사라짐

        if (dirty) { dirty = false; Rebuild(); }

        if (messageTimer > 0f)
        {
            messageTimer -= Time.unscaledDeltaTime;
            if (messageTimer <= 0f) messageText.text = "";
        }
    }

    // ── 화면 만들기 ─────────────────────────────────
    private void Build()
    {
        var canvas = MenuUI.CreateOverlayCanvas("ShopCanvas", 500);   // HUD 위, 일시정지(1100) 아래
        canvas.transform.SetParent(transform, false);
        root = canvas.gameObject;

        var win = HudStyle.NewRect(canvas.transform, "Window", Vector2.zero, new Vector2(W, H));
        win.anchorMin = win.anchorMax = new Vector2(0f, 0.5f);   // 화면 왼쪽 가운데 기준
        win.pivot = new Vector2(0f, 0.5f);
        win.anchoredPosition = new Vector2(LeftMargin, 0f);
        var bg = HudStyle.NewImage(win, "Bg", HudStyle.RoundedRect, WindowBg, Vector2.zero, new Vector2(W, H));
        bg.raycastTarget = true;   // 창 위 클릭이 뒤로 새지 않게
        bg.pixelsPerUnitMultiplier = 2f;
        var border = HudStyle.NewImage(win, "Border", HudStyle.RoundedFrame, WindowBorder, Vector2.zero, new Vector2(W, H));
        border.pixelsPerUnitMultiplier = 2f;

        // 머리줄: 상인 이름 / 소지금 / 닫기
        titleText = HudStyle.NewText(win, "Title", "상인", HudStyle.Bold, 30f, Color.white,
            TextAlignmentOptions.Left, new Vector2(-W / 2f + 30f + 150f, TitleY), new Vector2(300f, 40f));
        HudStyle.NewText(win, "Subtitle", "팔 물건은 인벤토리에서 클릭하세요", HudStyle.Regular, 16f, Dim,
            TextAlignmentOptions.Left, new Vector2(-W / 2f + 30f + 200f, SubtitleY), new Vector2(400f, 22f));
        moneyText = HudStyle.NewText(win, "Money", "", HudStyle.Bold, 28f, Color.white,
            TextAlignmentOptions.Right, new Vector2(W / 2f - 70f - 100f, TitleY), new Vector2(200f, 40f));
        moneyText.richText = true;
        var close = NewButton(win, "Close", "X", new Vector2(W / 2f - 36f, TitleY + 2f), new Vector2(36f, 36f), ChipOff, 20f);
        close.onClick.AddListener(Hide);

        HudStyle.NewImage(win, "Sep", null, new Color(1f, 1f, 1f, 0.15f), new Vector2(0f, SepY), new Vector2(W - 40f, 1.5f));

        // 분류 칩
        var cats = new int[] { -1, (int)ShopCategory.Ammo, (int)ShopCategory.Food, (int)ShopCategory.Medical, (int)ShopCategory.Weapon, (int)ShopCategory.Throwable, (int)ShopCategory.Upgrade };
        for (int i = 0; i < cats.Length; i++)
        {
            int c = cats[i];
            string label = c < 0 ? "전체" : ShopPricing.CategoryName((ShopCategory)c);
            var b = NewButton(win, "Chip", label, new Vector2(-W / 2f + 20f + 45f + i * 96f, ChipY), new Vector2(90f, ChipH), ChipOff, 17f);   // 칩 7개 (2026-11-21 투척 추가)
            b.onClick.AddListener(() => { filter = c; Rebuild(); });
            chips.Add(new KeyValuePair<int, Image>(c, b.GetComponent<Image>()));
        }

        // 목록 (스크롤)
        var viewport = HudStyle.NewRect(win, "Viewport", new Vector2(0f, ListY), new Vector2(ListW, ListH));
        viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = viewport.gameObject.AddComponent<Image>();   // 휠 스크롤을 받으려면 레이캐스트 대상이 필요
        vpImg.color = new Color(0f, 0f, 0f, 0.001f);
        content = HudStyle.NewRect(viewport, "Content", Vector2.zero, new Vector2(ListW, 0f));
        content.anchorMin = content.anchorMax = new Vector2(0.5f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        // 안내 줄
        messageText = HudStyle.NewText(win, "Message", "", HudStyle.SemiBold, 18f, Color.white,
            TextAlignmentOptions.Center, new Vector2(0f, -H / 2f + 28f), new Vector2(W - 40f, 26f));
    }

    private static Button NewButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, Color color, float fontSize)
    {
        var img = HudStyle.NewImage(parent, name, HudStyle.RoundedRect, color, pos, size);
        img.raycastTarget = true;
        img.pixelsPerUnitMultiplier = 3f;
        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var colors = b.colors;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
        b.colors = colors;
        HudStyle.NewText(img.transform, "Label", label, HudStyle.SemiBold, fontSize, Color.white,
            TextAlignmentOptions.Center, Vector2.zero, size);
        return b;
    }

    // ── 목록 채우기 ─────────────────────────────────
    private void Rebuild()
    {
        if (content == null) return;
        int money = PlayerMoney.Instance != null ? PlayerMoney.Instance.Amount : 0;
        moneyText.text = $"<color=#FFD700>$</color> {money:N0}";

        foreach (var chip in chips) chip.Value.color = chip.Key == filter ? ChipOn : ChipOff;

        for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);

        int n = 0;
        if (stock != null)
            foreach (var o in stock.offers)
            {
                if (filter >= 0 && (int)o.category != filter) continue;
                var offer = o;
                bool upgrade = o.item is UpgradeItemData;
                bool maxed = upgrade && ShopStock.PriceOf(o) <= 0;
                int price = ShopStock.PriceOf(o);
                string stockLabel = upgrade ? "" : o.stock < 0 ? "" : o.stock == 0 ? "품절" : $"재고 {o.stock}";
                bool can = !o.SoldOut && !maxed && money >= price;
                string label = maxed ? "최대" : o.SoldOut ? "품절" : "구매";
                AddRow(n++, o.item, ShopPricing.DisplayName(o.item), ShopPricing.Describe(o.item), stockLabel, maxed ? 0 : price,
                    label, can, () => OnBuy(offer));
            }

        if (n == 0)
        {
            var t = HudStyle.NewText(content, "Empty", "살 수 있는 물건이 없습니다",
                HudStyle.Regular, 18f, Dim, TextAlignmentOptions.Center, new Vector2(0f, -40f), new Vector2(ListW, 30f));
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            n = 1;
        }
        content.sizeDelta = new Vector2(ListW, n * (RowH + RowGap));
    }

    private void AddRow(int index, ItemData item, string name, string desc, string stockLabel, int price,
        string buttonLabel, bool enabled, UnityEngine.Events.UnityAction onClick)
    {
        float y = -(index * (RowH + RowGap)) - RowH / 2f;
        var row = HudStyle.NewImage(content, "Row", HudStyle.RoundedRect, index % 2 == 0 ? RowBg : RowBgAlt, new Vector2(0f, y), new Vector2(ListW, RowH));
        row.rectTransform.anchorMin = row.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        row.raycastTarget = true;   // 줄 위에서도 휠 스크롤
        var rt = row.transform;

        // 등급 색 줄 + 아이콘
        HudStyle.NewImage(rt, "Rarity", null, RarityColors.Get(item.rarity), new Vector2(-ListW / 2f + 3f, 0f), new Vector2(4f, RowH - 12f));
        var icon = HudStyle.NewImage(rt, "Icon", item.icon, item.icon != null ? Color.white : new Color(1f, 1f, 1f, 0.1f),
            new Vector2(-ListW / 2f + 36f, 0f), new Vector2(44f, 44f));
        icon.preserveAspect = true;

        HudStyle.NewText(rt, "Name", name, HudStyle.SemiBold, 20f, Color.white,
            TextAlignmentOptions.Left, new Vector2(-ListW / 2f + 70f + 160f, 11f), new Vector2(320f, 26f));
        HudStyle.NewText(rt, "Desc", desc, HudStyle.Regular, 14f, Dim,
            TextAlignmentOptions.Left, new Vector2(-ListW / 2f + 70f + 180f, -13f), new Vector2(360f, 20f));
        if (!string.IsNullOrEmpty(stockLabel))
            HudStyle.NewText(rt, "Stock", stockLabel, HudStyle.Regular, 15f, stockLabel == "품절" ? Bad : Dim,
                TextAlignmentOptions.Right, new Vector2(ListW / 2f - 275f, 0f), new Vector2(80f, 24f));
        // 가격 오른쪽 끝과 구매 버튼 사이 약 20px
        HudStyle.NewText(rt, "Price", price > 0 ? "$" + price.ToString("N0") : "-", HudStyle.Bold, 22f, enabled ? Gold : Bad,
            TextAlignmentOptions.Right, new Vector2(ListW / 2f - 171f, 0f), new Vector2(90f, 28f));

        var b = NewButton(rt, "Action", buttonLabel, new Vector2(ListW / 2f - 58f, 0f), new Vector2(96f, 38f), Accent, 17f);
        b.interactable = enabled;
        b.onClick.AddListener(onClick);
    }

    // ── 사기 ───────────────────────────────────────
    private void OnBuy(ShopStock.Offer o)
    {
        int price = ShopStock.PriceOf(o);
        var r = stock.Buy(o);
        if (r == ShopStock.Result.Ok && o.item is UpgradeItemData up && PlayerUpgrades.Instance != null)
            ShowMessage($"{PlayerUpgrades.ShortName(up.upgrade)} {PlayerUpgrades.Instance.Level(up.upgrade)}단계! (-${price})", Gold);
        else if (r == ShopStock.Result.Ok) ShowMessage($"{ShopPricing.DisplayName(o.item)} 구매 (-${price})", Gold);
        else ShowMessage(ShopStock.Message(r), Bad);
        Rebuild();
    }

    private void ShowMessage(string text, Color color)
    {
        if (messageText == null) return;
        messageText.text = text;
        messageText.color = color;
        messageTimer = string.IsNullOrEmpty(text) ? 0f : 2.5f;
    }
}
