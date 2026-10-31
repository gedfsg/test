using UnityEngine;

/// <summary>
/// 플레이어가 건물 안(축소된 트리거)에 들어가면 1층 높이(groundFloorHeight) 위쪽을
/// 셰이더로 완전히 잘라내서(discard) 안 보이게 함 — 지붕은 물론 2층 이상도 다 사라지고
/// 1층만 남음. 알파 블렌딩이 아니라 진짜로 안 그려지는 방식. 동시에 Fog로 주변을 어둡게
/// 만들어 지금 있는 방 주변만 보이게 함(좀보이드 스타일). 건물 콜라이더(문 있는 벽 1면 +
/// 막힌 벽 3면)/트리거 세팅은 _TempApplyBuildingInterior.cs(에디터 배치 툴)에서 담당.
/// </summary>
[RequireComponent(typeof(Collider))]
public class BuildingInterior : MonoBehaviour
{
    /// <summary>플레이어가 건물에 들어갈 때마다 (미션: 건물 방문 판정용)</summary>
    public static event System.Action<BuildingInterior> PlayerEntered;

    [Header("1층 높이 (건물 바닥에서 이 높이 위는 전부 잘라내서 1층만 보이게 함)")]
    public float groundFloorHeight = 5f;

    public string playerTag = "Player";

    [Header("실내 시야 제한 (좀보이드 스타일 - 안에 들어가면 주변이 어두워짐)")]
    public bool darkenSurroundings = true;
    public float indoorFogStartDistance = 2f;
    public float indoorFogEndDistance = 8f;
    public Color indoorFogColor = Color.black;

    [Header("실내에서 바깥 완전히 가리기 (검정 마스크)")]
    [Tooltip("실내에 있는 동안 벽 잘린 높이에 '실내만 뚫린 검정 판 + 안쪽만 보이는 검정 벽'을 켬. " +
             "벽에 바짝 붙어도 벽 너머 바깥이 안 보임")]
    public bool hideOutsideWhenIndoor = true;
    [Tooltip("마스크 구멍을 실내(트리거)보다 이만큼(m) 넓힘 → 벽 윗면(캡)까지는 보이게")]
    public float outsideMaskMargin = 0.6f;
    [Tooltip("구멍 둘레에서 위로 세우는 검정 벽 높이(m). 옆 고층 건물이 판 위로 솟아 보이는 것 방지")]
    public float outsideMaskWallHeight = 60f;
    [Tooltip("화면 마스크: 보이는 표면이 실내 상자 밖이면 검정으로 덮음. Q/E로 카메라를 돌려도 " +
             "카메라 쪽 옆 건물 지붕이 실내를 가리거나 화면에 보이지 않게 함")]
    public bool useScreenMask = true;
    [Tooltip("화면 마스크 사용 시, 실내 상자 밖의 고정 메시(옆 건물/소품)를 이 반경(m) 안에서 숨김. " +
             "카메라와 실내 사이에 있는 지붕이 실내를 가리지 않게 함 (어차피 검정으로 덮이는 부분)")]
    public float hideOccluderRadius = 90f;

    static readonly int CutHeightId = Shader.PropertyToID("_CutHeight");
    static readonly int CutEnabledId = Shader.PropertyToID("_CutEnabled");

    // 여러 건물이 겹쳐 있어도(모서리 등) 안개가 꼬이지 않게 전역 카운터로 관리.
    // 실외 원래 안개 설정은 맨 처음 건물에 들어갈 때 한 번만 저장해뒀다가 마지막에 나갈 때 복원.
    static int globalInsideCount = 0;
    public static bool IsAnyoneIndoors => globalInsideCount > 0;

    // 플레이어가 지금 들어가 있는 건물 (겹친 트리거면 마지막에 들어간 곳). 좀비 입구 대기 금지용
    static readonly System.Collections.Generic.List<BuildingInterior> occupied = new System.Collections.Generic.List<BuildingInterior>();
    public static BuildingInterior Current => occupied.Count > 0 ? occupied[occupied.Count - 1] : null;

    // 건물에서 나온 직후 안전 창: 입구 SafeExitRadius 안 좀비는 SafeExitSeconds 동안 공격 안 함
    public const float SafeExitSeconds = 1.5f, SafeExitRadius = 7f;
    static float safeExitUntil = -1f;
    static Vector3 safeExitDoor;

    /// <summary>pos가 지금 "건물에서 막 나온 입구" 안전 반경 안인지 (공격 금지 판정)</summary>
    public static bool InSafeExit(Vector3 pos)
    {
        if (Time.time >= safeExitUntil) return false;
        float dx = pos.x - safeExitDoor.x, dz = pos.z - safeExitDoor.z;
        return dx * dx + dz * dz < SafeExitRadius * SafeExitRadius;
    }

    /// <summary>pos가 이 건물 실내 트리거 안인지</summary>
    public bool ContainsPoint(Vector3 pos)
    {
        var t = FindTriggerCollider();
        return t != null && t.bounds.Contains(pos);
    }

    // ── 트리거를 실제 벽 안쪽으로 맞추기 ──
    // 씬의 트리거는 렌더러 바운딩 박스 기준이라 벽 밖으로 삐져나온 곳이 많음 (마트 현관 쪽 6m, 일부 건물 10m 이상) →
    // 건물 앞을 걷기만 해도 "실내"가 됨. 벽 콜라이더는 런타임에 만들어지므로 씬 로드 직후 실측해서 줄임 (늘리지는 않음)
    const float TriggerWallInset = 0.5f;  // 벽 안쪽으로 들이는 거리 (m)
    const float TriggerMinSize = 3f;      // 이보다 작게는 안 줄임 (m)
    const float WallProbeHeight = 1.2f;   // 지면에서 재는 높이 (m)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RegisterTriggerFit()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoadedFit;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoadedFit;
    }

    static void OnSceneLoadedFit(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (GameObject.FindWithTag("Player") == null) return;
        Physics.SyncTransforms();
        int fitted = 0;
        var all = FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None);
        foreach (var b in all) if (b.FitTriggerToWalls()) fitted++;
        Physics.SyncTransforms();
        Debug.Log($"[BuildingInterior] 실내 트리거 {fitted}/{all.Length}개를 벽 안쪽으로 맞춤");
    }

    bool FitTriggerToWalls()
    {
        var box = FindTriggerCollider() as BoxCollider;
        if (box == null) return false;
        bool changed = false;
        changed |= FitSide(box, 0, 1f);
        changed |= FitSide(box, 0, -1f);
        changed |= FitSide(box, 2, 1f);
        changed |= FitSide(box, 2, -1f);
        return changed;
    }

    // axis(0=x, 2=z) sign 방향 면: 바깥에서 안쪽으로 레이 9개 → 이 건물 벽에 처음 닿는 거리.
    // 25% 지점 값을 씀: 중앙값이면 ㄱ자 건물의 튀어나온 날개를 잘라내고, 최솟값이면 기둥 하나에 끌려감
    bool FitSide(BoxCollider box, int axis, float sign)
    {
        const float startOut = 8f;
        Transform tr = box.transform;
        Vector3 scale = tr.lossyScale;
        Vector3 dir = (axis == 0 ? tr.right : tr.forward) * sign;
        Vector3 lat = axis == 0 ? tr.forward : tr.right;
        float depth = Mathf.Abs(box.size[axis] * scale[axis]);
        int latAxis = axis == 0 ? 2 : 0;
        float width = Mathf.Abs(box.size[latAxis] * scale[latAxis]);

        Terrain terrain = Terrain.activeTerrain;
        Vector3 center = tr.TransformPoint(box.center);
        float floorY = box.bounds.min.y;
        if (terrain != null) floorY = Mathf.Max(floorY, terrain.SampleHeight(center) + terrain.transform.position.y);
        center.y = floorY + WallProbeHeight;

        var hits = new System.Collections.Generic.List<float>();
        for (int i = 1; i <= 9; i++)
        {
            Vector3 origin = center + dir * (depth / 2f + startOut) + lat * ((i / 10f - 0.5f) * width);
            float best = float.MaxValue;
            foreach (var h in Physics.RaycastAll(origin, -dir, startOut + depth, ~0, QueryTriggerInteraction.Ignore))
                if (h.distance < best && h.collider.transform.IsChildOf(transform)) best = h.distance;
            if (best < float.MaxValue) hits.Add(best - startOut); // 양수 = 벽이 트리거 면보다 안쪽 (삐져나옴)
        }
        if (hits.Count < 5) return false; // 벽을 제대로 못 잼 (이 높이에 벽이 없는 건물) → 그대로

        hits.Sort();
        float inward = hits[hits.Count / 4] + TriggerWallInset;
        if (inward <= 0.01f) return false;
        inward = Mathf.Min(inward, depth - TriggerMinSize);
        if (inward <= 0.01f) return false;

        float local = inward / Mathf.Abs(scale[axis]);
        Vector3 size = box.size, c = box.center;
        size[axis] -= local;
        // 로컬 축 방향과 스케일 부호에 맞춰 중심 이동 (반대쪽 면은 그대로)
        c[axis] -= sign * Mathf.Sign(scale[axis]) * local / 2f;
        box.size = size;
        box.center = c;
        return true;
    }

    // ── "실내" 표시 검증 ──
    // 트리거는 상자라 ㄱ자/ㄷ자 건물의 파인 곳(바깥)도 덮을 수 있음 → 머리 위에 이 건물 지붕이 있거나 사방 3면 이상이 벽이어야 실내로 표시
    const float EnclosedCheckInterval = 0.2f;
    static float nextEnclosedCheck;
    static bool enclosedCached;

    /// <summary>실내 트리거 안 + 건물 벽에 실제로 둘러싸여 있음 (HUD "실내" 표시용)</summary>
    public static bool IsPlayerEnclosed
    {
        get
        {
            BuildingInterior b = Current;
            if (b == null || fogPlayer == null) return false;
            if (Time.time < nextEnclosedCheck) return enclosedCached;
            nextEnclosedCheck = Time.time + EnclosedCheckInterval;
            Vector3 p = fogPlayer.position + Vector3.up * WallProbeHeight;
            enclosedCached = b.HitsOwnCollider(p, Vector3.up) || b.CountWallsAround(p) >= 3;
            return enclosedCached;
        }
    }

    int CountWallsAround(Vector3 pos)
    {
        int walls = 0;
        foreach (var d in new[] { transform.right, -transform.right, transform.forward, -transform.forward })
            if (HitsOwnCollider(pos, d)) walls++;
        return walls;
    }

    bool HitsOwnCollider(Vector3 pos, Vector3 dir)
    {
        foreach (var h in Physics.RaycastAll(pos, dir, 150f, ~0, QueryTriggerInteraction.Ignore))
            if (h.collider.transform.IsChildOf(transform)) return true;
        return false;
    }

    bool doorCached, hasDoor;
    Vector3 doorPos, doorOut;

    /// <summary>이 건물 문 중심 + 바깥 방향 (처음 쓸 때 한 번 계산)</summary>
    public bool TryGetDoor(out Vector3 door, out Vector3 outDir)
    {
        if (!doorCached)
        {
            doorCached = true;
            hasDoor = EntranceMarkers.TryGetDoor(this, out doorPos, out doorOut);
        }
        door = doorPos; outDir = doorOut;
        return hasDoor;
    }

    /// <summary>씬 다시 로드(재시작) 전에 호출 - static 카운터/저장값이 다음 판으로 넘어가 실내 안개가 남는 것 방지</summary>
    public static void ResetStatics()
    {
        globalInsideCount = 0;
        occupied.Clear();
        safeExitUntil = -1f;
        if (originalFogSaved)
        {
            RenderSettings.fog = originalFogEnabled; RenderSettings.fogMode = originalFogMode; RenderSettings.fogColor = originalFogColor;
            RenderSettings.fogStartDistance = originalFogStart; RenderSettings.fogEndDistance = originalFogEnd;
            RenderSettings.fogDensity = originalFogDensity;
        }
        originalFogSaved = false;
        hiddenCount.Clear();
        fogPlayer = null;
    }
    static bool originalFogSaved = false;
    static bool originalFogEnabled;
    static FogMode originalFogMode;
    static Color originalFogColor;
    static float originalFogStart, originalFogEnd, originalFogDensity;

    Renderer[] renderers;
    float cutHeightWorld;
    GameObject outsideMask;
    Material[][] instanceMats; // renderer별 인스턴스 머티리얼 슬롯

    void Awake()
    {
        Collider triggerCol = FindTriggerCollider();
        if (triggerCol != null) triggerCol.isTrigger = true;

        // 꺼진 렌더러(충돌용 숨김 메시 등)는 제외: 재질을 바꾸면 보이게 되고, 범위 계산(컷 높이)도 틀어짐
        renderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(true), r => r.enabled);
        float cutHeightWorldY = ComputeCutHeight();
        cutHeightWorld = cutHeightWorldY;

        Shader cutawayShader = Shader.Find("Custom/BuildingCutaway");
        if (cutawayShader == null)
        {
            Debug.LogError($"'{name}' — Custom/BuildingCutaway 셰이더를 찾을 수 없습니다.");
            return;
        }

        instanceMats = new Material[renderers.Length][];
        for (int r = 0; r < renderers.Length; r++)
        {
            Material[] originals = renderers[r].sharedMaterials;
            Material[] copies = new Material[originals.Length];
            for (int m = 0; m < originals.Length; m++)
            {
                Material src = originals[m];
                Material cut = new Material(cutawayShader);
                if (src != null)
                {
                    if (src.mainTexture != null) cut.mainTexture = src.mainTexture;
                    if (src.HasProperty("_BaseColor") || src.HasProperty("_Color")) cut.color = src.color;
                    // glTF 임포터 재질(glTF/PbrMetallicRoughness)은 색을 baseColorFactor에 저장 (Sketchfab/Quaternius 모델)
                    else if (src.HasProperty("baseColorFactor")) cut.color = src.GetColor("baseColorFactor");
                }
                cut.SetFloat(CutHeightId, cutHeightWorldY);
                cut.SetFloat(CutEnabledId, 0f);
                copies[m] = cut;
            }
            renderers[r].materials = copies;
            instanceMats[r] = copies;
        }
    }

    Collider FindTriggerCollider()
    {
        foreach (var c in GetComponents<Collider>())
            if (c.isTrigger) return c;
        return GetComponent<Collider>();
    }

    float ComputeCutHeight()
    {
        if (renderers.Length == 0) return transform.position.y + 1000f;
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b.min.y + groundFloorHeight;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        SetCut(true);
        occupied.Remove(this);
        occupied.Add(this);
        PlayerEntered?.Invoke(this);
        if (darkenSurroundings) EnterIndoorFog(other.transform);
        BuildingTransition.Play();
        PlayDoorSound(true);
        Camera.main?.GetComponent<CameraZoom>()?.SetIndoor(true);
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        SetCut(false);
        occupied.Remove(this);
        if (occupied.Count == 0 && TryGetDoor(out Vector3 door, out _))
        {
            safeExitDoor = door;
            safeExitUntil = Time.time + SafeExitSeconds;
        }
        if (darkenSurroundings) ExitIndoorFog();
        BuildingTransition.Play();
        PlayDoorSound(false);
        Camera.main?.GetComponent<CameraZoom>()?.SetIndoor(false);
    }

    // 문 열고/닫는 소리 (2D). 트리거 경계에서 들락날락하거나 겹친 건물 트리거로 연달아 울리지 않게 0.5초 간격 제한
    static float lastDoorSoundTime = -99f;
    static void PlayDoorSound(bool entering)
    {
        if (AudioManager.Library == null || Time.unscaledTime < lastDoorSoundTime + 0.5f) return;
        lastDoorSoundTime = Time.unscaledTime;
        AudioManager.PlayUI(SoundLibrary.Pick(entering ? AudioManager.Library.doorOpen : AudioManager.Library.doorClose), AudioManager.Vol.UI);
    }

    void SetCut(bool cut)
    {
        if (hideOutsideWhenIndoor)
        {
            if (cut && outsideMask == null) outsideMask = BuildOutsideMask();
            if (outsideMask != null) outsideMask.SetActive(cut);
            if (useScreenMask) SetOccludersHidden(cut);
        }

        if (instanceMats == null) return;
        float v = cut ? 1f : 0f;
        foreach (var mats in instanceMats)
            foreach (var m in mats)
                if (m != null) m.SetFloat(CutEnabledId, v);
    }

    // 실내만 뚫린 검정 마스크 (건물 루트 로컬 좌표로 만들어 자식으로 붙임 → 건물 회전/스케일 그대로 따라감)
    //  1) 벽 잘린 높이의 수평 판: 바깥쪽 아주 넓게, 가운데는 실내(트리거 + 여유) 크기로 뚫림
    //  2) 구멍 둘레에서 위로 세운 벽: 안쪽 면만 그려짐(바깥에서 보면 투명) → 카메라 쪽 벽은 안 가리고,
    //     반대편/옆 벽이 판 위로 솟은 옆 건물을 가림
    // 위에서 내려다보는 카메라 기준, 실내 바닥은 구멍을 통해서만 보이고 벽 너머 바깥은 전부 검정.
    GameObject BuildOutsideMask()
    {
        BoxCollider trigger = FindTriggerCollider() as BoxCollider;
        if (trigger == null) return null;

        Vector3 scale = transform.lossyScale;
        float mx = outsideMaskMargin / Mathf.Abs(scale.x), mz = outsideMaskMargin / Mathf.Abs(scale.z);
        float x0 = trigger.center.x - trigger.size.x / 2f - mx, x1 = trigger.center.x + trigger.size.x / 2f + mx;
        float z0 = trigger.center.z - trigger.size.z / 2f - mz, z1 = trigger.center.z + trigger.size.z / 2f + mz;
        float y = transform.InverseTransformPoint(new Vector3(0f, cutHeightWorld + 0.02f, 0f) + Vector3.ProjectOnPlane(transform.position, Vector3.up)).y;
        float top = y + outsideMaskWallHeight / Mathf.Abs(scale.y);
        float ox = 400f / Mathf.Abs(scale.x), oz = 400f / Mathf.Abs(scale.z);
        float cx = (x0 + x1) / 2f, cz = (z0 + z1) / 2f;

        var v = new System.Collections.Generic.List<Vector3>
        {
            // 0~3: 구멍(안쪽) 모서리, 4~7: 바깥 모서리  (순서: x0z0, x1z0, x1z1, x0z1)
            new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1),
            new Vector3(cx - ox, y, cz - oz), new Vector3(cx + ox, y, cz - oz), new Vector3(cx + ox, y, cz + oz), new Vector3(cx - ox, y, cz + oz),
            // 8~11: 구멍 둘레 위쪽 (벽 꼭대기)
            new Vector3(x0, top, z0), new Vector3(x1, top, z0), new Vector3(x1, top, z1), new Vector3(x0, top, z1),
        };
        var t = new System.Collections.Generic.List<int>();
        // 수평 판 (위를 향함): 바깥 사각형 - 안쪽 사각형 = 4개 사다리꼴
        for (int i = 0; i < 4; i++)
        {
            int a = i, b = (i + 1) % 4, A = 4 + i, B = 4 + (i + 1) % 4;
            t.AddRange(new[] { A, a, b, A, b, B });
        }
        // 구멍 둘레 벽 (실내 쪽을 향함)
        for (int i = 0; i < 4; i++)
        {
            int a = i, b = (i + 1) % 4, ta = 8 + i, tb = 8 + (i + 1) % 4;
            t.AddRange(new[] { a, tb, ta, a, b, tb }); // 시계방향 = 앞면 → 안쪽(구멍 중심)을 향함
        }

        var mesh = new Mesh { name = name + "_OutsideMask" };
        mesh.SetVertices(v);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        var go = new GameObject("OutsideMask");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = OutsideMaskMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        if (useScreenMask) BuildScreenMask(go.transform, x0, x1, z0, z1, y, trigger, scale);

        go.SetActive(false);
        return go;
    }

    // 화면 마스크 (Q/E로 카메라를 돌려도 실내만 보이게)
    // 위 판+벽은 카메라 반대편 바깥만 가리고, 카메라 쪽(실내와 카메라 사이)에 솟은 옆 건물 지붕은 못 가림.
    // → 실내 상자(구멍 크기 × 바닥~잘린 높이)를 스텐실 볼륨으로 그려서 "보이는 표면이 상자 안"인 픽셀만 표시하고
    //    나머지 화면은 전부 검정으로 덮음. 상자 앞을 가리는 지붕은 상자 밖 표면이라 검정이 됨.
    void BuildScreenMask(Transform parent, float x0, float x1, float z0, float z1, float y, BoxCollider trigger, Vector3 scale)
    {
        float bottom = trigger.center.y - trigger.size.y / 2f - 0.5f / Mathf.Abs(scale.y); // 바닥 표면이 상자 안에 들어오게 살짝 아래
        float top = y + 1f / Mathf.Abs(scale.y);

        // 1) 실내 상자 (닫힌 박스, 바깥을 향한 면)
        var box = new Mesh { name = name + "_IndoorVolume" };
        box.SetVertices(new System.Collections.Generic.List<Vector3>
        {
            new Vector3(x0, bottom, z0), new Vector3(x1, bottom, z0), new Vector3(x1, bottom, z1), new Vector3(x0, bottom, z1),
            new Vector3(x0, top, z0),    new Vector3(x1, top, z0),    new Vector3(x1, top, z1),    new Vector3(x0, top, z1),
        });
        box.SetTriangles(new[]
        {
            0, 1, 2, 0, 2, 3,   // 아래 (-Y)
            4, 6, 5, 4, 7, 6,   // 위 (+Y)
            0, 4, 5, 0, 5, 1,   // -Z
            2, 6, 7, 2, 7, 3,   // +Z
            1, 5, 6, 1, 6, 2,   // +X
            3, 7, 4, 3, 4, 0,   // -X
        }, 0);
        box.RecalculateBounds();
        AddMaskRenderer(parent, "IndoorVolume", box, ScreenMaskMaterials.volume);

        // 2) 화면 전체 검정 사각형 (셰이더가 클립 공간에 바로 찍음 → 컬링 안 되게 bounds 크게)
        var quad = new Mesh { name = "IndoorScreenQuad" };
        quad.SetVertices(new System.Collections.Generic.List<Vector3>
            { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) });
        quad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        quad.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        AddMaskRenderer(parent, "IndoorScreenMask", quad, ScreenMaskMaterials.screen);
    }

    // ── 가리는 물체 숨기기 ──
    // 여러 건물 트리거가 겹쳐도 꼬이지 않게 렌더러별 숨김 카운트로 관리 (0이 되면 다시 켬)
    static readonly System.Collections.Generic.Dictionary<Renderer, int> hiddenCount = new System.Collections.Generic.Dictionary<Renderer, int>();
    System.Collections.Generic.List<Renderer> hiddenByMe;

    void SetOccludersHidden(bool hide)
    {
        if (hide)
        {
            if (hiddenByMe != null || outsideMask == null) return;
            var volume = outsideMask.transform.Find("IndoorVolume");
            if (volume == null) return;
            Bounds column = volume.GetComponent<Renderer>().bounds;
            Vector2 c2 = new Vector2(column.center.x, column.center.z);
            hiddenByMe = new System.Collections.Generic.List<Renderer>();

            // 고정 메시만 대상 (MeshRenderer). 캐릭터/좀비(SkinnedMesh), 파티클, 물리로 움직이는 물체, 이 건물 자신은 제외
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || r.transform.IsChildOf(transform)) continue;
                Bounds b = r.bounds;
                if (b.Intersects(column)) continue;                  // 바닥/도로처럼 실내 상자와 겹치는 건 유지
                if (b.max.y < column.min.y) continue;                // 실내 바닥보다 낮은 건 가릴 일 없음
                if (Vector2.Distance(new Vector2(b.center.x, b.center.z), c2) - b.extents.magnitude > hideOccluderRadius) continue;
                if (r.GetComponentInParent<Rigidbody>() != null || r.CompareTag(playerTag)) continue;

                hiddenCount.TryGetValue(r, out int n);
                hiddenCount[r] = n + 1;
                r.enabled = false;
                hiddenByMe.Add(r);
            }
        }
        else
        {
            if (hiddenByMe == null) return;
            foreach (var r in hiddenByMe)
            {
                if (r == null) continue;
                hiddenCount.TryGetValue(r, out int n);
                if (n <= 1) { hiddenCount.Remove(r); r.enabled = true; }
                else hiddenCount[r] = n - 1;
            }
            hiddenByMe = null;
        }
    }

    static void AddMaskRenderer(Transform parent, string objName, Mesh mesh, Material mat)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    // Resources에 둔 머티리얼(빌드에 셰이더 포함용). 없으면 셰이더로 즉석 생성
    static class ScreenMaskMaterials
    {
        static Material v, s;
        public static Material volume => v != null ? v : (v = Resources.Load<Material>("IndoorVolumeMask") ?? new Material(Shader.Find("Hidden/IndoorVolumeMask")));
        public static Material screen => s != null ? s : (s = Resources.Load<Material>("IndoorScreenMask") ?? new Material(Shader.Find("Hidden/IndoorScreenMask")));
    }

    static Material outsideMaskMaterial;
    static Material OutsideMaskMaterial
    {
        get
        {
            if (outsideMaskMaterial != null) return outsideMaskMaterial;
            // Resources에 둬서 빌드에도 셰이더가 포함되게 함 (없으면 Unlit/Color로 즉석 생성)
            outsideMaskMaterial = Resources.Load<Material>("OutsideMask");
            if (outsideMaskMaterial == null) outsideMaskMaterial = new Material(Shader.Find("Unlit/Color")) { color = Color.black };
            return outsideMaskMaterial;
        }
    }

    // 좀보이드 스타일: 실내에 있을 때 Fog로 멀리 있는 바깥/다른 방을 어둡게 가려서
    // 지금 있는 방 주변만 보이게 함. 여러 건물이 겹쳐도 카운터로 관리해서 정상 작동.
    //
    // 주의: Built-in RP의 Fog는 "카메라로부터의 거리" 기준임. 이 게임 카메라는
    // CameraZoom.cs 기준 플레이어에서 20~80유닛(기본 40) 떨어진 탑뷰 카메라라서,
    // fogStartDistance/EndDistance를 0~14 같은 작은 절대값으로 주면 카메라~플레이어
    // 거리 자체가 이미 그 범위를 넘어버려서 아무 효과가 안 생김(또는 화면 전체가
    // 안개색). 그래서 트리거 진입 시점의 "카메라→플레이어 거리"를 구해서 그 위에
    // 상대값(indoorFogStartDistance/EndDistance)을 더하는 방식으로 계산함.
    static void EnterIndoorFogStatic(Transform player, float startMargin, float endMargin, Color color)
    {
        if (!originalFogSaved)
        {
            originalFogEnabled = RenderSettings.fog;
            originalFogMode = RenderSettings.fogMode;
            originalFogColor = RenderSettings.fogColor;
            originalFogStart = RenderSettings.fogStartDistance;
            originalFogEnd = RenderSettings.fogEndDistance;
            originalFogDensity = RenderSettings.fogDensity; // 날씨 안개(ExponentialSquared)는 밀도를 씀
            originalFogSaved = true;
        }

        float camDist = 0f;
        Camera cam = Camera.main;
        if (cam != null && player != null)
            camDist = Vector3.Distance(cam.transform.position, player.position);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = color;
        fogPlayer = player; fogStartMargin = startMargin; fogEndMargin = endMargin; // LateUpdate에서 계속 갱신
        RenderSettings.fogStartDistance = camDist + startMargin;
        RenderSettings.fogEndDistance = camDist + endMargin;
        globalInsideCount++;
    }

    // 진입 순간엔 카메라가 아직 실외 거리(40)라서, 그 뒤 실내 거리(55)로 줌아웃되면 실내가 안개 끝을 넘어 통째로 검정이 됨.
    // → 실내에 있는 동안 매 프레임 "지금 카메라→플레이어 거리 + 여유"로 안개 범위를 다시 맞춤.
    static Transform fogPlayer;
    static float fogStartMargin, fogEndMargin;

    void LateUpdate()
    {
        if (globalInsideCount <= 0 || fogPlayer == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        float camDist = Vector3.Distance(cam.transform.position, fogPlayer.position);
        RenderSettings.fogStartDistance = camDist + fogStartMargin;
        RenderSettings.fogEndDistance = camDist + fogEndMargin;
    }

    static void ExitIndoorFogStatic()
    {
        globalInsideCount = Mathf.Max(0, globalInsideCount - 1);
        if (globalInsideCount == 0 && originalFogSaved)
        {
            RenderSettings.fog = originalFogEnabled;
            RenderSettings.fogMode = originalFogMode;
            RenderSettings.fogColor = originalFogColor;
            RenderSettings.fogStartDistance = originalFogStart;
            RenderSettings.fogEndDistance = originalFogEnd;
            RenderSettings.fogDensity = originalFogDensity;
            // 다음에 들어갈 때 "그때의" 실외 안개를 다시 저장 (예전엔 게임 첫 진입 때 한 번만 저장해서,
            // 그 사이 날씨가 안개로 바뀌어도 나올 때마다 첫 진입 당시 안개로 되돌림 → WeatherManager 안개와 충돌)
            originalFogSaved = false;
        }
    }

    void EnterIndoorFog(Transform player) => EnterIndoorFogStatic(player, indoorFogStartDistance, indoorFogEndDistance, indoorFogColor);
    void ExitIndoorFog() => ExitIndoorFogStatic();
}
