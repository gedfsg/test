using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어 X-Ray 시스템.
/// 각 렌더러마다 2개의 고스트 생성:
///   1. StencilGhost - 플레이어가 보이는 곳에 스텐실=1 마킹 (마스크 역할)
///   2. XRayGhost    - 가려졌을 때만 텍스처 표시 (스텐실≠1 영역에만)
/// → 자기 자신(몸/총)에 의한 가림은 X-Ray 안 뜸, 외부(벽/나무)에 가려질 때만 뜸.
/// </summary>
public class PlayerXRay : MonoBehaviour
{
    [Header("X-Ray 색상 (텍스처 위 곱해질 틴트)")]
    public Color xrayTint = new Color(1f, 1f, 1f, 1f);

    [Header("가려졌을 때 투명도")]
    [Range(0f, 1f)] public float occludedAlpha = 0.85f;

    [Header("새 렌더러 감지 주기 (초)")]
    public float refreshInterval = 0.3f;

    // ─────────────────────────────────────────────
    private Shader xrayShader;
    private Shader stencilShader;
    private float  nextRefreshTime;
    private readonly HashSet<Renderer> processed = new HashSet<Renderer>();

    void Start()
    {
        xrayShader    = Shader.Find("Custom/PlayerXRay");
        stencilShader = Shader.Find("Custom/PlayerStencilWriter");

        if (xrayShader == null || stencilShader == null)
        {
            Debug.LogError("[PlayerXRay] 셰이더 못 찾음. Assets/Shaders/ 폴더의 PlayerXRay.shader & PlayerStencilWriter.shader 확인");
            enabled = false;
            return;
        }
        RefreshGhosts();
    }

    void LateUpdate()
    {
        if (Time.time >= nextRefreshTime)
        {
            nextRefreshTime = Time.time + refreshInterval;
            RefreshGhosts();
        }
    }

    /// <summary>외부에서 즉시 갱신 호출 (예: 무기 장착 직후)</summary>
    public void RefreshGhosts()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null) continue;
            string n = r.gameObject.name;
            if (n.EndsWith("_XRayGhost") || n.EndsWith("_StencilGhost")) continue;
            if (processed.Contains(r)) continue;

            CreateStencilGhost(r);
            CreateXRayGhost(r);
            processed.Add(r);
        }
    }

    // ── 스텐실 마스크 고스트 생성 ─────────────────────
    void CreateStencilGhost(Renderer original)
    {
        GameObject ghost = new GameObject(original.gameObject.name + "_StencilGhost");
        ghost.transform.SetParent(original.transform, false);
        ghost.layer = original.gameObject.layer;

        int matCount = original.sharedMaterials.Length;
        Material[] mats = new Material[matCount];
        for (int i = 0; i < matCount; i++) mats[i] = new Material(stencilShader);

        AttachMeshAndMaterials(ghost, original, mats);
    }

    // ── X-Ray 고스트 생성 ────────────────────────────
    void CreateXRayGhost(Renderer original)
    {
        GameObject ghost = new GameObject(original.gameObject.name + "_XRayGhost");
        ghost.transform.SetParent(original.transform, false);
        ghost.layer = original.gameObject.layer;

        Material[] origMats = original.sharedMaterials;
        Material[] ghostMats = new Material[origMats.Length];
        for (int i = 0; i < origMats.Length; i++)
        {
            Material xrayMat = new Material(xrayShader);

            if (origMats[i] != null)
            {
                Texture tex = null;
                if (origMats[i].HasProperty("_MainTex"))                 tex = origMats[i].GetTexture("_MainTex");
                if (tex == null && origMats[i].HasProperty("_BaseMap"))  tex = origMats[i].GetTexture("_BaseMap");
                if (tex != null) xrayMat.SetTexture("_MainTex", tex);

                Color baseColor = Color.white;
                if (origMats[i].HasProperty("_BaseColor")) baseColor = origMats[i].GetColor("_BaseColor");
                else if (origMats[i].HasProperty("_Color")) baseColor = origMats[i].color;
                xrayMat.SetColor("_Color", baseColor * xrayTint);
            }
            else
            {
                xrayMat.SetColor("_Color", xrayTint);
            }
            xrayMat.SetFloat("_Alpha", occludedAlpha);
            ghostMats[i] = xrayMat;
        }

        AttachMeshAndMaterials(ghost, original, ghostMats);
    }

    // ── 메쉬/머티리얼 부착 (SkinnedMesh / MeshRenderer 둘 다 지원) ──
    void AttachMeshAndMaterials(GameObject ghost, Renderer original, Material[] mats)
    {
        if (original is SkinnedMeshRenderer smr)
        {
            SkinnedMeshRenderer newSmr = ghost.AddComponent<SkinnedMeshRenderer>();
            newSmr.sharedMesh      = smr.sharedMesh;
            newSmr.bones           = smr.bones;
            newSmr.rootBone        = smr.rootBone;
            newSmr.localBounds     = smr.localBounds;
            newSmr.sharedMaterials = mats;
        }
        else if (original is MeshRenderer)
        {
            MeshFilter mfOrig = original.GetComponent<MeshFilter>();
            if (mfOrig == null || mfOrig.sharedMesh == null) { Destroy(ghost); return; }
            ghost.AddComponent<MeshFilter>().sharedMesh = mfOrig.sharedMesh;
            MeshRenderer newMr = ghost.AddComponent<MeshRenderer>();
            newMr.sharedMaterials = mats;
        }
        else
        {
            Destroy(ghost);
        }
    }
}
