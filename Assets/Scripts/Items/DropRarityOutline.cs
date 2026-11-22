using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 바닥에 떨어진 아이템 등급 외곽선 (2026-11-17). PickupItem.Start가 자동으로 붙임.
///  - 3D 메시(총 등): 같은 메시를 Custom/RarityOutline(뒤집힌 껍질, 화면 기준 일정 두께)으로 한 번 더 그림
///  - 아이콘 판(스프라이트): 같은 스프라이트를 Custom/RaritySilhouette로 1.12배 크게 뒤에 깔아 테두리처럼
///  - 색: 일반 흰 / 고급 초록 / 희귀 파랑 / 영웅 보라 / 전설 주황, 알파 0.45 (희미하지만 채도는 그대로)
///  - 성능: 플레이어 20m 안에서만 켬 (0.25초마다 거리 확인)
/// 예전 ItemOutlineGlow(하늘색 발광 + 둥실)는 그대로 두고 쓰지 않음 (PickupItem.addGlow 기본 끔).
/// (2026-11-18) 피드백 "희미해서 밤에 거의 안 보임" → 야간 가시성 강화
///  - 조명·안개 영향 없는 Unlit 발광 (셰이더가 원래 Unlit, 이제 색에 Emissive Intensity 1.5배 → Bloom에 걸림)
///  - 알파 0.45 → 0.9, 등급색 채도 최대 (일반 흰 / 고급 선명한 초록 / 희귀 청록빛 파랑 / 영웅 보라 / 전설 선명한 주황)
///  - 두께: 메시 외곽선 화면 2px → 3px(_Width 0.006), 아이콘 판 1.12배 (1.08~1.12)
///  - 플레이어 25m 안에서만 켬 (20m → 25m), 그림자 끔
///  - 탄약(AmmoData)은 탄종과 상관없이 흰색(일반 등급 취급)
/// (2026-11-21) 피드백 "총 내부 구멍·부품 경계까지 선이 생겨 지저분함" → 실루엣만 한 줄 ("스티커 실루엣")
///  - 메시: 확대 복제 대신 스텐실 2단계. Custom/RarityOutlineMask가 아이템 전체 모양을 스텐실에 기록 →
///    Custom/RarityOutline이 그 바깥에만 화면 2px 선을 그림. 부품 여러 개여도 합친 실루엣 바깥에만 선 하나.
///  - SkinnedMeshRenderer(수류탄·미니건 등)도 한 번 구워서(BakeMesh) 같은 방식으로 외곽선
///  - 아이콘 판: 1.12배 확대 복제 대신 같은 크기 아이콘 + 여백 있는 판(IconPad) 위에서 셰이더가 화면 2px만큼
///    알파를 넓혀 바깥 테두리만 칠함 (Tight 스프라이트 메시라 여백 판을 따로 만듦)
/// </summary>
public class DropRarityOutline : MonoBehaviour
{
    // public const float ShowRadius = 20f; // (이전)
    // public const float Alpha = 0.45f;   // (이전)
    // const float MeshWidth = 0.004f;     // (이전) 화면 높이 대비 (1080p 약 2px)
    public const float ShowRadius = 25f;
    public const float Alpha = 0.9f;
    public const float EmissiveIntensity = 1.5f; // HDR 색 배율 (Bloom 호환)
    // const float MeshWidth = 0.006f;   // (이전 2026-11-18) 화면 높이 대비 (1080p 약 3px)
    // const float SpriteScale = 1.12f;  // (이전 2026-11-18) 아이콘 확대 복제 배율
    const float WidthPx = 2f;            // (2026-11-21) 화면상 선 두께(픽셀), 거리와 무관
    const float IconPad = 1.3f;          // 아이콘 외곽선을 그릴 여백 판 크기 (스프라이트 대비). 선이 잘리지 않을 만큼만

    readonly List<Renderer> outlines = new List<Renderer>();
    static Transform player;
    static Shader outlineShader, silhouetteShader, maskShader;
    static Material maskMat; // 스텐실 마스크 (등급 상관없이 공용)
    static readonly Dictionary<Rarity, Material> meshMats = new Dictionary<Rarity, Material>();
    readonly List<Object> owned = new List<Object>(); // 이 아이템이 만든 메시·머티리얼 (OnDestroy에서 정리)
    float nextCheck;
    bool shown = true;

    /// <summary>등급별 외곽선 색 (요청 색 기준. 핫바/인벤토리 RarityColors와 달리 일반 = 흰색)</summary>
    public static Color ColorFor(Rarity r)
    {
        // (이전 2026-11-17) 초록 (0.30,0.90,0.35) / 파랑 #3A8DFF / 보라 #B04DFF / 주황 #FF9A1A, 알파 0.45
        Color c = r switch
        {
            Rarity.Common => Color.white,
            Rarity.Uncommon => new Color(0.15f, 1f, 0.25f),      // 선명한 초록
            Rarity.Rare => new Color(0.05f, 0.65f, 1f),          // 청록빛 파랑
            Rarity.Epic => new Color(0.72f, 0.2f, 1f),           // 보라
            Rarity.Legendary => new Color(1f, 0.5f, 0.02f),      // 선명한 주황
            _ => Color.white,
        };
        c.r *= EmissiveIntensity; c.g *= EmissiveIntensity; c.b *= EmissiveIntensity; // 발광 (HDR)
        c.a = Alpha;
        return c;
    }

    public void Setup(Rarity rarity)
    {
        if (outlineShader == null) outlineShader = Shader.Find("Custom/RarityOutline");
        if (silhouetteShader == null) silhouetteShader = Shader.Find("Custom/RaritySilhouette");
        Color col = ColorFor(rarity);

        if (maskShader == null) maskShader = Shader.Find("Custom/RarityOutlineMask");
        if (maskMat == null && maskShader != null) maskMat = new Material(maskShader) { name = "RarityOutlineMask" };

        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r.name == "_RarityOutline" || r.name == "_RarityOutlineMask") continue;
            if (r.GetComponentInParent<TMPro.TMP_Text>() != null) continue; // "F키로 획득" 글자
            if ((r is MeshRenderer || r is SkinnedMeshRenderer) && outlineShader != null && maskMat != null)
            {
                Mesh mesh = null;
                if (r is SkinnedMeshRenderer smr)
                {
                    // (2026-11-21) 바닥 아이템은 안 움직이니 현재 자세로 한 번 구움
                    if (smr.sharedMesh == null) continue;
                    mesh = new Mesh { name = smr.sharedMesh.name + "_Baked" };
                    smr.BakeMesh(mesh, true); // useScale: 렌더러 Transform 기준 로컬 좌표
                    owned.Add(mesh);
                }
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    mesh = mf.sharedMesh;
                }
                // (2026-11-21) 마스크(스텐실 기록) + 외곽선 두 개를 원본과 같은 자리·크기로 (확대 복제 없음)
                AddMeshLayer(r.transform, "_RarityOutlineMask", mesh, maskMat);
                outlines.Add(AddMeshLayer(r.transform, "_RarityOutline", mesh, MeshMat(rarity, col)));
            }
            else if (r is SpriteRenderer sr && silhouetteShader != null && sr.sprite != null)
            {
                // (2026-11-21) 여백 판(IconPad배) 위에 원래 크기 그대로 아이콘 알파를 펼쳐 놓고, 셰이더가 바깥 2px만 칠함
                var sp = sr.sprite;
                var go = new GameObject("_RarityOutline");
                go.transform.SetParent(sr.transform, false);
                go.transform.localPosition = new Vector3(0f, 0f, 0.01f); // 아이콘 뒤
                var quad = IconQuad(sp, IconPad);
                owned.Add(quad);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var omr = go.AddComponent<MeshRenderer>();
                var m = new Material(silhouetteShader) { name = "RaritySilhouette(Runtime)" };
                owned.Add(m);
                m.mainTexture = sp.texture;
                m.color = col;
                m.SetFloat("_WidthPx", WidthPx);
                Rect tr = sp.textureRect; float tw = sp.texture.width, th = sp.texture.height;
                m.SetVector("_UVRect", new Vector4(tr.xMin / tw, tr.yMin / th, tr.xMax / tw, tr.yMax / th));
                omr.sharedMaterial = m;
                omr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                omr.receiveShadows = false;
                omr.sortingLayerID = sr.sortingLayerID;
                omr.sortingOrder = sr.sortingOrder - 1;
                outlines.Add(omr);
            }
        }
        if (player == null) { var p = GameObject.FindWithTag("Player"); if (p != null) player = p.transform; }
        Refresh(true);
    }

    static Material MeshMat(Rarity r, Color c)
    {
        if (meshMats.TryGetValue(r, out var m) && m != null) return m;
        m = new Material(outlineShader) { name = "RarityOutline_" + r };
        m.SetColor("_Color", c);
        m.SetFloat("_WidthPx", WidthPx);
        meshMats[r] = m;
        return m;
    }

    /// <summary>원본 렌더러 자식으로 같은 메시를 다른 머티리얼로 한 번 더 그리는 오브젝트 (서브메시마다 같은 머티리얼)</summary>
    MeshRenderer AddMeshLayer(Transform parent, string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var mats = new Material[Mathf.Max(1, mesh.subMeshCount)];
        for (int i = 0; i < mats.Length; i++) mats[i] = mat;
        mr.sharedMaterials = mats;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mr;
    }

    /// <summary>스프라이트 사각형을 pad배로 넓힌 판. UV도 같은 비율로 바깥까지 → 아이콘은 원래 크기 그대로 보임</summary>
    static Mesh IconQuad(Sprite sp, float pad)
    {
        Rect tr = sp.textureRect; float tw = sp.texture.width, th = sp.texture.height;
        Vector2 uvMin = new Vector2(tr.xMin / tw, tr.yMin / th), uvMax = new Vector2(tr.xMax / tw, tr.yMax / th);
        Vector2 uvC = (uvMin + uvMax) * 0.5f, uvH = (uvMax - uvMin) * 0.5f * pad;
        // 스프라이트 사각형(피벗 기준) 월드 단위 범위
        Vector2 size = tr.size / sp.pixelsPerUnit;
        Vector2 min = -sp.pivot / sp.pixelsPerUnit;
        Vector2 c = min + size * 0.5f, h = size * 0.5f * pad;
        var mesh = new Mesh { name = "RarityIconPad" };
        mesh.vertices = new[] { new Vector3(c.x - h.x, c.y - h.y), new Vector3(c.x + h.x, c.y - h.y), new Vector3(c.x + h.x, c.y + h.y), new Vector3(c.x - h.x, c.y + h.y) };
        mesh.uv = new[] { uvC - uvH, new Vector2(uvC.x + uvH.x, uvC.y - uvH.y), uvC + uvH, new Vector2(uvC.x - uvH.x, uvC.y + uvH.y) };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateBounds();
        return mesh;
    }

    void OnDestroy()
    {
        foreach (var o in owned) if (o != null) Destroy(o);
    }

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.25f;
        Refresh(false);
    }

    void Refresh(bool force)
    {
        if (player == null) { var p = GameObject.FindWithTag("Player"); if (p != null) player = p.transform; }
        bool on = player != null && (player.position - transform.position).sqrMagnitude <= ShowRadius * ShowRadius;
        if (on == shown && !force) return;
        shown = on;
        foreach (var r in outlines) if (r != null) r.enabled = on;
    }
}
