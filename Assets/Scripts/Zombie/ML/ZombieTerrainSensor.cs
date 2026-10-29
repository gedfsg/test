using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 지형 센서. 좀비가 벽과 골목을 '보게' 해서 막다른 곳으로 몰아넣는 전략을 배울 수 있게 한다.
///
/// 관측 (21개):
///   [자기 주변 8]   내 위치에서 8방향(45°)으로 걸어갈 수 있는 거리. 1 = 15m 이상 트임, 0 = 바로 벽
///   [타겟 정보 1]   아래 12개가 유효한지 (타겟 위치를 모르면 0)
///   [타겟 주변 12]  내가 믿는 타겟 위치에서 12방향(30°)으로 걸어갈 수 있는 거리
///                   = 플레이어가 어느 쪽으로 도망칠 수 있는가. 방향은 내 기준 좌표.
///
/// 왜 물리 레이캐스트(RayPerceptionSensor)가 아니라 NavMesh 레이인가:
///   - 물리 레이는 레이어/태그로 벽만 골라내야 하는데, 학습장과 게임 씬의 레이어 구성이 달라서
///     학습 때와 다른 것을 '벽'으로 읽게 된다. NavMesh는 두 씬 모두 '걸을 수 있는 곳'이라는
///     같은 뜻을 가진다.
///   - 좀비와 플레이어는 NavMesh에 구멍을 내지 않으므로 레이에 섞이지 않는다 (지형만 본다).
///   - 더미가 도망 방향을 고를 때도 NavMesh.Raycast로 막다른 길을 판단한다 → 같은 잣대.
///
/// 벽 투시가 아니다: 타겟 주변 레이는 '내가 믿는 위치'(보일 때 실제 위치, 안 보이면 마지막 목격 지점)
/// 기준이다. 지형 자체는 맵을 아는 좀비라면 알 수 있는 정보로 본다.
///
/// ⚠ 이 컴포넌트를 붙이면 모델 입력이 바뀐다. 지형 센서로 학습한 모델에만 붙일 것.
///   (이전 모델에 붙이면 관측 개수가 안 맞아 오류가 난다)
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Zombie/Zombie Terrain Sensor")]
public class ZombieTerrainSensor : SensorComponent
{
    // 모델 입력 크기를 정하는 값이라 인스펙터에서 바꾸지 못하게 상수로 둔다.
    public const int SelfRays          = 8;
    public const int TargetRays        = 12;
    public const int ObservationLength = SelfRays + 1 + TargetRays;   // 21
    public const string SensorName     = "ZombieTerrain";

    private ZombieMLBrain brain;
    private ZombieAgent   agent;

    public override ISensor[] CreateSensors()
    {
        return new ISensor[] { new TerrainSensor(this) };
    }

    /// <summary>관측값을 buf에 채운다. 실패해도 길이는 항상 ObservationLength.</summary>
    internal void Fill(float[] buf)
    {
        System.Array.Clear(buf, 0, buf.Length);
        try
        {
            if (brain == null) brain = GetComponent<ZombieMLBrain>();
            if (agent == null) agent = GetComponent<ZombieAgent>();

            // ── 자기 주변 ──
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit me, 1.5f, NavMesh.AllAreas))
            {
                for (int k = 0; k < SelfRays; k++)
                {
                    Vector3 world = transform.TransformDirection(LocalDir(k, SelfRays));
                    buf[k] = TerrainProbe.Ray(me.position, world);
                }
            }

            // ── 타겟 주변 ──
            if (!TryGetBelievedTarget(out Vector3 target)) return;
            float[] ring = TerrainProbe.Ring(target);
            if (ring == null) return;

            buf[SelfRays] = 1f;
            for (int j = 0; j < TargetRays; j++)
            {
                Vector3 world = transform.TransformDirection(LocalDir(j, TargetRays));
                float bearing = Mathf.Atan2(world.x, world.z) * Mathf.Rad2Deg;
                buf[SelfRays + 1 + j] = TerrainProbe.Sample(ring, bearing);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ZombieTerrainSensor] {name}: 관측 실패 → 0으로 채움\n{e}", this);
            System.Array.Clear(buf, 0, buf.Length);
        }
    }

    /// <summary>
    /// 내가 믿는 타겟 위치. 내 믿음(보일 때 실제 위치 / 안 보이면 마지막 목격 지점)이 있으면 그것,
    /// 없으면 팀 공유 정보. 둘 다 없으면 false.
    /// </summary>
    private bool TryGetBelievedTarget(out Vector3 pos)
    {
        pos = Vector3.zero;

        if (brain != null && brain.HasObservation)
        {
            var obs = brain.LastObservation;
            if (obs.hasBelief)
            {
                Vector3 dir = transform.TransformDirection(obs.believedDirLocal);
                dir.y = 0f;
                if (dir.sqrMagnitude > 1e-6f) dir.Normalize();
                pos = transform.position + dir * obs.believedDistance;
                return true;
            }
        }

        if (agent != null && agent.intel != null && agent.intel.HasInfo)
        {
            pos = agent.intel.LastPosition;
            return true;
        }
        return false;
    }

    private static Vector3 LocalDir(int i, int count)
    {
        float rad = (360f / count * i) * Mathf.Deg2Rad;   // 0 = 정면, 시계 방향
        return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
    }

    // ─────────────────────────────────────────────
    private class TerrainSensor : ISensor
    {
        private readonly ZombieTerrainSensor owner;
        private readonly float[] buffer = new float[ObservationLength];
        private readonly ObservationSpec spec = ObservationSpec.Vector(ObservationLength);

        public TerrainSensor(ZombieTerrainSensor owner) { this.owner = owner; }

        public ObservationSpec GetObservationSpec() => spec;

        public int Write(ObservationWriter writer)
        {
            if (owner != null) owner.Fill(buffer);
            else System.Array.Clear(buffer, 0, buffer.Length);
            writer.AddList(buffer);
            return buffer.Length;
        }

        public byte[] GetCompressedObservation() => null;
        public void Update() { }
        public void Reset() { }
        public CompressionSpec GetCompressionSpec() => CompressionSpec.Default();
        public string GetName() => SensorName;
    }
}

/// <summary>
/// NavMesh 레이 계산 모음. 센서와 학습장(보상·지표)이 같은 잣대를 쓰도록 한곳에 둔다.
///
/// 같은 지점 주변의 고리(ring)는 한 스텝 안에서 여러 좀비가 물어도 한 번만 계산한다
/// (한 구역 좀비 5마리는 대개 같은 팀 정보를 보므로 같은 지점을 묻는다).
/// </summary>
public static class TerrainProbe
{
    /// <summary>타겟 주변 고리의 방향 수 (10° 간격).</summary>
    public const int   RingRays    = 36;
    /// <summary>이 거리(m) 이상 트여 있으면 1로 친다.</summary>
    public const float MaxDistance = 15f;

    private const float CacheCell = 0.25f;   // 이 간격 안의 지점은 같은 지점으로 본다

    private static readonly Dictionary<Vector2Int, float[]> cache = new Dictionary<Vector2Int, float[]>();
    private static readonly Stack<float[]> pool = new Stack<float[]>();
    private static readonly HashSet<Vector2Int> misses = new HashSet<Vector2Int>();
    private static int cacheStamp = int.MinValue;

    /// <summary>origin에서 dir(월드) 방향으로 걸어갈 수 있는 거리 / MaxDistance (0~1).</summary>
    public static float Ray(Vector3 origin, Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return 0f;
        dir.Normalize();

        Vector3 end = origin + dir * MaxDistance;
        if (!NavMesh.Raycast(origin, end, out NavMeshHit hit, NavMesh.AllAreas)) return 1f;

        Vector3 d = hit.position - origin;
        d.y = 0f;
        return Mathf.Clamp01(d.magnitude / MaxDistance);
    }

    /// <summary>
    /// center 주변 RingRays방향의 트인 정도. i번째 = 월드 방위 i×10° (0 = +z, 시계 방향).
    /// center 근처에 NavMesh가 없으면 null. 반환 배열은 이번 스텝 안에서만 유효하다 (보관 금지).
    /// </summary>
    public static float[] Ring(Vector3 center)
    {
        int stamp = Academy.IsInitialized ? Academy.Instance.TotalStepCount : Time.frameCount;
        if (stamp != cacheStamp)
        {
            foreach (var arr in cache.Values) pool.Push(arr);
            cache.Clear();
            misses.Clear();
            cacheStamp = stamp;
        }

        var key = new Vector2Int(Mathf.RoundToInt(center.x / CacheCell), Mathf.RoundToInt(center.z / CacheCell));
        if (cache.TryGetValue(key, out float[] hitRing)) return hitRing;
        if (misses.Contains(key)) return null;

        if (!NavMesh.SamplePosition(center, out NavMeshHit on, 2f, NavMesh.AllAreas))
        {
            misses.Add(key);
            return null;
        }

        float[] ring = pool.Count > 0 ? pool.Pop() : new float[RingRays];
        for (int i = 0; i < RingRays; i++)
        {
            float rad = (360f / RingRays * i) * Mathf.Deg2Rad;
            ring[i] = Ray(on.position, new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)));
        }
        cache[key] = ring;
        return ring;
    }

    /// <summary>고리에서 임의 월드 방위(도)의 값을 이웃 두 방향 사이 보간으로 읽는다.</summary>
    public static float Sample(float[] ring, float bearingDeg)
    {
        if (ring == null || ring.Length == 0) return 0f;
        float step = 360f / ring.Length;
        float f    = Mathf.Repeat(bearingDeg, 360f) / step;
        int   i0   = Mathf.FloorToInt(f) % ring.Length;
        int   i1   = (i0 + 1) % ring.Length;
        return Mathf.Lerp(ring[i0], ring[i1], f - Mathf.Floor(f));
    }

    /// <summary>center 주변의 평균 트인 정도 (0~1). 낮을수록 벽에 몰려 있다. NavMesh 밖이면 -1.</summary>
    public static float Openness(Vector3 center)
    {
        float[] ring = Ring(center);
        if (ring == null) return -1f;
        float sum = 0f;
        foreach (float v in ring) sum += v;
        return sum / ring.Length;
    }
}
