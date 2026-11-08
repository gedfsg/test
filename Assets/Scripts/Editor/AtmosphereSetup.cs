using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

/// <summary>
/// 분위기 세팅 (플레이테스트 피드백 4순위: "28 Days Later" 황혼~붉은 톤).
/// 메뉴: Tools/FX/Setup Atmosphere  (다시 실행하면 값만 갱신)
///  1) Post Processing Stack v2 프로필 생성: 컬러 그레이딩(황혼/붉은 톤, 채도↓ 대비↑) + 비네트 + 블룸(네온/불빛 글로우)
///  2) Main Camera에 PostProcessLayer, 씬에 전역 PostFX_Volume
///  3) DayNightCycle 밤 = 높이 뜬 청보라 달빛(그림자 생김) + 보라 안개, 노을 = 주황
///  씬 값(DayNightCycle 필드)은 씬에 저장된 값이 코드 기본값보다 우선이라 여기서 직접 바꾸고 씬 저장.
/// </summary>
public static class AtmosphereSetup
{
    const string ProfilePath = "Assets/Settings/PostFX/Atmosphere_Profile.asset";
    const string ResourcesPath = "Packages/com.unity.postprocessing/PostProcessing/PostProcessResources.asset";

    [MenuItem("Tools/FX/Setup Atmosphere")]
    public static void Setup()
    {
        var profile = BuildProfile();
        SetupCamera();
        SetupVolume(profile);
        SetupDayNight();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[AtmosphereSetup] 완료 - 포스트 프로세싱(컬러 그레이딩/비네트/블룸) + 달빛/보라 안개, 씬 저장");
    }

    // ── 1) 프로필 ──
    static PostProcessProfile BuildProfile()
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ProfilePath));
        var profile = AssetDatabase.LoadAssetAtPath<PostProcessProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<PostProcessProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        // 색조: 따뜻하게(주황) + 살짝 붉게, 채도 낮추고 대비 올려 거친 필름 느낌. 그림자는 붉은 갈색, 밝은 곳은 주황
        var cg = Get<ColorGrading>(profile);
        cg.gradingMode.Override(GradingMode.HighDefinitionRange);
        cg.tonemapper.Override(Tonemapper.Neutral);
        // (1차 값 temperature 22 / tint 8 / lift 오프셋 -0.03은 화면 전체가 검붉게 뭉개져서 밤엔 거의 안 보였음 → 약하게)
        cg.temperature.Override(14f);
        cg.tint.Override(3f);
        cg.saturation.Override(-15f);
        cg.contrast.Override(12f);
        cg.postExposure.Override(0.35f);
        cg.lift.Override(new Vector4(1.02f, 0.97f, 0.96f, 0f));     // 그림자: 살짝 붉게 (어둡게 누르진 않음)
        cg.gamma.Override(new Vector4(1f, 0.98f, 0.95f, 0f));
        cg.gain.Override(new Vector4(1.06f, 0.97f, 0.86f, 0f));     // 밝은 곳: 주황빛

        // 비네트: 가장자리 어둡게 (탑뷰라 시선이 플레이어 쪽으로 모이게)
        var vg = Get<Vignette>(profile);
        vg.mode.Override(VignetteMode.Classic);
        vg.color.Override(new Color(0.05f, 0.01f, 0.01f));
        vg.intensity.Override(0.38f);
        vg.smoothness.Override(0.45f);
        vg.rounded.Override(false);

        // 블룸: HDR(밝기 1 초과)만 번지게 → 네온 간판/가로등/불/조명이 빛남, 일반 지면은 그대로
        var bl = Get<Bloom>(profile);
        bl.intensity.Override(2.5f);
        bl.threshold.Override(1.1f);
        bl.softKnee.Override(0.5f);
        bl.diffusion.Override(7f);
        bl.color.Override(new Color(1f, 0.88f, 0.8f));

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    static T Get<T>(PostProcessProfile p) where T : PostProcessEffectSettings
    {
        if (p.TryGetSettings(out T s)) { s.enabled.Override(true); return s; }
        s = p.AddSettings<T>();
        s.enabled.Override(true);
        AssetDatabase.AddObjectToAsset(s, p); // 프로필 에셋 안에 서브 에셋으로 저장 (안 하면 씬 다시 열 때 효과가 사라짐)
        return s;
    }

    // ── 2) 카메라 / 볼륨 ──
    static void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogWarning("[AtmosphereSetup] Main Camera 없음"); return; }
        var layer = cam.GetComponent<PostProcessLayer>();
        if (layer == null) layer = Undo.AddComponent<PostProcessLayer>(cam.gameObject);
        layer.Init(AssetDatabase.LoadAssetAtPath<PostProcessResources>(ResourcesPath)); // 셰이더 리소스 (빌드 포함)
        layer.volumeTrigger = cam.transform;
        layer.volumeLayer = 1 << cam.gameObject.layer;                 // 볼륨은 카메라와 같은 레이어(Default)에 둠
        layer.antialiasingMode = PostProcessLayer.Antialiasing.None;   // 카메라 MSAA 그대로 사용
        cam.allowHDR = true;                                           // 블룸이 HDR 밝기를 써야 네온만 번짐
        EditorUtility.SetDirty(layer);
    }

    static void SetupVolume(PostProcessProfile profile)
    {
        var go = GameObject.Find("PostFX_Volume");
        if (go == null) { go = new GameObject("PostFX_Volume"); Undo.RegisterCreatedObjectUndo(go, "PostFX Volume"); }
        go.layer = Camera.main != null ? Camera.main.gameObject.layer : 0;
        var vol = go.GetComponent<PostProcessVolume>();
        if (vol == null) vol = go.AddComponent<PostProcessVolume>();
        vol.isGlobal = true;
        vol.priority = 0f;
        vol.weight = 1f;
        vol.sharedProfile = profile;
        EditorUtility.SetDirty(vol);
    }

    // ── 3) 달빛 / 안개 ──
    static void SetupDayNight()
    {
        var dnc = Object.FindFirstObjectByType<DayNightCycle>();
        if (dnc == null) { Debug.LogWarning("[AtmosphereSetup] DayNightCycle 없음"); return; }
        Undo.RecordObject(dnc, "Atmosphere DayNight");
        // 밤: 예전엔 각도 -30°(땅 밑)라 달빛 그림자가 없었음 → 35° 높이에서 청보라 달빛 (탑뷰에선 하늘이 안 보여서 달을 빛으로 표현)
        dnc.nightLightColor = new Color(0.42f, 0.45f, 0.95f);
        dnc.nightLightIntensity = 0.45f;
        dnc.nightLightAngle = 35f;
        dnc.nightAmbient = new Color(0.2f, 0.18f, 0.3f);       // 살짝 보라, 예전보다 조금 밝게 (컬러 그레이딩이 따뜻하게 바꾸면서 어두워 보임)
        dnc.nightFogColor = new Color(0.17f, 0.08f, 0.24f);    // 보라 안개
        dnc.duskColor = new Color(1f, 0.42f, 0.15f);           // 노을 주황 (예전 씬 값은 갈색)
        EditorUtility.SetDirty(dnc);
    }
}
