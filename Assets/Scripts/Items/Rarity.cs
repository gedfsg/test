using UnityEngine;

// Uncommon은 나중에 추가해서 맨 뒤 (에셋에 int로 저장돼 있어서 중간에 넣으면 기존 Rare/Epic 값이 밀림)
public enum Rarity { Common, Rare, Epic, Legendary, Uncommon }

public static class RarityColors
{
    // (이전) Common (0.8,0.8,0.8) / Rare (0.3,0.6,1) / Epic (0.7,0.3,1) / Legendary (1,0.6,0.1)
    // 2026-11-12: 인벤토리·핫바 등급 테두리 색 통일 (스펙 HEX)
    public static Color Get(Rarity r) => r switch
    {
        Rarity.Common    => new Color32(0xAA, 0xAA, 0xAA, 0xFF), // 회색 #AAAAAA
        Rarity.Uncommon  => new Color(0.35f, 0.85f, 0.35f),      // 초록 (스펙에 없음 - 기존 유지)
        Rarity.Rare      => new Color32(0x4A, 0x90, 0xE2, 0xFF), // 파랑 #4A90E2
        Rarity.Epic      => new Color32(0x9B, 0x59, 0xB6, 0xFF), // 보라 #9B59B6
        Rarity.Legendary => new Color32(0xF3, 0x9C, 0x12, 0xFF), // 주황 #F39C12
        _ => Color.white
    };
}