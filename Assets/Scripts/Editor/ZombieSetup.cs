using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.Animations;
using System.IO;

public class ZombieSetup : EditorWindow
{
    [MenuItem("Tools/Setup Zombie (Selected)")]
    static void SetupSelectedZombie()
    {
        GameObject zombie = Selection.activeGameObject;
        if (zombie == null)
        {
            Debug.LogError("씬에서 좀비 오브젝트를 선택해주세요!");
            return;
        }

        // 1. 애니메이터 컨트롤러 자동 생성/연결
        string controllerPath = "Assets/Character/Zombie/ZombieAnimator.controller";
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            controller = CreateZombieAnimatorController(controllerPath);
        }

        Animator anim = zombie.GetComponent<Animator>();
        if (anim == null) anim = zombie.AddComponent<Animator>();
        anim.runtimeAnimatorController = controller;
        anim.applyRootMotion = false;

        // 2. NavMeshAgent
        NavMeshAgent agent = zombie.GetComponent<NavMeshAgent>();
        if (agent == null) agent = zombie.AddComponent<NavMeshAgent>();
        agent.radius = 0.4f;
        agent.height = 1.8f;
        agent.speed = 2f;

        // 3. 콜라이더
        CapsuleCollider col = zombie.GetComponent<CapsuleCollider>();
        if (col == null) col = zombie.AddComponent<CapsuleCollider>();
        col.height = 1.8f;
        col.radius = 0.4f;
        col.center = new Vector3(0, 0.9f, 0);

        // 4. ZombieController
        if (zombie.GetComponent<ZombieController>() == null)
            zombie.AddComponent<ZombieController>();

        Debug.Log($"✅ {zombie.name} 좀비 세팅 완료!");
    }

    [MenuItem("Tools/Set Zombie FBX to Humanoid")]
    static void SetZombieFBXHumanoid()
    {
        string folder = "Assets/Character/Zombie/rig-fbx_joined";
        if (!Directory.Exists(folder))
        {
            Debug.LogError($"폴더 없음: {folder}");
            return;
        }

        string[] files = Directory.GetFiles(folder, "*.fbx", SearchOption.AllDirectories);
        int count = 0;
        foreach (string file in files)
        {
            string path = file.Replace('\\', '/');
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer != null && importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.SaveAndReimport();
                count++;
            }
        }

        // animations-fbx도 처리
        string animFolder = "Assets/Character/Zombie/animations-fbx";
        if (Directory.Exists(animFolder))
        {
            string[] animFiles = Directory.GetFiles(animFolder, "*.fbx", SearchOption.AllDirectories);
            foreach (string file in animFiles)
            {
                string path = file.Replace('\\', '/');
                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer != null && importer.animationType != ModelImporterAnimationType.Human)
                {
                    importer.animationType = ModelImporterAnimationType.Human;
                    importer.SaveAndReimport();
                    count++;
                }
            }
        }
    }

    static AnimatorController CreateZombieAnimatorController(string path)
    {
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);

        // 파라미터 추가
        controller.AddParameter("IsWalking", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        // idle 애니메이션 찾기
        AnimationClip idleClip = FindClip("idle_220f");
        AnimationClip walkClip = FindClip("walk_64f");
        AnimationClip attackClip = FindClip("attack_left_70f");

        // Idle 상태
        AnimatorState idleState = sm.AddState("Idle");
        idleState.motion = idleClip;
        sm.defaultState = idleState;

        // Walk 상태
        AnimatorState walkState = sm.AddState("Walk");
        walkState.motion = walkClip;

        // Attack 상태
        AnimatorState attackState = sm.AddState("Attack");
        attackState.motion = attackClip;

        // Idle → Walk
        AnimatorStateTransition t1 = idleState.AddTransition(walkState);
        t1.AddCondition(AnimatorConditionMode.If, 0, "IsWalking");
        t1.hasExitTime = false;
        t1.duration = 0.2f;

        // Walk → Idle
        AnimatorStateTransition t2 = walkState.AddTransition(idleState);
        t2.AddCondition(AnimatorConditionMode.IfNot, 0, "IsWalking");
        t2.hasExitTime = false;
        t2.duration = 0.2f;

        // Any State → Attack
        AnimatorStateTransition t3 = sm.AddAnyStateTransition(attackState);
        t3.AddCondition(AnimatorConditionMode.If, 0, "Attack");
        t3.hasExitTime = false;
        t3.duration = 0.1f;

        // Attack → Idle (자동)
        AnimatorStateTransition t4 = attackState.AddTransition(idleState);
        t4.hasExitTime = true;
        t4.exitTime = 0.9f;
        t4.duration = 0.1f;

        AssetDatabase.SaveAssets();
        Debug.Log($"✅ ZombieAnimator 생성: {path}");
        return controller;
    }

    static AnimationClip FindClip(string nameContains)
    {
        string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/Character/Zombie/animations-fbx" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.ToLower().Contains(nameContains.ToLower()))
            {
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip != null && !clip.name.StartsWith("__preview__"))
                    return clip;
            }
        }
        Debug.LogWarning($"애니메이션 못 찾음: {nameContains}");
        return null;
    }
}
