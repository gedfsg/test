using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 상인 설정 (Resources/TraderSettings.asset). 메뉴 'Tools ▸ Shop ▸ 상인 설치'가 만든다.
///  - 모델: 시민 남자 9종 (Character/Floreswa) + 각 모델의 아바타
///  - 대기 동작: Mixamo 'unarmed idle looking' (두리번거리며 서 있기)
///  - 없으면 상인은 캡슐 모양으로 나온다 (기능은 같음)
/// </summary>
[CreateAssetMenu(fileName = "TraderSettings", menuName = "Shop/Trader Settings")]
public class TraderSettings : ScriptableObject
{
    public const string ResourcePath = "TraderSettings";

    [System.Serializable]
    public class Model
    {
        public GameObject prefab;
        public Avatar avatar;
    }

    [Header("모델")]
    public List<Model> models = new List<Model>();
    public RuntimeAnimatorController idleController;
    [Tooltip("모델 크기 배율 (모델이 너무 크거나 작으면 조절)")]
    public float modelScale = 1f;

    [Header("배치")]
    [Tooltip("맵에 동시에 있는 상인 수")]
    public int traderCount = 3;
    [Tooltip("새 날이 되면 상인들이 다른 곳으로 옮겨 가고 진열대도 새로 채운다")]
    public bool relocateEachDay = true;
    [Tooltip("플레이어(시작 위치)에서 최소 이만큼 떨어진 곳에 (m)")]
    public float minDistanceFromPlayer = 40f;
    [Tooltip("상인끼리 최소 거리 (m)")]
    public float minDistanceBetween = 120f;

    [Header("미니맵")]
    [Tooltip("아직 못 찾은 상인: 미니맵에 이 반경(m)의 '이 근처 어딘가' 원만 보임. 원 중심은 실제 위치에서 어긋나 있음")]
    public float hintRadius = 45f;
    [Tooltip("상인이 이 거리(m) 안에 들어오면 '발견' → 그날은 미니맵에 정확한 핀")]
    public float discoverRadius = 20f;

    [Header("상호작용")]
    [Tooltip("이 거리 안에서 F키로 상점을 연다 (m)")]
    public float interactRadius = 3f;

    private static TraderSettings cached;
    private static bool searched;

    public static TraderSettings Load()
    {
        if (!searched) { searched = true; cached = Resources.Load<TraderSettings>(ResourcePath); }
        return cached;
    }
}
