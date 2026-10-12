using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 일회성 스크립트 - 확인 후 삭제할 것
// BuildingInterior가 붙은 모든 건물에 대해:
//  1) 기존 벽 BoxCollider 5개(_TempApplyBuildingInterior가 바운딩 박스 가장자리에 세운 것)에서
//     문 위치(박스 2개로 나뉜 면의 가운데 틈)를 읽어냄
//  2) 실제 보이는 벽(렌더 메시)까지의 거리를 레이캐스트로 재서 문 구멍 깊이를 정함
//  3) 벽 BoxCollider 5개 제거 → BuildingWallCollider(런타임에 렌더 메시 그대로 양면
//     MeshCollider를 만들고 문 구멍만 잘라냄) 부착. 메시가 자식에 있는 건물(Sketchfab 마트 등)은
//     그 자식 오브젝트에 부착 (문 구멍 좌표도 자식 메시 로컬로 저장)
//  4) 진입 트리거를 실제 실내 크기로 축소 (예전엔 바운딩 박스 95%라 벽 바깥까지 삐져나와서
//     건물 옆을 스치기만 해도 실내 모드가 켜졌음)
// 재실행해도 안전: 박스가 이미 없으면 BuildingWallCollider에 저장된 문 위치를 그대로 씀.
public static class TempFixBuildingWallColliders
{
    const float DoorOutsideMargin = 1.0f;  // 문 구멍을 건물 바깥쪽으로 이만큼(m) 더 늘림
    const float DoorInsideMargin = 1.5f;   // 보이는 벽면을 지나 실내 쪽으로 이만큼(m) 더 뚫음
    const float TriggerWallInset = 0.3f;   // 트리거를 실내 벽면에서 이만큼(m) 안쪽으로
    const float MinDoorWidth = 1.8f;       // 문 구멍 최소 폭(m). 플레이어 지름 1m + 여유

    // ── 모델별 실제 문 위치 표 (2026-10-12, 모델 33개 4면 정사영 스크린샷을 보고 하나씩 확인) ──
    // 자동 감지(깊이/색)로는 Kenney 모델의 문과 창문이 구분되지 않아서 사람이 눈으로 정함.
    // 값은 메시 로컬 좌표라 같은 모델이면 인스턴스 크기/회전이 달라도 그대로 적용됨.
    // DoorDef(법선 축 0=X / 2=Z, 면 방향 +1/-1, 폭 축(법선이 X면 Z, Z면 X) 범위 min, max)
    // 차고 문(넓고 손잡이가 낮은 셔터)은 제외. 문이 여러 개면 도로 쪽(기존 면)과 가까운 문을 씀.
    struct DoorDef
    {
        public int axis; public float sign, min, max;
        public DoorDef(int axis, float sign, float min, float max) { this.axis = axis; this.sign = sign; this.min = min; this.max = max; }
    }

    static readonly Dictionary<string, DoorDef[]> DoorTable = new Dictionary<string, DoorDef[]>
    {
        { "building-a", new[] { new DoorDef(2, -1, -0.32109f, -0.11994f) } },
        { "building-b", new[] { new DoorDef(2, -1, 0.06819f, 0.20901f) } },
        { "building-c", new[] { new DoorDef(2, -1, 0.11139f, 0.25220f) } },
        { "building-e", new[] { new DoorDef(2, 1, -0.27129f, -0.13551f), new DoorDef(2, -1, -0.26681f, -0.13103f) } },
        { "building-f", new[] { new DoorDef(2, -1, 0.09799f, 0.29916f) } },
        { "building-g", new[] { new DoorDef(2, -1, 0.03299f, 0.23415f) } },
        { "building-h", new[] { new DoorDef(2, -1, 0.11139f, 0.25220f) } },
        { "building-i", new[] { new DoorDef(2, 1, -0.07229f, 0.06696f), new DoorDef(2, -1, -0.10277f, 0.10412f) } },
        { "building-j", new[] { new DoorDef(2, 1, -0.12495f, 0.08627f), new DoorDef(2, -1, -0.12650f, 0.08471f) } },
        { "building-k", new[] { new DoorDef(2, -1, -0.12380f, 0.08708f) } },
        { "building-l", new[] { new DoorDef(2, 1, -0.13473f, 0.00609f), new DoorDef(2, -1, -0.16199f, 0.03415f) } },
        { "building-m", new[] { new DoorDef(2, 1, -0.06963f, 0.06829f), new DoorDef(2, -1, -0.10075f, 0.09397f) } },
        { "building-skyscraper-a", new[] { new DoorDef(2, 1, -0.07389f, 0.06846f), new DoorDef(2, -1, -0.06846f, 0.07389f) } },
        { "building-skyscraper-b", new[] { new DoorDef(2, 1, -0.07389f, 0.06846f), new DoorDef(2, -1, -0.06846f, 0.07389f) } },
        { "building-skyscraper-c", new[] { new DoorDef(2, 1, -0.07170f, 0.07064f), new DoorDef(2, -1, -0.10227f, 0.09806f) } },
        { "building-skyscraper-d", new[] { new DoorDef(2, 1, -0.07431f, 0.06986f), new DoorDef(2, -1, -0.09607f, 0.10708f) } },
        { "building-skyscraper-e", new[] { new DoorDef(2, 1, -0.10112f, 0.04123f), new DoorDef(2, -1, -0.12558f, 0.09057f) } },
        { "building-type-d", new[] { new DoorDef(2, -1, -0.22871f, -0.08628f), new DoorDef(2, 1, -0.23276f, -0.09033f) } },
        { "building-type-e", new[] { new DoorDef(2, 1, -0.07094f, 0.06888f), new DoorDef(2, -1, -0.06888f, 0.07094f) } },
        { "building-type-g", new[] { new DoorDef(2, 1, 0.40463f, 0.54529f), new DoorDef(2, -1, 0.00561f, 0.14626f) } },
        { "building-type-h", new[] { new DoorDef(2, 1, -0.07057f, 0.06153f), new DoorDef(2, -1, -0.06153f, 0.07057f) } },
        { "building-type-k", new[] { new DoorDef(2, 1, -0.27372f, -0.13409f), new DoorDef(2, -1, 0.12508f, 0.26471f) } },
        { "building-type-l", new[] { new DoorDef(2, 1, -0.21294f, -0.07360f), new DoorDef(2, -1, 0.20194f, 0.34129f) } },
        { "building-type-n", new[] { new DoorDef(2, 1, 0.06855f, 0.20582f), new DoorDef(2, -1, 0.07510f, 0.21236f) } },
        { "building-type-o", new[] { new DoorDef(2, -1, -0.45557f, -0.31601f) } },
        { "building-type-p", new[] { new DoorDef(2, -1, -0.47271f, -0.33279f) } },
        { "building-type-q", new[] { new DoorDef(2, -1, -0.06948f, 0.06977f), new DoorDef(2, 1, -0.46809f, -0.33532f) } },
        { "building-type-r", new[] { new DoorDef(2, -1, 0.07209f, 0.21164f), new DoorDef(0, -1, 0.06975f, 0.21126f) } },
        { "building-type-s", new[] { new DoorDef(2, -1, -0.02321f, 0.11922f), new DoorDef(0, -1, 0.23358f, 0.37069f) } },
        { "building-type-t", new[] { new DoorDef(2, 1, -0.07444f, 0.06799f), new DoorDef(2, -1, -0.06736f, 0.07506f) } },
        { "low-detail-building-i", new[] { new DoorDef(2, -1, -0.02357f, 0.02393f) } },
        { "low-detail-building-l", new[] { new DoorDef(0, 1, -0.02674f, 0.02518f), new DoorDef(0, -1, -0.02518f, 0.02674f), new DoorDef(2, 1, -0.02594f, 0.02613f), new DoorDef(2, -1, -0.02613f, 0.02594f) } },
        { "building_G", new[] { new DoorDef(2, 1, -0.00083f, 0.00083f) } },
        // 경찰서 (Sketchfab low_poly_police_station): -X 면 파란 기둥 사이 정문 (2026-10-13 스크린샷 확인)
        { "PublicBuilding_Police_Pack2_0", new[] { new DoorDef(0, -1, -0.72734f, 0.70013f) } },
    };

    [MenuItem("Tools/Map/Fix Building Wall Colliders (Mesh + Door)")]
    static void Apply()
    {
        var buildings = Object.FindObjectsByType<BuildingInterior>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int applied = 0;
        var warnings = new List<string>();

        bool oldBackfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true; // 실내에서 바깥으로 쏘는 측정 레이가 벽 안쪽(뒷면)을 맞추게
        try
        {
            foreach (var bi in buildings)
            {
                string warn = FixOne(bi.gameObject);
                if (warn != null) warnings.Add(warn);
                if (bi.GetComponentInChildren<BuildingWallCollider>() != null) applied++;
            }
        }
        finally
        {
            Physics.queriesHitBackfaces = oldBackfaces;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        string msg = $"✅ 건물 벽 콜라이더 교체 {applied}/{buildings.Length}개 (실제 메시 + 문 구멍 + 트리거 축소)";
        if (warnings.Count > 0) msg += "\n⚠️ " + string.Join("\n⚠️ ", warnings);
        Debug.Log(msg);
    }

    // 성공하면 null, 문제가 있으면 경고 문자열
    static string FixOne(GameObject go)
    {
        // 메시는 루트에 있으면 루트 것을, 없으면 자식에서 찾음
        // (Sketchfab 모델은 루트 밑 자식 오브젝트에 메시가 붙어 있음 - 예: 마트 building_G)
        MeshFilter mf = go.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            var childFilters = go.GetComponentsInChildren<MeshFilter>(true);
            if (childFilters.Length == 1) mf = childFilters[0];
            else
            {
                // 자식 메시가 여럿이면(경찰서: 보이는 메시 + 충돌용 메시) 문 위치 표에 있는 메시를 씀
                mf = System.Array.Find(childFilters, f => f.sharedMesh != null && DoorTable.ContainsKey(f.sharedMesh.name));
                if (mf == null)
                    return $"'{go.name}' — 메시가 {childFilters.Length}개인데 문 위치 표에 있는 메시가 없어 건너뜀";
            }
        }
        Renderer rend = mf.GetComponent<Renderer>();
        if (mf.sharedMesh == null || rend == null)
            return $"'{go.name}' — MeshFilter/Renderer가 없어 건너뜀";
        if (!mf.sharedMesh.isReadable)
            return $"'{go.name}' — 메시 Read/Write가 꺼져 있어 건너뜀";

        GameObject meshGo = mf.gameObject;
        Transform t = go.transform;

        // 계산은 전부 "건물 루트 로컬" 좌표로 함 (벽 박스/트리거가 루트에 있으므로).
        // 메시 로컬 → 루트 로컬 변환 행렬. 메시가 루트에 있으면 단위 행렬이라 기존 건물은 결과 동일.
        Matrix4x4 meshToRoot = t.worldToLocalMatrix * mf.transform.localToWorldMatrix;
        Bounds lb = TransformBounds(mf.sharedMesh.bounds, meshToRoot);
        Bounds wb = rend.bounds;

        BoxCollider trigger = null;
        var wallBoxes = new List<BoxCollider>();
        foreach (var bc in go.GetComponents<BoxCollider>())
        {
            if (bc.isTrigger) trigger = bc;
            else wallBoxes.Add(bc);
        }
        if (trigger == null) return $"'{go.name}' — 진입 트리거가 없어 건너뜀";

        // BuildingWallCollider는 자기 MeshFilter로 콜라이더를 만들기 때문에 메시가 있는 오브젝트에 붙임
        var wall = meshGo.GetComponent<BuildingWallCollider>();

        // 측정용 임시 MeshCollider (원본 메시 그대로, 문 구멍 없음)
        var probe = meshGo.AddComponent<MeshCollider>();
        probe.sharedMesh = mf.sharedMesh;
        Physics.SyncTransforms();

        try
        {
            // ── 1) 문 위치: 박스 2개로 나뉜 면 = 문 있는 면, 두 박스 사이 틈 = 문 ──
            bool hasDoor;
            string warnNoTable = null;
            int normalAxis = 0;
            float sideSign = 1f, gapMin = 0f, gapMax = 0f;
            if (wallBoxes.Count > 0)
            {
                // 문 없는 건물(공간 부족으로 4면 다 막힌 경우)이면 false
                hasDoor = FindDoorFromBoxes(wallBoxes, lb, out normalAxis, out sideSign, out gapMin, out gapMax);
            }
            else if (wall != null)
            {
                // 재실행: 이미 변환된 건물은 저장된 문 구멍에서 문 면/폭을 다시 읽음
                hasDoor = wall.hasDoor;
                if (hasDoor) ReadDoorFromSlot(wall, lb, meshToRoot, out normalAxis, out sideSign, out gapMin, out gapMax);
            }
            else if (DoorTable.ContainsKey(mf.sharedMesh.name))
            {
                hasDoor = true; // 처음 들어갈 수 있게 만드는 건물: 문은 아래 문 위치 표에서 정해짐
            }
            else
            {
                return $"'{go.name}' — 벽 박스도 BuildingWallCollider도 없어 문 위치를 알 수 없음";
            }

            // ── 1-1) 모델별 실제 문 위치 표(DoorTable)가 있으면 그걸로 정함 ──
            // 예전엔 '도로 쪽 면 가운데'에 무조건 구멍을 냈는데, 모델의 실제 문과 어긋나서
            // 보이는 벽을 뚫고 지나가거나(구멍 자리) 진짜 문으로는 못 들어가는(막힘) 문제가 있었음.
            // 문이 여러 개면 지금까지 쓰던 면(도로 쪽)과 가장 같은 방향의 문을 고름.
            if (DoorTable.TryGetValue(mf.sharedMesh.name, out DoorDef[] doors))
            {
                Vector3 prefer = Vector3.zero;
                if (hasDoor) prefer[normalAxis] = sideSign;
                float bestScore = float.NegativeInfinity;
                foreach (var d in doors)
                {
                    DoorToRoot(d, mf.sharedMesh.bounds, meshToRoot, lb, out int ax, out float sg, out float g0, out float g1);
                    Vector3 dir = Vector3.zero;
                    dir[ax] = sg;
                    float score = Vector3.Dot(dir, prefer);
                    if (score > bestScore) { bestScore = score; normalAxis = ax; sideSign = sg; gapMin = g0; gapMax = g1; }
                }
                hasDoor = true;

                // 플레이어(지름 1m)가 여유 있게 지나가도록 최소 폭 보장
                int alongAx = normalAxis == 0 ? 2 : 0;
                float minLocal = MinDoorWidth / Mathf.Abs(t.lossyScale[alongAx]);
                if (gapMax - gapMin < minLocal)
                {
                    float mid = (gapMin + gapMax) * 0.5f;
                    gapMin = mid - minLocal * 0.5f;
                    gapMax = mid + minLocal * 0.5f;
                }
            }
            else
            {
                warnNoTable = $"'{go.name}' — 문 위치 표에 '{mf.sharedMesh.name}' 모델이 없어 기존 문 위치 유지";
            }

            // 실내 바닥 높이 (측정 레이/격자를 이 높이 기준으로 쏨)
            float floorY = FindFloorY(wb, probe);

            // ── 2) 트리거를 실제 실내 크기로 (문 구멍 깊이를 실내 기준으로 재므로 먼저 함) ──
            string triggerWarn = ShrinkTriggerToInterior(go, t, wb, floorY, lb, trigger, probe);

            Bounds slotRoot = hasDoor
                ? BuildDoorSlot(t, wb, floorY, lb, probe, trigger.center, normalAxis, sideSign, gapMin, gapMax)
                : new Bounds();
            // 저장은 BuildingWallCollider가 쓰는 메시 로컬 좌표로
            Bounds slot = hasDoor ? TransformBounds(slotRoot, meshToRoot.inverse) : new Bounds();

            // ── 3) 박스 제거 + BuildingWallCollider 부착 ──
            foreach (var bc in wallBoxes) Undo.DestroyObjectImmediate(bc);
            if (wall == null) wall = Undo.AddComponent<BuildingWallCollider>(meshGo);
            Undo.RecordObject(wall, "Set door slot");
            wall.hasDoor = hasDoor;
            wall.doorSlotCenter = slot.center;
            wall.doorSlotSize = slot.size;
            EditorUtility.SetDirty(wall);

            return triggerWarn ?? warnNoTable;
        }
        finally
        {
            Object.DestroyImmediate(probe);
        }
    }

    // 벽 박스들을 "어느 면에 붙어있나"로 묶어서 2개짜리 면(=문 면)과 틈을 찾음
    static bool FindDoorFromBoxes(List<BoxCollider> boxes, Bounds lb, out int normalAxis, out float sideSign, out float gapMin, out float gapMax)
    {
        normalAxis = 0; sideSign = 1f; gapMin = gapMax = 0f;
        var groups = new Dictionary<(int, int), List<BoxCollider>>();
        foreach (var bc in boxes)
        {
            int axis = bc.size.x < bc.size.z ? 0 : 2;                    // 얇은 축 = 벽의 법선 축
            int sign = bc.center[axis] >= lb.center[axis] ? 1 : -1;
            var key = (axis, sign);
            if (!groups.ContainsKey(key)) groups[key] = new List<BoxCollider>();
            groups[key].Add(bc);
        }

        foreach (var kv in groups)
        {
            if (kv.Value.Count != 2) continue;
            normalAxis = kv.Key.Item1;
            sideSign = kv.Key.Item2;
            int along = normalAxis == 0 ? 2 : 0;
            BoxCollider a = kv.Value[0], b = kv.Value[1];
            if (a.center[along] > b.center[along]) { var tmp = a; a = b; b = tmp; }
            gapMin = a.center[along] + a.size[along] * 0.5f;
            gapMax = b.center[along] - b.size[along] * 0.5f;
            return gapMax > gapMin;
        }
        return false;
    }

    // 저장된 문 구멍(메시 로컬 AABB)에서 문 면(법선 축/방향)과 폭 범위를 루트 로컬 기준으로 복원
    static void ReadDoorFromSlot(BuildingWallCollider wall, Bounds lb, Matrix4x4 meshToRoot, out int normalAxis, out float sideSign, out float gapMin, out float gapMax)
    {
        Bounds slot = TransformBounds(new Bounds(wall.doorSlotCenter, wall.doorSlotSize), meshToRoot);
        Vector3 d = slot.center - lb.center;
        normalAxis = Mathf.Abs(d.x) / lb.extents.x > Mathf.Abs(d.z) / lb.extents.z ? 0 : 2;
        sideSign = Mathf.Sign(d[normalAxis]);
        int along = normalAxis == 0 ? 2 : 0;
        gapMin = slot.min[along];
        gapMax = slot.max[along];
    }

    // 실내 바닥 높이. 메시 맨 아래에 두꺼운 밑판(마트 모델의 인도 블록처럼 1.6m짜리)이 있으면
    // 그 윗면을, 아니면 메시 맨 아래(wb.min.y)를 씀. 바닥 안(밑판 속)에서 재면 모든 칸이
    // "지붕 있는 실내"로 잘못 잡히기 때문. 바닥이 1m 정도 살짝 높은 Kenney 건물은 기준 미만이라
    // 기존과 동일하게 wb.min.y. 가구 등에 걸리지 않게 5지점에서 재서 중앙값을 씀.
    const float PlinthThreshold = 1.2f; // Locomotion.maxStepHeight와 같은 값 (이보다 높으면 걸어 못 올라감)

    static float FindFloorY(Bounds wb, MeshCollider probe)
    {
        var tops = new List<float>();
        foreach (var f in new[] { new Vector2(0.5f, 0.5f), new Vector2(0.25f, 0.25f), new Vector2(0.75f, 0.25f), new Vector2(0.25f, 0.75f), new Vector2(0.75f, 0.75f) })
        {
            Vector3 o = new Vector3(Mathf.Lerp(wb.min.x, wb.max.x, f.x), wb.min.y + 3f, Mathf.Lerp(wb.min.z, wb.max.z, f.y));
            float top = wb.min.y;
            foreach (var h in Physics.RaycastAll(o, Vector3.down, 3.5f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider == probe) top = Mathf.Max(top, h.point.y);
            tops.Add(top);
        }
        tops.Sort();
        float median = tops[tops.Count / 2];
        return median - wb.min.y > PlinthThreshold ? median : wb.min.y;
    }

    // 메시 로컬 문 정의 → 건물 루트 로컬의 (법선 축, 방향, 폭 범위). 메시가 자식에 회전돼 있어도(마트) 맞게 변환
    static void DoorToRoot(DoorDef d, Bounds meshLb, Matrix4x4 meshToRoot, Bounds lbRoot,
        out int normalAxis, out float sideSign, out float gapMin, out float gapMax)
    {
        int along = d.axis == 0 ? 2 : 0;
        Vector3 c = meshLb.center, size = Vector3.zero;
        c[d.axis] = meshLb.center[d.axis] + d.sign * meshLb.extents[d.axis];
        c[along] = (d.min + d.max) * 0.5f;
        size[d.axis] = meshLb.size[d.axis] * 0.001f;
        size[along] = d.max - d.min;
        size.y = meshLb.size.y;
        Bounds r = TransformBounds(new Bounds(c, size), meshToRoot);

        normalAxis = r.size.x / lbRoot.size.x < r.size.z / lbRoot.size.z ? 0 : 2; // 얇은 쪽 = 법선
        sideSign = Mathf.Sign(r.center[normalAxis] - lbRoot.center[normalAxis]);
        int rootAlong = normalAxis == 0 ? 2 : 0;
        gapMin = r.min[rootAlong];
        gapMax = r.max[rootAlong];
    }

    // AABB를 행렬로 변환한 뒤 다시 AABB로 (8개 꼭짓점을 감쌈). 단위 행렬이면 그대로.
    static Bounds TransformBounds(Bounds b, Matrix4x4 m)
    {
        Bounds result = new Bounds(m.MultiplyPoint3x4(b.min), Vector3.zero);
        for (int i = 1; i < 8; i++)
        {
            Vector3 corner = new Vector3(
                (i & 1) != 0 ? b.max.x : b.min.x,
                (i & 2) != 0 ? b.max.y : b.min.y,
                (i & 4) != 0 ? b.max.z : b.min.z);
            result.Encapsulate(m.MultiplyPoint3x4(corner));
        }
        return result;
    }

    // 문 구멍(로컬 AABB): 폭 = 박스 틈, 높이 = 건물 전체, 깊이 = 건물 바깥 ~ 실제 벽면 안쪽.
    // 벽면 위치는 "실내(트리거 중심)에서 문 쪽으로" 쏴서 잼. 바깥에서 쏘면 처마 기둥/현관 계단
    // 같은 앞쪽 장식에 먼저 맞아서 구멍이 벽면까지 못 닿는 문제가 있었음(building-c 등).
    static Bounds BuildDoorSlot(Transform t, Bounds wb, float floorY, Bounds lb, MeshCollider probe, Vector3 triggerCenterLocal, int normalAxis, float sideSign, float gapMin, float gapMax)
    {
        int along = normalAxis == 0 ? 2 : 0;
        float gapMid = (gapMin + gapMax) * 0.5f;

        Vector3 edgeLocal = lb.center;
        edgeLocal[normalAxis] = lb.center[normalAxis] + sideSign * lb.extents[normalAxis];
        edgeLocal[along] = gapMid;

        Vector3 axisLocal = Vector3.zero;
        axisLocal[normalAxis] = sideSign;
        Vector3 outward = t.TransformDirection(axisLocal);
        outward.y = 0f;
        outward.Normalize();

        Vector3 edgeWorld = t.TransformPoint(edgeLocal);
        float outerA = t.InverseTransformPoint(edgeWorld + outward * DoorOutsideMargin)[normalAxis];

        // 실내 쪽 시작점: 법선 축은 트리거 중심, 폭 축은 문 중앙
        Vector3 insideLocal = triggerCenterLocal;
        insideLocal[along] = gapMid;
        Vector3 insideWorld = t.TransformPoint(insideLocal);

        // 여러 높이에서 실내→문 방향으로 쏴서 가장 가까운 막힘(벽 안쪽 면, 문 바로 뒤 가구 등)을 찾음.
        // 그 지점보다 DoorInsideMargin 더 안쪽부터 바깥까지 전부 잘라서 확실히 드나들 수 있게 함.
        float nearest = float.MaxValue;
        float maxDist = wb.size.magnitude;
        foreach (float h in new[] { 0.3f, 0.6f, 0.9f, 1.2f, 1.5f, 1.8f })
        {
            Vector3 origin = new Vector3(insideWorld.x, floorY + h, insideWorld.z);
            foreach (var hit in Physics.RaycastAll(origin, outward, maxDist, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider == probe && hit.distance > 0.3f && hit.distance < nearest) nearest = hit.distance;
        }

        float innerA;
        if (nearest == float.MaxValue)
        {
            innerA = edgeLocal[normalAxis]; // 막힌 게 없으면 자를 것도 없음
        }
        else
        {
            float cutFromInside = Mathf.Max(0f, nearest - DoorInsideMargin);
            Vector3 innerWorld = new Vector3(insideWorld.x, floorY + 1f, insideWorld.z) + outward * cutFromInside;
            innerA = t.InverseTransformPoint(innerWorld)[normalAxis];
        }

        Vector3 min = new Vector3(), max = new Vector3();
        min[normalAxis] = Mathf.Min(outerA, innerA);
        max[normalAxis] = Mathf.Max(outerA, innerA);
        min[along] = gapMin;
        max[along] = gapMax;
        min.y = lb.min.y - lb.size.y * 0.1f;
        max.y = lb.max.y + lb.size.y * 0.1f;

        var slot = new Bounds();
        slot.SetMinMax(min, max);
        return slot;
    }

    // 트리거를 실제 실내 크기로 맞춤.
    // 건물 풋프린트를 격자로 나눠 "실내 칸"(위에 지붕이 있고, 벽 속이 아니고, 4방향 모두 벽으로
    // 둘러싸인 칸)만 골라낸 뒤 그 칸들을 감싸는 사각형 - TriggerWallInset 으로 트리거를 정함.
    // 중심에서 레이 4방향만 쏘는 방식은 ㄱ자/내부 칸막이가 있는 건물에서 트리거가 한쪽 방에만
    // 쪼그라들어서(building-b 등) 격자 방식으로 함. 처마 밑 현관처럼 한쪽이 트인 곳은 제외됨.
    static string ShrinkTriggerToInterior(GameObject go, Transform t, Bounds wb, float floorY, Bounds lb, BoxCollider trigger, MeshCollider probe)
    {
        const int Grid = 24;
        float maxDist = wb.size.magnitude;

        Vector3 cellHalf = new Vector3(lb.size.x / Grid * 0.5f, 0f, lb.size.z / Grid * 0.5f);

        // 바닥 위 sampleHeight 높이에서 실내 칸들을 감싸는 사각형
        bool Scan(float sampleHeight, out Vector3 min, out Vector3 max)
        {
            bool any = false;
            min = max = Vector3.zero;
            for (int ix = 0; ix < Grid; ix++)
            {
                for (int iz = 0; iz < Grid; iz++)
                {
                    Vector3 lp = new Vector3(
                        Mathf.Lerp(lb.min.x, lb.max.x, (ix + 0.5f) / Grid),
                        lb.center.y,
                        Mathf.Lerp(lb.min.z, lb.max.z, (iz + 0.5f) / Grid));
                    Vector3 wp = t.TransformPoint(lp);
                    wp.y = floorY + sampleHeight;

                    if (!IsInteriorCell(wp, t, probe, maxDist)) continue;

                    Vector3 lo = lp - cellHalf, hi = lp + cellHalf;
                    if (!any) { min = lo; max = hi; any = true; }
                    else { min = Vector3.Min(min, lo); max = Vector3.Max(max, hi); }
                }
            }
            return any;
        }

        bool found = Scan(1.0f, out Vector3 cellMin, out Vector3 cellMax);

        // 실내 바닥이 1m쯤 높은 건물(경찰서 등)은 1m 높이가 바닥 면에 걸려 "벽 속"으로 잘못 판정됨
        // → 결과가 바닥 면적의 25% 미만이면 1.8m 높이로 다시 재서 더 넓은 쪽을 씀 (기존 91개 건물은 전부 29% 이상)
        float footprint = lb.size.x * lb.size.z;
        float Area(Vector3 a, Vector3 b) => (b.x - a.x) * (b.z - a.z);
        if (!found || Area(cellMin, cellMax) < footprint * 0.25f)
        {
            if (Scan(1.8f, out Vector3 min2, out Vector3 max2) && (!found || Area(min2, max2) > Area(cellMin, cellMax)))
            {
                cellMin = min2; cellMax = max2; found = true;
            }
        }

        if (!found) return $"'{go.name}' — 벽은 교체됨. 단, 실내 칸을 못 찾아 트리거는 기존 크기 유지";

        Undo.RecordObject(trigger, "Shrink building trigger");

        // 로컬 단위로 인셋 (스케일이 축마다 달라서 축별로 환산)
        Vector3 scale = t.lossyScale;
        float insetX = TriggerWallInset / Mathf.Abs(scale.x);
        float insetZ = TriggerWallInset / Mathf.Abs(scale.z);
        cellMin.x += insetX; cellMax.x -= insetX;
        cellMin.z += insetZ; cellMax.z -= insetZ;

        Vector3 oldMin = trigger.center - trigger.size * 0.5f;
        Vector3 oldMax = trigger.center + trigger.size * 0.5f;
        float oldArea = (oldMax.x - oldMin.x) * (oldMax.z - oldMin.z);

        // Y(높이)는 기존 트리거 그대로
        Vector3 newMin = new Vector3(cellMin.x, oldMin.y, cellMin.z);
        Vector3 newMax = new Vector3(cellMax.x, oldMax.y, cellMax.z);
        trigger.center = (newMin + newMax) * 0.5f;
        trigger.size = newMax - newMin;

        float ratio = (trigger.size.x * trigger.size.z) / oldArea;
        if (ratio < 0.5f)
            return $"'{go.name}' — 벽은 교체됨. 단, 트리거가 {ratio:P0}로 크게 줄었으니 확인 필요";
        return null;
    }

    // 지붕 아래 + 벽 속 아님 + 수평 4방향 모두 같은 건물 벽에 막힘 = 실내
    static bool IsInteriorCell(Vector3 wp, Transform t, MeshCollider probe, float maxDist)
    {
        if (!HitsProbe(wp, Vector3.up, probe, maxDist)) return false;

        foreach (var c in Physics.OverlapSphere(wp, 0.3f, ~0, QueryTriggerInteraction.Ignore))
            if (c == probe) return false; // 벽 속

        Vector3 r = t.right; r.y = 0f; r.Normalize();
        Vector3 f = t.forward; f.y = 0f; f.Normalize();
        return HitsProbe(wp, r, probe, maxDist) && HitsProbe(wp, -r, probe, maxDist)
            && HitsProbe(wp, f, probe, maxDist) && HitsProbe(wp, -f, probe, maxDist);
    }

    static bool HitsProbe(Vector3 origin, Vector3 dir, MeshCollider probe, float maxDist)
    {
        foreach (var h in Physics.RaycastAll(origin, dir, maxDist, ~0, QueryTriggerInteraction.Ignore))
            if (h.collider == probe) return true;
        return false;
    }
}
