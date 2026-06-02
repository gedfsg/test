using UnityEngine;

public enum Rarity { Common, Rare, Epic, Legendary }

public static class RarityColors
{
    public static Color Get(Rarity r) => r switch
    {
        Rarity.Common    => new Color(0.8f, 0.8f, 0.8f),     // 회색
        Rarity.Rare      => new Color(0.3f, 0.6f, 1.0f),     // 파랑
        Rarity.Epic      => new Color(0.7f, 0.3f, 1.0f),     // 보라
        Rarity.Legendary => new Color(1.0f, 0.6f, 0.1f),     // 주황
        _ => Color.white
    };
}