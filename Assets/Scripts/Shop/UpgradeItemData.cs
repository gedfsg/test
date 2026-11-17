using UnityEngine;

/// <summary>영구 강화 종류 (상점 4단계)</summary>
public enum UpgradeType { MoveSpeed, StaminaMax, StaminaRegen, MaxHealth }

/// <summary>
/// 상점 진열대에 올라가는 '영구 강화' 상품. 게임 중에 PlayerUpgrades가 만들어 쓰는 데이터 (에셋 아님).
/// 사면 바로 적용되고 가방을 차지하지 않음
/// </summary>
public class UpgradeItemData : ItemData
{
    public UpgradeType upgrade;
}
