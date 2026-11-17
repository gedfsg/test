using UnityEngine;

/// <summary>상점 분류 (구매 탭의 필터)</summary>
public enum ShopCategory { Ammo, Food, Medical, Weapon, Other, Upgrade }

/// <summary>
/// 상점 가격·분류·설명 (상점 2단계). 아이템마다 가격을 입력하지 않고 종류·등급으로 정한다.
///  돈 기준: 일반 좀비 1마리 ≈ $2 (PlayerMoney)
///   - 탄약: 한 묶음(AmmoData.ammoAmount발) × 발당 가격  (권총탄 30발 ≈ $6)
///   - 음식·물 / 치료: 등급별 기본값 (일반 $4 ~ 전설 $30), 치료는 1.5배
///   - 총기: 등급별 (일반 $25 ~ 영웅 $110), 근접무기는 0.6배. 전설은 상점에서 안 팜 (팔기만 가능)
///   - 판매가 = 구매가 × sellRatio (35%)
/// 숫자를 바꾸려면 아래 표만 고치면 된다.
/// </summary>
public static class ShopPricing
{
    public const float SellRatio = 0.35f;

    // 발당 가격
    private const float PistolRound = 0.2f, ARRound = 0.25f, ShotgunShell = 0.6f, SniperRound = 1.0f;

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
                return Mathf.Max(1, Mathf.RoundToInt(per * Mathf.Max(1, a.ammoAmount)));
            }
            case ShopCategory.Food:
                return ByRarity(item.rarity, 4, 6, 10, 18, 30);
            case ShopCategory.Medical:
                return Mathf.RoundToInt(ByRarity(item.rarity, 4, 6, 10, 18, 30) * 1.5f);
            case ShopCategory.Weapon:
            {
                var w = (WeaponData)item;
                int p = ByRarity(item.rarity, 25, 40, 60, 110, 200);
                return w.type == WeaponType.Melee ? Mathf.RoundToInt(p * 0.6f) : p;
            }
            default:
                return ByRarity(item.rarity, 2, 3, 5, 9, 15);   // 재료·방어구 (팔기 기준용)
        }
    }

    /// <summary>상인에게 팔 때 받는 값 (한 개)</summary>
    public static int SellPrice(ItemData item) => item == null ? 0 : Mathf.Max(1, Mathf.RoundToInt(BuyPrice(item) * SellRatio));

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
            return $"{WeaponTypeName(a.weaponType)}용 · {a.ammoAmount}발 한 묶음";
        if (item is WeaponData w)
        {
            if (w.type == WeaponType.Melee) return $"{RarityName(w.rarity)} · 근접 · 피해 {w.damage:0}";
            string dmg = w.type == WeaponType.Shotgun && w.pelletCount > 1 ? $"{w.damage:0}×{w.pelletCount}" : $"{w.damage:0}";
            return $"{RarityName(w.rarity)} · {WeaponTypeName(w.type)} · 피해 {dmg} · 장탄 {w.maxAmmo}";
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
