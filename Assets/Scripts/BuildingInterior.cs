using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어가 건물 트리거에 들어오면 지정된 오브젝트(지붕, 윗벽 등)를 숨김.
/// 건물에 BoxCollider(Is Trigger) 붙이고, 숨길 오브젝트들을 배열에 드래그.
/// </summary>
[RequireComponent(typeof(Collider))]
public class BuildingInterior : MonoBehaviour
{
    [Header("플레이어 진입 시 숨길 오브젝트 (지붕, 윗벽 등)")]
    public GameObject[] objectsToHide;

    [Header("페이드 설정")]
    public bool useFade = true;          // 즉시 사라지지 않고 부드럽게
    public float fadeDuration = 0.3f;
    public string playerTag = "Player";

    // ─────────────────────────────────────────────
    private bool playerInside;
    private readonly List<Renderer> cachedRenderers = new List<Renderer>();

    void Awake()
    {
        // 트리거 콜라이더 자동 설정
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;

        // 숨길 오브젝트들의 모든 렌더러 캐싱
        foreach (var obj in objectsToHide)
        {
            if (obj == null) continue;
            cachedRenderers.AddRange(obj.GetComponentsInChildren<Renderer>(true));
        }
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
        if (useFade)
            StartCoroutine(FadeRoutine(hide));
        else
            foreach (var r in cachedRenderers)
                if (r != null) r.enabled = !hide;
    }

    System.Collections.IEnumerator FadeRoutine(bool hide)
    {
        // 단순 버전: 페이드 시간만큼 기다린 후 토글
        // (커스텀 셰이더 호환 위해 알파 보간 대신 단순 토글)
        yield return new WaitForSeconds(hide ? 0f : fadeDuration);
        foreach (var r in cachedRenderers)
            if (r != null) r.enabled = !hide;
    }

    void OnDrawGizmosSelected()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.3f);
        Gizmos.matrix = transform.localToWorldMatrix;
        if (col is BoxCollider box)
            Gizmos.DrawCube(box.center, box.size);
    }
}
