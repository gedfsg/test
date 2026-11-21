using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// (2026-11-22 C2) 자식의 오목(concave) MeshCollider를 전부 "양면"으로 바꿈 (런타임, Awake).
///
/// 문제: 학교(Enterable_School, MeshCollider 854개)의 벽 충돌 메시가 한쪽 면만 막아서
///  바깥→안은 막히고 안→바깥은 통과(또는 반대)되는 일방통행 지점이 58곳 (실내 트리거 둘레 1m 간격 캡슐 검사).
///  PhysX는 메시 앞면끼리만 충돌하므로, 벽을 뒤에서 통과해 들어가면 앞면에 막혀 다시 못 나옴 → "들어왔는데 못 나감".
/// 해결: 삼각형을 뒤집은 사본을 원래 면에서 Offset(m)만큼 뒤쪽으로 옮겨 넣은 메시로 교체 (BuildingWallCollider와 같은 방식,
///  같은 자리에 겹치면 빠른 이동 시 뚫려서 간격을 둠). 원본 메시는 안 건드리고, 같은 원본은 한 번만 만들어 공유.
/// 메시 에셋으로 저장 안 함 (git 용량) - 렌더 메시가 Read/Write 켜져 있어야 함.
/// </summary>
[DisallowMultipleComponent]
public class DoubleSidedMeshColliders : MonoBehaviour
{
    [Tooltip("뒤집은 면을 원래 면에서 뒤쪽으로 옮기는 거리 (m)")]
    public float offset = 0.12f;

    static readonly Dictionary<Mesh, Mesh> cache = new Dictionary<Mesh, Mesh>();

    void Awake()
    {
        int n = 0;
        foreach (var mc in GetComponentsInChildren<MeshCollider>(true))
        {
            if (mc.isTrigger || mc.convex || mc.sharedMesh == null || !mc.sharedMesh.isReadable) continue;
            if (mc.sharedMesh.name.EndsWith("_2Sided")) continue;
            mc.sharedMesh = DoubleSided(mc.sharedMesh, offset);
            n++;
        }
    }

    public static Mesh DoubleSided(Mesh src, float offset)
    {
        if (cache.TryGetValue(src, out var m) && m != null) return m;
        var v = src.vertices;
        var t = src.triangles;
        var verts = new List<Vector3>(v.Length * 2);
        var tris = new List<int>(t.Length * 2);
        verts.AddRange(v);
        tris.AddRange(t);
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
            Vector3 nrm = Vector3.Cross(b - a, c - a);
            if (nrm.sqrMagnitude < 1e-10f) continue;
            nrm.Normalize();
            Vector3 sh = -nrm * offset; // 원래 면의 뒤쪽으로
            int k = verts.Count;
            verts.Add(a + sh); verts.Add(c + sh); verts.Add(b + sh); // 감김 반대 = 뒤집힌 면
            tris.Add(k); tris.Add(k + 1); tris.Add(k + 2);
        }
        m = new Mesh { name = src.name + "_2Sided", indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        cache[src] = m;
        return m;
    }
}
