using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Mixamo 애니메이션 전체 설정 (플레이어 + 좀비).
/// 메뉴: Tools/Animation/Setup Mixamo (All)  (다시 실행해도 같은 결과)
///  1) Character/Motion/Mixamo/**, Character/Zombie/Mixamo/** 의 FBX → Humanoid (Create From This Model)
///     idle/walk/run/crawl/strafe/sprint 은 Loop, 수평 이동은 루트로 빼서 몸이 제자리. 캐릭터 모델 파일(Parasite L Starkie)은 삭제
///  2) 플레이어 PlayerAnimator.controller
///     - Upper Body 레이어(상체 마스크, 가중치 1):
///       대기 자세 WeaponPose(Int) 0=도끼 1=권총 2=소총  ← 무기 바꾸면 PlayerController가 설정
///       한 번 재생: MeleeAttack / Reload / UseItem / WeaponSwitch / Hit (Trigger) → 끝나면 현재 무기 대기 자세로
///     - Base Layer: Dead(Bool) → 사망 (전신)
///  3) 좀비 ZombieAnimator.controller
///     - Attack(Trigger) + AttackIndex(Int 0/1) → 공격 / 목 물기(neck bite) 랜덤
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
        SetupLocomotion();
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
            bool loop = lower.Contains("idle") || lower.Contains("walk") || lower.Contains("run") || lower.Contains("crawl")
                     || lower.Contains("strafe") || lower.Contains("sprint");

            // 이미 원하는 설정이면 재임포트 생략 (전체 실행이 빨라짐)
            if (imp.animationType == ModelImporterAnimationType.Human && imp.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel
                && imp.clipAnimations.Length > 0 && System.Array.TrueForAll(imp.clipAnimations, c => c.loopTime == loop && !c.lockRootPositionXZ && c.lockRootRotation))
                continue;

            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            var clips = imp.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.loopTime = loop;
                // 수평 이동(XZ)은 포즈에 굽지 않음 → 이동량이 루트로 빠지고, 루트 이동은 안 쓰니(applyRootMotion 꺼짐) 몸이 항상 제자리.
                // (굽으면 달리기 한 사이클 동안 몸이 앞으로 쭉 나갔다가 루프 때 원위치로 튀어서 미끄러지듯 어색했음)
                c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = false;
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
        SetupImports(PlayerDir + "/Drinking"); SetupImports(PlayerDir + "/Dying"); SetupImports(PlayerDir + "/MeleeRun");
        SetupPlayer();
        SetupLocomotion();
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
            State(sm, "AxeIdle", Clip(Axe + "unarmed idle.fbx"), 0f, new Vector3(300, 0, 0)), // 도끼는 상체 레이어를 평소 끔(가중치 0) → 동작 끝나고 꺼지는 동안 섞여도 어색하지 않게 맨손 대기
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

        // 상체 레이어 마스크 = 팔(어깨~손)만. 몸통/머리/하체는 이동 트리 그대로 → 공격할 때 몸 전체가 돌아가지 않고 팔만 움직임
        var mask = layers[li].avatarMask;
        if (mask != null)
        {
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, false);
            foreach (var part in new[] { AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers, AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK })
                mask.SetHumanoidBodyPartActive(part, true);
            EditorUtility.SetDirty(mask);
        }

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

    // ── 하체 이동 블렌드 트리 (무기별) ──
    // Base Layer: 기존 "Blend Tree"(맨손) + Loco_Axe / Loco_Pistol / Loco_Rifle 을 WeaponPose로 전환 (3 = 맨손)
    // 블렌드 위치(Horizontal, Vertical)는 각 클립의 실제 루트 이동 방향으로 자동 배치: 걷기 = 반경 1, Shift 대쉬 = 반경 2
    // (Locomotion.cs가 걷기 ±1, 스프린트 ±2를 넣음)
    const string RunDir = PlayerDir + "/MeleeRun/"; // 소총 8방향 walk/run/sprint + 도끼용 Sword And Shield Run 이 섞여 있음 (파일명으로 구분)

    static void SetupLocomotion()
    {
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerControllerPath);
        if (ac == null) return;
        var layers = ac.layers;
        var sm = layers[0].stateMachine;

        AnimatorState unarmed = null;
        foreach (var cs in sm.states) if (cs.state.name == "Blend Tree") unarmed = cs.state;
        // 다시 실행 시 예전 트리 정리 (블렌드 트리는 컨트롤러 안의 하위 에셋이라 같이 지움)
        foreach (var cs in sm.states)
            if (cs.state.name.StartsWith("Loco_"))
            {
                if (cs.state.motion is BlendTree old) AssetDatabase.RemoveObjectFromAsset(old);
                sm.RemoveState(cs.state);
            }
        if (unarmed != null) foreach (var t in unarmed.transitions) if (t.destinationState == null || t.destinationState.name.StartsWith("Loco_")) unarmed.RemoveTransition(t);

        // 도끼: 전투 자세 없이 맨손처럼 (상체 레이어 평소 꺼짐 → 팔도 이 트리가 결정). 발 미끄러짐 줄이려고 클립 보폭 속도에 맞춰 재생 속도 조절
        // (캐릭터 humanScale 1.495 기준: unarmed run forward ≈ 4.1/s → 걷기 이동 5에 맞게 1.23배, Shift 대쉬 9에는 2배)
        const string Ch17 = "Assets/Character/testmotioh/Ch17_nonPBR@";
        var axe = TreeExplicit(ac, sm, "Loco_Axe", new Vector3(500, 300, 0), new (string, Vector2, float)[]
        {
            (Axe + "unarmed idle.fbx", Vector2.zero, 1f),
            (Axe + "unarmed run forward.fbx", new Vector2(0f, 1f), 1.23f),
            (Axe + "unarmed run back.fbx", new Vector2(0f, -1f), 1.5f),
            (Ch17 + "Walk Left.fbx", new Vector2(-1f, 0f), 2.0f),
            (Ch17 + "Walk Right.fbx", new Vector2(1f, 0f), 2.0f),
            (Ch17 + "Walk Forward Left.fbx", new Vector2(-0.7f, 0.7f), 2.0f),
            (Ch17 + "Walk Forward Right.fbx", new Vector2(0.7f, 0.7f), 2.0f),
            (Ch17 + "Walk Backward Left.fbx", new Vector2(-0.7f, -0.7f), 2.0f),
            (Ch17 + "Walk Backward Right.fbx", new Vector2(0.7f, -0.7f), 2.0f),
            (Axe + "unarmed run forward.fbx", new Vector2(0f, 2f), 2.0f),
            (Axe + "unarmed run back.fbx", new Vector2(0f, -2f), 2.2f),
        });
        var pistol = Tree(ac, sm, "Loco_Pistol", Pistol + "pistol idle.fbx",
            // arc(방향 전환하며 걷기)는 대각선 이동이 아니라 제외. strafe = 왼쪽, strafe (2) = 오른쪽 (루트 커브로 확인)
            new[] { Pistol + "pistol walk.fbx", Pistol + "pistol walk backward.fbx", Pistol + "pistol strafe.fbx", Pistol + "pistol strafe (2).fbx" },
            new[] { Pistol + "pistol run.fbx", Pistol + "pistol run backward.fbx" }, new Vector3(500, 380, 0));
        string[] dirs = { "forward", "backward", "left", "right", "forward left", "forward right", "backward left", "backward right" };
        var rifle = Tree(ac, sm, "Loco_Rifle", RunDir + "idle aiming.fbx",
            System.Array.ConvertAll(dirs, d => RunDir + "walk " + d + ".fbx"),
            System.Array.ConvertAll(dirs, d => RunDir + "sprint " + d + ".fbx"), new Vector3(500, 460, 0));

        var locos = new[] { axe, pistol, rifle };
        for (int i = 0; i < 3; i++)
        {
            if (unarmed != null) Link(unarmed, locos[i], i);
            for (int j = 0; j < 3; j++) if (i != j) Link(locos[i], locos[j], j);
            if (unarmed != null) Link(locos[i], unarmed, 3); // 맨손(3) → 기존 트리
        }
        sm.defaultState = axe; // 시작 무기 = 도끼

        ac.layers = layers;
        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
    }

    static void Link(AnimatorState from, AnimatorState to, int pose)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false; t.duration = 0.2f;
        t.AddCondition(AnimatorConditionMode.Equals, pose, "WeaponPose");
    }

    // 위치/재생속도를 직접 지정하는 블렌드 트리 (제자리 클립은 루트 이동이 없어서 방향 자동 판별이 안 됨)
    static AnimatorState TreeExplicit(AnimatorController ac, AnimatorStateMachine sm, string name, Vector3 pos, (string path, Vector2 at, float speed)[] entries)
    {
        var tree = new BlendTree { name = name, blendType = BlendTreeType.FreeformDirectional2D, blendParameter = "Horizontal", blendParameterY = "Vertical", hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(tree, ac);
        foreach (var (path, at, _) in entries) { var c = Clip(path); if (c != null) tree.AddChild(c, at); }
        var children = tree.children;
        int k = 0;
        foreach (var (path, at, speed) in entries) { if (Clip(path) == null) continue; children[k].timeScale = speed; k++; }
        tree.children = children;
        var state = sm.AddState(name, pos);
        state.motion = tree;
        return state;
    }

    static AnimatorState Tree(AnimatorController ac, AnimatorStateMachine sm, string name, string idle, string[] walks, string[] runs, Vector3 pos)
    {
        var tree = new BlendTree { name = name, blendType = BlendTreeType.FreeformDirectional2D, blendParameter = "Horizontal", blendParameterY = "Vertical", hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(tree, ac);
        var idleClip = Clip(idle); if (idleClip != null) tree.AddChild(idleClip, Vector2.zero);
        foreach (var f in walks) { var c = Clip(f); if (c != null) tree.AddChild(c, MoveDir(c) * 1f); }
        foreach (var f in runs) { var c = Clip(f); if (c != null) tree.AddChild(c, MoveDir(c) * 2f); }
        var state = sm.AddState(name, pos);
        state.motion = tree;
        return state;
    }

    // 클립의 실제 이동 방향 (루트 커브 시작→끝, 수평면). 캐릭터 기준 x = 오른쪽, z = 앞
    static Vector2 MoveDir(AnimationClip c)
    {
        float dx = 0f, dz = 0f;
        foreach (var b in AnimationUtility.GetCurveBindings(c))
        {
            if (b.propertyName != "RootT.x" && b.propertyName != "RootT.z") continue;
            var cv = AnimationUtility.GetEditorCurve(c, b);
            float d = cv.Evaluate(c.length) - cv.Evaluate(0f);
            if (b.propertyName == "RootT.x") dx = d; else dz = d;
        }
        var v = new Vector2(dx, dz);
        return v.sqrMagnitude > 1e-4f ? v.normalized : Vector2.up;
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
        // 두 공격 모두 1.3초 = 공격 쿨타임(1.5초)보다 짧게 → 동작 중간에 다음 공격이 끊고 들어오지 않음
        // (zombie biting은 바닥에 엎드려 뜯어먹는 동작이라 머리를 땅에 박아서 서서 덤벼드는 neck bite로 교체)
        Attack("Attack", "zombie attack.fbx", 0, 0, 1.3f);
        Attack("Attack2", "zombie neck bite.fbx", 1, 80, 1.3f);

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
