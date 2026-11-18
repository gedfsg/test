using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 지역·건물 이름표(월드 라벨)를 "미션 중일 때만" 보이게 (2026-11-18).
/// 예전엔 공원·라디오 타워·건물 입구(경찰서/병원/마트 등) 이름표가 항상 떠 있었음 → 평소엔 숨김.
/// 지금 진행 중인 미션 목표(미니맵 핀과 같은 MissionManager.GetMapPins - 메인 목표 / 발견된 장소형 서브) 위치에서
/// ShowRadius 안에 있는 이름표만 켬. 미션이 끝나면(핀이 사라지면) 다시 꺼짐.
/// 미션 중에만 생기는 표식(정찰 깃발·좀비 떼 지역 이름표)은 원래 미션 때 만들고 지우므로 여기 등록 안 함.
/// MissionManager.Start가 이 컴포넌트를 붙임 (MissionManager가 없는 씬이면 이름표는 계속 숨김).
/// </summary>
public class MissionLabels : MonoBehaviour
{
    public const float ShowRadius = 30f; // 목표 지점(건물 안 한 점)과 문 위 이름표 사이가 큰 건물에서 20m 넘게 떨어짐
    const float CheckInterval = 0.5f;

    struct Entry { public GameObject go; public Vector3 anchor; }
    static readonly List<Entry> labels = new List<Entry>();
    readonly List<MissionManager.MapPin> pins = new List<MissionManager.MapPin>();
    float nextCheck;

    /// <summary>이름표 등록 (바로 숨김). anchor = 그 장소 위치 (문·장소 중심)</summary>
    public static void Register(GameObject label, Vector3 anchor)
    {
        if (label == null) return;
        label.SetActive(false);
        labels.Add(new Entry { go = label, anchor = anchor });
    }

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + CheckInterval;
        pins.Clear();
        var mm = MissionManager.Instance;
        if (mm != null) mm.GetMapPins(pins);
        labels.RemoveAll(e => e.go == null); // 씬 다시 불러오면 예전 판 이름표는 사라짐
        foreach (var e in labels)
        {
            bool on = false;
            foreach (var p in pins)
            {
                float dx = p.pos.x - e.anchor.x, dz = p.pos.z - e.anchor.z;
                if (dx * dx + dz * dz <= ShowRadius * ShowRadius) { on = true; break; }
            }
            if (e.go.activeSelf != on) e.go.SetActive(on);
        }
    }
}
