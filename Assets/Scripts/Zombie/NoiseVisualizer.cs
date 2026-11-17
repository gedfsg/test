using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 소음 반경 시각화 (2026-11-12, 발표 영상용 디버그).
/// NoiseSystem.Emitted를 받아 그 자리에 실제 반경(날씨 배수 반영) 크기의 주황 원을 게임 화면에 그림.
///  - 테두리: LineRenderer (진한 주황 #FF8C00, 두께 borderWidth)
///  - 채움: 원판 메시 (연한 주황, 알파 fillAlpha)
///  - 처음 expandTime 동안 0 → 반경으로 퍼지고, duration 동안 유지하다 마지막 fadeTime 동안 흐려짐
/// 이 컴포넌트가 씬에 있을 때만 그림 (평소 게임에는 없음 → WeatherComparisonDirector가 붙임).
/// </summary>
public class NoiseVisualizer : MonoBehaviour
{
    public Color color = new Color32(0xFF, 0x8C, 0x00, 0xFF); // #FF8C00
    [Range(0f, 1f)] public float fillAlpha = 0.3f;
    public float borderWidth = 2f;
    [Tooltip("원 전체 표시 시간(초)")]
    public float duration = 1.5f;
    public float expandTime = 0.35f;
    [Tooltip("마지막 몇 초 동안 흐려지는지")]
    public float fadeTime = 0.6f;
    [Tooltip("지면 위로 띄우는 높이 (지면에 묻히지 않게)")]
    public float groundOffset = 0.25f;
    public int segments = 96;
    [Tooltip("지붕·나무 위에도 보이게 깊이 검사 없이 그림 (발표 영상용)")]
    public bool drawOnTop = false;

    class Ring { public GameObject go; public LineRenderer line; public MeshRenderer fill; public Vector3 center; public float radius, born; }
    readonly List<Ring> rings = new List<Ring>();
    Material lineMat, fillMat;

    void OnEnable() => NoiseSystem.Emitted += OnEmitted;
    void OnDisable() => NoiseSystem.Emitted -= OnEmitted;

    void OnDestroy()
    {
        foreach (var r in rings) if (r.go != null) Destroy(r.go);
        if (lineMat != null) Destroy(lineMat);
        if (fillMat != null) Destroy(fillMat);
    }

    void OnEmitted(Vector3 pos, float radius)
    {
        if (lineMat == null) lineMat = MakeMat(1);
        if (fillMat == null) fillMat = MakeMat(0);

        // 지면 높이에 맞춤 (총구 높이 → 발밑)
        float y = pos.y;
        if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore)) y = hit.point.y;
        var center = new Vector3(pos.x, y + groundOffset, pos.z);

        var go = new GameObject("NoiseRing");
        go.transform.position = center;
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = segments;
        line.widthMultiplier = borderWidth;
        line.sharedMaterial = lineMat;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.alignment = LineAlignment.TransformZ; // 바닥에 눕힘 (아래 회전)
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(go.transform, false);
        fillGo.transform.localPosition = new Vector3(0f, 0f, 0.02f); // 테두리보다 살짝 아래
        fillGo.AddComponent<MeshFilter>().sharedMesh = DiscMesh(segments);
        var fill = fillGo.AddComponent<MeshRenderer>();
        fill.sharedMaterial = fillMat;
        fill.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fill.receiveShadows = false;

        rings.Add(new Ring { go = go, line = line, fill = fill, center = center, radius = radius, born = Time.time });
    }

    void LateUpdate()
    {
        for (int i = rings.Count - 1; i >= 0; i--)
        {
            var r = rings[i];
            float age = Time.time - r.born;
            if (age >= duration || r.go == null) { if (r.go != null) Destroy(r.go); rings.RemoveAt(i); continue; }

            float grow = expandTime > 0f ? Mathf.SmoothStep(0f, 1f, age / expandTime) : 1f;
            float alpha = Mathf.Clamp01((duration - age) / Mathf.Max(0.01f, fadeTime));
            float rad = r.radius * grow;

            // 테두리 (선 두께 절반만큼 안쪽에 그려서 바깥 끝 = 실제 반경)
            float lineR = Mathf.Max(0.01f, rad - borderWidth / 2f);
            for (int s = 0; s < segments; s++)
            {
                float a = s * Mathf.PI * 2f / segments;
                r.line.SetPosition(s, new Vector3(Mathf.Cos(a) * lineR, Mathf.Sin(a) * lineR, 0f));
            }
            Color edge = color; edge.a = alpha;
            r.line.startColor = r.line.endColor = edge;

            r.fill.transform.localScale = new Vector3(rad, rad, 1f);
            Color f = color; f.a = fillAlpha * alpha;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_Color", f);
            r.fill.SetPropertyBlock(mpb);
        }
    }

    /// <summary>반투명 무조명 재질. drawOnTop이면 깊이 검사 없이 (UI/Default + ZTest Always), 채움(0)보다 테두리(1)를 나중에</summary>
    Material MakeMat(int order)
    {
        if (!drawOnTop) return new Material(Shader.Find("Sprites/Default"));
        var m = new Material(Shader.Find("UI/Default"));
        m.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
        m.renderQueue = 3900 + order;
        return m;
    }

    static Mesh discMesh;
    /// <summary>반지름 1 원판 (XY 평면, 부모 회전으로 바닥에 눕힘)</summary>
    static Mesh DiscMesh(int seg)
    {
        if (discMesh != null) return discMesh;
        var v = new Vector3[seg + 1];
        var t = new int[seg * 3];
        v[0] = Vector3.zero;
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            v[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            // 위(카메라 쪽, 부모 -Z = 월드 +Y)에서 보이게 감는 순서
            t[i * 3] = 0; t[i * 3 + 1] = (i + 1) % seg + 1; t[i * 3 + 2] = i + 1;
        }
        discMesh = new Mesh { name = "NoiseDisc", vertices = v, triangles = t };
        discMesh.RecalculateBounds();
        return discMesh;
    }
}
