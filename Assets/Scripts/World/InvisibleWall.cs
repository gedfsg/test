using UnityEngine;

/// <summary>
/// 투명 벽 표시용 (2026-11-12). 플레이어·좀비 이동만 막고, 총알은 그냥 통과 (Bullet.TryProcessHit에서 무시).
/// 다리·경사로 가장자리 낙하 방지 벽(Tools → Map → Build Bridge Safety Walls)에 붙음.
/// </summary>
[DisallowMultipleComponent]
public class InvisibleWall : MonoBehaviour { }
