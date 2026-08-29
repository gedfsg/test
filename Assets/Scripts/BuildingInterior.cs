using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 플레이어가 건물 트리거에 들어오면 지정된 오브젝트(지붕, 윗벽 등)를 반투명하게 처리.
/// 건물에 BoxCollider(Is Trigger) 붙이고, 숨길 오브젝트들을 배열에 드래그.
/// 충돌(막힘)용 콜라이더는 별도의 non-trigger Collider를 추가로 사용할 것
/// (이 스크립트는 isTrigger=true인 콜라이더만 트리거로 사용함).
/// </summary>
[RequireComponent(typeof(Collider))]
public class BuildingInterior : MonoBehaviour
{
    [Header("플레이어 진입 시 반투명 처리할 오브젝트 (지붕, 윗벽 등)")]
    public GameObject[] objectsToHide;

    [Header("페이드 설정")]
    public bool useFade = true;          // 즉시 바뀌지 않고 부드럽게
    public float fadeDuration = 0.3f;
    public string playerTag = "Player";

    [Header("진입 시 알파값 (0=완전 투명, 1=불투명, 완전히 사라지지 않게 0보다 크게)")]
    [Range(0.05f, 1f)] public float hiddenAlpha = 0.3f;

    // ─────────────────────────────────────────────
    private bool playerInside;
    private readonly Dictionary<Renderer, Material[]> instanceMats = new Dictionary<Renderer, Material[]>();
    private Coroutine fadeRoutine;

    void Awake()
    {
        // 이 컴포넌트가 쓸 트리거 콜라이더를 찾음 (isTrigger=true인 것 우선)
        Collider triggerCol = FindTriggerCollider();
        if (triggerCol != null) triggerCol.isTrigger = true;

        // 숨길 오브젝트들의 렌더러를 인스턴스 머티리얼로 전환 (다른 건물에 영향 없게)
        foreach (var obj in objectsToHide)
        {
            if (obj == null) continue;
            foreach (var r in obj.GetComponentsInChildren<Renderer>(true))
            {
                if (instanceMats.ContainsKey(r)) continue;
                Material[] mats = r.materials; // 접근 시 자동으로 인스턴스 복제됨
                instanceMats[r] = mats;
                foreach (var m in mats) PrepareTransparent(m);
            }
        }
    }

    // 여러 Collider가 있을 수 있음(막힘용 콜라이더 별도 추가 시) → isTrigger=true인 것만 사용
    private Collider FindTriggerCollider()
    {
        foreach (var c in GetComponents<Collider>())
            if (c.isTrigger) return c;
        return GetComponent<Collider>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (playerInside) return;
        playerInside = true;
        SetHidden(true);
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (!playerInside) return;
        playerInside = false;
        SetHidden(false);
    }

    void SetHidden(bool hide)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        float targetAlpha = hide ? hiddenAlpha : 1f;
        fadeRoutine = useFade ? StartCoroutine(FadeRoutine(targetAlpha)) : null;
        if (!useFade) ApplyAlphaToAll(targetAlpha);
    }

    IEnumerator FadeRoutine(float targetAlpha)
    {
        Dictionary<Material, float> startAlphas = new Dictionary<Material, float>();
        foreach (var mats in instanceMats.Values)
            foreach (var m in mats)
                if (!startAlphas.ContainsKey(m))
                    startAlphas[m] = GetAlpha(m);

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeDuration;
            foreach (var kv in startAlphas)
                SetAlpha(kv.Key, Mathf.Lerp(kv.Value, targetAlpha, t));
            yield return null;
        }

        foreach (var kv in startAlphas)
            SetAlpha(kv.Key, targetAlpha);
    }

    void ApplyAlphaToAll(float alpha)
    {
        foreach (var mats in instanceMats.Values)
            foreach (var m in mats)
                SetAlpha(m, alpha);
    }

    // ── 머티리얼 알파 유틸 ────────────────────────────
    static float GetAlpha(Material m) => GetColor(m).a;

    static Color GetColor(Material m)
    {
        if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
        if (m.HasProperty("_Color")) return m.color;
        return Color.white;
    }

    static void SetAlpha(Material m, float alpha)
    {
        Color c = GetColor(m);
        c.a = alpha;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        else if (m.HasProperty("_Color")) m.color = c;
    }

    // URP Lit/Simple Lit 머티리얼을 알파 블렌딩 가능하게 전환.
    // 알파=1일 때는 기존 Opaque와 시각적으로 동일하게 보임.
    static void PrepareTransparent(Material m)
    {
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
        if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = (int)RenderQueue.Transparent;
    }

    void OnDrawGizmosSelected()
    {
        Collider col = FindTriggerCollider();
        if (col == null) return;
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.3f);
        Gizmos.matrix = transform.localToWorldMatrix;
        if (col is BoxCollider box)
            Gizmos.DrawCube(box.center, box.size);
    }
}
