using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

// 일회성 스크립트 - 확인 후 삭제할 것
// "Super Low-poly Stylized Zombies" 팩(Assets/Character/Zombie/Stylized/)에는
// 모델만 있고 Walk/Idle/Attack 애니메이션이 없어서(CameraAction 프리뷰 클립 1개뿐),
// 기존 ZombieMale/Female용 ZombieAnimator.controller(Humanoid)를 그대로 재사용하기 위해
// CC_Base_* 본 구조를 Humanoid로 수동 매핑해서 Avatar를 코드로 직접 빌드함.
public static class TempBuildStylizedZombies
{
    const string FbxPath = "Assets/Character/Zombie/Stylized/Zombies stylized concatenated.fbx";
    const string TexDir  = "Assets/Character/Zombie/Stylized/textures/";
    const string OutDir  = "Assets/Character/Zombie/Stylized/Generated/";
    const string ControllerPath = "Assets/Character/Zombie/ZombieAnimator.controller";

    class ZombieDef
    {
        public string prefabName;
        public string meshChildName;   // FBX 루트 밑의 메시 오브젝트 이름
        public string diffuseTex, normalTex;
        public float maxHealth, moveSpeed, attackRange, attackDamage;
    }

    static readonly ZombieDef[] Defs = {
        new ZombieDef {
            prefabName = "Zombie_Normal", meshChildName = "Cartoon_zombie1",
            diffuseTex = "Cartoon_zombie1_Bake_Diffuse", normalTex = "Cartoon_zombie1_Bake_Normal",
            maxHealth = 50f, moveSpeed = 2f, attackRange = 1.8f, attackDamage = 10f,
        },
        new ZombieDef {
            prefabName = "Zombie_Fat", meshChildName = "Cartoon_Zombie8_obese_man",
            diffuseTex = "Cartoon_Zombie8_obese_man_Bake_Diffuse", normalTex = "Cartoon_Zombie8_obese_man_Bake_Normal",
            maxHealth = 400f, moveSpeed = 1f, attackRange = 2.5f, attackDamage = 30f,
        },
        new ZombieDef {
            prefabName = "Zombie_Police", meshChildName = "Obese_Police_Zombie1",
            diffuseTex = "Obese_Police_Zombie1_Bake_Diffuse", normalTex = "Obese_Police_Zombie1_Bake_Normal",
            maxHealth = 80f, moveSpeed = 2.5f, attackRange = 1.8f, attackDamage = 15f,
        },
        new ZombieDef {
            prefabName = "Zombie_Girl", meshChildName = "Cartoon_zombie5_female",
            diffuseTex = "Cartoon_zombie5_female_Bake_Diffuse", normalTex = "Cartoon_zombie5_female_Bake_Normal",
            maxHealth = 40f, moveSpeed = 3f, attackRange = 1.8f, attackDamage = 8f,
        },
    };

    // CC_Base_* 본 이름 → Unity Humanoid 표준 본 이름
    static readonly Dictionary<string, string> BoneMap = new Dictionary<string, string>
    {
        { "CC_Base_Hip", "Hips" },
        { "CC_Base_Waist", "Spine" },
        { "CC_Base_Spine01", "Chest" },
        { "CC_Base_Spine02", "UpperChest" },
        { "CC_Base_NeckTwist01", "Neck" },
        { "CC_Base_Head", "Head" },
        { "CC_Base_L_Clavicle", "LeftShoulder" },
        { "CC_Base_L_Upperarm", "LeftUpperArm" },
        { "CC_Base_L_Forearm", "LeftLowerArm" },
        { "CC_Base_L_Hand", "LeftHand" },
        { "CC_Base_R_Clavicle", "RightShoulder" },
        { "CC_Base_R_Upperarm", "RightUpperArm" },
        { "CC_Base_R_Forearm", "RightLowerArm" },
        { "CC_Base_R_Hand", "RightHand" },
        { "CC_Base_L_Thigh", "LeftUpperLeg" },
        { "CC_Base_L_Calf", "LeftLowerLeg" },
        { "CC_Base_L_Foot", "LeftFoot" },
        { "CC_Base_R_Thigh", "RightUpperLeg" },
        { "CC_Base_R_Calf", "RightLowerLeg" },
        { "CC_Base_R_Foot", "RightFoot" },
    };

    [MenuItem("Tools/Zombie/Build Stylized Zombie Prefabs (4종)")]
    static void Run()
    {
        if (!AssetDatabase.IsValidFolder(OutDir))
            AssetDatabase.CreateFolder("Assets/Character/Zombie/Stylized", "Generated");

        var fbxRoot = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (fbxRoot == null) { Debug.LogError("FBX를 못 찾음: " + FbxPath); return; }
        if (controller == null) { Debug.LogError("ZombieAnimator.controller를 못 찾음: " + ControllerPath); return; }

        // FBX 전체를 씬에 임시로 인스턴스화 (메시의 SkinnedMeshRenderer.bones가 올바른 스켈레톤을 참조하게 하려면
        // 메시와 해당 RL_BoneRoot를 같은 인스턴스 안에서 떼어내야 함)
        var fbxInstance = Object.Instantiate(fbxRoot);

        var created = new List<string>();
        foreach (var def in Defs)
        {
            string path = BuildOne(fbxInstance, def, controller);
            if (path != null) created.Add(path);
        }

        Object.DestroyImmediate(fbxInstance);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("✅ 스타일라이즈 좀비 프리팹 " + created.Count + "개 생성:\n" + string.Join("\n", created));
    }

    static string BuildOne(GameObject fbxInstance, ZombieDef def, AnimatorController controller)
    {
        var meshSrc = fbxInstance.transform.Find(def.meshChildName);
        if (meshSrc == null) { Debug.LogError(def.prefabName + ": 메시 '" + def.meshChildName + "' 못 찾음"); return null; }
        var smr = meshSrc.GetComponent<SkinnedMeshRenderer>();
        if (smr == null) { Debug.LogError(def.prefabName + ": SkinnedMeshRenderer 없음"); return null; }

        // 이 메시가 실제로 참조하는 스켈레톤(RL_BoneRoot N) 찾기
        var boneRoot = smr.rootBone;
        while (boneRoot.parent != fbxInstance.transform) boneRoot = boneRoot.parent;

        // 새 루트를 만들고 메시+스켈레톤을 그 밑으로 옮김 (참조는 Transform 객체라서 안 끊김)
        var root = new GameObject(def.prefabName);
        meshSrc.SetParent(root.transform, false);
        meshSrc.name = "body";
        boneRoot.SetParent(root.transform, false);

        // 머티리얼 생성 (Diffuse + Normal)
        var mat = BuildMaterial(def);
        smr.sharedMaterial = mat;

        // Humanoid Avatar 빌드
        var avatar = BuildHumanoidAvatar(root, boneRoot, def.prefabName);
        if (avatar == null) { Object.DestroyImmediate(root); return null; }

        var animator = root.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        var agent = root.AddComponent<UnityEngine.AI.NavMeshAgent>();
        agent.radius = 0.4f;
        agent.height = 1.8f;

        var col = root.AddComponent<CapsuleCollider>();
        col.height = 1.8f;
        col.radius = 0.5f;
        col.center = new Vector3(0, 0.9f, 0);

        var zc = root.AddComponent<ZombieController>();
        zc.maxHealth = def.maxHealth;
        zc.moveSpeed = def.moveSpeed;
        zc.attackRange = def.attackRange;
        zc.attackDamage = def.attackDamage;

        string prefabPath = OutDir + def.prefabName + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        return prefabPath;
    }

    static Material BuildMaterial(ZombieDef def)
    {
        var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + def.diffuseTex + ".png");
        var normal  = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + def.normalTex + ".png");

        var shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = def.prefabName + "_Mat" };
        if (diffuse != null) mat.SetTexture("_MainTex", diffuse);
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.EnableKeyword("_NORMALMAP");
        }

        string matPath = OutDir + def.prefabName + "_Mat.mat";
        AssetDatabase.CreateAsset(mat, matPath);
        return mat;
    }

    static Avatar BuildHumanoidAvatar(GameObject root, Transform boneRoot, string name)
    {
        var human = new List<HumanBone>();
        foreach (var kv in BoneMap)
        {
            var t = FindDeep(boneRoot, kv.Key);
            if (t == null) { Debug.LogError(name + ": 본 '" + kv.Key + "' 못 찾음"); return null; }
            human.Add(new HumanBone
            {
                boneName = kv.Key,
                humanName = kv.Value,
                limit = new HumanLimit { useDefaultValues = true }
            });
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
        avatar.name = name + "_Avatar";
        if (!avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError(name + ": Avatar 빌드 실패 (isValid=" + avatar.isValid + " isHuman=" + avatar.isHuman + ")");
            return null;
        }

        string avatarPath = OutDir + name + "_Avatar.asset";
        AssetDatabase.CreateAsset(avatar, avatarPath);
        return avatar;
    }

    static void CollectSkeleton(Transform t, List<SkeletonBone> list)
    {
        list.Add(new SkeletonBone
        {
            name = t.name,
            position = t.localPosition,
            rotation = t.localRotation,
            scale = t.localScale,
        });
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
