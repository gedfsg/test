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
///   2) 삼각형을 뒤집은 사본을 하나 더 넣어 양면으로 만든다.
///      (PhysX 레이캐스트는 기본적으로 뒷면을 못 맞춤 → 실내에서 쏜 총알/스텝업 레이가
///       벽 안쪽 면을 통과해버리는 걸 막기 위함)
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

    MeshCollider wallCollider;

    void Awake()
    {
        Build();
    }

    void Build()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogWarning($"[BuildingWallCollider] '{name}' — 같은 오브젝트에 MeshFilter가 없어 벽 콜라이더를 못 만듭니다.");
            return;
        }
        Mesh src = mf.sharedMesh;
        if (!src.isReadable)
        {
            Debug.LogWarning($"[BuildingWallCollider] '{name}' — '{src.name}' 메시의 Read/Write가 꺼져 있어 벽 콜라이더를 못 만듭니다.");
            return;
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

        // 양면: 같은 정점으로 감는 순서만 뒤집은 삼각형을 추가
        int oneSided = tris.Count;
        for (int i = 0; i < oneSided; i += 3)
        {
            tris.Add(tris[i]);
            tris.Add(tris[i + 2]);
            tris.Add(tris[i + 1]);
        }

        Mesh m = new Mesh { name = src.name + "_WallCollider" };
        if (verts.Count > 65535) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();

        wallCollider = gameObject.AddComponent<MeshCollider>();
        wallCollider.sharedMesh = m;
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
