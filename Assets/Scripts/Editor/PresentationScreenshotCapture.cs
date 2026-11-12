using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 발표 영상용 스크린샷 3장 자동 촬영 (Tools → Presentation → Capture Overview Screenshots). 2026-11-12
///
///  1) city_overview.png     : 현재 씬(main)을 하늘에서 비스듬히 내려다본 도시 전경 (낮 조명으로 잠깐 바꿨다가 되돌림)
///  2) crate_grades.png      : 일반/희귀/영웅/전설 상자 4개 + 등급 색 테두리 + 등급 라벨
///  3) weapons_collection.png: Items/Weapons/Liberator 총기 29종 6×5 그리드 + 이름 + 등급 색 테두리
///
/// - 2·3번은 미리보기 씬(PreviewScene)에 임시로 만들어 찍고 지움 → main 씬은 건드리지 않음.
/// - 글자는 Pretendard .otf로 메모리에서만 만든 TMP 폰트를 씀 (Resources/HUD/Fonts 폰트 에셋에 글리프가 추가 저장되지 않게).
/// - 상자: 게임 속 상자는 나무(일반)·금속(희귀)·군용(영웅) 3종 (LootCrate.CreateCrate와 같은 모양·색).
///   "전설" 상자는 게임에 없어서 전시용으로만 만든 것 (어두운 금색).
/// - 저장: 바탕 화면\발표영상_스크린샷\ (1920×1080 PNG)
/// </summary>
public static class PresentationScreenshotCapture
{
    const int Width = 1920, Height = 1080;
    const string LogTag = "[Presentation]";
    const string WeaponFolder = "Assets/Items/Weapons/Liberator";
    const string FontPath = "Assets/Fonts/Pretendard/Pretendard-Bold.otf";

    static string OutDir => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory), "발표영상_스크린샷");

    static readonly Color Background = new Color(0.16f, 0.16f, 0.17f);
    static TMP_FontAsset font;
    static readonly List<Object> temp = new List<Object>(); // 촬영 후 지울 임시 머티리얼·텍스처·폰트

    [MenuItem("Tools/Presentation/Capture Overview Screenshots")]
    public static void CaptureAll()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning($"{LogTag} Play 모드를 끄고 실행하세요."); return; }
        Directory.CreateDirectory(OutDir);
        try
        {
            CaptureCity();
            CaptureCrates();
            CaptureWeapons();
            Debug.Log($"{LogTag} 저장 완료: {OutDir} (city_overview / crate_grades / weapons_collection)");
        }
        finally
        {
            foreach (var o in temp) if (o != null) Object.DestroyImmediate(o);
            temp.Clear();
            font = null;
        }
    }

    // ───────────────────────── 1) 도시 전경 ─────────────────────────
    static void CaptureCity()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.name.Equals("main")) { Debug.LogWarning($"{LogTag} 도시 전경은 main 씬을 열고 찍어야 합니다 (지금: {scene.name}) → 건너뜀"); return; }

        // 건물 = 실내 판정이 있는 건물(BuildingInterior) + Kenney 도시 건물 메시(building-*). 위치는 렌더러 바운드 중심
        var buildings = new List<Bounds>();
        var seen = new HashSet<Transform>();
        foreach (var bi in Object.FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None))
            if (TryBounds(bi.transform, out var b)) { buildings.Add(b); seen.Add(bi.transform); }
        // 농장(Outdoor 태그 - 실내 판정은 뺐지만 건물임)
        foreach (var go in GameObject.FindGameObjectsWithTag(FarmOutdoorFixer.OutdoorTag))
            if (!seen.Contains(go.transform) && TryBounds(go.transform, out var fb)) { buildings.Add(fb); seen.Add(go.transform); }
        // 들어갈 수 없는 건물 메시 (building_E 등)
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!r.name.StartsWith("building", System.StringComparison.OrdinalIgnoreCase)) continue;
            bool counted = false;
            for (var p = r.transform; p != null; p = p.parent) if (seen.Contains(p)) { counted = true; break; }
            if (counted) continue;
            seen.Add(r.transform);
            buildings.Add(r.bounds);
        }
        if (buildings.Count == 0) { Debug.LogWarning($"{LogTag} 건물을 못 찾음 → 도시 전경 건너뜀"); return; }
        var city = buildings[0];
        foreach (var b in buildings) city.Encapsulate(b.center);

        // 낮 조명으로 잠깐 변경 (끝나면 되돌림)
        var dnc = Object.FindFirstObjectByType<DayNightCycle>();
        Light sun = dnc != null ? dnc.directionalLight : null;
        Quaternion sunRot = sun != null ? sun.transform.rotation : Quaternion.identity;
        float sunInt = sun != null ? sun.intensity : 0f; Color sunCol = sun != null ? sun.color : Color.white;
        Color amb = RenderSettings.ambientLight; bool fog = RenderSettings.fog;
        var camGo = new GameObject("PresentationCityCam") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                sun.intensity = dnc.dayLightIntensity;
                sun.color = dnc.dayLightColor;
                RenderSettings.ambientLight = dnc.dayAmbient;
            }
            RenderSettings.fog = false; // 멀리 있는 건물까지 선명하게

            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 1f; cam.farClipPlane = 3000f;
            // 맵 밖 허공(모서리)이 검게 나오지 않게 하늘색 배경
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.76f, 0.88f);

            // 남쪽 위에서 북쪽을 비스듬히 (피치 38°). 건물이 가장 많이 들어오는 거리를 찾음 (가까울수록 크게 보임)
            Vector3 center = new Vector3(city.center.x, 15f, city.center.z);
            float best = 0f; int bestCount = -1;
            for (float dist = 350f; dist <= 1100f; dist += 50f)
            {
                PlaceCityCam(cam, center, dist);
                int n = CountVisible(cam, buildings);
                if (n > bestCount + 3) { bestCount = n; best = dist; } // 3채 이상 늘 때만 더 멀리 (너무 작아지지 않게)
            }
            PlaceCityCam(cam, center, best);
            RenderToPng(cam, "city_overview.png");
            Debug.Log($"{LogTag} 도시 전경: 건물 {bestCount}/{buildings.Count}채 화면 안 (거리 {best:0}m)");
        }
        finally
        {
            Object.DestroyImmediate(camGo);
            if (sun != null) { sun.transform.rotation = sunRot; sun.intensity = sunInt; sun.color = sunCol; }
            RenderSettings.ambientLight = amb; RenderSettings.fog = fog;
        }
    }

    const float CityPitch = 52f;

    static void PlaceCityCam(Camera cam, Vector3 center, float dist)
    {
        Quaternion rot = Quaternion.Euler(CityPitch, 0f, 0f); // 북쪽(+Z)을 보며 비스듬히 내려다봄 (지평선·맵 밖 허공이 안 보이게)
        cam.transform.SetPositionAndRotation(center - rot * Vector3.forward * dist, rot);
    }

    static int CountVisible(Camera cam, List<Bounds> buildings)
    {
        int n = 0;
        foreach (var b in buildings)
        {
            Vector3 v = cam.WorldToViewportPoint(b.center);
            if (v.z > 0f && v.x > 0.02f && v.x < 0.98f && v.y > 0.02f && v.y < 0.98f) n++;
        }
        return n;
    }

    static bool TryBounds(Transform t, out Bounds b)
    {
        b = default;
        bool any = false;
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return any;
    }

    // ───────────────────────── 2) 상자 등급 비교 ─────────────────────────
    static void CaptureCrates()
    {
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var tiers = new[] { Rarity.Common, Rarity.Rare, Rarity.Epic, Rarity.Legendary };
            var names = new[] { "일반", "희귀", "영웅", "전설" };
            var subs = new[] { "나무 상자", "금속 상자", "군용 상자", "전설 상자" };
            const float spacing = 2.1f;
            var labels = new List<TextMeshPro>();

            // 바닥 (중립 어두운 회색)
            var floor = Primitive(PrimitiveType.Plane, preview, "Floor", Lit(new Color(0.22f, 0.22f, 0.23f), 0.05f));
            floor.transform.localScale = new Vector3(12f, 1f, 12f); // 화면 끝까지 바닥 (지평선이 안 보이게)

            for (int i = 0; i < tiers.Length; i++)
            {
                float x = (i - 1.5f) * spacing;
                BuildCrate(preview, new Vector3(x, 0f, 0f), tiers[i]);
                Color c = RarityColors.Get(tiers[i]);
                labels.Add(Text(preview, names[i], new Vector3(x, 1.62f, 0f), 0.62f, c));
                labels.Add(Text(preview, subs[i], new Vector3(x, 1.08f, 0f), 0.28f, new Color(0.85f, 0.85f, 0.85f)));
                // 상자 아래 등급색 발광 판 (테두리 색을 한 번 더 강조)
                var pad = Primitive(PrimitiveType.Quad, preview, "Pad", Unlit(new Color(c.r, c.g, c.b) * 0.55f));
                pad.transform.SetPositionAndRotation(new Vector3(x, 0.005f, 0f), Quaternion.Euler(90f, 0f, 0f));
                pad.transform.localScale = new Vector3(1.5f, 1.15f, 1f);
            }
            Lights(preview, new Vector3(45f, -30f, 0f));

            var cam = PreviewCamera(preview);
            cam.fieldOfView = 30f;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 4.4f, -8.4f), Quaternion.Euler(23f, 0f, 0f)); // 지평선이 화면 위로 벗어나게
            // 글자가 카메라를 보게
            foreach (var t in labels) t.transform.rotation = cam.transform.rotation;
            RenderToPng(cam, "crate_grades.png");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    /// <summary>LootCrate.CreateCrate와 같은 모양(몸통 1×0.55×0.65 + 뚜껑) + 모서리 12개에 등급색 테두리 막대</summary>
    static void BuildCrate(Scene scene, Vector3 pos, Rarity tier)
    {
        Color body = tier switch
        {
            Rarity.Rare => new Color(0.18f, 0.34f, 0.62f),      // 금속 - 파랑 (게임과 같음)
            Rarity.Epic => new Color(0.22f, 0.36f, 0.16f),      // 군용 - 녹색 (게임과 같음)
            Rarity.Legendary => new Color(0.30f, 0.22f, 0.08f), // 전시용 - 어두운 금색 (게임엔 없음)
            _ => new Color(0.45f, 0.28f, 0.12f),                // 나무 - 갈색 (게임과 같음)
        };
        var mat = Lit(body, tier == Rarity.Rare ? 0.5f : 0.1f);
        var root = new GameObject("Crate_" + tier);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, -18f, 0f));

        Vector3 size = new Vector3(1.0f, 0.55f, 0.65f);
        var b = Primitive(PrimitiveType.Cube, scene, "Body", mat, root.transform);
        b.transform.localPosition = new Vector3(0f, size.y / 2f, 0f);
        b.transform.localScale = size;
        var lid = Primitive(PrimitiveType.Cube, scene, "Lid", mat, root.transform);
        lid.transform.localPosition = new Vector3(0f, size.y + 0.05f, 0f);
        lid.transform.localScale = new Vector3(size.x + 0.04f, 0.1f, size.z + 0.04f);

        // 테두리: 상자 전체(뚜껑 포함) 바깥 모서리 12개
        var edge = Unlit(RarityColors.Get(tier));
        Vector3 ext = new Vector3(size.x + 0.06f, size.y + 0.12f, size.z + 0.06f);
        Vector3 c = new Vector3(0f, ext.y / 2f, 0f);
        const float th = 0.045f;
        for (int axis = 0; axis < 3; axis++)
            for (int s1 = -1; s1 <= 1; s1 += 2)
                for (int s2 = -1; s2 <= 1; s2 += 2)
                {
                    Vector3 p = c, sc = Vector3.one * th;
                    int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
                    p[a1] += s1 * ext[a1] / 2f; p[a2] += s2 * ext[a2] / 2f;
                    sc[axis] = ext[axis] + th;
                    var bar = Primitive(PrimitiveType.Cube, scene, "Edge", edge, root.transform);
                    bar.transform.localPosition = p; bar.transform.localScale = sc;
                }
    }

    // ───────────────────────── 3) 총기 전시 ─────────────────────────
    static void CaptureWeapons()
    {
        var weapons = AssetDatabase.FindAssets("t:WeaponData", new[] { WeaponFolder })
            .Select(g => AssetDatabase.LoadAssetAtPath<WeaponData>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(w => w != null && w.type != WeaponType.Melee && w.weaponPrefab != null)
            .OrderBy(w => RarityOrder(w.rarity)).ThenBy(w => w.type).ThenBy(w => w.itemName)
            .ToList();
        if (weapons.Count == 0) { Debug.LogWarning($"{LogTag} {WeaponFolder}에서 총기를 못 찾음 → 건너뜀"); return; }

        const int cols = 6;
        int rows = Mathf.CeilToInt(weapons.Count / (float)cols);
        const float cellW = 3f, cellH = 2.2f, gap = 0.12f;
        float gridW = cols * cellW, gridH = rows * cellH;
        const float titleH = 0.9f;

        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var panelMat = Unlit(new Color(0.21f, 0.21f, 0.22f));
            for (int i = 0; i < weapons.Count; i++)
            {
                var w = weapons[i];
                int col = i % cols, row = i / cols;
                Vector3 cc = new Vector3(-gridW / 2f + cellW * (col + 0.5f), gridH / 2f - cellH * (row + 0.5f), 0f);
                Color rc = RarityColors.Get(w.rarity);

                // 칸 배경 + 등급색 테두리 (4변)
                var panel = Primitive(PrimitiveType.Quad, preview, "Cell", panelMat);
                panel.transform.position = cc + new Vector3(0f, 0f, 0.6f);
                panel.transform.localScale = new Vector3(cellW - gap, cellH - gap, 1f);
                Frame(preview, cc + new Vector3(0f, 0f, 0.55f), cellW - gap, cellH - gap, 0.05f, Unlit(rc));

                // 총 모델: 가장 긴 축 = 가로, 중간 축 = 세로로 눕혀서 칸 위쪽 영역(2.5×1.15)에 맞춤
                PlaceGun(preview, w.weaponPrefab, cc + new Vector3(0f, 0.28f, 0f), 2.5f, 1.15f);

                Text(preview, w.itemName, cc + new Vector3(0f, -0.62f, 0f), 0.3f, Color.white);
                Text(preview, $"{RarityName(w.rarity)} · {TypeName(w.type)}", cc + new Vector3(0f, -0.88f, 0f), 0.19f, rc);
            }
            Text(preview, $"총기 {weapons.Count}종", new Vector3(0f, gridH / 2f + titleH * 0.5f, 0f), 0.5f, Color.white);
            Lights(preview, new Vector3(25f, 20f, 0f));

            var cam = PreviewCamera(preview);
            cam.orthographic = true;
            float totalH = gridH + titleH + 0.4f;
            float aspect = Width / (float)Height;
            cam.orthographicSize = Mathf.Max(totalH, (gridW + 0.6f) / aspect) / 2f;
            cam.transform.SetPositionAndRotation(new Vector3(0f, titleH / 2f, -20f), Quaternion.identity);
            RenderToPng(cam, "weapons_collection.png");
            Debug.Log($"{LogTag} 총기 {weapons.Count}종 ({cols}×{rows})");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    static void PlaceGun(Scene scene, GameObject prefab, Vector3 center, float maxW, float maxH)
    {
        var go = Object.Instantiate(prefab);
        go.name = prefab.name;
        SceneManager.MoveGameObjectToScene(go, scene);
        // 화면에 필요 없는 것 끄기 (총구 화염 빛·파티클 등)
        foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;

        var rends = go.GetComponentsInChildren<Renderer>().Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
        if (rends.Length == 0) return;
        Bounds b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);

        // 축 크기 순서: 가장 긴 축 → X(가로), 중간 → Y(세로), 가장 짧은 축 → Z(카메라 쪽)
        var axes = new[] { (Vector3.right, b.size.x), (Vector3.up, b.size.y), (Vector3.forward, b.size.z) }
            .OrderByDescending(a => a.Item2).ToArray();
        Quaternion q = Quaternion.LookRotation(axes[2].Item1, axes[1].Item1); // 짧은 축=Z, 중간 축=Y 로 보는 회전
        go.transform.rotation = Quaternion.Inverse(q);

        b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
        float s = Mathf.Min(maxW / Mathf.Max(0.001f, b.size.x), maxH / Mathf.Max(0.001f, b.size.y));
        go.transform.localScale = Vector3.one * s;
        b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
        go.transform.position += center - b.center;
    }

    static int RarityOrder(Rarity r) => r switch
    {
        Rarity.Common => 0, Rarity.Uncommon => 1, Rarity.Rare => 2, Rarity.Epic => 3, Rarity.Legendary => 4, _ => 5
    };

    static string RarityName(Rarity r) => r switch
    {
        Rarity.Common => "일반", Rarity.Uncommon => "고급", Rarity.Rare => "희귀", Rarity.Epic => "영웅", Rarity.Legendary => "전설", _ => r.ToString()
    };

    static string TypeName(WeaponType t) => t switch
    {
        WeaponType.Pistol => "권총", WeaponType.AR => "소총", WeaponType.Shotgun => "샷건", WeaponType.Sniper => "저격총", _ => t.ToString()
    };

    // ───────────────────────── 공통 도우미 ─────────────────────────
    static Camera PreviewCamera(Scene scene)
    {
        var go = new GameObject("PresentationCam");
        SceneManager.MoveGameObjectToScene(go, scene);
        var cam = go.AddComponent<Camera>();
        cam.scene = scene; // 이 미리보기 씬만 그림
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Background;
        cam.nearClipPlane = 0.1f; cam.farClipPlane = 100f;
        return cam;
    }

    static void Lights(Scene scene, Vector3 keyEuler)
    {
        AddLight(scene, keyEuler, 1.15f, new Color(1f, 0.97f, 0.92f), LightShadows.Soft);
        AddLight(scene, new Vector3(20f, 150f, 0f), 0.45f, new Color(0.8f, 0.86f, 1f), LightShadows.None); // 반대편 채움광
        AddLight(scene, new Vector3(10f, 0f, 0f), 0.35f, Color.white, LightShadows.None);                 // 정면 약한 채움광
    }

    static void AddLight(Scene scene, Vector3 euler, float intensity, Color color, LightShadows shadows)
    {
        var go = new GameObject("Light");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.rotation = Quaternion.Euler(euler);
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional; l.intensity = intensity; l.color = color; l.shadows = shadows;
    }

    static GameObject Primitive(PrimitiveType type, Scene scene, string name, Material mat, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (parent != null) go.transform.SetParent(parent, false);
        else SceneManager.MoveGameObjectToScene(go, scene);
        return go;
    }

    static void Frame(Scene scene, Vector3 c, float w, float h, float th, Material mat)
    {
        void Bar(Vector3 p, Vector3 s)
        {
            var q = Primitive(PrimitiveType.Quad, scene, "Frame", mat);
            q.transform.position = p; q.transform.localScale = s;
        }
        Bar(c + new Vector3(0f, h / 2f - th / 2f, 0f), new Vector3(w, th, 1f));
        Bar(c + new Vector3(0f, -h / 2f + th / 2f, 0f), new Vector3(w, th, 1f));
        Bar(c + new Vector3(-w / 2f + th / 2f, 0f, 0f), new Vector3(th, h, 1f));
        Bar(c + new Vector3(w / 2f - th / 2f, 0f, 0f), new Vector3(th, h, 1f));
    }

    static Material Lit(Color c, float gloss)
    {
        var m = new Material(Shader.Find("Standard")) { color = c, hideFlags = HideFlags.HideAndDontSave };
        m.SetFloat("_Glossiness", gloss);
        temp.Add(m);
        return m;
    }

    static Material Unlit(Color c)
    {
        var m = new Material(Shader.Find("Unlit/Color")) { color = c, hideFlags = HideFlags.HideAndDontSave };
        temp.Add(m);
        return m;
    }

    static TextMeshPro Text(Scene scene, string text, Vector3 pos, float size, Color color)
    {
        if (font == null)
        {
            var src = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (src != null)
            {
                font = TMP_FontAsset.CreateFontAsset(src); // 메모리 전용 동적 폰트
                font.hideFlags = HideFlags.HideAndDontSave;
                temp.Add(font);
                if (font.atlasTexture != null) temp.Add(font.atlasTexture);
                if (font.material != null) temp.Add(font.material);
            }
        }
        var go = new GameObject("Label_" + text);
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.position = pos;
        var t = go.AddComponent<TextMeshPro>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size * 10f; // TMP 3D 글자: fontSize 10 ≈ 1m
        t.color = color;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.rectTransform.sizeDelta = new Vector2(10f, 2f);
        t.ForceMeshUpdate();
        return t;
    }

    static void RenderToPng(Camera cam, string file)
    {
        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        var prev = RenderTexture.active;
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        try
        {
            cam.aspect = Width / (float)Height;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            string path = Path.Combine(OutDir, file);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"{LogTag} {file} 저장 ({Width}×{Height}) → {path}");
        }
        finally
        {
            cam.targetTexture = null;
            RenderTexture.active = prev;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
    }
}
