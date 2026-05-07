using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Linq;

/// <summary>
/// 유니티 상단 메뉴 Tools → 좀비 Animator 자동 생성 으로 실행
/// </summary>
public class ZombieAnimatorSetup : Editor
{
    [MenuItem("Tools/좀비 Animator 자동 생성")]
    public static void CreateZombieAnimator()
    {
        // ── 1. FBX에서 애니메이션 클립 로드 ──────────────────
        AnimationClip idleClip   = LoadClip("Assets/Character/Zombie/Animations/Zombie Idle.fbx");
        AnimationClip walkClip   = LoadClip("Assets/Character/Zombie/Animations/Parasite L Starkie@Zombie Walk.fbx");
        AnimationClip runClip    = LoadClip("Assets/Character/Zombie/Animations/Parasite L Starkie@Zombie Running.fbx");
        AnimationClip attackClip = LoadClip("Assets/Character/Zombie/Animations/Zombie Attack.fbx");
        AnimationClip deathClip  = LoadClip("Assets/Character/Zombie/Animations/Zombie Death.fbx");

        // 로드 실패 시 경고 출력
        if (idleClip   == null) Debug.LogWarning("[ZombieSetup] Zombie Idle 클립을 찾을 수 없음");
        if (walkClip   == null) Debug.LogWarning("[ZombieSetup] Zombie Walk 클립을 찾을 수 없음");
        if (runClip    == null) Debug.LogWarning("[ZombieSetup] Zombie Running 클립을 찾을 수 없음");
        if (attackClip == null) Debug.LogWarning("[ZombieSetup] Zombie Attack 클립을 찾을 수 없음");
        if (deathClip  == null) Debug.LogWarning("[ZombieSetup] Zombie Death 클립을 찾을 수 없음");

        // ── 2. AnimatorController 생성 ────────────────────────
        string savePath = "Assets/Character/Zombie/ZombieAnimator.controller";
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(savePath);

        // ── 3. 파라미터 추가 ──────────────────────────────────
        controller.AddParameter("Speed",  AnimatorControllerParameterType.Float);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("IsDead", AnimatorControllerParameterType.Bool);

        // ── 4. 상태(State) 생성 ───────────────────────────────
        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        AnimatorState idleState   = sm.AddState("Idle");
        AnimatorState walkState   = sm.AddState("Walk");
        AnimatorState runState    = sm.AddState("Run");
        AnimatorState attackState = sm.AddState("Attack");
        AnimatorState deadState   = sm.AddState("Dead");

        // ── 5. 애니메이션 클립 연결 ───────────────────────────
        if (idleClip   != null) idleState.motion   = idleClip;
        if (walkClip   != null) walkState.motion   = walkClip;
        if (runClip    != null) runState.motion    = runClip;
        if (attackClip != null) attackState.motion = attackClip;
        if (deathClip  != null) deadState.motion   = deathClip;

        // 기본 상태는 Idle
        sm.defaultState = idleState;

        // ── 6. 트랜지션(전환 조건) 설정 ──────────────────────

        // Idle ↔ Walk (정규화된 Speed 0~1 기준)
        AddTransition(idleState, walkState, "Speed", 0.01f, AnimatorConditionMode.Greater);
        AddTransition(walkState, idleState, "Speed", 0.01f, AnimatorConditionMode.Less);

        // Walk ↔ Run (정규화된 Speed 0~1 기준)
        AddTransition(walkState, runState,  "Speed", 0.6f,  AnimatorConditionMode.Greater);
        AddTransition(runState,  walkState, "Speed", 0.6f,  AnimatorConditionMode.Less);

        // AnyState → Attack (Attack 트리거)
        AnimatorStateTransition attackTr = sm.AddAnyStateTransition(attackState);
        attackTr.AddCondition(AnimatorConditionMode.If, 0, "Attack");
        attackTr.hasExitTime = false;
        attackTr.duration    = 0.1f;

        // Attack → Idle (모션 끝나면 자동 복귀)
        AnimatorStateTransition attackExit = attackState.AddExitTransition();
        attackExit.hasExitTime = true;
        attackExit.exitTime    = 0.9f;
        attackExit.duration    = 0.15f;

        // AnyState → Dead (IsDead = true)
        AnimatorStateTransition deadTr = sm.AddAnyStateTransition(deadState);
        deadTr.AddCondition(AnimatorConditionMode.If, 0, "IsDead");
        deadTr.hasExitTime = false;
        deadTr.duration    = 0.1f;

        // ── 7. 저장 ───────────────────────────────────────────
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[ZombieSetup] ZombieAnimator.controller 생성 완료 → " + savePath);
        EditorUtility.DisplayDialog(
            "완료",
            "ZombieAnimator.controller 생성 완료!\n\nAssets/Character/Zombie/ 에 저장됨\n\n다음: 좀비 오브젝트의 Animator 컴포넌트에 드래그하세요.",
            "확인"
        );
    }

    // FBX 파일에서 AnimationClip을 꺼내는 헬퍼 함수
    private static AnimationClip LoadClip(string path)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        // __preview__ 같은 내부 클립 제외
        return assets.OfType<AnimationClip>()
                     .FirstOrDefault(c => !c.name.StartsWith("__"));
    }

    // 트랜지션 추가 헬퍼 함수
    private static void AddTransition(AnimatorState from, AnimatorState to,
                                      string param, float threshold,
                                      AnimatorConditionMode mode)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.AddCondition(mode, threshold, param);
        t.hasExitTime = false;
        t.duration    = 0.15f;
    }
}
