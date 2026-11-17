using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 메뉴 'Tools ▸ Shop ▸ 상인 설치 (모델·대기 동작)':
///  - Character/Floreswa/Prefabs의 시민 남자 모델(male*)만 모음 (안경·소파 제외)
///  - 아바타: 그 FBX가 Humanoid면 자기 것, 아니면 같은 체형(male01_1·2·3처럼 앞부분이 같은) Humanoid 모델의 아바타를 빌려 씀.
///    빌릴 것도 없는 모델은 대기 동작을 못 하므로 뺌 (지금은 male01_1만 Humanoid라 male01_1~3이 들어감.
///    더 다양하게 하려면 male02_1, male03_1 FBX의 Rig을 Humanoid로 바꾸고 다시 실행)
///  - Mixamo 'unarmed idle looking'으로 대기 동작 컨트롤러(Assets/Prefabs/Shop/TraderIdle.controller)를 만듦
///  - Resources/TraderSettings.asset에 저장 → TraderManager가 게임 시작 때 읽음
/// 다시 실행하면 모델 목록·컨트롤러만 새로 채우고, 배치 설정(상인 수·거리)은 그대로 둔다.
/// </summary>
public static class TraderSetupTool
{
    private const string PrefabFolder = "Assets/Character/Floreswa/Prefabs";
    private const string ModelFolder = "Assets/Character/Floreswa/Models";
    private const string IdleFbx = "Assets/Character/Motion/Mixamo/ProMeleeAxePack/unarmed idle looking ver. 1.fbx";
    private const string ControllerPath = "Assets/Prefabs/Shop/TraderIdle.controller";
    private const string SettingsPath = "Assets/Resources/TraderSettings.asset";

    [MenuItem("Tools/Shop/상인 설치 (모델·대기 동작)")]
    private static void Setup()
    {
        // 모델 + 아바타
        var models = new List<TraderSettings.Model>();
        var skipped = new List<string>();
        if (AssetDatabase.IsValidFolder(PrefabFolder))
        {
            // 1) 남자 모델과 각자의 Humanoid 아바타
            var found = new List<KeyValuePair<GameObject, Avatar>>();
            var familyAvatar = new Dictionary<string, Avatar>();   // "male01" → Humanoid 아바타
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                if (!name.StartsWith("male")) continue;   // 안경·소파 제외
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                var avatar = AssetDatabase.LoadAllAssetsAtPath($"{ModelFolder}/{name}.fbx").OfType<Avatar>()
                    .FirstOrDefault(a => a != null && a.isHuman);
                found.Add(new KeyValuePair<GameObject, Avatar>(prefab, avatar));
                if (avatar != null) familyAvatar[Family(name)] = avatar;
            }
            // 2) 아바타가 없으면 같은 체형 것을 빌림, 그것도 없으면 제외
            foreach (var kv in found)
            {
                var avatar = kv.Value;
                if (avatar == null) familyAvatar.TryGetValue(Family(kv.Key.name), out avatar);
                if (avatar == null) { skipped.Add(kv.Key.name); continue; }
                models.Add(new TraderSettings.Model { prefab = kv.Key, avatar = avatar });
            }
        }

        // 대기 동작
        var clip = AssetDatabase.LoadAllAssetsAtPath(IdleFbx).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        AnimatorController controller = null;
        if (clip != null)
        {
            EnsureFolder("Assets/Prefabs/Shop");
            if (AssetDatabase.LoadAssetAtPath<Object>(ControllerPath) != null) AssetDatabase.DeleteAsset(ControllerPath);
            controller = AnimatorController.CreateAnimatorControllerAtPathWithClip(ControllerPath, clip);
        }

        // 설정 에셋
        EnsureFolder("Assets/Resources");
        var settings = AssetDatabase.LoadAssetAtPath<TraderSettings>(SettingsPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<TraderSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }
        settings.models = models;
        settings.idleController = controller;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Selection.activeObject = settings;
        EditorGUIUtility.PingObject(settings);

        string msg = $"상인 모델 {models.Count}종: {string.Join(", ", models.Select(m => m.prefab.name))}\n" +
                     (skipped.Count > 0 ? $"제외 (Humanoid 아님): {string.Join(", ", skipped)}\n" : "") +
                     (controller != null ? $"대기 동작: {clip.name} → {ControllerPath}" : "⚠ 대기 동작 클립을 못 찾았습니다 (가만히 서 있음)") + "\n\n" +
                     $"저장: {SettingsPath}\n상인 수·거리 등은 이 에셋의 Inspector에서 바꿀 수 있습니다.";
        Debug.Log("[상인 설치] " + msg.Replace("\n", " / "));
        EditorUtility.DisplayDialog("상인 설치", msg, "확인");
    }

    /// <summary>"male02_3" → "male02" (같은 체형 묶음)</summary>
    private static string Family(string name)
    {
        int i = name.IndexOf('_');
        return i > 0 ? name.Substring(0, i) : name;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
