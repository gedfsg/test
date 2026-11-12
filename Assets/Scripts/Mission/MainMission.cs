using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 진행 중인 메인 미션 1개 (런타임 상태). 정의는 MainMissionData.
/// 2026-11-12: 하루가 지나도 미완료 메인 미션을 버리지 않고 다음 날로 넘겨서 언제든 완료할 수 있게 함
///  → 예전엔 MissionManager가 "오늘 미션 1개" 상태(CurrentMain/MainProgress/hordeCenter/정찰 깃발 등)를 직접 들고 있었는데,
///    동시에 여러 개가 진행될 수 있어서 미션별 상태를 이 클래스로 옮김.
/// </summary>
public class MainMission
{
    public readonly MainMissionData data;
    public readonly int day;                 // 공개된 날
    public int progress;                     // 처치 수 / 정찰 수 / 대기 초 등
    public bool done;
    public string detail = "";               // HUD 미션명 옆 보조 표시 (예: "7/20")

    // 종류별 상태 (해당 종류만 사용)
    public readonly List<GameObject> objects = new List<GameObject>(); // 상호작용 오브젝트·깃발·좀비 떼 표시 (완료 후 다음 날 정리)
    public readonly List<KeyCardPickup> keyCards = new List<KeyCardPickup>();
    public readonly List<Transform> reconFlags = new List<Transform>();
    public readonly List<bool> reconVisited = new List<bool>();
    public Vector3 hordeCenter;
    public float radioSeconds;
    public Vector3? guideTarget;             // 미니맵 핀 / (꺼둔) 방향 화살표 대상

    public MainMission(MainMissionData data, int day) { this.data = data; this.day = day; }

    public MainMissionType Type => data.type;
    public string Title => data.title;
    public string ActionText => data.ActionText;
    public bool IsEscape => data.type == MainMissionType.Escape;

    /// <summary>월드에 놓은 미션 오브젝트 삭제 (완료된 미션을 다음 날 정리할 때)</summary>
    public void Cleanup(List<KeyCardPickup> worldCards)
    {
        foreach (var go in objects) if (go != null) Object.Destroy(go);
        objects.Clear();
        foreach (var k in keyCards)
        {
            worldCards.Remove(k);
            if (k != null) Object.Destroy(k.gameObject);
        }
        keyCards.Clear();
        reconFlags.Clear();
        reconVisited.Clear();
    }
}
