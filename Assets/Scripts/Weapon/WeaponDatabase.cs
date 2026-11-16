using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 무기 목록 한 곳에 모음 (2026-11-16, Resources/WeaponDatabase.asset).
///  - weapons     : 게임에 있는 모든 무기 (Liberator 30종 + 스나이퍼 2종 + 특수 5종)
///  - shopWeapons : 상점 교환 전용 특수 무기 5종 (화염방사기·전기톱·RPG·미니건·레이저). 루팅 테이블엔 없음 (드롭 0)
/// 상점 시스템(팀원 담당)은 WeaponDatabase.Instance.shopWeapons / Find("RPG") 로 꺼내 쓰면 됨.
/// 에셋은 Tools/Weapons/Build Sniper + Special Weapons 가 자동 생성·갱신.
/// </summary>
[CreateAssetMenu(fileName = "WeaponDatabase", menuName = "Inventory/Weapon Database")]
public class WeaponDatabase : ScriptableObject
{
    public List<WeaponData> weapons = new List<WeaponData>();
    [Tooltip("상점 교환 전용 (월드 배치·드롭 없음)")]
    public List<WeaponData> shopWeapons = new List<WeaponData>();

    static WeaponDatabase cached;
    public static WeaponDatabase Instance => cached != null ? cached : (cached = Resources.Load<WeaponDatabase>("WeaponDatabase"));

    /// <summary>이름(itemName 또는 에셋 이름)으로 찾기. 없으면 null</summary>
    public WeaponData Find(string name)
    {
        foreach (var w in weapons)
            if (w != null && (w.itemName == name || w.name == name)) return w;
        return null;
    }

    public bool IsShopOnly(WeaponData w) => w != null && shopWeapons.Contains(w);
}
