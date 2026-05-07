using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

/// <summary>
/// PlayerAnimator Controller 자동 세팅 (Kevin Iglesias 애니메이션 기반)
/// Tools → Setup Player Animator
/// </summary>
public class PlayerAnimatorSetup : EditorWindow
{
    // Kevin Iglesias Male 애니메이션 경로
    const string KI = "Assets/Kevin Iglesias/Human Animations/Animations/Male";

    // Mixamo 구르기 경로 (Kevin Iglesias에 없음)
    const string MOTION = "Assets/Character/Motion";

    [MenuItem("Tools/Setup Player Animator")]
    public static void SetupAnimator()
    {
        string path = "Assets/Character/PlayerAnimator.controller";
        AnimatorController ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (ctrl == null) { Debug.LogError("PlayerAnimator.controller 없음!"); return; }

        // ── 1. 파라미터 초기화 ──────────────────────────────────────────
        ctrl.parameters = new AnimatorControllerParameter[0];
        ctrl.AddParameter("Horizontal", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Vertical",   AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Roll",       AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Shoot",      AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Reload",     AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("GetHit",     AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Death",      AnimatorControllerParameterType.Trigger);

        // ── 2. 기존 레이어 전체 초기화 ──────────────────────────────────
        var layers = ctrl.layers;
        for (int i = layers.Length - 1; i >= 1; i--)
            ctrl.RemoveLayer(i);

        var rootSM = ctrl.layers[0].stateMachine;
        foreach (var s in rootSM.states)        rootSM.RemoveState(s.state);
        foreach (var t in rootSM.anyStateTransitions) rootSM.RemoveAnyStateTransition(t);
        foreach (var sub in rootSM.stateMachines) rootSM.RemoveStateMachine(sub.stateMachine);

        // ── 3. 클립 로드 ────────────────────────────────────────────────
        // 대기
        AnimationClip idle     = Clip($"{KI}/Idles/HumanM@MilitaryIdle01.fbx");

        // 걷기 (8방향)
        AnimationClip walkFwd  = Clip($"{KI}/Movement/Walk/HumanM@Walk01_Forward.fbx");
        AnimationClip walkBwd  = Clip($"{KI}/Movement/Walk/HumanM@Walk01_Backward.fbx");
        AnimationClip walkL    = Clip($"{KI}/Movement/Walk/HumanM@Walk01_Left.fbx");
        AnimationClip walkR    = Clip($"{KI}/Movement/Walk/HumanM@Walk01_Right.fbx");
        AnimationClip walkFL   = Clip($"{KI}/Movement/Walk/HumanM@Walk01_ForwardLeft.fbx");
        AnimationClip walkFR   = Clip($"{KI}/Movement/Walk/HumanM@Walk01_ForwardRight.fbx");
        AnimationClip walkBL   = Clip($"{KI}/Movement/Walk/HumanM@Walk01_BackwardLeft.fbx");
        AnimationClip walkBR   = Clip($"{KI}/Movement/Walk/HumanM@Walk01_BackwardRight.fbx");

        // 달리기 (8방향)
        AnimationClip runFwd   = Clip($"{KI}/Movement/Run/HumanM@Run01_Forward.fbx");
        AnimationClip runBwd   = Clip($"{KI}/Movement/Run/HumanM@Run01_Backward.fbx");
        AnimationClip runL     = Clip($"{KI}/Movement/Run/HumanM@Run01_Left.fbx");
        AnimationClip runR     = Clip($"{KI}/Movement/Run/HumanM@Run01_Right.fbx");
        AnimationClip runFL    = Clip($"{KI}/Movement/Run/HumanM@Run01_ForwardLeft.fbx");
        AnimationClip runFR    = Clip($"{KI}/Movement/Run/HumanM@Run01_ForwardRight.fbx");
        AnimationClip runBL    = Clip($"{KI}/Movement/Run/HumanM@Run01_BackwardLeft.fbx");
        AnimationClip runBR    = Clip($"{KI}/Movement/Run/HumanM@Run01_BackwardRight.fbx");

        // 전투
        AnimationClip shoot    = Clip($"{KI}/Combat/Rifle/HumanM@Rifle_Aim01_Shoot01.fbx");
        AnimationClip reload   = Clip($"{KI}/Combat/Rifle/HumanM@Rifle_Reload01.fbx");
        AnimationClip getHit   = Clip($"{KI}/Combat/HumanM@Damage01.fbx");
        AnimationClip death    = Clip($"{KI}/Combat/HumanM@Death01.fbx");

        // 구르기 (Mixamo)
        AnimationClip roll     = Clip($"{MOTION}/Sprinting Forward Roll.fbx");

        // ── 4. 이동 블렌드 트리 (2D Freeform Directional) ───────────────
        var moveState = rootSM.AddState("Movement", new Vector3(250, 150));
        var tree = new BlendTree();
        AssetDatabase.AddObjectToAsset(tree, ctrl);
        tree.name            = "MovementTree";
        tree.blendType       = BlendTreeType.FreeformDirectional2D;
        tree.blendParameter  = "Horizontal";
        tree.blendParameterY = "Vertical";

        // 대기 (중앙)
        AddClip(tree, idle,    0,    0);

        // 걷기 (배율 1)
        AddClip(tree, walkFwd,  0,   1);
        AddClip(tree, walkBwd,  0,  -1);
        AddClip(tree, walkL,   -1,   0);
        AddClip(tree, walkR,    1,   0);
        AddClip(tree, walkFL,  -1,   1);
        AddClip(tree, walkFR,   1,   1);
        AddClip(tree, walkBL,  -1,  -1);
        AddClip(tree, walkBR,   1,  -1);

        // 달리기 (배율 2)
        AddClip(tree, runFwd,   0,   2);
        AddClip(tree, runBwd,   0,  -2);
        AddClip(tree, runL,    -2,   0);
        AddClip(tree, runR,     2,   0);
        AddClip(tree, runFL,   -2,   2);
        AddClip(tree, runFR,    2,   2);
        AddClip(tree, runBL,   -2,  -2);
        AddClip(tree, runBR,    2,  -2);

        moveState.motion    = tree;
        rootSM.defaultState = moveState;

        // ── 5. 사격 스테이트 ────────────────────────────────────────────
        var shootState = rootSM.AddState("Shoot", new Vector3(500, 0));
        shootState.motion = shoot;

        var toShoot = rootSM.AddAnyStateTransition(shootState);
        toShoot.AddCondition(AnimatorConditionMode.If, 0, "Shoot");
        toShoot.duration            = 0.05f;
        toShoot.canTransitionToSelf = false;

        var shootExit = shootState.AddTransition(moveState);
        shootExit.hasExitTime = true;
        shootExit.exitTime    = 0.9f;
        shootExit.duration    = 0.1f;

        // ── 6. 리로드 스테이트 ──────────────────────────────────────────
        var reloadState = rootSM.AddState("Reload", new Vector3(500, 150));
        reloadState.motion = reload;

        var toReload = rootSM.AddAnyStateTransition(reloadState);
        toReload.AddCondition(AnimatorConditionMode.If, 0, "Reload");
        toReload.duration            = 0.1f;
        toReload.canTransitionToSelf = false;

        var reloadExit = reloadState.AddTransition(moveState);
        reloadExit.hasExitTime = true;
        reloadExit.exitTime    = 0.9f;
        reloadExit.duration    = 0.15f;

        // ── 7. 구르기 스테이트 ──────────────────────────────────────────
        var rollState = rootSM.AddState("Roll", new Vector3(250, 300));
        rollState.motion = roll;

        var toRoll = rootSM.AddAnyStateTransition(rollState);
        toRoll.AddCondition(AnimatorConditionMode.If, 0, "Roll");
        toRoll.duration            = 0.05f;
        toRoll.canTransitionToSelf = false;

        var rollExit = rollState.AddTransition(moveState);
        rollExit.hasExitTime = true;
        rollExit.exitTime    = 0.9f;
        rollExit.duration    = 0.15f;

        // ── 8. 피격 스테이트 ────────────────────────────────────────────
        var hitState = rootSM.AddState("GetHit", new Vector3(500, 300));
        hitState.motion = getHit;

        var toHit = rootSM.AddAnyStateTransition(hitState);
        toHit.AddCondition(AnimatorConditionMode.If, 0, "GetHit");
        toHit.duration            = 0.05f;
        toHit.canTransitionToSelf = false;

        var hitExit = hitState.AddTransition(moveState);
        hitExit.hasExitTime = true;
        hitExit.exitTime    = 1f;
        hitExit.duration    = 0.1f;

        // ── 9. 사망 스테이트 ────────────────────────────────────────────
        var deathState = rootSM.AddState("Death", new Vector3(500, 450));
        deathState.motion = death;

        var toDeath = rootSM.AddAnyStateTransition(deathState);
        toDeath.AddCondition(AnimatorConditionMode.If, 0, "Death");
        toDeath.duration            = 0.1f;
        toDeath.canTransitionToSelf = false;

        // ── 저장 ────────────────────────────────────────────────────────
        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        Debug.Log("✅ PlayerAnimator 세팅 완료! (Kevin Iglesias 기반)");
    }

    static AnimationClip Clip(string fbxPath)
    {
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (a is AnimationClip c && !c.name.StartsWith("__preview__"))
                return c;
        Debug.LogWarning("클립 없음: " + fbxPath);
        return null;
    }

    static void AddClip(BlendTree tree, AnimationClip clip, float x, float y)
    {
        if (clip != null) tree.AddChild(clip, new Vector2(x, y));
    }
}
