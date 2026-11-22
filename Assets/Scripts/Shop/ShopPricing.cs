using UnityEngine;

/// <summary>상점 분류 (구매 탭의 필터)</summary>
public enum ShopCategory { Ammo, Food, Medical, Weapon, Other, Upgrade, Throwable }   // 새 값은 맨 뒤에

/// <summary>
/// 상점 가격·분류·설명 (상점 2단계). 아이템마다 가격을 입력하지 않고 종류·등급으로 정한다.
///  돈 기준: 일반 좀비 1마리 ≈ $2 (PlayerMoney)
///   - 탄약: 한 묶음(AmmoData.ammoAmount발) × 발당 가격  (2026-11-22 2.5배: 권총 15발 $8, 소총 30발 $18, 산탄 8발 $12, 저격 5발 $12, 특수 40발 $40)
///   - 음식·물 / 치료: 등급별 기본값 (일반 $4 ~ 전설 $30), 치료는 1.5배
///     (2026-11-22) 돈이 남아서 생필품 구매가 × ConsumableMarkup(3) → 음식 $12 ~ $90, 치료 $18 ~ $135.
///      팔 때는 예전 값 기준 그대로 (비싸게 사서 비싸게 되파는 돈벌이가 안 되게)
///   - 총기: 등급별 (일반 $25 ~ 영웅 $110), 근접무기는 0.6배. 전설은 상점에서 안 팜 (팔기만 가능)
///   - 상점 전용 특수 무기 (WeaponDatabase.shopWeapons): 종류별 $140 ~ $240, 특수 탄약 40발 $40
///   - 투척: 수류탄 $20, 화염병 $15, 섬광탄 $15
///   - 판매가 = 구매가 × sellRatio (35%)
/// 숫자를 바꾸려면 아래 표만 고치면 된다.
/// </summary>
public static class ShopPricing
{
    public const float SellRatio = 0.35f;
    /// <summary>(2026-11-22) 음식·물·치료 구매가 배수. 판매가에는 안 붙음</summary>
    public const float ConsumableMarkup = 3f;

    // 발당 가격
    // private const float PistolRound = 0.2f, ARRound = 0.25f, ShotgunShell = 0.6f, SniperRound = 1.0f; private const float SpecialRound = 0.5f; // (이전)
    // (2026-11-22) 돈이 남아서 탄약 2.5배
    private const float PistolRound = 0.5f, ARRound = 0.6f, ShotgunShell = 1.5f, SniperRound = 2.5f;
    private const float SpecialRound = 1.0f;   // 특수 무기 공용 탄약 (화염방사기 연료·미니건·레이저·RPG)

    /// <summary>상점 전용 특수 무기 값</summary>
    public static int SpecialWeaponPrice(SpecialWeaponKind k)
    {
        switch (k)
        {
            case SpecialWeaponKind.Chainsaw: return 140;
            case SpecialWeaponKind.Flamethrower: return 180;
            case SpecialWeaponKind.Laser: return 200;
            case SpecialWeaponKind.Minigun: return 220;
            case SpecialWeaponKind.RPG: return 240;
            default: return 200;
        }
    }

    public static int ThrowablePrice(ThrowableData t)
    {
        switch (t.throwableType)
        {
            case ThrowableType.Grenade: return 20;
            default: return 15;   // 화염병·섬광탄
        }
    }

    private static int ByRarity(Rarity r, int common, int uncommon, int rare, int epic, int legendary)
    {
        switch (r)
        {
            case Rarity.Common: return common;
            case Rarity.Uncommon: return uncommon;
            case Rarity.Rare: return rare;
            case Rarity.Epic: return epic;
            case Rarity.Legendary: return legendary;
            default: return common;
        }
    }

    public static ShopCategory CategoryOf(ItemData item)
    {
        if (item is UpgradeItemData) return ShopCategory.Upgrade;
        if (item is ThrowableData) return ShopCategory.Throwable;
        if (item is AmmoData) return ShopCategory.Ammo;
        if (item is WeaponData) return ShopCategory.Weapon;
        if (item is ConsumableData c)
        {
            bool heal = c.healAmount > 0f || c.healPerSecond > 0f;
            bool food = c.foodAmount > 0f || c.waterAmount > 0f;
            if (food && !heal) return ShopCategory.Food;
            return ShopCategory.Medical;   // 치료 + 버프 약품 (진통제·아드레날린 등)
        }
        return ShopCategory.Other;
    }

    /// <summary>상인이 파는 값 (한 묶음/한 개). 팔지 않는 물건이면 0</summary>
    public static int BuyPrice(ItemData item)
    {
        if (item == null) return 0;
        if (item is UpgradeItemData up) return PlayerUpgrades.NextPrice(up.upgrade);   // 단계마다 오름 (최대면 0)
        switch (CategoryOf(item))
        {
            case ShopCategory.Ammo:
            {
                var a = (AmmoData)item;
                float per = ARRound;
                if (a.weaponType == WeaponType.Pistol || a.weaponType == WeaponType.SMG) per = PistolRound;
                else if (a.weaponType == WeaponType.Shotgun) per = ShotgunShell;
                else if (a.weaponType == WeaponType.Sniper) per = SniperRound;
                else if (a.weaponType == WeaponType.Ranged) per = SpecialRound;
                return Mathf.Max(1, Mathf.RoundToInt(per * Mathf.Max(1, a.ammoAmount)));
            }
            case ShopCategory.Food:
            case ShopCategory.Medical:
                return Mathf.RoundToInt(ConsumableBase(item) * ConsumableMarkup);   // (2026-11-22) 생필품 비싸게
            case ShopCategory.Weapon:
            {
                var w = (WeaponData)item;
                if (w.specialKind != SpecialWeaponKind.None) return SpecialWeaponPrice(w.specialKind);
                int p = ByRarity(item.rarity, 25, 40, 60, 110, 200);
                return w.type == WeaponType.Melee ? Mathf.RoundToInt(p * 0.6f) : p;
            }
            case ShopCategory.Throwable:
                return ThrowablePrice((ThrowableData)item);
            default:
                return ByRarity(item.rarity, 2, 3, 5, 9, 15);   // 재료·방어구 (팔기 기준용)
        }
    }

    /// <summary>상인에게 팔 때 받는 값 (한 개)</summary>
    // public static int SellPrice(ItemData item) => item == null ? 0 : Mathf.Max(1, Mathf.RoundToInt(BuyPrice(item) * SellRatio)); // (이전)
    public static int SellPrice(ItemData item)
    {
        if (item == null) return 0;
        var cat = CategoryOf(item);
        float basePrice = cat == ShopCategory.Food || cat == ShopCategory.Medical ? ConsumableBase(item) : BuyPrice(item);   // 생필품은 올리기 전 값으로
        return Mathf.Max(1, Mathf.RoundToInt(basePrice * SellRatio));
    }

    /// <summary>음식·물·치료의 원래(올리기 전) 값: 등급별 $4 ~ $30, 치료는 1.5배</summary>
    static float ConsumableBase(ItemData item)
    {
        float p = ByRarity(item.rarity, 4, 6, 10, 18, 30);
        return CategoryOf(item) == ShopCategory.Medical ? p * 1.5f : p;
    }

    /// <summary>
    /// 상인 진열대 총기 등급 확률 (총 한 자루를 고를 때 그 등급이 나올 비율, 합 100 아니어도 됨).
    /// 등급을 먼저 뽑고 그 등급 안에서는 같은 확률 → 등급에 총이 많아도 그 등급 전체 확률은 그대로
    /// </summary>
    public static float GunRarityWeight(Rarity r)
    {
        switch (r)
        {
            case Rarity.Common: return 50f;
            case Rarity.Uncommon: return 30f;
            case Rarity.Rare: return 15f;
            case Rarity.Epic: return 5f;
            default: return 0f;   // 전설은 상점에 안 나옴
        }
    }

    /// <summary>상인 진열대에 올라갈 수 있는 물건인지</summary>
    public static bool Sellable(ItemData item)
    {
        if (item == null) return false;
        var cat = CategoryOf(item);
        if (cat == ShopCategory.Other) return false;
        if (cat == ShopCategory.Weapon && item.rarity == Rarity.Legendary) return false;   // 전설 총기는 상자(사냥꾼)에서만
        return true;
    }

    public static string CategoryName(ShopCategory c)
    {
        switch (c)
        {
            case ShopCategory.Ammo: return "탄약";
            case ShopCategory.Food: return "음식·물";
            case ShopCategory.Medical: return "치료";
            case ShopCategory.Weapon: return "총기";
            case ShopCategory.Upgrade: return "강화";
            case ShopCategory.Throwable: return "투척";
            default: return "기타";
        }
    }

    public static string RarityName(Rarity r)
    {
        switch (r)
        {
            case Rarity.Common: return "일반";
            case Rarity.Uncommon: return "고급";
            case Rarity.Rare: return "희귀";
            case Rarity.Epic: return "영웅";
            case Rarity.Legendary: return "전설";
            default: return "";
        }
    }

    public static string WeaponTypeName(WeaponType t)
    {
        switch (t)
        {
            case WeaponType.Pistol: return "권총";
            case WeaponType.AR: return "돌격소총";
            case WeaponType.SMG: return "기관단총";
            case WeaponType.Shotgun: return "산탄총";
            case WeaponType.Sniper: return "저격소총";
            case WeaponType.Melee: return "근접무기";
            case WeaponType.Ranged: return "특수 무기";
            default: return "총기";
        }
    }

    /// <summary>상점에 보이는 이름. 탄약 에셋 이름이 영어(AR Ammo, Ammo…)라서 탄약만 한국어로 바꿔 보여 줌</summary>
    public static string DisplayName(ItemData item)
    {
        if (item is AmmoData a)
        {
            switch (a.weaponType)
            {
                case WeaponType.Pistol: return "권총탄";
                case WeaponType.SMG: return "기관단총탄";
                case WeaponType.AR: return "소총탄";
                case WeaponType.Shotgun: return "산탄";
                case WeaponType.Sniper: return "저격탄";
                case WeaponType.Ranged: return "특수 탄약";
            }
        }
        return item != null ? item.itemName : "";
    }

    /// <summary>목록에 쓰는 한 줄 설명</summary>
    public static string Describe(ItemData item)
    {
        if (item is UpgradeItemData up)
        {
            int lv = PlayerUpgrades.Instance != null ? PlayerUpgrades.Instance.Level(up.upgrade) : 0;
            return lv >= PlayerUpgrades.MaxLevel
                ? $"{PlayerUpgrades.EffectText(up.upgrade)} · 최대 단계"
                : $"{PlayerUpgrades.EffectText(up.upgrade)} · {lv}단계 → {lv + 1}단계 (영구)";
        }
        if (item is AmmoData a)
            return a.weaponType == WeaponType.Ranged
                ? $"화염방사기·미니건·레이저·RPG 공용 · {a.ammoAmount}발 한 묶음"
                : $"{WeaponTypeName(a.weaponType)}용 · {a.ammoAmount}발 한 묶음";
        if (item is WeaponData w)
        {
            var wdb = WeaponDatabase.Instance;
            string tag = wdb != null && wdb.IsShopOnly(w) ? "상점 전용" : RarityName(w.rarity);
            if (w.type == WeaponType.Melee) return $"{tag} · 근접 · 피해 {w.damage:0}";
            string dmg = w.type == WeaponType.Shotgun && w.pelletCount > 1 ? $"{w.damage:0}×{w.pelletCount}" : $"{w.damage:0}";
            return $"{tag} · {WeaponTypeName(w.type)} · 피해 {dmg} · 장탄 {w.maxAmmo}";
        }
        if (item is ThrowableData t)
        {
            switch (t.throwableType)
            {
                case ThrowableType.Grenade: return $"폭발 반경 {t.radius:0}m · 피해 {t.damage:0} · 신관 {t.fuseTime:0}초";
                case ThrowableType.Molotov: return $"불길 반경 {t.radius:0}m · {t.fireDuration:0}초간 타오름";
                default: return $"섬광 반경 {t.flashRadius:0}m · 기절 {t.stunDuration:0}초";
            }
        }
        if (item is ConsumableData c)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (c.healAmount > 0f) parts.Add($"체력 +{c.healAmount:0}");
            if (c.healPerSecond > 0f && c.healDuration > 0f) parts.Add($"{c.healDuration:0}초간 체력 +{c.healPerSecond * c.healDuration:0}");
            if (c.foodAmount > 0f) parts.Add($"포만감 +{c.foodAmount:0}%");
            if (c.waterAmount > 0f) parts.Add($"수분 +{c.waterAmount:0}%");
            if (c.staminaAmount > 0f) parts.Add("스태미나 회복");
            if (c.buffType == BuffType.SpeedBoost) parts.Add("이동속도 증가");
            if (c.buffType == BuffType.Adrenaline) parts.Add("아드레날린");
            return parts.Count > 0 ? string.Join(" · ", parts) : RarityName(c.rarity);
        }
        return RarityName(item.rarity);
    }
}
