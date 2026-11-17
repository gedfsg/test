using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 바닥에 떨어진 아이템 등급 외곽선 (2026-11-17). PickupItem.Start가 자동으로 붙임.
///  - 3D 메시(총 등): 같은 메시를 Custom/RarityOutline(뒤집힌 껍질, 화면 기준 일정 두께)으로 한 번 더 그림
///  - 아이콘 판(스프라이트): 같은 스프라이트를 Custom/RaritySilhouette로 1.12배 크게 뒤에 깔아 테두리처럼
///  - 색: 일반 흰 / 고급 초록 / 희귀 파랑 / 영웅 보라 / 전설 주황, 알파 0.45 (희미하지만 채도는 그대로)
///  - 성능: 플레이어 20m 안에서만 켬 (0.25초마다 거리 확인)
/// 예전 ItemOutlineGlow(하늘색 발광 + 둥실)는 그대로 두고 쓰지 않음 (PickupItem.addGlow 기본 끔).
/// </summary>
public class DropRarityOutline : MonoBehaviour
{
    public const float ShowRadius = 20f;
    public const float Alpha = 0.45f;
    const float MeshWidth = 0.004f;   // 화면 높이 대비 (1080p 약 2px)
    const float SpriteScale = 1.12f;

    readonly List<Renderer> outlines = new List<Renderer>();
    static Transform player;
    static Shader outlineShader, silhouetteShader;
    static readonly Dictionary<Rarity, Material> meshMats = new Dictionary<Rarity, Material>();
    float nextCheck;
    bool shown = true;

    /// <summary>등급별 외곽선 색 (요청 색 기준. 핫바/인벤토리 RarityColors와 달리 일반 = 흰색)</summary>
    public static Color ColorFor(Rarity r)
    {
        Color c = r switch
        {
            Rarity.Common => Color.white,
            Rarity.Uncommon => new Color(0.30f, 0.90f, 0.35f),   // 초록
            Rarity.Rare => new Color32(0x3A, 0x8D, 0xFF, 0xFF),    // 파랑
            Rarity.Epic => new Color32(0xB0, 0x4D, 0xFF, 0xFF),    // 보라
            Rarity.Legendary => new Color32(0xFF, 0x9A, 0x1A, 0xFF), // 주황
            _ => Color.white,
        };
        c.a = Alpha;
        return c;
    }

    public void Setup(Rarity rarity)
    {
        if (outlineShader == null) outlineShader = Shader.Find("Custom/RarityOutline");
        if (silhouetteShader == null) silhouetteShader = Shader.Find("Custom/RaritySilhouette");
        Color col = ColorFor(rarity);

        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r.name == "_RarityOutline") continue;
            if (r.GetComponentInParent<TMPro.TMP_Text>() != null) continue; // "F키로 획득" 글자
            if (r is MeshRenderer mr && outlineShader != null)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var go = new GameObject("_RarityOutline");
                go.transform.SetParent(r.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var omr = go.AddComponent<MeshRenderer>();
                var mats = new Material[mf.sharedMesh.subMeshCount];
                for (int i = 0; i < mats.Length; i++) mats[i] = MeshMat(rarity, col);
                omr.sharedMaterials = mats;
                omr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                omr.receiveShadows = false;
                outlines.Add(omr);
            }
            else if (r is SpriteRenderer sr && silhouetteShader != null && sr.sprite != null)
            {
                var go = new GameObject("_RarityOutline");
                go.transform.SetParent(sr.transform, false);
                go.transform.localScale = Vector3.one * SpriteScale;
                go.transform.localPosition = new Vector3(0f, 0f, 0.01f); // 아이콘 뒤
                var osr = go.AddComponent<SpriteRenderer>();
                osr.sprite = sr.sprite;
                osr.sharedMaterial = new Material(silhouetteShader) { name = "RaritySilhouette(Runtime)" };
                osr.sharedMaterial.color = col;
                osr.sortingLayerID = sr.sortingLayerID;
                osr.sortingOrder = sr.sortingOrder - 1;
                outlines.Add(osr);
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
        m.SetFloat("_Width", MeshWidth);
        meshMats[r] = m;
        return m;
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
