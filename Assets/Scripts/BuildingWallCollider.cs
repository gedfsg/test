using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 건물 벽 충돌을 "눈에 보이는 벽 메시" 그대로 만든다.
///
/// 예전에는 건물 바운딩 박스 가장자리에 BoxCollider 벽 4면을 세웠는데, Kenney 건물은
/// 벽이 바운딩 박스보다 평균 1.7m(최대 5m 이상) 안쪽에 있어서
///   - 실내에서는 보이는 벽을 그냥 통과하고 (박스는 그보다 바깥에 있음)
///   - 총알도 보이는 벽을 지나 바깥 박스에서야 사라지고
///   - 실외에서는 벽 앞 허공에 막히는(유령 벽) 문제가 있었다.
///
/// 그래서 Awake에서 렌더 메시를 그대로 복사해 MeshCollider를 만들되,
///   1) doorSlot(로컬 AABB) 안에 들어오는 벽 삼각형은 잘라내서 문으로 드나들 수 있게 하고
///   2) 삼각형을 뒤집은 사본을 wallThickness만큼 실내 쪽으로 옮겨 넣어 "두께 있는 벽"으로 만든다.
///      (PhysX는 메시 앞면끼리만 충돌/레이캐스트 → 실내에서도 앞면인 안쪽 면이 있어야
///       캐릭터·총알·스텝업 레이가 막힘. 같은 자리에 겹치면 빠른 이동 시 뚫리므로 간격을 둠)
///
/// 메시를 에셋으로 저장하지 않고 런타임에 만드는 이유: 건물 91개분 메시 에셋이 git에
/// 10MB 가까이 쌓이는 걸 피하기 위함. 문 위치/트리거 크기는 에디터 툴
/// (Tools → Map → Fix Building Wall Colliders)이 계산해서 이 컴포넌트에 저장해둔다.
/// </summary>
[DisallowMultipleComponent]
public class BuildingWallCollider : MonoBehaviour
{
    [Tooltip("문을 낼지 여부. 끄면 벽 전체가 막힘.")]
    public bool hasDoor = true;

    [Tooltip("문 구멍 영역 중심 (이 오브젝트 로컬 좌표).")]
    public Vector3 doorSlotCenter;

    [Tooltip("문 구멍 영역 크기 (이 오브젝트 로컬 좌표). 이 안의 벽 삼각형은 잘라냄.")]
    public Vector3 doorSlotSize;

    [Tooltip("콜라이더 벽 두께(m). 안쪽 면을 보이는 벽에서 이만큼 실내 쪽으로 둠.")]
    public float wallThickness = 0.25f;

    MeshCollider wallCollider;

    void Awake()
    {
        Build();
    }

    /// <summary>
    /// 벽 MeshCollider 생성. 런타임엔 Awake에서 호출되고, 에디터 툴(Tools → Map → Rebake NavMesh With Building Walls)이
    /// NavMesh 굽기 직전에 임시로 호출해서 NavMesh에 벽(문 구멍 제외)을 반영한다. 만든 콜라이더 반환 (실패 시 null)
    /// </summary>
    public MeshCollider Build()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogWarning($"[BuildingWallCollider] '{name}' — 같은 오브젝트에 MeshFilter가 없어 벽 콜라이더를 못 만듭니다.");
            return null;
        }
        Mesh src = mf.sharedMesh;
        if (!src.isReadable)
        {
            Debug.LogWarning($"[BuildingWallCollider] '{name}' — '{src.name}' 메시의 Read/Write가 꺼져 있어 벽 콜라이더를 못 만듭니다.");
            return null;
        }

        Vector3[] srcVerts = src.vertices;
        int[] srcTris = src.triangles;
        Bounds slot = new Bounds(doorSlotCenter, doorSlotSize);

        var verts = new List<Vector3>(srcVerts.Length);
        var tris = new List<int>(srcTris.Length);

        for (int i = 0; i < srcTris.Length; i += 3)
        {
            Vector3 a = srcVerts[srcTris[i]], b = srcVerts[srcTris[i + 1]], c = srcVerts[srcTris[i + 2]];

            Bounds tb = new Bounds(a, Vector3.zero);
            tb.Encapsulate(b);
            tb.Encapsulate(c);

            if (!hasDoor || !tb.Intersects(slot))
                AddPolygon(new List<Vector3> { a, b, c }, verts, tris);   // 문과 무관한 삼각형은 그대로
            else
                AddClippedOutside(a, b, c, slot, verts, tris);             // 문 구멍과 겹치면 바깥 부분만 남김
        }

        // 두께 있는 벽: 각 삼각형을 안쪽(법선 반대)으로 wallThickness만큼 옮긴 뒤집힌 사본을 추가.
        // 바깥 면은 바깥을, 안쪽 면은 실내를 향함 → 어느 쪽에서 부딪혀도 앞면 충돌이라 확실히 막힘.
        // (예전엔 같은 자리에 뒤집힌 삼각형을 겹쳐 양면을 만들었는데, 달리기/구르기로 벽에 빠르게
        //  파고들면 물리엔진이 겹친 두 면 중 어느 쪽이 바깥인지 헷갈려 반대편으로 밀어내는 일이 생겼음.
        //  단면만 쓰면 실내에서는 뒷면이라 아예 안 막힘 → 둘 다 실측으로 확인됨)
        AddInnerShell(verts, tris);

        Mesh m = new Mesh { name = src.name + "_WallCollider" };
        if (verts.Count > 65535) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();

        wallCollider = gameObject.AddComponent<MeshCollider>();
        wallCollider.sharedMesh = m;
        return wallCollider;
    }

    // 현재 삼각형들(바깥 면) 각각에 대해, 법선 반대 방향으로 wallThickness(월드 m)만큼 옮기고
    // 감는 순서를 뒤집은 사본(안쪽 면)을 추가한다. 건물 스케일이 축마다 달라서(예: 27 x 20 x 24)
    // 법선/오프셋은 스케일을 반영한 공간에서 계산한 뒤 다시 로컬로 되돌림.
    void AddInnerShell(List<Vector3> verts, List<int> tris)
    {
        Vector3 scale = transform.lossyScale;
        Vector3 invScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
        int outerTriCount = tris.Count;

        for (int i = 0; i < outerTriCount; i += 3)
        {
            Vector3 a = Vector3.Scale(verts[tris[i]], scale);
            Vector3 b = Vector3.Scale(verts[tris[i + 1]], scale);
            Vector3 c = Vector3.Scale(verts[tris[i + 2]], scale);
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-10f) continue; // 넓이 0인 삼각형
            Vector3 offset = -n.normalized * wallThickness;

            int baseIndex = verts.Count;
            verts.Add(Vector3.Scale(a + offset, invScale));
            verts.Add(Vector3.Scale(b + offset, invScale));
            verts.Add(Vector3.Scale(c + offset, invScale));
            tris.Add(baseIndex);
            tris.Add(baseIndex + 2);
            tris.Add(baseIndex + 1);
        }
    }

    // 삼각형에서 box 바깥 부분만 남긴다 (Sutherland–Hodgman 방식).
    // 박스의 6개 면으로 차례대로 잘라서, 각 면 "바깥쪽" 조각은 바로 채택하고
    // "안쪽" 조각만 다음 면으로 넘긴다. 6면을 다 통과한 마지막 조각 = 박스 안 → 버림.
    static void AddClippedOutside(Vector3 a, Vector3 b, Vector3 c, Bounds box, List<Vector3> verts, List<int> tris)
    {
        var inside = new List<Vector3> { a, b, c };
        for (int axis = 0; axis < 3; axis++)
        {
            for (int side = 0; side < 2; side++)
            {
                float d = side == 0 ? box.min[axis] : box.max[axis];
                float sign = side == 0 ? 1f : -1f; // sign * (p[axis] - d) >= 0 이면 박스 안쪽
                var inPart = new List<Vector3>();
                var outPart = new List<Vector3>();
                SplitPolygon(inside, axis, d, sign, inPart, outPart);
                AddPolygon(outPart, verts, tris);
                inside = inPart;
                if (inside.Count < 3) return;
            }
        }
    }

    static void SplitPolygon(List<Vector3> poly, int axis, float d, float sign, List<Vector3> inPart, List<Vector3> outPart)
    {
        for (int i = 0; i < poly.Count; i++)
        {
            Vector3 p = poly[i], q = poly[(i + 1) % poly.Count];
            float fp = sign * (p[axis] - d);
            float fq = sign * (q[axis] - d);

            if (fp >= 0f) inPart.Add(p); else outPart.Add(p);

            // 변이 평면을 가로지르면 교점을 양쪽 조각에 모두 넣음
            if ((fp >= 0f) != (fq >= 0f))
            {
                Vector3 x = p + (q - p) * (fp / (fp - fq));
                inPart.Add(x);
                outPart.Add(x);
            }
        }
    }

    // 볼록 다각형을 부채꼴로 삼각형화해서 추가
    static void AddPolygon(List<Vector3> poly, List<Vector3> verts, List<int> tris)
    {
        if (poly.Count < 3) return;
        int baseIndex = verts.Count;
        verts.AddRange(poly);
        for (int i = 1; i < poly.Count - 1; i++)
        {
            tris.Add(baseIndex);
            tris.Add(baseIndex + i);
            tris.Add(baseIndex + i + 1);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!hasDoor) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0f, 1f, 0.3f, 0.35f);
        Gizmos.DrawCube(doorSlotCenter, doorSlotSize);
    }
}
