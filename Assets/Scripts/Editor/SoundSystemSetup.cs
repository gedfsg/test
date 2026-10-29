using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Tools → Audio → Setup Sound System
///  1) Assets/Audio/_Processed (Tools/Audio/preprocess_audio.py 결과) 임포트 설정
///     - 짧은 효과음: Decompress On Load + Preload / 반복음(_loop): Streaming + Vorbis
///     - 3D로 쓰는 소리(좀비/총/피격)는 Force To Mono (3D 위치 표현은 모노가 정확, 메모리 절반)
///  2) Resources/Audio/GameMixer.mixer: Master → SFX / Zombie / UI / Ambient 그룹 + 볼륨 노출 파라미터
///  3) Resources/Audio/SoundLibrary.asset: 파일 이름(용도_번호.wav) 기준으로 클립 자동 분류
///  4) WeaponData: SMG 계열에 SMG 발사음 지정 (나머지는 비워두면 총 종류별 기본 소리 - SoundLibrary)
/// 다시 실행해도 안전 (있는 건 갱신만).
/// </summary>
public static class SoundSystemSetup
{
    const string ProcessedDir = "Assets/Audio/_Processed";
    const string ResourcesDir = "Assets/Resources/Audio";
    const string LibraryPath = ResourcesDir + "/SoundLibrary.asset";
    const string MixerPath = ResourcesDir + "/GameMixer.mixer";

    // SoundLibrary 필드 ← _Processed 안 파일 이름 접두어
    static readonly Dictionary<string, string> LibraryMap = new Dictionary<string, string>
    {
        { "zombieGroan", "zombie/zombie_groan_" }, { "zombieScream", "zombie/zombie_scream_" }, { "zombieDeath", "zombie/zombie_death_" },
        { "firePistol", "weapon/fire_pistol_" }, { "fireSmg", "weapon/fire_smg_" }, { "fireRifle", "weapon/fire_rifle_" },
        { "fireShotgun", "weapon/fire_shotgun_" }, { "fireSniper", "weapon/fire_sniper_" },
        { "reloadPistol", "weapon/reload_pistol_" }, { "reloadRifle", "weapon/reload_rifle_" },
        { "hitFlesh", "impact/hit_flesh_" }, { "hitMelee", "impact/hit_melee_" },
        { "bloodSplatter", "impact/blood_splatter_" }, { "bodyFall", "impact/body_fall_" },
        { "footstep", "player/footstep_" }, { "playerHurt", "player/hurt_" }, { "playerDeath", "player/death_" }, { "roll", "player/roll_" },
        { "inventoryOpen", "ui/inventory_open_" }, { "inventoryClose", "ui/inventory_close_" }, { "pickup", "ui/pickup_" },
        { "doorOpen", "ui/door_open_" }, { "doorClose", "ui/door_close_" }, { "crateOpen", "ui/crate_open_" },
        { "click", "ui/click_" }, { "heartbeat", "ui/heartbeat_" }, { "missionNotification", "ui/notification_" },
        { "rainLoop", "weather/rain_loop_" },
    };

    // 3D로 재생하는 폴더 → 모노로
    static readonly string[] MonoFolders = { "zombie", "weapon", "impact" };

    // WeaponType.AR 중 SMG (이름 기준)
    static readonly string[] SmgNames = { "MP7", "P90", "UMP45", "Vector", "Skorpion", "SMG" };

    [MenuItem("Tools/Audio/Setup Sound System")]
    public static void Setup()
    {
        int imported = ApplyImportSettings();
        string mixerResult = EnsureMixer();
        int libCount = BuildLibrary();
        int weapons = AssignWeaponSounds();
        AssetDatabase.SaveAssets();
        Debug.Log($"[Sound Setup] 임포트 설정 {imported}개 / 믹서: {mixerResult} / 라이브러리 클립 {libCount}개 / SMG 발사음 지정 {weapons}개");
    }

    // ── 1) 임포트 설정 ─────────────────────────────
    static int ApplyImportSettings()
    {
        int n = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { ProcessedDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) continue;

            bool loop = Path.GetFileNameWithoutExtension(path).Contains("_loop");
            string folder = Path.GetFileName(Path.GetDirectoryName(path));
            var s = importer.defaultSampleSettings;
            if (loop)
            {
                s.loadType = AudioClipLoadType.Streaming;          // 긴 반복음: 메모리에 다 안 올림
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.6f;
                s.preloadAudioData = false;
                importer.loadInBackground = true;
            }
            else
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad;   // 짧은 효과음: 재생 지연 없이
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.7f;
                s.preloadAudioData = true;
                importer.loadInBackground = false;
            }
            importer.defaultSampleSettings = s;
            importer.forceToMono = MonoFolders.Contains(folder);
            importer.SaveAndReimport();
            n++;
        }
        return n;
    }

    // ── 2) 오디오 믹서 ─────────────────────────────
    // AudioMixer 그룹/노출 파라미터를 만드는 공개 API가 없어서 에디터 내부 클래스(AudioMixerController)를 리플렉션으로 사용.
    // Unity 버전이 바뀌어 실패하면: 에디터에서 직접 GameMixer를 만들고 그룹 SFX/Zombie/UI/Ambient +
    // 볼륨 노출 파라미터(MasterVolume, SFXVolume, ZombieVolume, UIVolume, AmbientVolume)를 추가하면 됨.
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    static string EnsureMixer()
    {
        if (AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath) != null) return "이미 있음";
        Directory.CreateDirectory(ResourcesDir);
        try
        {
            var asm = typeof(Editor).Assembly;
            var ctrlType = asm.GetType("UnityEditor.Audio.AudioMixerController");
            var groupType = asm.GetType("UnityEditor.Audio.AudioMixerGroupController");
            var pathType = asm.GetType("UnityEditor.Audio.AudioGroupParameterPath");

            object ctrl = ctrlType.GetMethod("CreateMixerControllerAtPath", All).Invoke(null, new object[] { MixerPath });
            object master = ctrlType.GetProperty("masterGroup", All).GetValue(ctrl);
            Expose(ctrl, ctrlType, groupType, pathType, master, AudioManager.MasterParam);

            foreach (var ch in (AudioManager.Channel[])Enum.GetValues(typeof(AudioManager.Channel)))
            {
                object g = ctrlType.GetMethod("CreateNewGroup", All).Invoke(ctrl, new object[] { ch.ToString(), false });
                ctrlType.GetMethod("AddChildToParent", All).Invoke(ctrl, new[] { g, master });
                Expose(ctrl, ctrlType, groupType, pathType, g, ch + "Volume");
            }
            EditorUtility.SetDirty((UnityEngine.Object)ctrl);
            AssetDatabase.SaveAssets();
            return "생성";
        }
        catch (Exception e)
        {
            Debug.LogError("[Sound Setup] 믹서 자동 생성 실패 - 수동으로 만들어야 함 (SoundSystemSetup.EnsureMixer 주석 참고): " + e);
            return "실패";
        }
    }

    // 그룹 볼륨을 노출 파라미터로 등록하고 이름 붙이기 (스크립트에서 mixer.SetFloat(name, dB))
    static void Expose(object ctrl, Type ctrlType, Type groupType, Type pathType, object group, string name)
    {
        object guid = groupType.GetMethod("GetGUIDForVolume", All).Invoke(group, null);
        object path = Activator.CreateInstance(pathType, group, guid);
        ctrlType.GetMethod("AddExposedParameter", All).Invoke(ctrl, new[] { path });

        var prop = ctrlType.GetProperty("exposedParameters", All);
        var arr = (Array)prop.GetValue(ctrl);
        for (int i = 0; i < arr.Length; i++)
        {
            object p = arr.GetValue(i);
            var guidField = p.GetType().GetField("guid", All);
            if (!guidField.GetValue(p).Equals(guid)) continue;
            p.GetType().GetField("name", All).SetValue(p, name);
            arr.SetValue(p, i); // 구조체라 다시 넣어야 반영
        }
        prop.SetValue(ctrl, arr);
    }

    // ── 3) 사운드 라이브러리 ────────────────────────
    static int BuildLibrary()
    {
        Directory.CreateDirectory(ResourcesDir);
        var lib = AssetDatabase.LoadAssetAtPath<SoundLibrary>(LibraryPath);
        if (lib == null)
        {
            lib = ScriptableObject.CreateInstance<SoundLibrary>();
            AssetDatabase.CreateAsset(lib, LibraryPath);
        }

        var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { ProcessedDir })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        int total = 0;
        foreach (var kv in LibraryMap)
        {
            var found = clips.Where(p => p.Substring(ProcessedDir.Length + 1).StartsWith(kv.Value, StringComparison.OrdinalIgnoreCase))
                             .Select(AssetDatabase.LoadAssetAtPath<AudioClip>)
                             .Where(c => c != null).ToArray();
            if (found.Length == 0) Debug.LogWarning($"[Sound Setup] {kv.Key}: '{kv.Value}*' 파일 없음");
            total += found.Length;

            var field = typeof(SoundLibrary).GetField(kv.Key);
            if (field.FieldType == typeof(AudioClip)) field.SetValue(lib, found.FirstOrDefault());
            else field.SetValue(lib, found);
        }
        EditorUtility.SetDirty(lib);
        return total;
    }

    // ── 4) 무기 데이터 ─────────────────────────────
    static int AssignWeaponSounds()
    {
        var lib = AssetDatabase.LoadAssetAtPath<SoundLibrary>(LibraryPath);
        if (lib == null || lib.fireSmg == null || lib.fireSmg.Length == 0) return 0;
        int n = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:WeaponData"))
        {
            var wd = AssetDatabase.LoadAssetAtPath<WeaponData>(AssetDatabase.GUIDToAssetPath(guid));
            if (wd == null || wd.type != WeaponType.AR || wd.fireSound != null) continue; // 직접 넣은 소리는 유지
            if (!SmgNames.Any(s => wd.name.Equals(s, StringComparison.OrdinalIgnoreCase))) continue;
            wd.fireSound = lib.fireSmg[0];
            EditorUtility.SetDirty(wd);
            n++;
        }
        return n;
    }
}
