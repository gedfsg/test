using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 상인 한 명의 진열대 (상점 2단계). 사고팔기 처리도 여기서.
///  - Generate(seed): 루팅 데이터(LootDatabase)에 있는 아이템으로 채우되, 분류마다 한두 가지씩 빠진다
///      (상인마다 빠진 게 달라 다른 상인을 찾아갈 이유가 생김). 탄약도 재고 한정. 전설 총기는 안 팜
///  - 3단계에서 상인마다 하나씩 들고, 매일 새로 채운다.
/// </summary>
public class ShopStock
{
    public class Offer
    {
        public ItemData item;
        public int price;
        public int stock;          // -1 = 무제한
        public ShopCategory category;
        public bool SoldOut => stock == 0;
    }

    /// <summary>상인이 영구 강화를 하나 진열할 확률</summary>
    public const double UpgradeChance = 0.6;

    public readonly List<Offer> offers = new List<Offer>();
    public string traderName = "상인";   // 상점 창 제목
    /// <summary>상인에게 묶인 진열대면 true (상인이 사라지면 창을 닫음). 시험용은 false</summary>
    public bool boundToTrader;

    public enum Result { Ok, NotEnoughMoney, SoldOut, BagFull, AmmoFull, NoInventory, Maxed }

    public static string Message(Result r)
    {
        switch (r)
        {
            case Result.NotEnoughMoney: return "돈이 부족합니다";
            case Result.SoldOut: return "다 팔렸습니다";
            case Result.BagFull: return "가방이 꽉 찼습니다";
            case Result.AmmoFull: return "이 탄약은 더 들 수 없습니다";
            case Result.NoInventory: return "인벤토리를 찾지 못했습니다";
            case Result.Maxed: return "이미 최대 단계입니다";
            default: return "";
        }
    }

    // ── 진열대 만들기 ──────────────────────────────
    /// <summary>
    /// 진열대 채우기. 분류마다 거의 다 팔되 한두 가지씩 빠진다 (상인마다 빠진 게 달라서 돌아다닐 이유가 생김).
    ///  탄약: 4종 중 1~2종 빠짐 (묶음 재고 3~6) / 음식·물, 치료: 각각 1~2종 빠짐 (재고 1~4)
    ///  총: 1~2정 (재고 1). 등급 확률 일반 50% · 고급 30% · 희귀 15% · 영웅 5% (ShopPricing.GunRarityWeight), 근접무기·전설 제외
    ///  영구 강화: UpgradeChance 확률로 1개 (아직 최대가 아닌 것 중에서, 재고 1)
    /// </summary>
    public static ShopStock Generate(int seed)
    {
        var rng = new System.Random(seed);
        var s = new ShopStock();
        var pool = CollectItems();

        // 탄약: 무기 종류별 묶음 (같은 종류가 여럿이면 묶음이 큰 것)
        var ammoByType = new Dictionary<WeaponType, AmmoData>();
        foreach (var it in pool)
            if (it is AmmoData a && (!ammoByType.TryGetValue(a.weaponType, out var cur) || a.ammoAmount > cur.ammoAmount))
                ammoByType[a.weaponType] = a;
        var ammo = new List<ItemData>();
        foreach (var t in new[] { WeaponType.Pistol, WeaponType.AR, WeaponType.SMG, WeaponType.Shotgun, WeaponType.Sniper })
            if (ammoByType.TryGetValue(t, out var a)) ammo.Add(a);
        s.AddWithGaps(ammo, 3, 6, rng);

        s.AddWithGaps(Candidates(pool, ShopCategory.Food), 1, 4, rng);
        s.AddWithGaps(Candidates(pool, ShopCategory.Medical), 1, 3, rng);

        // 영구 강화: 0~1개
        var up = PlayerUpgrades.Instance;
        if (up != null && rng.NextDouble() < UpgradeChance)
        {
            var open = new List<UpgradeType>();
            foreach (var t in PlayerUpgrades.All) if (!up.IsMaxed(t)) open.Add(t);
            if (open.Count > 0) s.Add(up.Item(open[rng.Next(open.Count)]), 1);
        }

        var guns = Candidates(pool, ShopCategory.Weapon).FindAll(w => ((WeaponData)w).type != WeaponType.Melee);
        int ng = rng.Next(1, 3);
        for (int i = 0; i < ng && guns.Count > 0; i++)
        {
            var g = PickGun(guns, rng);
            if (g == null) break;
            s.Add(g, 1);
            guns.Remove(g);
        }
        return s;
    }

    /// <summary>등급 확률대로 등급을 먼저 고르고, 그 등급 안에서 한 자루</summary>
    private static ItemData PickGun(List<ItemData> guns, System.Random rng)
    {
        var byRarity = new Dictionary<Rarity, List<ItemData>>();
        foreach (var g in guns)
        {
            if (!byRarity.TryGetValue(g.rarity, out var list)) byRarity[g.rarity] = list = new List<ItemData>();
            list.Add(g);
        }
        float total = 0f;
        foreach (var kv in byRarity) total += ShopPricing.GunRarityWeight(kv.Key);
        if (total <= 0f) return null;
        double roll = rng.NextDouble() * total;
        foreach (var kv in byRarity)
        {
            roll -= ShopPricing.GunRarityWeight(kv.Key);
            if (roll <= 0) return kv.Value[rng.Next(kv.Value.Count)];
        }
        foreach (var kv in byRarity) if (ShopPricing.GunRarityWeight(kv.Key) > 0f) return kv.Value[rng.Next(kv.Value.Count)];
        return null;
    }

    private static List<ItemData> Candidates(List<ItemData> pool, ShopCategory cat)
        => pool.FindAll(i => ShopPricing.CategoryOf(i) == cat && ShopPricing.Sellable(i));

    /// <summary>목록에서 1~2개를 빼고 나머지를 진열 (2개 이하이면 1개만 뺌, 1개뿐이면 그대로). 원래 순서 유지</summary>
    private void AddWithGaps(List<ItemData> items, int minStock, int maxStock, System.Random rng)
    {
        if (items.Count == 0) return;
        int gaps = items.Count <= 1 ? 0 : items.Count <= 3 ? 1 : rng.Next(1, 3);
        var skip = new HashSet<int>();
        while (skip.Count < gaps) skip.Add(rng.Next(items.Count));
        for (int i = 0; i < items.Count; i++)
            if (!skip.Contains(i)) Add(items[i], rng.Next(minStock, maxStock + 1));
    }

    private static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var tmp = list[i]; list[i] = list[j]; list[j] = tmp; }
    }

    private void Add(ItemData item, int stock)
    {
        offers.Add(new Offer { item = item, price = ShopPricing.BuyPrice(item), stock = stock, category = ShopPricing.CategoryOf(item) });
    }

    /// <summary>루팅 데이터에 들어 있는 모든 아이템 (중복 없이)</summary>
    public static List<ItemData> CollectItems()
    {
        var set = new HashSet<ItemData>();
        var db = LootDatabase.Instance;
        if (db != null)
        {
            FromTable(db.crateWood, set);
            FromTable(db.crateMetal, set);
            FromTable(db.crateMilitary, set);
            FromTable(db.crateMilitaryGuaranteed, set);
            foreach (var r in db.buildingRules) if (r != null) FromTable(r.table, set);
            foreach (var r in db.zombieDrops) if (r != null) FromTable(r.table, set);
        }
        var list = new List<ItemData>(set);
        list.Sort((a, b) => string.CompareOrdinal(a.itemName, b.itemName));   // 시드가 같으면 같은 진열대
        return list;
    }

    private static void FromTable(LootTable t, HashSet<ItemData> set)
    {
        if (t == null || t.entries == null) return;
        foreach (var e in t.entries) if (e != null && e.item != null) set.Add(e.item);
    }

    // ── 사기 ──────────────────────────────────────
    /// <summary>지금 가격 (영구 강화는 단계마다 오름, 최대면 0)</summary>
    public static int PriceOf(Offer o) => o.item is UpgradeItemData u ? PlayerUpgrades.NextPrice(u.upgrade) : o.price;

    public Result Buy(Offer o)
    {
        var money = PlayerMoney.Instance;
        if (o == null || money == null) return Result.NotEnoughMoney;
        if (o.SoldOut) return Result.SoldOut;

        if (o.item is UpgradeItemData up)
        {
            var ups = PlayerUpgrades.Instance;
            if (ups == null || ups.IsMaxed(up.upgrade)) return Result.Maxed;
            int price = PlayerUpgrades.NextPrice(up.upgrade);
            if (!money.TrySpend(price)) return Result.NotEnoughMoney;
            ups.LevelUp(up.upgrade);
            if (o.stock > 0) o.stock--;
            return Result.Ok;
        }

        if (!money.CanAfford(o.price)) return Result.NotEnoughMoney;

        if (o.item is AmmoData a)
        {
            var ammo = AmmoInventory.Instance;
            if (ammo == null) return Result.NoInventory;
            if (ammo.GetAmmo(a.weaponType) >= ammo.GetMax(a.weaponType)) return Result.AmmoFull;
            money.TrySpend(o.price);
            ammo.AddAmmo(a.weaponType, a.ammoAmount);
        }
        else
        {
            var inv = Object.FindFirstObjectByType<InventoryManager>();
            if (inv == null) return Result.NoInventory;
            money.TrySpend(o.price);
            bool added = o.item is WeaponData w ? inv.AddItemWithAmmo(w, 1, w.maxAmmo) : inv.AddItem(o.item, 1);
            if (!added)
            {
                money.Add(o.price);   // 환불
                return Result.BagFull;
            }
        }
        if (o.stock > 0) o.stock--;
        return Result.Ok;
    }

    // ── 팔기 (가방 칸에서 count개) ─────────────────
    /// <summary>가방 칸에서 count개를 판다. 판 개수를 돌려줌 (0이면 실패)</summary>
    public static int Sell(InventorySlot slot, int count, out int earned)
    {
        earned = 0;
        var money = PlayerMoney.Instance;
        var inv = Object.FindFirstObjectByType<InventoryManager>();
        if (slot == null || slot.item == null || money == null || inv == null) return 0;
        int price = ShopPricing.SellPrice(slot.item);
        int n = Mathf.Clamp(count, 1, Mathf.Max(1, slot.amount));
        string id = slot.slotId;
        for (int i = 0; i < n; i++) inv.RemoveItemById(id);   // 1개씩 빠지고, 다 팔리면 칸이 사라짐
        earned = price * n;
        money.Add(earned);
        return n;
    }
}
