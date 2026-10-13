using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Mixamo Pro Melee Axe Pack 설정 + 플레이어 Animator에 도끼 상체 동작 추가.
/// 메뉴: Tools/Animation/Setup Mixamo Axe (다시 실행해도 같은 결과)
///  1) Assets/Character/Motion/Mixamo/**/*.fbx → Humanoid 리그 (Create From This Model), idle/walk/run은 Loop
///  2) PlayerAnimator.controller "Upper Body" 레이어(상체 마스크)에
///     AxeIdle(standing idle, 기본) ⇄ MeleeAttack(standing melee attack horizontal) + MeleeAttack 트리거
///     레이어 기본 가중치 0 - 도끼 들 때만 PlayerController가 1로 올림 (총 자세는 기존 그대로)
/// </summary>
public static class MixamoAxeSetup
{
    const string Dir = "Assets/Character/Motion/Mixamo";
    const string ControllerPath = "Assets/Character/PlayerAnimator.controller";
    const string IdleFbx = Dir + "/ProMeleeAxePack/standing idle.fbx";
    const string AttackFbx = Dir + "/ProMeleeAxePack/standing melee attack horizontal.fbx";
    public const string AttackTrigger = "MeleeAttack";
    public const string UpperLayer = "Upper Body";
    const float AttackDuration = 0.8f; // 휘두르기 재생 시간 (원본 2.4초는 너무 느리고 0.55초는 너무 빠름). 0.7 지점부터 연타 가능 ≈ 쿨타임 0.5초

    [MenuItem("Tools/Animation/Setup Mixamo Axe")]
    public static void Run()
    {
        int n = SetupImports();
        SetupController();
        Debug.Log($"[MixamoAxeSetup] FBX {n}개 Humanoid 설정 + Upper Body 레이어에 AxeIdle/MeleeAttack 추가");
    }

    static int SetupImports()
    {
        int n = 0;
        foreach (var path in Directory.GetFiles(Dir, "*.fbx", SearchOption.AllDirectories)) // 하위 폴더(도끼/권총/재장전 팩) 전부
        {
            var p = path.Replace('\\', '/');
            var imp = AssetImporter.GetAtPath(p) as ModelImporter;
            if (imp == null) continue;
            string lower = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
            bool loop = lower.Contains("idle") || lower.Contains("walk") || lower.Contains("run ");

            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            var clips = imp.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.loopTime = loop;
                c.lockRootRotation = true;      // 제자리 동작 (루트 회전/높이/위치 고정)
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = true;
            }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
            n++;
        }
        return n;
    }

    static AnimationClip Clip(string fbx)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx))
            if (o is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
        return null;
    }

    static void SetupController()
    {
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var idle = Clip(IdleFbx); var attack = Clip(AttackFbx);
        if (ac == null || idle == null || attack == null) { Debug.LogError($"[MixamoAxeSetup] 컨트롤러/클립 없음 ctrl={ac} idle={idle} attack={attack}"); return; }

        if (System.Array.Find(ac.parameters, p => p.name == AttackTrigger) == null)
            ac.AddParameter(AttackTrigger, AnimatorControllerParameterType.Trigger);

        var layers = ac.layers;
        int li = System.Array.FindIndex(layers, l => l.name == UpperLayer);
        if (li < 0) { Debug.LogError("[MixamoAxeSetup] Upper Body 레이어 없음"); return; }
        var sm = layers[li].stateMachine;

        // 다시 실행해도 중복 안 생기게 같은 이름 상태 제거
        foreach (var cs in sm.states)
            if (cs.state.name == "AxeIdle" || cs.state.name == "MeleeAttack") sm.RemoveState(cs.state);

        var idleState = sm.AddState("AxeIdle", new Vector3(300, 100, 0));
        idleState.motion = idle;
        var atk = sm.AddState("MeleeAttack", new Vector3(300, 220, 0));
        atk.motion = attack;
        atk.speed = attack.length / AttackDuration; // 쿨타임 안에 끝나게 빨리 재생
        sm.defaultState = idleState;

        var toAtk = idleState.AddTransition(atk);
        toAtk.hasExitTime = false; toAtk.duration = 0.05f;
        toAtk.AddCondition(AnimatorConditionMode.If, 0, AttackTrigger);
        var again = atk.AddTransition(atk); // 연타: 휘두르는 도중 다시 누르면 처음부터
        again.hasExitTime = true; again.exitTime = 0.7f; again.duration = 0.05f;
        again.AddCondition(AnimatorConditionMode.If, 0, AttackTrigger);
        var back = atk.AddTransition(idleState);
        back.hasExitTime = true; back.exitTime = 0.9f; back.duration = 0.1f;

        layers[li].defaultWeight = 0f; // 도끼 들 때만 1 (PlayerController)
        ac.layers = layers;
        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
    }
}
