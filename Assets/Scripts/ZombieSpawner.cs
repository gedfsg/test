using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class ZombieSpawner : MonoBehaviour
{
    [Header("스폰 설정")]
    public GameObject zombiePrefab;
    public int initialCount = 5;

    [Header("재스폰 딜레이 (초)")]
    public float minRespawnDelay = 5f;
    public float maxRespawnDelay = 10f;

    [Header("스폰 범위")]
    public float spawnRadius = 25f;       // 스포너 중심 반경
    public float minPlayerDistance = 10f; // 플레이어로부터 최소 거리

    private Transform player;

    void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.transform;

        for (int i = 0; i < initialCount; i++)
            SpawnZombie();
    }

    void SpawnZombie()
    {
        Vector3 pos = GetSpawnPosition();
        if (pos == Vector3.zero) return;

        GameObject zombie = Instantiate(zombiePrefab, pos, Quaternion.identity);

        Health health = zombie.GetComponent<Health>();
        if (health != null)
            health.onDeath.AddListener(OnZombieDied);
    }

    void OnZombieDied()
    {
        StartCoroutine(RespawnRoutine());
    }

    IEnumerator RespawnRoutine()
    {
        float delay = Random.Range(minRespawnDelay, maxRespawnDelay);
        yield return new WaitForSeconds(delay);
        SpawnZombie();
    }

    Vector3 GetSpawnPosition()
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            // 스포너 주변 랜덤 방향 + 거리
            float angle = Random.Range(0f, 360f);
            float dist  = Random.Range(minPlayerDistance, spawnRadius);
            Vector3 candidate = transform.position +
                new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad)) * dist;

            // 플레이어와 너무 가까우면 스킵
            if (player != null && Vector3.Distance(candidate, player.position) < minPlayerDistance)
                continue;

            // NavMesh 위 유효한 위치 탐색
            NavMeshHit hit;
            if (NavMesh.SamplePosition(candidate, out hit, 5f, NavMesh.AllAreas))
                return hit.position;
        }

        Debug.LogWarning("[ZombieSpawner] 유효한 스폰 위치를 찾지 못했습니다.");
        return Vector3.zero;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, spawnRadius);

        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, minPlayerDistance);
    }
}
