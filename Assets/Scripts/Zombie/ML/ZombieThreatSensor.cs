using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// 위협 센서 (반격하는 플레이어). 플레이어가 나를 겨누고 있는지, 어느 쪽으로 비키면 되는지,
/// 재장전 중인지, 내 체력이 얼마인지를 알려준다. 조율기가 정한 자리까지 '살아서' 가는 법을 배우는 데 쓴다.
///
/// 관측 (7개):
///   [0] 상대가 총을 들었는가 (1/0) — 학습장: 더미의 사격이 켜져 있을 때 / 게임: 항상 1
///   [1] 조준 정렬: 상대 총구 방향과 '상대→나' 방향의 코사인 (1 = 정확히 나를 겨눔, -1 = 등을 보임)
///   [2] 조준 옆 방향: 같은 두 방향의 사인 (+ = 총구 기준 오른쪽에 있음) — 어느 쪽으로 비킬지
///   [3] 상대가 재장전 중인가 (1/0) — 틈
///   [4] 내 체력 비율 (0~1)
///   [5] 내가 지금 상대를 보고 있는가 (1/0) — 0이면 [1][2][3][6]은 0으로 채운다 (벽 너머는 모른다)
///   [6] 상대가 지금 나를 표적으로 잡았는가 (1/0) — 학습장: 더미의 표적 / 게임: 정렬이 아주 높으면 1
///
/// ⚠ 다른 센서처럼 모델 입력이 바뀐다. 이 센서로 학습한 모델(이름에 'shoot')에만 붙일 것.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Zombie/Zombie Threat Sensor")]
public class ZombieThreatSensor : SensorComponent
{
    public const int    ObservationLength = 7;
    public const string SensorName        = "ZombieThreat";

    [Tooltip("켜면 [4] 내 체력을 항상 1로 넣는다.\n" +
             "shoot3부터는 좀비 체력이 사실상 무한(1,000,000)인 상태로 학습해서, 모델은 체력 1만 봤다.\n" +
             "게임에서 실제 체력(0.5 등)을 넣으면 입력 정규화 때문에 처음 보는 극단값이 되어 움직임이 이상해질 수 있다.\n" +
             "체력이 있는 상태로 학습한 예전 모델(shoot1/shoot2)을 쓸 때만 끈다.")]
    public bool constantHealth = true;

    private ZombieController body;
    private ZombieAgent      agent;

    public override ISensor[] CreateSensors()
    {
        return new ISensor[] { new ThreatSensor(this) };
    }

    internal void Fill(float[] buf)
    {
        System.Array.Clear(buf, 0, buf.Length);
        try
        {
            if (body  == null) body  = GetComponent<ZombieController>();
            if (agent == null) agent = GetComponent<ZombieAgent>();
            if (body == null) return;

            // 내 체력: 학습장은 가상 체력, 게임은 실제 체력
            buf[4] = constantHealth ? 1f
                   : agent != null && agent.arena != null ? agent.arena.HealthFraction(body) : body.HealthNormalized;

            Transform target = body.CurrentTarget;
            if (target == null) return;

            var dummy = target.GetComponent<DummyPlayer>();
            bool armed = dummy == null || dummy.ShootingActive;
            buf[0] = armed ? 1f : 0f;
            if (!armed) return;

            bool sees = body.SeesTarget;
            buf[5] = sees ? 1f : 0f;
            if (!sees) return;

            Vector3 aim = dummy != null ? dummy.AimForward : target.forward;
            aim.y = 0f;
            Vector3 toMe = transform.position - target.position;
            toMe.y = 0f;
            if (aim.sqrMagnitude < 1e-4f || toMe.sqrMagnitude < 1e-4f) return;
            aim.Normalize();
            toMe.Normalize();

            float cos = Vector3.Dot(aim, toMe);
            float sin = aim.x * toMe.z - aim.z * toMe.x;   // 위에서 본 외적: + = 총구 기준 한쪽
            buf[1] = cos;
            buf[2] = -sin;
            buf[3] = dummy != null && dummy.IsReloading ? 1f : 0f;
            buf[6] = dummy != null ? (dummy.AimTarget == transform ? 1f : 0f) : (cos > 0.985f ? 1f : 0f);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ZombieThreatSensor] {name}: 관측 실패 → 0으로 채움\n{e}", this);
            System.Array.Clear(buf, 0, buf.Length);
        }
    }

    private class ThreatSensor : ISensor
    {
        private readonly ZombieThreatSensor owner;
        private readonly float[] buffer = new float[ObservationLength];
        private readonly ObservationSpec spec = ObservationSpec.Vector(ObservationLength);

        public ThreatSensor(ZombieThreatSensor owner) { this.owner = owner; }

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
