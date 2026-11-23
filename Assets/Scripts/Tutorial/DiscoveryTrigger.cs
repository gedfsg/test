using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// (2026-11-23) "처음 발견" 대상 표시. 이 컴포넌트가 붙은 오브젝트에 플레이어가 detectRadius 안으로 오고
/// 화면에 보이면 TutorialManager가 해당 단계(stepId)를 한 번 띄움.
///  - 건물(경찰서·병원·마트·창고·교회·학교)·주유소는 TutorialManager가 게임 시작 때 자동으로 붙임 (씬 수정 없음)
///  - 좀비·아이템·상자처럼 계속 생기는 것은 붙이지 않고 TutorialManager가 종류별로 주변을 훑어서 찾음
///  - 그 밖의 오브젝트에 손으로 붙여서 새 안내를 만들 수도 있음 (stepId = TutorialStep 에셋의 stepId)
/// 거리 체크는 TutorialManager가 한꺼번에 함 (이 컴포넌트는 Update 없음 → 많이 붙여도 가벼움)
/// </summary>
public class DiscoveryTrigger : MonoBehaviour
{
    [Tooltip("띄울 단계 (TutorialStep.stepId)")]
    public string stepId;

    [Tooltip("플레이어가 이 거리(m) 안에 오면 발견")]
    public float detectRadius = 7f;

    [Tooltip("스포트라이트 중심 (월드). 비워 두면 이 오브젝트 위치")]
    public bool useFocusPoint;
    public Vector3 focusPoint;

    /// <summary>지금 켜져 있는 트리거 전부 (TutorialManager가 훑음)</summary>
    public static readonly List<DiscoveryTrigger> All = new List<DiscoveryTrigger>();

    public Vector3 Focus => useFocusPoint ? focusPoint : transform.position;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);
}
