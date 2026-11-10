using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 메뉴 UI 사운드 + BGM 설정 (Tools → Audio → Setup UI Audio). 재실행해도 안전.
///  1) GameMixer에 BGM 그룹 + 노출 파라미터 BGMVolume 추가 (없을 때만)
///  2) BGM 가져오기 설정: 메인 BGM은 스트리밍(40초 루프), 죽음 스팅어는 메모리
///  3) Resources/Audio/UIAudio.prefab 에 소리 매핑 (클립·볼륨·피치)
///
/// 매핑 기준 (Assets/Audio/UI + Kenney 팩 Assets/Audio/UI/Audio, 길이·음량 실측):
///  호버 rollover2 (0.06초, 가장 짧고 부드러움) / 클릭 click1 (0.09초) / 뒤로 click3 (0.09초, 낮은 피치)
///  토글 switch7 (0.19초) / 슬라이더 switch13 (0.03초 틱, 작게) / 패널 열기·닫기 inventory open_close (쉭, 닫기는 낮은 피치)
///  일시정지 ON·OFF switch16 (0.36초, ON 낮게·OFF 높게) / 적용 new-notification-3 (0.5초 확인음) / 기본값 복원 switch22
///  메인 메뉴 BGM main_bgm 0.3 / 죽음 스팅어 death_bgm 0.7
/// </summary>
public static class UIAudioSetup
{
    const string MixerPath = "Assets/Resources/Audio/GameMixer.mixer";
    const string PrefabPath = "Assets/Resources/Audio/UIAudio.prefab";
    const string Ui = "Assets/Audio/UI/";
    const string Kenney = "Assets/Audio/UI/Audio/";
    const string Bgm = "Assets/Audio/BGM/";
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [MenuItem("Tools/Audio/Setup UI Audio")]
    public static void Setup()
    {
        string mixer = EnsureBgmGroup();
        SetImport(Bgm + "main_bgm.mp3", AudioClipLoadType.Streaming);
        SetImport(Bgm + "death_bgm.mp3", AudioClipLoadType.DecompressOnLoad);
        BuildPrefab();
        AssetDatabase.SaveAssets();
        Debug.Log($"[UIAudioSetup] 완료 - 믹서 BGM 그룹: {mixer}, UIAudio 매핑 프리팹 생성");
    }

    // ── 1) 믹서 BGM 그룹 ───────────────────────────
    static string EnsureBgmGroup()
    {
        var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        if (mixer == null) return "믹서 없음 (Tools → Audio → Setup Sound System 먼저)";
        if (mixer.FindMatchingGroups("BGM").Length > 0) return "이미 있음";
        try
        {
            // 공개 API가 없어서 에디터 내부 AudioMixerController를 리플렉션으로 씀 (SoundSystemSetup과 같은 방식)
            var asm = typeof(Editor).Assembly;
            var ctrlType = asm.GetType("UnityEditor.Audio.AudioMixerController");
            var groupType = asm.GetType("UnityEditor.Audio.AudioMixerGroupController");
            var pathType = asm.GetType("UnityEditor.Audio.AudioGroupParameterPath");
            object ctrl = mixer;
            object master = ctrlType.GetProperty("masterGroup", All).GetValue(ctrl);
            object g = ctrlType.GetMethod("CreateNewGroup", All).Invoke(ctrl, new object[] { "BGM", false });
            ctrlType.GetMethod("AddChildToParent", All).Invoke(ctrl, new[] { g, master });

            object guid = groupType.GetMethod("GetGUIDForVolume", All).Invoke(g, null);
            object path = Activator.CreateInstance(pathType, g, guid);
            ctrlType.GetMethod("AddExposedParameter", All).Invoke(ctrl, new[] { path });
            var prop = ctrlType.GetProperty("exposedParameters", All);
            var arr = (Array)prop.GetValue(ctrl);
            for (int i = 0; i < arr.Length; i++)
            {
                object p = arr.GetValue(i);
                if (!p.GetType().GetField("guid", All).GetValue(p).Equals(guid)) continue;
                p.GetType().GetField("name", All).SetValue(p, "BGMVolume");
                arr.SetValue(p, i); // 구조체라 다시 넣어야 반영
            }
            prop.SetValue(ctrl, arr);
            EditorUtility.SetDirty(mixer);
            AssetDatabase.SaveAssets();
            return "생성";
        }
        catch (Exception e)
        {
            Debug.LogError("[UIAudioSetup] BGM 그룹 자동 생성 실패 - GameMixer에 BGM 그룹을 만들고 볼륨을 BGMVolume으로 노출해 주세요: " + e);
            return "실패";
        }
    }

    static void SetImport(string path, AudioClipLoadType loadType)
    {
        var imp = AssetImporter.GetAtPath(path) as AudioImporter;
        if (imp == null) return;
        var s = imp.defaultSampleSettings;
        if (s.loadType == loadType) return;
        s.loadType = loadType;
        imp.defaultSampleSettings = s;
        imp.SaveAndReimport();
    }

    // ── 3) 매핑 프리팹 ─────────────────────────────
    static void BuildPrefab()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        var go = new GameObject("UIAudio");
        var a = go.AddComponent<UIAudio>();
        a.hover = E(Kenney + "rollover2.ogg", 0.5f);
        a.click = E(Kenney + "click1.ogg", 0.6f);
        a.back = E(Kenney + "click3.ogg", 0.6f, 0.85f);
        a.toggle = E(Kenney + "switch7.ogg", 0.55f);
        a.slider = E(Kenney + "switch13.ogg", 0.3f);
        a.panelOpen = E(Ui + "yodguard-inventory-open_close-3-540173.mp3", 0.5f);
        a.panelClose = E(Ui + "yodguard-inventory-open_close-3-540173.mp3", 0.45f, 0.85f);
        a.pauseOn = E(Kenney + "switch16.ogg", 0.55f, 0.8f);
        a.pauseOff = E(Kenney + "switch16.ogg", 0.55f, 1.05f);
        a.apply = E(Ui + "dragon-studio-new-notification-3-398649.mp3", 0.5f);
        a.defaults = E(Kenney + "switch22.ogg", 0.55f, 0.9f);
        a.menuBgm = Clip(Bgm + "main_bgm.mp3");
        a.menuBgmVolume = 0.3f;
        a.deathStinger = Clip(Bgm + "death_bgm.mp3");
        a.deathVolume = 0.7f;
        PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        UnityEngine.Object.DestroyImmediate(go);
    }

    static UIAudio.Entry E(string path, float volume, float pitch = 1f) => new UIAudio.Entry { clip = Clip(path), volume = volume, pitch = pitch };

    static AudioClip Clip(string path)
    {
        var c = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (c == null) Debug.LogWarning("[UIAudioSetup] 소리 파일 없음: " + path);
        return c;
    }
}
