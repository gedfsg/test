using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 인벤토리/핫바 슬롯 드래그 앤 드롭 (SlotView가 이벤트를 넘겨줌)
///  - 인벤토리 → 핫바: 등록 (칸 타입 체크: 1,2 = 무기, 3~5 = 소비). 칸에 있던 아이템은 인벤토리 그 자리로
///  - 인벤토리 ↔ 인벤토리: 자리 교환 (빈 칸에 놓으면 맨 뒤로)
///  - 핫바 ↔ 핫바: 자리 교환 (둘 다 타입 맞을 때만)
///  - 핫바 → 인벤토리: 해제 (놓은 칸 위치로, 빈 칸이면 맨 뒤)
/// 드래그 중 칸 위에 올리면 초록(가능)/빨강(거부) 테두리. 잘못된 칸에 놓으면 거부 + 안내 문구.
/// </summary>
public static class SlotDragDrop
{
    static readonly Color OkColor = new Color(0.35f, 0.9f, 0.45f, 1f);
    static readonly Color RejectColor = new Color(0.95f, 0.2f, 0.18f, 1f);

    static SlotView source;
    static RectTransform ghost;
    static SlotView hovered;

    public static bool IsDragging => source != null;

    public static void Begin(SlotView slot, PointerEventData e)
    {
        Cleanup();
        if (slot == null || slot.CurrentItem == null || slot.Owner == SlotView.SlotOwner.None) return;
        source = slot;
        ContextMenuUI.Instance?.Hide();

        // 마우스를 따라다니는 반투명 아이콘
        var canvas = slot.GetComponentInParent<Canvas>().rootCanvas;
        var go = new GameObject("DragGhost", typeof(RectTransform), typeof(Image));
        ghost = (RectTransform)go.transform;
        ghost.SetParent(canvas.transform, false);
        ghost.SetAsLastSibling();
        ghost.sizeDelta = new Vector2(64f, 64f);
        var img = go.GetComponent<Image>();
        img.sprite = slot.IconSprite;
        img.preserveAspect = true;
        img.raycastTarget = false; // 아래 칸이 드롭을 받아야 함
        img.color = new Color(1f, 1f, 1f, img.sprite != null ? 0.85f : 0f);
        Move(e);
    }

    public static void Move(PointerEventData e)
    {
        if (ghost != null) ghost.position = e.position;
    }

    public static void Hover(SlotView slot, bool enter)
    {
        if (source == null || slot == null || slot == source) return;
        if (enter) { hovered = slot; slot.SetDropHint(CanDrop(source, slot) ? OkColor : RejectColor); }
        else { slot.SetDropHint(null); if (hovered == slot) hovered = null; }
    }

    public static void Drop(SlotView target)
    {
        if (source == null || target == null || target == source) return;
        var src = source;
        bool ok = CanDrop(src, target);
        Cleanup(); // 아래 처리에서 UI가 다시 그려질 수 있어서 먼저 정리
        if (!ok)
        {
            PickupFeedbackUI.Instance?.ShowWarning(target.Index < WeaponHotbarUI.SlotTypeFilter.WeaponSlotCount && target.Owner == SlotView.SlotOwner.Hotbar
                ? "1, 2번은 무기 전용" : "3~5번은 소비 아이템 전용");
            return;
        }
        Execute(src, target);
    }

    public static void End() => Cleanup();

    static void Cleanup()
    {
        if (hovered != null) hovered.SetDropHint(null);
        hovered = null;
        if (ghost != null) Object.Destroy(ghost.gameObject);
        ghost = null;
        source = null;
    }

    // ── 규칙 ──
    static bool CanDrop(SlotView src, SlotView dst)
    {
        var hb = WeaponHotbarUI.Instance;
        if (src.Owner == SlotView.SlotOwner.Inventory && dst.Owner == SlotView.SlotOwner.Hotbar)
            return WeaponHotbarUI.SlotTypeFilter.CanPlace(dst.Index, src.CurrentItem);
        if (src.Owner == SlotView.SlotOwner.Hotbar && dst.Owner == SlotView.SlotOwner.Hotbar)
            return hb != null && WeaponHotbarUI.SlotTypeFilter.CanPlace(dst.Index, hb.GetItem(src.Index))
                              && WeaponHotbarUI.SlotTypeFilter.CanPlace(src.Index, hb.GetItem(dst.Index));
        return dst.Owner == SlotView.SlotOwner.Inventory; // 인벤토리 ↔ 인벤토리, 핫바 → 인벤토리
    }

    // ── 실행 ──
    static void Execute(SlotView src, SlotView dst)
    {
        var hb = WeaponHotbarUI.Instance;
        var inv = Object.FindFirstObjectByType<InventoryManager>();
        var ui = Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (inv == null) return;
        var list = inv.inventory;

        if (src.Owner == SlotView.SlotOwner.Inventory && dst.Owner == SlotView.SlotOwner.Inventory)
        {
            // 자리 교환 (빈 칸이면 맨 뒤로)
            if (src.Index >= list.Count) return;
            if (dst.Index >= list.Count) { var s = list[src.Index]; list.RemoveAt(src.Index); list.Add(s); }
            else (list[src.Index], list[dst.Index]) = (list[dst.Index], list[src.Index]);
        }
        else if (src.Owner == SlotView.SlotOwner.Inventory && dst.Owner == SlotView.SlotOwner.Hotbar)
        {
            if (hb == null || src.Index >= list.Count) return;
            var slot = list[src.Index];
            var old = hb.GetItem(dst.Index);
            if (old == slot.item && !(slot.item is WeaponData))
            {
                // 같은 소비 아이템이면 개수 합침
                hb.PutSlot(dst.Index, old, hb.GetAmmo(dst.Index) + slot.amount);
                list.RemoveAt(src.Index);
            }
            else
            {
                int count = slot.item is WeaponData wd ? (slot.currentAmmo >= 0 ? slot.currentAmmo : wd.maxAmmo) : slot.amount;
                var (oldItem, oldCount) = hb.TakeSlot(dst.Index);
                if (oldItem != null) list[src.Index] = ToInventorySlot(oldItem, oldCount); // 원래 있던 건 인벤토리 그 자리로
                else list.RemoveAt(src.Index);
                hb.PutSlot(dst.Index, slot.item, count);
            }
        }
        else if (src.Owner == SlotView.SlotOwner.Hotbar && dst.Owner == SlotView.SlotOwner.Hotbar)
        {
            hb?.SwapSlots(src.Index, dst.Index);
        }
        else if (src.Owner == SlotView.SlotOwner.Hotbar && dst.Owner == SlotView.SlotOwner.Inventory)
        {
            if (hb == null) return;
            // 해제: 놓은 칸 위치에 끼워 넣음 (빈 칸이면 맨 뒤)
            if (list.Count >= inv.maxCapacity) { PickupFeedbackUI.Instance?.ShowWarning("인벤토리가 가득 참"); return; }
            var (item, count) = hb.TakeSlot(src.Index);
            list.Insert(Mathf.Min(dst.Index, list.Count), ToInventorySlot(item, count));
        }

        if (ui != null && ui.isInventoryOpen) ui.UpdateUI();
    }

    // 핫바의 count: 무기 = 탄수, 소비 = 개수 → 인벤토리 칸 형식으로
    static InventorySlot ToInventorySlot(ItemData item, int count)
    {
        if (item is WeaponData) { var s = new InventorySlot(item, 1); s.currentAmmo = count; return s; }
        return new InventorySlot(item, Mathf.Max(1, count));
    }
}
