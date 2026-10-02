using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 맵 시작 시 건물 내부 + 야외 곳곳에 탄약 아이템을 자동 배치.
/// ZombieSpawner가 쓰는 IndoorSpawnPoints(91개)를 그대로 재사용해서 건물 내부에 뿌리고,
/// 야외는 맵 중심 기준 랜덤 위치(NavMesh 위)에 뿌림. 시각(박스+바운스+반짝임)은
/// AmmoDropSpawner를 그대로 재사용(좀비 드랍과 동일한 룩).
/// 일정 시간마다 부족한 만큼 다시 채워 넣음(옵션, enableRespawn으로 끌 수 있음).
/// </summary>
public class ItemSpawner : MonoBehaviour
{
    [Header("탄약 데이터 풀 (랜덤으로 섞어서 스폰)")]
    public AmmoData[] ammoPool;

    [Header("실내 스폰 (비워두면 씬의 IndoorSpawnPoints를 자동으로 찾음)")]
    public Transform[] indoorSpawnPoints;
    [Range(0f, 1f)] public float indoorSpawnPointUseRatio = 0.3f;

    [Header("야외 랜덤 스폰")]
    public bool enableOutdoorScatter = true;
    public int outdoorScatterCount = 15;
    public float outdoorScatterRadius = 120f;
    public Vector3 mapCenter = Vector3.zero;

    [Header("수량 / 재생성")]
    public int minAmountPerItem = 10;
    public int maxAmountPerItem = 30;
    public int maxActiveItems = 40;
    public bool enableRespawn = true;
    public float respawnCheckInterval = 60f;

    readonly List<GameObject> activeItems = new List<GameObject>();
    float respawnTimer;

    void Start()
    {
        if (indoorSpawnPoints == null || indoorSpawnPoints.Length == 0)
        {
            var container = GameObject.Find("IndoorSpawnPoints");
            if (container != null)
            {
                indoorSpawnPoints = new Transform[container.transform.childCount];
                for (int i = 0; i < container.transform.childCount; i++)
                    indoorSpawnPoints[i] = container.transform.GetChild(i);
            }
        }

        SpawnBatch();
    }

    void Update()
    {
        if (!enableRespawn) return;

        respawnTimer += Time.deltaTime;
        if (respawnTimer < respawnCheckInterval) return;
        respawnTimer = 0f;

        activeItems.RemoveAll(i => i == null);
        if (activeItems.Count < maxActiveItems)
            SpawnBatch();
    }

    void SpawnBatch()
    {
        activeItems.RemoveAll(i => i == null);
        if (ammoPool == null || ammoPool.Length == 0) return;

        if (indoorSpawnPoints != null && indoorSpawnPoints.Length > 0)
        {
            int indoorCount = Mathf.RoundToInt(indoorSpawnPoints.Length * indoorSpawnPointUseRatio);
            var shuffled = new List<Transform>(indoorSpawnPoints);
            Shuffle(shuffled);
            for (int i = 0; i < indoorCount && i < shuffled.Count && activeItems.Count < maxActiveItems; i++)
                SpawnAt(shuffled[i].position);
        }

        if (enableOutdoorScatter)
        {
            for (int i = 0; i < outdoorScatterCount && activeItems.Count < maxActiveItems; i++)
            {
                Vector2 rnd = Random.insideUnitCircle * outdoorScatterRadius;
                Vector3 candidate = mapCenter + new Vector3(rnd.x, 0f, rnd.y);
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 15f, NavMesh.AllAreas))
                    SpawnAt(hit.position);
            }
        }
    }

    void SpawnAt(Vector3 pos)
    {
        var data = ammoPool[Random.Range(0, ammoPool.Length)];
        int amount = Random.Range(minAmountPerItem, maxAmountPerItem + 1);
        var go = AmmoDropSpawner.Spawn(pos, data, amount);
        if (go != null) activeItems.Add(go);
    }

    static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
