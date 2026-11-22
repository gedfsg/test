using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 스나이퍼 2종 + 특수 무기 5종 → 무기 프리팹 + 아이콘 + WeaponData + Resources/WeaponDatabase 자동 생성 (2026-11-16).
/// 메뉴: Tools/Weapons/Build Sniper + Special Weapons (다시 실행하면 같은 경로에 덮어씀)
///       Tools/Weapons/Add Muzzle Light to All (모든 원거리 무기 프리팹 총구에 MuzzleLight)
///
/// 프리팹 규칙은 LiberatorWeaponBuilder와 같음 (WeaponHandAttacher가 그대로 쓰도록):
///  - 루트 원점 = 손잡이, 총구 방향 = +Z, 위 = +Y
///  - 자식 MuzzlePoint(총구 끝), LeftHandPoint(왼손), Model(원본 모델, 크기 맞춤)
/// 원본 모델마다 축·크기가 제각각이라 (FBX 100배, GLB 0.14배, 총구가 X/Y/Z 중 어디인지 등)
/// 항목마다 총구 방향(fwd)과 위(up)를 원본 좌표로 직접 적고 실제 길이(m)로 맞춤.
/// </summary>
public static class SpecialWeaponBuilder
{
    const string PrefabDir = "Assets/Weapon Assets/Prefabs";
    const string IconDir = "Assets/Weapon Assets/Icons";
    const string SniperDataDir = "Assets/Items/Weapons/Sniper";
    const string SpecialDataDir = "Assets/Items/Weapons/Special";
    const string DatabasePath = "Assets/Resources/WeaponDatabase.asset";

    class Cfg
    {
        public string model, display, file;
        public bool special;           // 상점 전용
        public bool melee;             // 한 손 근접 (전기톱)
        public Vector3 fwd, up;        // 원본 모델(인스턴스 월드 좌표)에서 총구 방향 / 위 방향
        public float length;           // 게임 안 길이(m). AR(LG_AK-556) = 1.12
        public float gripT = 0.37f;    // 뒤끝에서 손잡이까지 비율
        public float leftT = 0.68f;    // 뒤끝에서 왼손(앞 손잡이)까지 비율
        public System.Action<WeaponData> stats;
        // 원본 재질 이름(앞부분) → 색. 텍스처 없이 흰색(0.8)으로 들어온 모델(Mosin, 화염방사기)에 색 입힘
        public (string prefix, Color color, float metal)[] recolor;
    }

    const string MaterialDir = "Assets/Weapon Assets/Materials";

    static readonly Color Yellow = new Color32(0xFF, 0xD0, 0x70, 0xFF);

    static List<Cfg> Configs() => new List<Cfg>
    {
        // ── 스나이퍼 (등급 아이템, 루팅) ──
        new Cfg { model = "Assets/Weapon Assets/sniper/Mosin_Sniper.fbx", display = "Mosin", file = "Mosin_Sniper",
                  fwd = Vector3.back, up = Vector3.up, length = 1.35f, gripT = 0.30f, leftT = 0.55f,
                  recolor = new[] { ("Mosin_Parts", new Color(0.16f, 0.16f, 0.17f), 0.6f), ("Mosin_Wood", new Color(0.42f, 0.25f, 0.12f), 0f),
                                    ("PSU_Scope", new Color(0.07f, 0.07f, 0.08f), 0.5f) },
                  stats = d => { d.type = WeaponType.Sniper; d.rarity = Rarity.Rare; d.damage = 120f; d.effectiveRange = 80f; d.maxAmmo = 5;
                                 d.reloadTime = 3.5f; d.attackRate = 1.2f; d.noiseRadius = 100f; d.penetrationCount = 2;
                                 d.bulletSpeed = 250f; d.recoil = 0f; d.autoFire = false; } },
        new Cfg { model = "Assets/Weapon Assets/sniper/L118a1.fbx", display = "L118A1", file = "L118a1",
                  fwd = Vector3.left, up = Vector3.up, length = 1.30f, gripT = 0.30f, leftT = 0.55f,
                  stats = d => { d.type = WeaponType.Sniper; d.rarity = Rarity.Epic; d.damage = 180f; d.effectiveRange = 120f; d.maxAmmo = 8;
                                 d.reloadTime = 3.0f; d.attackRate = 1.0f; d.noiseRadius = 120f; d.penetrationCount = 3;
                                 d.bulletSpeed = 300f; d.recoil = 0f; d.autoFire = false; } },

        // ── 특수 무기 5종 (상점 교환 전용, 배치·드롭 없음) ──
        new Cfg { model = "Assets/Weapon Assets/Special Weapons/fire_gun_low.fbx", display = "화염방사기", file = "Flamethrower", special = true,
                  fwd = Vector3.left, up = Vector3.up, length = 1.15f, gripT = 0.35f, leftT = 0.62f,
                  recolor = new[] { ("TT_checker", new Color(0.33f, 0.36f, 0.22f), 0.3f) }, // 올리브색 (원본은 UV 체커 텍스처가 빠진 흰색)
                  stats = d => { d.type = WeaponType.Ranged; d.specialKind = SpecialWeaponKind.Flamethrower;
                                 // 데미지 20/초: 0.05초마다 1 (연료 100 = 탄 100, 초당 20 소모 → 5초)
                                 d.damage = 1f; d.attackRate = 0.05f; d.maxAmmo = 100; d.reloadTime = 2.5f; d.autoFire = true;
                                 d.effectiveRange = 10f; d.bulletSpeed = 25f; d.recoil = 6f;
                                 d.burnDps = 10f; d.burnDuration = 5f; d.noiseRadius = 80f;
                                 d.lightColor = new Color32(0xFF, 0x55, 0x00, 0xFF); d.lightIntensity = 2.0f; } },
        new Cfg { model = "Assets/Weapon Assets/Special Weapons/chainsaw_KrivoRukiy.fbx", display = "전기톱", file = "Chainsaw", special = true, melee = true,
                  fwd = Vector3.back, up = Vector3.up, length = 1.0f, gripT = 0.12f, leftT = 0.30f,
                  stats = d => { d.type = WeaponType.Melee; d.specialKind = SpecialWeaponKind.Chainsaw;
                                 d.damage = 60f; d.attackRate = 1f / 3f; d.effectiveRange = 2f; d.maxAmmo = 0; d.autoFire = false;
                                 d.fuelCapacity = 120f; d.fuelPerSecond = 2f; d.noiseRadius = 70f; d.staminaCost = 0f;
                                 // 손 자세는 도끼와 같은 방식 (rigidGrip). 상점 연동 때 실제로 들어보고 조정 필요
                                 // d.positionOffset = Vector3.zero; d.rotationOffset = new Vector3(-90f, 0f, 0f); // (이전) 도끼처럼 한 손 휘두르기
                                 // d.swingPositionOffset = new Vector3(0.035f, -0.06f, -0.09f); d.rigidGrip = true;
                                 // (2026-11-22) 양손으로 들고(총과 같은 그립, 왼손 = 위 손잡이) 누르는 동안 계속 갈아냄 (PlayerController.ChainsawHeld)
                                 d.positionOffset = new Vector3(0.01f, 0.06f, -0.2f); d.rotationOffset = new Vector3(30f, 170f, 0f);
                                 d.swingPositionOffset = Vector3.zero; d.rigidGrip = false; } },
        new Cfg { model = "Assets/Weapon Assets/Special Weapons/low-poly_rpg-7.glb", display = "RPG-7", file = "RPG", special = true,
                  fwd = Vector3.left, up = Vector3.up, length = 1.20f, gripT = 0.45f, leftT = 0.62f,
                  stats = d => { d.type = WeaponType.Ranged; d.specialKind = SpecialWeaponKind.RPG;
                                 d.damage = 300f; d.splashDamage = 150f; d.splashRadius = 8f; d.knockback = 6f;
                                 d.maxAmmo = 1; d.reloadTime = 3f; d.attackRate = 1f; d.autoFire = false;
                                 d.effectiveRange = 70f; d.bulletSpeed = 45f; d.recoil = 0f; d.noiseRadius = 90f;
                                 d.lightColor = new Color32(0xFF, 0x88, 0x00, 0xFF); } },
        new Cfg { model = "Assets/Weapon Assets/Special Weapons/M134 Minigun.fbx", display = "미니건", file = "Minigun", special = true,
                  fwd = Vector3.right, up = Vector3.up, length = 1.25f, gripT = 0.35f, leftT = 0.55f,
                  stats = d => { d.type = WeaponType.Ranged; d.specialKind = SpecialWeaponKind.Minigun;
                                 d.damage = 25f; d.attackRate = 0.05f; d.spinUpTime = 1f; d.maxAmmo = 200; d.reloadTime = 4f; d.autoFire = true;
                                 d.effectiveRange = 45f; d.bulletSpeed = 160f; d.recoil = 4f; d.noiseRadius = 90f;
                                 d.lightColor = Yellow; d.lightIntensity = 1.2f; } },   // 초당 20번 번쩍 → 강렬하게 깜빡임
        new Cfg { model = "Assets/Weapon Assets/Special Weapons/low-poly_portal_gun.glb", display = "레이저", file = "LaserGun", special = true,
                  fwd = Vector3.forward, up = Vector3.up, length = 0.55f, gripT = 0.30f, leftT = 0.55f,
                  stats = d => { d.type = WeaponType.Ranged; d.specialKind = SpecialWeaponKind.Laser;
                                 // 즉발 관통: 탄속 500m/s (사거리 60m를 0.12초) + 좀비 5마리 관통
                                 d.damage = 80f; d.attackRate = 0.5f; d.maxAmmo = 30; d.reloadTime = 2f; d.autoFire = false;
                                 d.penetrationCount = 5; d.effectiveRange = 60f; d.bulletSpeed = 500f; d.recoil = 0f; d.noiseRadius = 40f;
                                 d.lightColor = new Color32(0x00, 0xEE, 0xFF, 0xFF); d.lightIntensity = 1.2f; } },
    };

    [MenuItem("Tools/Weapons/Build Sniper + Special Weapons")]
    public static void BuildAll()
    {
        foreach (var dir in new[] { PrefabDir, IconDir, SniperDataDir, SpecialDataDir }) Directory.CreateDirectory(dir);
        var log = new System.Text.StringBuilder();
        var shop = new List<WeaponData>();
        foreach (var c in Configs())
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(c.model);
            if (src == null) { log.AppendLine("  ✗ 모델 없음: " + c.model); continue; }
            GameObject prefab = BuildPrefab(src, c);
            Sprite icon = LiberatorWeaponBuilder.RenderIcon(prefab, $"{IconDir}/icon_{c.file}.png");
            WeaponData data = BuildData(prefab, icon, c);
            if (c.special) shop.Add(data);
            log.AppendLine($"  ✓ {c.display} ({data.type}/{data.specialKind} {data.rarity}) dmg={data.damage}");
        }
        AssetDatabase.SaveAssets();
        AddMuzzleLightToAll();
        BuildDatabase(shop);
        Debug.Log("[SpecialWeaponBuilder] 완료\n" + log);
    }

    // ── 프리팹: 원본 통째로 Model 밑에 넣고, 손잡이 원점 / +Z 총구 좌표계로 맞춤 ──
    static GameObject BuildPrefab(GameObject src, Cfg c)
    {
        var inst = (GameObject)Object.Instantiate(src);
        inst.name = src.name;
        inst.transform.position = Vector3.zero;
        try
        {
            var verts = new List<Vector3>();
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null) { var m = mf.transform.localToWorldMatrix; foreach (var v in mf.sharedMesh.vertices) verts.Add(m.MultiplyPoint3x4(v)); }
            // 스킨 메시(미니건)는 sharedMesh 정점이 바인드 포즈라 실제 모양과 다름 → 현재 뼈 자세로 구워서 측정
            foreach (var sm in inst.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (sm.sharedMesh != null)
                {
                    var baked = new Mesh(); sm.BakeMesh(baked, true);
                    var m = sm.transform.localToWorldMatrix; foreach (var v in baked.vertices) verts.Add(m.MultiplyPoint3x4(v));
                    Object.DestroyImmediate(baked);
                    sm.updateWhenOffscreen = true; // 오래된 localBounds(9m)로 컬링/아이콘 크기가 틀어지지 않게
                }

            // 원본에 같이 들어온 카메라·조명(Blender 씬) 제거 —— 남기면 손에 든 총이 카메라로 렌더하거나 조명 5개가 켜짐
            foreach (var cam in inst.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(cam.gameObject);
            foreach (var li in inst.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(li.gameObject);
            foreach (var an in inst.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(an);
            if (c.recolor != null) Recolor(inst, c);

            // 총구 방향 축으로 앞/뒤 끝, 위 축으로 높이
            Vector3 f = c.fwd.normalized, u = c.up.normalized;
            float lo = float.MaxValue, hi = float.MinValue, ylo = float.MaxValue, yhi = float.MinValue;
            Vector3 sum = Vector3.zero;
            foreach (var v in verts) { float t = Vector3.Dot(v, f), y = Vector3.Dot(v, u); lo = Mathf.Min(lo, t); hi = Mathf.Max(hi, t); ylo = Mathf.Min(ylo, y); yhi = Mathf.Max(yhi, y); sum += v; }
            float len = hi - lo;
            float scale = c.length / len;
            Vector3 center = sum / Mathf.Max(1, verts.Count);
            Vector3 side = Vector3.Cross(u, f);
            float sideCenter = Vector3.Dot(center, side);

            // 손잡이(원점): 뒤에서 gripT, 높이 60%, 좌우는 정점 평균
            Vector3 grip = f * (lo + len * c.gripT) + u * (ylo + (yhi - ylo) * 0.6f) + side * sideCenter;
            // 총구 높이 = 앞쪽 3% 정점 평균 높이
            double ys = 0; int yn = 0;
            foreach (var v in verts) if (Vector3.Dot(v, f) >= hi - len * 0.03f) { ys += Vector3.Dot(v, u); yn++; }
            float muzzleY = yn > 0 ? (float)(ys / yn) : (ylo + yhi) * 0.5f;

            var root = new GameObject("SW_" + c.file);
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            // 원본 → (손잡이 원점, f→+Z, u→+Y) 좌표
            Quaternion toLocal = Quaternion.Inverse(Quaternion.LookRotation(f, u));
            inst.transform.SetParent(model.transform, true);
            inst.transform.localPosition = toLocal * (inst.transform.position - grip);
            inst.transform.localRotation = toLocal * inst.transform.rotation;
            model.transform.localScale = Vector3.one * scale;

            float gripU = Vector3.Dot(grip, u);
            var muzzle = new GameObject("MuzzlePoint").transform;
            muzzle.SetParent(root.transform, false);
            muzzle.localPosition = new Vector3(0f, (muzzleY - gripU) * scale, len * (1f - c.gripT) * scale);

            var left = new GameObject("LeftHandPoint").transform;
            left.SetParent(root.transform, false);
            if (c.melee)
                left.localPosition = new Vector3(0f, -0.05f, -0.04f); // 한 손 무기 (도끼와 같음)
            else
            {
                left.localPosition = new Vector3(0f, (muzzleY - gripU) * scale - 0.01f, len * (c.leftT - c.gripT) * scale);
                left.localRotation = Quaternion.Euler(0f, 0f, 90f); // AR_A_1과 동일
            }

            string path = $"{PrefabDir}/{root.name}.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }
        finally { if (inst != null) Object.DestroyImmediate(inst); }
    }

    // 원본 재질 이름 앞부분이 맞으면 같은 이름의 새 재질(Standard, 색만)로 교체
    static void Recolor(GameObject inst, Cfg c)
    {
        Directory.CreateDirectory(MaterialDir);
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                foreach (var (prefix, color, metal) in c.recolor)
                    if (mats[i] != null && mats[i].name.StartsWith(prefix))
                    {
                        string path = $"{MaterialDir}/SW_{c.file}_{prefix}.mat";
                        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
                        m.color = color; m.SetFloat("_Metallic", metal); m.SetFloat("_Glossiness", 0.35f);
                        EditorUtility.SetDirty(m);
                        mats[i] = m;
                    }
            r.sharedMaterials = mats;
        }
    }

    static WeaponData BuildData(GameObject prefab, Sprite icon, Cfg c)
    {
        string path = $"{(c.special ? SpecialDataDir : SniperDataDir)}/{c.file}.asset";
        var d = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
        bool isNew = d == null;
        if (isNew) d = ScriptableObject.CreateInstance<WeaponData>();
        d.itemName = c.display; d.icon = icon; d.itemType = ItemType.Weapon;
        d.isStackable = false; d.maxStackSize = 1;
        d.weaponPrefab = prefab;
        d.rarity = Rarity.Legendary; // 특수 무기는 전설 (스나이퍼는 stats에서 덮어씀)
        d.holsterRotation = BackpackManager.DefaultHolsterRotation;
        // 손 위치 기본값: AR과 같음 (근접은 stats에서 덮어씀)
        d.positionOffset = new Vector3(0.01f, 0.06f, -0.2f); d.rotationOffset = new Vector3(30f, 170f, 0f);
        c.stats(d);
        AssignSpecialSounds(d);
        if (isNew) AssetDatabase.CreateAsset(d, path); else EditorUtility.SetDirty(d);
        return d;
    }

    // (2026-11-22) 특수 무기 소리: Assets/Audio/weapon/sp/ → 반복(화염방사기·전기톱·미니건) / 1회(RPG·레이저). SpecialWeaponAudio가 재생
    const string SpecialSoundDir = "Assets/Audio/weapon/sp";
    static void AssignSpecialSounds(WeaponData d)
    {
        AudioClip Clip(string f) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{SpecialSoundDir}/{f}.mp3");
        switch (d.specialKind)
        {
            case SpecialWeaponKind.Flamethrower: d.loopSound = Clip("flamethrower"); break;
            case SpecialWeaponKind.Chainsaw: d.loopSound = Clip("chainsaw"); break;
            case SpecialWeaponKind.Minigun: d.loopSound = Clip("minigun"); break;
            case SpecialWeaponKind.RPG: d.fireSound = Clip("rpg"); break;
            case SpecialWeaponKind.Laser: d.fireSound = Clip("laser"); break;
        }
    }

    [MenuItem("Tools/Weapons/Assign Special Weapon Sounds")]
    public static void AssignAllSpecialSounds()
    {
        int n = 0;
        foreach (var g in AssetDatabase.FindAssets("t:WeaponData", new[] { SpecialDataDir }))
        {
            var d = AssetDatabase.LoadAssetAtPath<WeaponData>(AssetDatabase.GUIDToAssetPath(g));
            if (d == null || d.specialKind == SpecialWeaponKind.None) continue;
            AssignSpecialSounds(d); EditorUtility.SetDirty(d); n++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[SpecialWeapons] 특수 무기 소리 연결: {n}개");
    }

    // ── Resources/WeaponDatabase: Assets/Items/Weapons 아래 모든 WeaponData + 상점 전용 목록 ──
    static void BuildDatabase(List<WeaponData> shop)
    {
        var db = AssetDatabase.LoadAssetAtPath<WeaponDatabase>(DatabasePath);
        if (db == null) { db = ScriptableObject.CreateInstance<WeaponDatabase>(); AssetDatabase.CreateAsset(db, DatabasePath); }
        db.weapons = new List<WeaponData>();
        foreach (var g in AssetDatabase.FindAssets("t:WeaponData", new[] { "Assets/Items/Weapons" }))
        {
            var w = AssetDatabase.LoadAssetAtPath<WeaponData>(AssetDatabase.GUIDToAssetPath(g));
            if (w != null) db.weapons.Add(w);
        }
        db.shopWeapons = shop;
        EditorUtility.SetDirty(db);
        AssetDatabase.SaveAssets();
        Debug.Log($"[SpecialWeaponBuilder] WeaponDatabase: 무기 {db.weapons.Count}개 (상점 전용 {shop.Count}개)");
    }

    // ── 발사 섬광 재질 (2026-11-17): Resources/FX/MuzzleFlash.mat (가산 합성 파티클 셰이더) + 코드로 그린 섬광 텍스처 ──
    //  (이전 1차) 흰색 원 + 4갈래 줄기, 색은 _TintColor로 → 약하고 뭉개짐
    //  (2차) 색이 들어간 별 모양: 중앙 흰(FFFFFF) → 노랑(FFE060) → 주황(FF8020), 긴 4갈래 + 짧은 대각 4갈래. 실행할 때마다 다시 그림
    const string FlashTexPath = "Assets/Resources/FX/muzzle_flash.png";
    const string FlashMatPath = "Assets/Resources/FX/MuzzleFlash.mat";

    public static Material EnsureMuzzleFlashMaterial()
    {
        Directory.CreateDirectory("Assets/Resources/FX");
        {
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            Color white = Color.white, yellow = new Color32(0xFF, 0xE0, 0x60, 0xFF), orange = new Color32(0xFF, 0x80, 0x20, 0xFF);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float ang = Mathf.Atan2(v, u);
                    float core = Mathf.Clamp01(1f - r / 0.28f);                                         // 가운데 밝은 원
                    float glow = Mathf.Clamp01(1f - r / 0.55f) * 0.55f;                                 // 주변 번짐
                    float longRays = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 2f)), 40f) * Mathf.Clamp01(1f - r);          // 십자 (끝까지)
                    float shortRays = Mathf.Pow(Mathf.Abs(Mathf.Sin(ang * 2f)), 60f) * Mathf.Clamp01(1f - r / 0.6f) * 0.8f; // 대각선 (짧게)
                    float a = Mathf.Clamp01(core + glow + longRays + shortRays);
                    // 색: 중심 흰 → 노랑 → 주황
                    Color c = r < 0.18f ? white : r < 0.45f ? Color.Lerp(white, yellow, (r - 0.18f) / 0.27f) : Color.Lerp(yellow, orange, Mathf.Clamp01((r - 0.45f) / 0.5f));
                    c.a = a;
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            File.WriteAllBytes(FlashTexPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(FlashTexPath);
            var imp = (TextureImporter)AssetImporter.GetAtPath(FlashTexPath);
            imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
        }
        var mat = AssetDatabase.LoadAssetAtPath<Material>(FlashMatPath);
        if (mat == null)
        {
            var sh = Shader.Find("Legacy Shaders/Particles/Additive");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            mat = new Material(sh) { name = "MuzzleFlash" };
            AssetDatabase.CreateAsset(mat, FlashMatPath);
        }
        mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(FlashTexPath);
        if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f)); // 텍스처 색 그대로 (파티클 가산은 틴트 ×2)
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }

    // ── 총구 불빛 일괄 부착 ──
    static readonly string[] MuzzleNames = { "MuzzlePoint", "Muzzle", "FirePoint" };

    [MenuItem("Tools/Weapons/Add Muzzle Light to All")]
    public static void AddMuzzleLightToAll()
    {
        EnsureMuzzleFlashMaterial(); // 2026-11-17 발사 섬광 Quad 재질
        int added = 0, kept = 0, noMuzzle = 0;
        var skipped = new System.Text.StringBuilder();
        var done = new HashSet<GameObject>();
        foreach (var g in AssetDatabase.FindAssets("t:WeaponData"))
        {
            var d = AssetDatabase.LoadAssetAtPath<WeaponData>(AssetDatabase.GUIDToAssetPath(g));
            if (d == null || d.type == WeaponType.Melee || d.weaponPrefab == null || !done.Add(d.weaponPrefab)) continue;
            string path = AssetDatabase.GetAssetPath(d.weaponPrefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform muzzle = null;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (System.Array.IndexOf(MuzzleNames, t.name) >= 0) { muzzle = t; break; }
                // 총구 없는 옛 프리팹(구 무기팩 ShotGun_B → Assets/Prefab/Weapon/Shotgun_slow, 루팅에 없음)은 건너뛰고 요약에만 표시
                if (muzzle == null) { noMuzzle++; skipped.AppendLine("  - " + path); continue; }
                if (muzzle.GetComponentInChildren<MuzzleLight>(true) != null) { kept++; continue; }
                var go = new GameObject("MuzzleLight");
                go.transform.SetParent(muzzle, false);
                go.transform.localPosition = new Vector3(0f, 0f, 0.05f); // 총구 바로 앞
                var li = go.AddComponent<Light>();
                // li.type = LightType.Point; li.color = Yellow; li.intensity = 0.8f; li.range = 2.5f; // (이전) 바닥에 원이 생김
                li.type = LightType.Spot; li.color = Yellow; li.intensity = 2f; li.range = 6f; li.spotAngle = 28f; // 2026-11-17 조준등 (2차: 28°, 2)
                li.shadows = LightShadows.None; li.enabled = false; // 손에 들 때 Weapon이 켬
                go.AddComponent<MuzzleLight>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                added++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        Debug.Log($"[MuzzleLight] 부착 {added}개, 이미 있음 {kept}개, 총구 없어서 건너뜀 {noMuzzle}개\n{skipped}");
    }
}
