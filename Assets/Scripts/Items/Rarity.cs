using UnityEngine;

// Uncommon은 나중에 추가해서 맨 뒤 (에셋에 int로 저장돼 있어서 중간에 넣으면 기존 Rare/Epic 값이 밀림)
public enum Rarity { Common, Rare, Epic, Legendary, Uncommon }

public static class RarityColors
{
    public static Color Get(Rarity r) => r switch
    {
        Rarity.Common    => new Color(0.8f, 0.8f, 0.8f),     // 회색
        Rarity.Uncommon  => new Color(0.35f, 0.85f, 0.35f),  // 초록
        Rarity.Rare      => new Color(0.3f, 0.6f, 1.0f),     // 파랑
        Rarity.Epic      => new Color(0.7f, 0.3f, 1.0f),     // 보라
        Rarity.Legendary => new Color(1.0f, 0.6f, 0.1f),     // 주황
        _ => Color.white
    };
}