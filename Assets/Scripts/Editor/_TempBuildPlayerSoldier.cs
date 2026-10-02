using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

// 일회성 스크립트 - 확인 후 삭제할 것
// Assets/Character/Player_Soldier/low_poly_soldiers_rigged_free.glb 안에는 병사 2종(스켈레톤 2개)이
// 들어있고, 그 중 Armature.001_329(= Scar 소총을 든 쪽, 헬멧/수염 있음)를 플레이어로 사용함.
// 애니메이션 클립이 0개라서(쇼케이스용 모델팩) 기존 PlayerAnimator.controller(Humanoid)를
// 그대로 재사용하기 위해 Blender Rigify의 DEF-* 본을 Unity Humanoid로 수동 매핑해서
// Avatar를 코드로 직접 빌드함. (손가락 포함 - 이 리그는 손가락 뼈까지 다 있음)
//
// 모델이 내보내질 때 축이 안 맞아서(Y-up 보정 누락으로 추정) 그대로 두면 바닥에 눕는다 -
// ModelCorrection 래퍼를 하나 끼워서 X축 -90도로 보정함(좀비 Police 방향 수정과 같은 패턴).
public static class TempBuildPlayerSoldier
{
    const string GlbPath = "Assets/Character/Player_Soldier/low_poly_soldiers_rigged_free.glb";
    const string OutDir = "Assets/Character/Player_Soldier/Generated/";
    const string ArmaturePath = "root/GLTF_SceneRootNode/Armature.001_329/GLTF_created_1";

    // 무기(Scar)는 제외하고 몸 파츠만 포함
    static readonly string[] BodyMeshNames = {
        "Object_182", "Object_184", "Object_188", "Object_190", "Object_192", "Object_194", "Object_196"
    };
    const string WeaponMeshName = "Object_186"; // Scar 소총 - 안 씀 (우리 게임 무기 사용)
    const string SkeletonRootName = "GLTF_created_1_rootJoint";

    static readonly Dictionary<string, string> BoneMap = new Dictionary<string, string>
    {
        { "DEF-pelvis_165", "Hips" },
        { "DEF-spine_166", "Spine" },
        { "DEF-Chest_167", "Chest" },
        { "DEF-neck_168", "Neck" },
        { "DEF-head_169", "Head" },
        { "DEF-shoulder.L_170", "LeftShoulder" },
        { "DEF-arm.L_171", "LeftUpperArm" },
        { "DEF-lowerarm.L_172", "LeftLowerArm" },
        { "DEF-hand.L_173", "LeftHand" },
        { "DEF-shoulder.R_282", "RightShoulder" },
        { "DEF-arm.R_283", "RightUpperArm" },
        { "DEF-lowerarm.R_284", "RightLowerArm" },
        { "DEF-hand.R_285", "RightHand" },
        { "DEF-leg.L_189", "LeftUpperLeg" },
        { "DEF-shin-L_190", "LeftLowerLeg" },
        { "DEF-foot.L_191", "LeftFoot" },
        { "DEF-toes.L_192", "LeftToes" },
        { "DEF-leg.R_276", "RightUpperLeg" },
        { "DEF-shin-R_277", "RightLowerLeg" },
        { "DEF-foot.R_278", "RightFoot" },
        { "DEF-toes.R_279", "RightToes" },
        // 왼손 손가락 (01-03=엄지, 04-06=검지, 07-09=중지, 010-012=약지, 013-015=새끼)
        { "DEF-finger01.L_174", "LeftThumbProximal" },
        { "DEF-finger02.L_175", "LeftThumbIntermediate" },
        { "DEF-finger03.L_176", "LeftThumbDistal" },
        { "DEF-finger04.L_177", "LeftIndexProximal" },
        { "DEF-finger05.L_178", "LeftIndexIntermediate" },
        { "DEF-finger06.L_179", "LeftIndexDistal" },
        { "DEF-finger07.L_180", "LeftMiddleProximal" },
        { "DEF-finger08.L_181", "LeftMiddleIntermediate" },
        { "DEF-finger09.L_182", "LeftMiddleDistal" },
        { "DEF-finger010.L_183", "LeftRingProximal" },
        { "DEF-finger011.L_184", "LeftRingIntermediate" },
        { "DEF-finger012.L_185", "LeftRingDistal" },
        { "DEF-finger013.L_186", "LeftLittleProximal" },
        { "DEF-finger014.L_187", "LeftLittleIntermediate" },
        { "DEF-finger015.L_188", "LeftLittleDistal" },
        // 오른손 손가락
        { "DEF-finger01.R_286", "RightThumbProximal" },
        { "DEF-finger02.R_287", "RightThumbIntermediate" },
        { "DEF-finger03.R_288", "RightThumbDistal" },
        { "DEF-finger04.R_289", "RightIndexProximal" },
        { "DEF-finger05.R_290", "RightIndexIntermediate" },
        { "DEF-finger06.R_291", "RightIndexDistal" },
        { "DEF-finger07.R_292", "RightMiddleProximal" },
        { "DEF-finger08.R_293", "RightMiddleIntermediate" },
        { "DEF-finger09.R_294", "RightMiddleDistal" },
        { "DEF-finger010.R_295", "RightRingProximal" },
        { "DEF-finger011.R_296", "RightRingIntermediate" },
        { "DEF-finger012.R_297", "RightRingDistal" },
        { "DEF-finger013.R_298", "RightLittleProximal" },
        { "DEF-finger014.R_299", "RightLittleIntermediate" },
        { "DEF-finger015.R_300", "RightLittleDistal" },
    };

    // (자식본, 부모본) - 이 GLB는 전부 평평하게(flat) root 밑에 바로 내보내져서
    // 실제 인체 계층구조(골반→척추→가슴→...)가 없음. Humanoid Avatar는 진짜
    // 부모-자식 계층(상위 본이 하위 본의 ancestor)을 요구하므로 수동으로 재구성함.
    // worldPositionStays=true로 옮겨서 bind pose의 월드 위치/회전은 그대로 유지.
    static readonly (string child, string parent)[] Reparent =
    {
        ("DEF-spine_166", "DEF-pelvis_165"),
        ("DEF-Chest_167", "DEF-spine_166"),
        ("DEF-neck_168", "DEF-Chest_167"),
        ("DEF-head_169", "DEF-neck_168"),

        ("DEF-shoulder.L_170", "DEF-Chest_167"),
        ("DEF-arm.L_171", "DEF-shoulder.L_170"),
        ("DEF-lowerarm.L_172", "DEF-arm.L_171"),
        ("DEF-hand.L_173", "DEF-lowerarm.L_172"),

        ("DEF-shoulder.R_282", "DEF-Chest_167"),
        ("DEF-arm.R_283", "DEF-shoulder.R_282"),
        ("DEF-lowerarm.R_284", "DEF-arm.R_283"),
        ("DEF-hand.R_285", "DEF-lowerarm.R_284"),

        ("DEF-leg.L_189", "DEF-pelvis_165"),
        ("DEF-shin-L_190", "DEF-leg.L_189"),
        ("DEF-foot.L_191", "DEF-shin-L_190"),
        ("DEF-toes.L_192", "DEF-foot.L_191"),

        ("DEF-leg.R_276", "DEF-pelvis_165"),
        ("DEF-shin-R_277", "DEF-leg.R_276"),
        ("DEF-foot.R_278", "DEF-shin-R_277"),
        ("DEF-toes.R_279", "DEF-foot.R_278"),

        // 손가락: 01-03=엄지 04-06=검지 07-09=중지 010-012=약지 013-015=새끼 (각 3마디 체인)
        ("DEF-finger01.L_174", "DEF-hand.L_173"), ("DEF-finger02.L_175", "DEF-finger01.L_174"), ("DEF-finger03.L_176", "DEF-finger02.L_175"),
        ("DEF-finger04.L_177", "DEF-hand.L_173"), ("DEF-finger05.L_178", "DEF-finger04.L_177"), ("DEF-finger06.L_179", "DEF-finger05.L_178"),
        ("DEF-finger07.L_180", "DEF-hand.L_173"), ("DEF-finger08.L_181", "DEF-finger07.L_180"), ("DEF-finger09.L_182", "DEF-finger08.L_181"),
        ("DEF-finger010.L_183", "DEF-hand.L_173"), ("DEF-finger011.L_184", "DEF-finger010.L_183"), ("DEF-finger012.L_185", "DEF-finger011.L_184"),
        ("DEF-finger013.L_186", "DEF-hand.L_173"), ("DEF-finger014.L_187", "DEF-finger013.L_186"), ("DEF-finger015.L_188", "DEF-finger014.L_187"),

        ("DEF-finger01.R_286", "DEF-hand.R_285"), ("DEF-finger02.R_287", "DEF-finger01.R_286"), ("DEF-finger03.R_288", "DEF-finger02.R_287"),
        ("DEF-finger04.R_289", "DEF-hand.R_285"), ("DEF-finger05.R_290", "DEF-finger04.R_289"), ("DEF-finger06.R_291", "DEF-finger05.R_290"),
        ("DEF-finger07.R_292", "DEF-hand.R_285"), ("DEF-finger08.R_293", "DEF-finger07.R_292"), ("DEF-finger09.R_294", "DEF-finger08.R_293"),
        ("DEF-finger010.R_295", "DEF-hand.R_285"), ("DEF-finger011.R_296", "DEF-finger010.R_295"), ("DEF-finger012.R_297", "DEF-finger011.R_296"),
        ("DEF-finger013.R_298", "DEF-hand.R_285"), ("DEF-finger014.R_299", "DEF-finger013.R_298"), ("DEF-finger015.R_300", "DEF-finger014.R_299"),
    };

    [MenuItem("Tools/Player/Build Soldier Model")]
    static void Run()
    {
        if (!AssetDatabase.IsValidFolder(OutDir))
            AssetDatabase.CreateFolder("Assets/Character/Player_Soldier", "Generated");

        var glbRoot = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
        if (glbRoot == null) { Debug.LogError("GLB를 못 찾음: " + GlbPath); return; }

        var instance = Object.Instantiate(glbRoot);
        var armature = instance.transform.Find(ArmaturePath);
        if (armature == null) { Debug.LogError("스켈레톤을 못 찾음: " + ArmaturePath); Object.DestroyImmediate(instance); return; }

        var skeletonRoot = armature.Find(SkeletonRootName);
        if (skeletonRoot == null) { Debug.LogError("스켈레톤 루트를 못 찾음: " + SkeletonRootName); Object.DestroyImmediate(instance); return; }

        // 새 루트 생성. (주의: 모델의 bind pose 축이 안 맞아 그대로 두면 눕는데,
        // 보정 회전을 아바타 빌드에 포함되는 하위 계층에 넣으면 리타겟 시 Mecanim이
        // 그 회전을 무시/리셋해버림 - 좀비 Police 때처럼 "Animator가 올라앉은
        // 오브젝트 자체"를 나중에 돌려야 함. 그래서 여기서는 보정 없이 네이티브
        // bind pose 그대로 두고, 통합 단계에서 이 프리팹의 루트 로컬 회전을
        // X축 -90도로 설정한다.)
        var root = new GameObject("PlayerSoldier");

        int moved = 0;
        foreach (var meshName in BodyMeshNames)
        {
            var meshGo = armature.Find(meshName);
            if (meshGo == null) { Debug.LogWarning("메시 못 찾음: " + meshName); continue; }
            meshGo.SetParent(root.transform, false);
            moved++;
        }
        skeletonRoot.SetParent(root.transform, false);

        // 안 쓰는 무기(Scar)는 원본 인스턴스에 남아있으니 그냥 버림(아래서 instance 전체 Destroy)

        // 평평한(flat) 본 구조를 실제 인체 계층구조로 재구성 (worldPositionStays=true로
        // bind pose의 월드 위치/회전은 그대로 유지한 채 부모만 바꿈)
        foreach (var pair in Reparent)
        {
            var child = FindDeep(skeletonRoot, pair.child);
            var parent = FindDeep(skeletonRoot, pair.parent);
            if (child == null || parent == null)
            {
                Debug.LogError("재배치 실패: child=" + pair.child + "(" + (child != null) + ") parent=" + pair.parent + "(" + (parent != null) + ")");
                Object.DestroyImmediate(instance); Object.DestroyImmediate(root);
                return;
            }
            child.SetParent(parent, true);
        }

        // Humanoid Avatar 빌드
        var human = new List<HumanBone>();
        foreach (var kv in BoneMap)
        {
            var t = FindDeep(skeletonRoot, kv.Key);
            if (t == null) { Debug.LogError("본 못 찾음: " + kv.Key); Object.DestroyImmediate(instance); Object.DestroyImmediate(root); return; }
            human.Add(new HumanBone { boneName = kv.Key, humanName = kv.Value, limit = new HumanLimit { useDefaultValues = true } });
        }

        var skeleton = new List<SkeletonBone>();
        CollectSkeleton(root.transform, skeleton);

        var desc = new HumanDescription
        {
            human = human.ToArray(),
            skeleton = skeleton.ToArray(),
            upperArmTwist = 0.5f, lowerArmTwist = 0.5f,
            upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
            armStretch = 0.05f, legStretch = 0.05f,
            feetSpacing = 0f, hasTranslationDoF = false,
        };

        var avatar = AvatarBuilder.BuildHumanAvatar(root, desc);
        avatar.name = "PlayerSoldier_Avatar";
        if (!avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError("Avatar 빌드 실패 isValid=" + avatar.isValid + " isHuman=" + avatar.isHuman);
            Object.DestroyImmediate(instance); Object.DestroyImmediate(root);
            return;
        }

        string avatarPath = OutDir + "PlayerSoldier_Avatar.asset";
        AssetDatabase.CreateAsset(avatar, avatarPath);

        // 애니메이터 (컨트롤러는 나중에 Player 교체 스크립트에서 연결)
        var animator = root.AddComponent<Animator>();
        animator.avatar = avatar;

        string prefabPath = OutDir + "PlayerSoldier.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

        Object.DestroyImmediate(root);
        Object.DestroyImmediate(instance);
        AssetDatabase.SaveAssets();

        Debug.Log("✅ PlayerSoldier 프리팹 생성 완료: " + prefabPath + " (몸 파츠 " + moved + "/7, Scar 무기 제외)");
    }

    static void CollectSkeleton(Transform t, List<SkeletonBone> list)
    {
        list.Add(new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
        foreach (Transform c in t) CollectSkeleton(c, list);
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            var r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }
}
