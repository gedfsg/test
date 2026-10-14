using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Liberator Gun Pack(GLB) → 무기 프리팹 + 아이콘 + WeaponData 자동 생성.
/// 메뉴: Tools/Weapons/Build Liberator Weapons (다시 실행하면 같은 경로에 덮어씀)
///
/// 프리팹 규칙은 기존 AR_A_1 프리팹과 동일 (WeaponHandAttacher가 그대로 쓰도록):
///  - 루트 원점 = 방아쇠/손잡이 근처, 총구 방향 = +Z, 위 = +Y
///  - 자식 MuzzlePoint(총구 끝, +Z 방향), LeftHandPoint(왼손 위치)
///  - 모델은 "Model" 자식에 0.5배 축소 (GLB 원본이 실제 크기의 약 2배)
/// 분류: 권총/샷건/AR/SMG + 티어(Common/Rare/Epic). 저격총·기관총·탄약·부착물은 제외.
/// SMG는 AR 탄약을 공유하므로 WeaponType.AR로 저장 (이름으로 구분).
/// </summary>
public static class LiberatorWeaponBuilder
{
    const string GlbPath = "Assets/LiberatorGuns/low_poly_colored_liberator_gun_pack.glb";
    const string PrefabDir = "Assets/LiberatorGuns/Prefabs";
    const string IconDir = "Assets/LiberatorGuns/Icons";
    const string DataDir = "Assets/Items/Weapons/Liberator";
    const float ModelScale = 0.5f;

    enum Cat { Pistol, Shotgun, AR, SMG, Melee }

    struct Entry
    {
        public string node, display; public Cat cat; public Rarity tier;
        public Entry(string node, string display, Cat cat, Rarity tier) { this.node = node; this.display = display; this.cat = cat; this.tier = tier; }
    }

    // GLB 노드 이름(뒤의 "_숫자" 제외) → 표시 이름 / 종류 / 티어
    static readonly Entry[] Table =
    {
        // 권총 (P탄약, 15발) 10/15/20
        new Entry("G-17", "G17", Cat.Pistol, Rarity.Common),
        new Entry("P9", "P9", Cat.Pistol, Rarity.Common),
        new Entry("FN FIVE-SEVEN", "Five-seveN", Cat.Pistol, Rarity.Rare),
        new Entry("RARAN Prototype 1130", "RARAN 1130", Cat.Pistol, Rarity.Rare),
        new Entry("IWI/MRI Deagle", "Deagle", Cat.Pistol, Rarity.Epic),
        new Entry("Blazeson & Julius 357", "B&J .357", Cat.Pistol, Rarity.Epic),
        // 샷건 (SG탄약, 6발, 산탄 5발) 40/60/80
        new Entry("S12P8", "S12P8", Cat.Shotgun, Rarity.Common),
        new Entry("XMA-10", "XMA-10", Cat.Shotgun, Rarity.Rare),
        new Entry("SAIGA-12", "SAIGA-12", Cat.Shotgun, Rarity.Rare),
        new Entry("AA-12", "AA-12", Cat.Shotgun, Rarity.Epic),
        // AR (AR탄약, 30발) 20/25/30
        new Entry("AK-556", "AK-556", Cat.AR, Rarity.Common),
        new Entry("AR-15", "AR-15", Cat.AR, Rarity.Common),
        new Entry("H&K G36K", "G36K", Cat.AR, Rarity.Common),
        new Entry("FAMAS F1", "FAMAS", Cat.AR, Rarity.Common),
        new Entry("L85A2", "L85A2", Cat.AR, Rarity.Rare),
        new Entry("Steyr AUG", "AUG", Cat.AR, Rarity.Rare),
        new Entry("IMI Galil-762", "Galil", Cat.AR, Rarity.Rare),
        new Entry("OTs-14 Groza", "Groza", Cat.AR, Rarity.Rare),
        new Entry("FN SCAR-L", "SCAR-L", Cat.AR, Rarity.Epic),
        new Entry("FN SCAR-H", "SCAR-H", Cat.AR, Rarity.Epic),
        new Entry("H&K G3", "G3", Cat.AR, Rarity.Epic),
        new Entry("AS-762", "AS-762", Cat.AR, Rarity.Epic),
        new Entry("AAC Honey Badger", "Honey Badger", Cat.AR, Rarity.Epic),
        // SMG (AR탄약 공유, 30발, 빠른 연사) 12/16/20
        new Entry("Skorpion", "Skorpion", Cat.SMG, Rarity.Common),
        new Entry("H&K UMP45", "UMP45", Cat.SMG, Rarity.Common),
        new Entry("H&K MP7", "MP7", Cat.SMG, Rarity.Common),
        new Entry("KRISS VECTOR", "Vector", Cat.SMG, Rarity.Rare),
        new Entry("FN P90", "P90", Cat.SMG, Rarity.Rare),
        new Entry("SR-3m", "SR-3M", Cat.SMG, Rarity.Epic),
        // 근접 (도끼 대신 팩에 있는 토마호크)
        new Entry("Tomahawk", "Axe", Cat.Melee, Rarity.Common),
    };

    [MenuItem("Tools/Weapons/Build Liberator Weapons")]
    public static void BuildAll()
    {
        var glb = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
        if (glb == null) { Debug.LogError("[LiberatorWeaponBuilder] GLB 없음: " + GlbPath); return; }
        foreach (var d in new[] { PrefabDir, IconDir, DataDir }) Directory.CreateDirectory(d);

        var inst = (GameObject)Object.Instantiate(glb);
        inst.transform.position = Vector3.zero;
        var log = new System.Text.StringBuilder();
        try
        {
            foreach (var e in Table)
            {
                Transform node = FindNode(inst.transform, e.node);
                if (node == null) { log.AppendLine($"  ✗ 노드 못 찾음: {e.node}"); continue; }
                GameObject prefab = BuildPrefab(node, e);
                Sprite icon = BuildIcon(prefab, e);
                WeaponData data = BuildData(prefab, icon, e);
                log.AppendLine($"  ✓ {e.display} ({e.cat} {e.tier}) dmg={data.damage}");
            }
        }
        finally { Object.DestroyImmediate(inst); }
        AssetDatabase.SaveAssets();
        Debug.Log("[LiberatorWeaponBuilder] 완료\n" + log);
    }

    // "AK-556_28"처럼 뒤에 "_숫자"가 붙은 이름을 접두어로 찾음 (이름에 '/'가 있어서 Transform.Find 못 씀)
    static Transform FindNode(Transform root, string prefix)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            int us = t.name.LastIndexOf('_');
            if (us > 0 && t.name.Substring(0, us).TrimEnd() == prefix && t.GetComponentInChildren<MeshFilter>() != null) return t;
        }
        return null;
    }

    // ── 프리팹 ─────────────────────────────────
    static GameObject BuildPrefab(Transform node, Entry e)
    {
        // 월드 좌표 정점 수집
        var verts = new List<Vector3>();
        foreach (var mf in node.GetComponentsInChildren<MeshFilter>())
        {
            var m = mf.sharedMesh; if (m == null) continue;
            var mat = mf.transform.localToWorldMatrix;
            foreach (var v in m.vertices) verts.Add(mat.MultiplyPoint3x4(v));
        }
        Bounds b = new Bounds(verts[0], Vector3.zero);
        foreach (var v in verts) b.Encapsulate(v);

        // 긴 축(X 또는 Z) = 총열 방향. 양 끝 8% 구간의 세로 폭이 작은 쪽이 총구(개머리판/손잡이 쪽이 더 높음)
        Vector3 axis = b.size.x > b.size.z ? Vector3.right : Vector3.forward;
        float lo = Vector3.Dot(b.min, axis), hi = Vector3.Dot(b.max, axis), len = hi - lo;
        float spanLo = Span(verts, axis, lo, lo + len * 0.08f), spanHi = Span(verts, axis, hi - len * 0.08f, hi);
        bool muzzleAtHi = e.cat == Cat.Melee ? spanHi > spanLo : spanHi < spanLo; // 도끼는 날(넓은 쪽)이 앞
        Vector3 fwd = muzzleAtHi ? axis : -axis;

        // 손잡이 원점: 뒤에서 37% 지점, 아래에서 60% 높이 (AR_A_1/Pistol_K 실측 비율). 도끼는 손잡이 끝에서 15%
        float gripT = e.cat == Cat.Melee ? 0.15f : 0.37f;
        float gripAxis = muzzleAtHi ? lo + len * gripT : hi - len * gripT;
        Vector3 grip = SetAlong(b.center, axis, gripAxis);
        grip.y = b.min.y + b.size.y * (e.cat == Cat.Melee ? 0.5f : 0.6f);

        // 총구 높이 = 앞쪽 3% 정점들의 평균 높이
        float frontLo = muzzleAtHi ? hi - len * 0.03f : lo, frontHi = muzzleAtHi ? hi : lo + len * 0.03f;
        float muzzleY = AvgY(verts, axis, frontLo, frontHi, b.center.y);

        // 루트 = 손잡이 위치, +Z = 총구 방향
        var root = new GameObject("LG_" + Safe(e.display));
        var model = new GameObject("Model");
        model.transform.SetParent(root.transform, false);
        var pivot = new GameObject("_pivot");
        pivot.transform.SetPositionAndRotation(grip, Quaternion.LookRotation(fwd, Vector3.up));
        var copy = Object.Instantiate(node.gameObject);
        copy.name = node.name;
        copy.transform.SetPositionAndRotation(node.position, node.rotation);
        // pivot(손잡이 위치, 총구 방향) 기준 좌표로 바꿔서 Model 밑에 넣음 → "손잡이 원점, +Z 총구" 좌표계
        Matrix4x4 toLocal = pivot.transform.worldToLocalMatrix;
        Vector3 lp = toLocal.MultiplyPoint3x4(copy.transform.position);
        Quaternion lr = Quaternion.Inverse(pivot.transform.rotation) * copy.transform.rotation;
        copy.transform.SetParent(model.transform, false);
        copy.transform.localPosition = lp;
        copy.transform.localRotation = lr;
        copy.transform.localScale = node.lossyScale;
        model.transform.localScale = Vector3.one * ModelScale;
        Object.DestroyImmediate(pivot);

        float frontLocal = len * (1f - gripT) * ModelScale;
        float muzzleLocalY = (muzzleY - grip.y) * ModelScale;
        var muzzle = new GameObject("MuzzlePoint").transform;
        muzzle.SetParent(root.transform, false);
        muzzle.localPosition = new Vector3(0f, muzzleLocalY, frontLocal);

        var left = new GameObject("LeftHandPoint").transform;
        left.SetParent(root.transform, false);
        if (e.cat == Cat.Pistol || e.cat == Cat.Melee)
            left.localPosition = new Vector3(0f, -0.05f, -0.04f); // Pistol_K와 동일 (한 손 무기라 손잡이 근처)
        else
        {
            left.localPosition = new Vector3(0f, muzzleLocalY - 0.01f, len * (0.68f - gripT) * ModelScale);
            left.localRotation = Quaternion.Euler(0f, 0f, 90f); // AR_A_1과 동일
        }

        string path = $"{PrefabDir}/{root.name}.prefab";
        var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return saved;
    }

    static Vector3 SetAlong(Vector3 p, Vector3 axis, float value) => p + axis * (value - Vector3.Dot(p, axis));

    static float Span(List<Vector3> verts, Vector3 axis, float a, float b)
    {
        float mn = float.MaxValue, mx = float.MinValue;
        foreach (var v in verts) { float t = Vector3.Dot(v, axis); if (t < a || t > b) continue; mn = Mathf.Min(mn, v.y); mx = Mathf.Max(mx, v.y); }
        return mx > mn ? mx - mn : 0f;
    }

    static float AvgY(List<Vector3> verts, Vector3 axis, float a, float b, float fallback)
    {
        double sum = 0; int n = 0;
        foreach (var v in verts) { float t = Vector3.Dot(v, axis); if (t < a || t > b) continue; sum += v.y; n++; }
        return n > 0 ? (float)(sum / n) : fallback;
    }

    static string Safe(string s) => s.Replace("&", "and").Replace(".", "").Replace(" ", "_").Replace("/", "_");

    // ── 아이콘 (옆에서 본 정사영, 투명 배경, 총구가 오른쪽) ──
    static Sprite BuildIcon(GameObject prefab, Entry e)
    {
        const int W = 256, H = 128;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.position = new Vector3(0f, -5000f, 0f);
        Bounds b = new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);

        var camGo = new GameObject("_IconCam"); var cam = camGo.AddComponent<Camera>();
        cam.enabled = false; cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0); cam.nearClipPlane = 0.01f; cam.farClipPlane = 50f;
        camGo.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up); // 오른쪽 = +Z(총구)
        camGo.transform.position = b.center + Vector3.right * 10f;
        float aspect = (float)W / H;
        cam.aspect = aspect;
        cam.orthographicSize = Mathf.Max(b.extents.y, b.extents.z / aspect) * 1.1f;
        var lightGo = new GameObject("_IconLight"); var li = lightGo.AddComponent<Light>();
        li.type = LightType.Directional; li.intensity = 1.2f; lightGo.transform.rotation = Quaternion.LookRotation(new Vector3(-1f, -0.6f, 0.3f));

        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt; cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        RenderTexture.active = prev; cam.targetTexture = null;
        var px = tex.GetPixels32();
        for (int i = 0; i < px.Length; i++) if (px[i].a > 0) px[i].a = 255; // 불투명 셰이더가 알파를 덜 쓰는 경우 보정
        tex.SetPixels32(px); tex.Apply();
        string path = $"{IconDir}/icon_{Safe(e.display)}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo); Object.DestroyImmediate(go);

        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single; imp.alphaIsTransparency = true; imp.mipmapEnabled = false;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ── WeaponData ──
    static WeaponData BuildData(GameObject prefab, Sprite icon, Entry e)
    {
        string path = $"{DataDir}/{Safe(e.display)}.asset";
        var d = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
        bool isNew = d == null;
        if (isNew) d = ScriptableObject.CreateInstance<WeaponData>();

        int t = (int)e.tier; // 0 Common, 1 Rare, 2 Epic
        d.itemName = e.display; d.icon = icon; d.itemType = ItemType.Weapon; d.rarity = e.tier;
        d.isStackable = false; d.maxStackSize = 1;
        d.weaponPrefab = prefab;
        d.penetrating = false;
        d.holsterRotation = BackpackManager.DefaultHolsterRotation; // 등에 멜 때: 총구 왼쪽 위 대각선
        switch (e.cat)
        {
            case Cat.Pistol:
                d.type = WeaponType.Pistol; d.damage = new[] { 10f, 15f, 20f }[t]; d.maxAmmo = 15;
                d.attackRate = 0.25f; d.autoFire = false; d.reloadTime = 1.5f;
                d.bulletSpeed = 80f; d.effectiveRange = 25f; d.recoil = 2f;
                d.positionOffset = new Vector3(0.01f, 0.06f, -0.2f); d.rotationOffset = new Vector3(30f, 200f, -20f); // Pistol K와 동일
                break;
            case Cat.Shotgun:
                // 산탄 5발 전부 맞으면 40/60/80 → 한 발당 8/12/16
                d.type = WeaponType.Shotgun; d.damage = new[] { 8f, 12f, 16f }[t]; d.pelletCount = 5; d.spreadAngle = 8f;
                d.maxAmmo = 6; d.attackRate = 0.8f; d.autoFire = false; d.reloadTime = 2.5f;
                d.bulletSpeed = 80f; d.effectiveRange = 15f; d.recoil = 0f;
                d.positionOffset = new Vector3(0.01f, 0.06f, -0.2f); d.rotationOffset = new Vector3(30f, 170f, 0f);
                break;
            case Cat.AR:
                d.type = WeaponType.AR; d.damage = new[] { 20f, 25f, 30f }[t]; d.maxAmmo = 30;
                d.attackRate = 0.1f; d.autoFire = true; d.reloadTime = 2f;
                d.bulletSpeed = 150f; d.effectiveRange = 40f; d.recoil = 2f;
                d.positionOffset = new Vector3(0.01f, 0.06f, -0.2f); d.rotationOffset = new Vector3(30f, 170f, 0f); // AR_A와 동일
                break;
            case Cat.SMG:
                d.type = WeaponType.AR; // AR 탄약 공유
                d.damage = new[] { 12f, 16f, 20f }[t]; d.maxAmmo = 30;
                d.attackRate = 0.06f; d.autoFire = true; d.reloadTime = 1.6f;
                d.bulletSpeed = 120f; d.effectiveRange = 25f; d.recoil = 3f;
                d.positionOffset = new Vector3(0.01f, 0.06f, -0.2f); d.rotationOffset = new Vector3(30f, 170f, 0f);
                break;
            case Cat.Melee:
                // 도끼: 데미지 35, 사거리 2m, 쿨타임 0.5초
                d.type = WeaponType.Melee; d.damage = 35f; d.attackRate = 0.5f; d.effectiveRange = 2f; d.maxAmmo = 0;
                d.autoFire = false;
                // 휘두를 때 (손 뼈 측정: 손가락 -Z, 엄지 +Y, 손바닥 +X / 도끼: 손잡이 +Z, 날 +Y):
                // 주먹 쥔 방향 = 손잡이가 엄지 쪽으로 서고 날이 손가락(앞) 쪽. 이전 (0,-90,0)은 손잡이가 팔 연장선이라 끝을 쥔 것처럼 보였음
                d.positionOffset = Vector3.zero; d.rotationOffset = new Vector3(-90f, 0f, 0f);
                // 들고 다닐 때 (Play 촬영 비교): 서 있을 땐 다리 옆으로 곧게 늘어뜨려 날이 아래, 움직일 땐 손 아래로 매달림
                d.carryRotationOffset = new Vector3(0f, 180f, 90f);
                d.carryMoveRotationOffset = new Vector3(90f, 0f, 0f);
                // 휘두를 때 손 위치: 손바닥 중심(손목에서 손가락 쪽 0.09, 손바닥 쪽 0.035)에 손잡이 아래쪽 35% 지점이 오게
                // (프리팹 원점은 손잡이 15% 지점 → 손잡이 방향(엄지 쪽 +Y)으로 0.06 내림)
                d.swingPositionOffset = new Vector3(0.035f, -0.06f, -0.09f);
                // 들고 다닐 때도 위와 같은 주먹 쥐기로 고정 (carry 값과 섞으면 뛸 때/공격 끝날 때 손 안에서 돌아 흐물거림)
                d.rigidGrip = true;
                break;
        }
        if (isNew) AssetDatabase.CreateAsset(d, path); else EditorUtility.SetDirty(d);
        return d;
    }
}
