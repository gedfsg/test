using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Mixamo 애니메이션 전체 설정 (플레이어 + 좀비).
/// 메뉴: Tools/Animation/Setup Mixamo (All)  (다시 실행해도 같은 결과)
///  1) Character/Motion/Mixamo/**, Character/Zombie/Mixamo/** 의 FBX → Humanoid (Create From This Model)
///     idle/walk/run/crawl/strafe 은 Loop, 전부 제자리(루트 고정). 캐릭터 모델 파일(Parasite L Starkie)은 삭제
///  2) 플레이어 PlayerAnimator.controller
///     - Upper Body 레이어(상체 마스크, 가중치 1):
///       대기 자세 WeaponPose(Int) 0=도끼 1=권총 2=소총  ← 무기 바꾸면 PlayerController가 설정
///       한 번 재생: MeleeAttack / Reload / UseItem / WeaponSwitch / Hit (Trigger) → 끝나면 현재 무기 대기 자세로
///     - Base Layer: Dead(Bool) → 사망 (전신)
///  3) 좀비 ZombieAnimator.controller
///     - Attack(Trigger) + AttackIndex(Int 0/1) → 공격 / 물기 랜덤
///     - IsRunning(Bool) → 달리기 (러너), Hit(Trigger) → 비틀거림, Die(Trigger) → 사망
/// </summary>
public static class MixamoAxeSetup
{
    const string PlayerDir = "Assets/Character/Motion/Mixamo";
    const string ZombieDir = "Assets/Character/Zombie/Mixamo";
    const string PlayerControllerPath = "Assets/Character/PlayerAnimator.controller";
    const string ZombieControllerPath = "Assets/Character/Zombie/ZombieAnimator.controller";

    const string Axe = PlayerDir + "/ProMeleeAxePack/";
    const string Pistol = PlayerDir + "/PistolLocomotion/";
    const string Rifle = PlayerDir + "/Reloading/";

    public const string AttackTrigger = "MeleeAttack";
    public const string UpperLayer = "Upper Body";

    [MenuItem("Tools/Animation/Setup Mixamo (All)")]
    public static void Run()
    {
        int removed = RemoveCharacterModels();
        int n = SetupImports(PlayerDir) + SetupImports(ZombieDir);
        SetupPlayer();
        SetupZombie();
        Debug.Log($"[MixamoSetup] 모델 파일 {removed}개 삭제, FBX {n}개 Humanoid 설정, 플레이어/좀비 Animator 구성 완료");
    }

    // 예전 메뉴 이름 호환 (도끼만 하던 버전) → 전체 설정으로
    [MenuItem("Tools/Animation/Setup Mixamo Axe")]
    public static void RunLegacy() => Run();

    // Mixamo "With Skin" 캐릭터 모델 (애니메이션 아님, 각 15MB) 삭제
    static int RemoveCharacterModels()
    {
        int n = 0;
        foreach (var dir in new[] { PlayerDir, ZombieDir })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.GetFiles(dir, "Parasite L Starkie.fbx", SearchOption.AllDirectories))
                if (AssetDatabase.DeleteAsset(f.Replace('\\', '/'))) n++;
        }
        return n;
    }

    static int SetupImports(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        int n = 0;
        foreach (var path in Directory.GetFiles(dir, "*.fbx", SearchOption.AllDirectories))
        {
            var p = path.Replace('\\', '/');
            var imp = AssetImporter.GetAtPath(p) as ModelImporter;
            if (imp == null) continue;
            string lower = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
            bool loop = lower.Contains("idle") || lower.Contains("walk") || lower.Contains("run") || lower.Contains("crawl") || lower.Contains("strafe");

            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            var clips = imp.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.loopTime = loop;
                c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = true; // 제자리 동작
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
        Debug.LogError("[MixamoSetup] 클립 없음: " + fbx);
        return null;
    }

    static void EnsureParam(AnimatorController ac, string name, AnimatorControllerParameterType type)
    {
        if (System.Array.Find(ac.parameters, p => p.name == name) == null) ac.AddParameter(name, type);
    }

    // 다시 실행해도 중복 안 생기게 같은 이름 상태 + 그 상태로 가는 AnyState 전환 제거
    static void ClearStates(AnimatorStateMachine sm, params string[] names)
    {
        foreach (var t in sm.anyStateTransitions)
            if (t.destinationState == null || System.Array.IndexOf(names, t.destinationState.name) >= 0) sm.RemoveAnyStateTransition(t);
        foreach (var cs in sm.states)
            if (System.Array.IndexOf(names, cs.state.name) >= 0) sm.RemoveState(cs.state);
    }

    static AnimatorState State(AnimatorStateMachine sm, string name, AnimationClip clip, float playSeconds, Vector3 pos)
    {
        var s = sm.AddState(name, pos);
        s.motion = clip;
        if (clip != null && playSeconds > 0f) s.speed = clip.length / playSeconds; // 원하는 재생 시간에 맞춰 속도 조절
        return s;
    }

    // ── 플레이어 ──
    /// <summary>FBX 재임포트 없이 플레이어 Animator만 다시 구성 (클립 교체 시)</summary>
    [MenuItem("Tools/Animation/Rebuild Player Animator Only")]
    public static void SetupPlayerOnly()
    {
        SetupImports(PlayerDir + "/Drinking"); SetupImports(PlayerDir + "/Dying");
        SetupPlayer();
        Debug.Log("[MixamoSetup] 플레이어 Animator 재구성 완료");
    }

    static void SetupPlayer()
    {
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerControllerPath);
        if (ac == null) { Debug.LogError("[MixamoSetup] 플레이어 컨트롤러 없음"); return; }
        EnsureParam(ac, "WeaponPose", AnimatorControllerParameterType.Int);
        foreach (var t in new[] { AttackTrigger, "Reload", "UseItem", "Hit", "WeaponSwitch" }) EnsureParam(ac, t, AnimatorControllerParameterType.Trigger);
        EnsureParam(ac, "Dead", AnimatorControllerParameterType.Bool);

        var layers = ac.layers;
        int li = System.Array.FindIndex(layers, l => l.name == UpperLayer);
        if (li < 0) { Debug.LogError("[MixamoSetup] Upper Body 레이어 없음"); return; }
        var sm = layers[li].stateMachine;
        ClearStates(sm, "AxeIdle", "PistolIdle", "RifleIdle", "MeleeAttack", "Reload", "UseItem", "WeaponSwitch", "Hit");
        foreach (var t in sm.entryTransitions) sm.RemoveEntryTransition(t);

        // 대기 자세 3종 (WeaponPose로 선택)
        var idles = new[]
        {
            State(sm, "AxeIdle", Clip(Axe + "standing idle.fbx"), 0f, new Vector3(300, 0, 0)),
            State(sm, "PistolIdle", Clip(Pistol + "pistol idle.fbx"), 0f, new Vector3(300, 80, 0)),
            State(sm, "RifleIdle", Clip(Rifle + "rifle aiming idle.fbx"), 0f, new Vector3(300, 160, 0)),
        };
        sm.defaultState = idles[0];
        for (int i = 0; i < 3; i++)
        {
            var e = sm.AddEntryTransition(idles[i]);
            e.AddCondition(AnimatorConditionMode.Equals, i, "WeaponPose");
            for (int j = 0; j < 3; j++)
            {
                if (i == j) continue;
                var t = idles[i].AddTransition(idles[j]);
                t.hasExitTime = false; t.duration = 0.15f;
                t.AddCondition(AnimatorConditionMode.Equals, j, "WeaponPose");
            }
        }

        // 한 번 재생 동작 → 끝나면 Exit → Entry에서 WeaponPose에 맞는 대기 자세로
        void OneShot(string name, string trigger, AnimationClip clip, float seconds, bool canRepeat, float y)
        {
            var s = State(sm, name, clip, seconds, new Vector3(650, y, 0));
            var a = sm.AddAnyStateTransition(s);
            a.hasExitTime = false; a.duration = 0.05f; a.canTransitionToSelf = canRepeat;
            a.AddCondition(AnimatorConditionMode.If, 0, trigger);
            var x = s.AddExitTransition();
            x.hasExitTime = true; x.exitTime = 0.9f; x.duration = 0.1f;
        }
        OneShot("MeleeAttack", AttackTrigger, Clip(Axe + "standing melee attack horizontal.fbx"), 0.8f, true, 0);  // 원본 2.4초 → 0.8초 (쿨타임 0.5초)
        OneShot("Reload", "Reload", Clip(Rifle + "reloading.fbx"), 1.8f, false, 80);
        OneShot("UseItem", "UseItem", Clip(PlayerDir + "/Drinking/Drinking.fbx"), 3.0f, false, 160);             // 마시기 (원본 8.9초 → 3초, 상체만 → 걸으면서 사용 가능)
        OneShot("WeaponSwitch", "WeaponSwitch", Clip(PlayerDir + "/WeaponSwitch/Draw A Great Sword 1.fbx"), 0.6f, false, 240);
        OneShot("Hit", "Hit", Clip(Rifle + "hit reaction.fbx"), 0.6f, true, 320);

        layers[li].defaultWeight = 1f; // 무기 자세를 상체 레이어가 담당 (사망 시만 0)

        // Base Layer: 사망 (전신, Dying)
        var baseSm = layers[0].stateMachine;
        ClearStates(baseSm, "Dead");
        var dead = State(baseSm, "Dead", Clip(PlayerDir + "/Dying/Dying.fbx"), 0f, new Vector3(300, 250, 0));
        var toDead = baseSm.AddAnyStateTransition(dead);
        toDead.hasExitTime = false; toDead.duration = 0.1f; toDead.canTransitionToSelf = false;
        toDead.AddCondition(AnimatorConditionMode.If, 0, "Dead");

        ac.layers = layers;
        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
    }

    // ── 좀비 ──
    static void SetupZombie()
    {
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ZombieControllerPath);
        if (ac == null) { Debug.LogError("[MixamoSetup] 좀비 컨트롤러 없음"); return; }
        EnsureParam(ac, "Hit", AnimatorControllerParameterType.Trigger);
        EnsureParam(ac, "IsRunning", AnimatorControllerParameterType.Bool);
        EnsureParam(ac, "AttackIndex", AnimatorControllerParameterType.Int);

        var layers = ac.layers;
        var sm = layers[0].stateMachine;
        AnimatorState Find(string n) { foreach (var cs in sm.states) if (cs.state.name == n) return cs.state; return null; }
        var idle = Find("Idle"); var walk = Find("Walk");
        if (idle == null || walk == null) { Debug.LogError("[MixamoSetup] 좀비 Idle/Walk 상태 없음"); return; }
        ClearStates(sm, "Attack", "Attack2", "Run", "Hit", "Death");

        // 사망을 제일 먼저 확인 (다른 AnyState 전환보다 우선)
        var death = State(sm, "Death", Clip(ZombieDir + "/zombie death.fbx"), 0f, new Vector3(550, 250, 0));
        var ad = sm.AddAnyStateTransition(death); ad.hasExitTime = false; ad.duration = 0.1f; ad.canTransitionToSelf = false;
        ad.AddCondition(AnimatorConditionMode.If, 0, "Die");

        // 공격 2종 랜덤 (AttackIndex 0 = 공격, 1 = 물기)
        void Attack(string name, string fbx, int index, float y, float seconds)
        {
            var s = State(sm, name, Clip(ZombieDir + "/" + fbx), seconds, new Vector3(550, y, 0));
            var a = sm.AddAnyStateTransition(s);
            a.hasExitTime = false; a.duration = 0.1f; a.canTransitionToSelf = false;
            a.AddCondition(AnimatorConditionMode.If, 0, "Attack");
            a.AddCondition(AnimatorConditionMode.Equals, index, "AttackIndex");
            var b = s.AddTransition(idle); b.hasExitTime = true; b.exitTime = 0.9f; b.duration = 0.1f;
        }
        Attack("Attack", "zombie attack.fbx", 0, 0, 0f);
        Attack("Attack2", "zombie biting.fbx", 1, 80, 2.5f); // 원본 6.6초는 너무 길어서 2.5초로

        // 달리기 (러너)
        var run = State(sm, "Run", Clip(ZombieDir + "/zombie run.fbx"), 0f, new Vector3(300, 250, 0));
        var wr = walk.AddTransition(run); wr.hasExitTime = false; wr.duration = 0.15f; wr.AddCondition(AnimatorConditionMode.If, 0, "IsRunning");
        var rw = run.AddTransition(walk); rw.hasExitTime = false; rw.duration = 0.15f; rw.AddCondition(AnimatorConditionMode.IfNot, 0, "IsRunning");
        var ri = run.AddTransition(idle); ri.hasExitTime = false; ri.duration = 0.15f; ri.AddCondition(AnimatorConditionMode.IfNot, 0, "IsWalking");

        // 피격 비틀거림 (좀비 팩에 없어서 플레이어 팩의 hit reaction을 짧게)
        var hit = State(sm, "Hit", Clip(Rifle + "hit reaction.fbx"), 0.45f, new Vector3(550, 160, 0));
        var ah = sm.AddAnyStateTransition(hit); ah.hasExitTime = false; ah.duration = 0.05f; ah.canTransitionToSelf = true;
        ah.AddCondition(AnimatorConditionMode.If, 0, "Hit");
        var hi = hit.AddTransition(idle); hi.hasExitTime = true; hi.exitTime = 0.9f; hi.duration = 0.1f;

        ac.layers = layers;
        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
    }
}
