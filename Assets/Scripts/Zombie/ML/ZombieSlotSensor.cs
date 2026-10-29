using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// 자리 센서 (조율기 + 강화학습). 무리 조율기(HunterPackTactics)가 이 좀비에게 정해 준 자리를 알려준다.
///
/// 순수 다중 에이전트 학습에서 포위가 안 나온 이유는 "다섯 마리가 똑같아서 누가 어디로 갈지 정할 단서가 없다"였다.
/// 그 결정은 조율기가 하고, 강화학습은 '그 자리까지 어떻게 움직이고 언제 덮칠지'만 배운다.
///
/// 관측 (6개):
///   [0]    명령 있음 (1/0) — 무리가 플레이어를 모르면 0
///   [1,2]  명령 지점 방향 (내 기준 x, z)
///   [3]    명령 지점까지 거리 / 30
///   [4]    덮치기 명령인가 (1/0) — 명령 지점이 곧 플레이어 위치 (조이기 단계, 코앞, 탱커 몰이)
///   [5]    무리가 본 플레이어의 가장 큰 빈틈 / 360 (작을수록 거의 갇힘)
///
/// ⚠ 지형·역할 센서처럼 모델 입력이 바뀐다. 이 센서로 학습한 모델(이름에 'slot')에만 붙일 것.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Zombie/Zombie Slot Sensor")]
public class ZombieSlotSensor : SensorComponent
{
    public const int    ObservationLength = 6;
    public const string SensorName        = "ZombieSlot";

    private ZombieController body;

    public override ISensor[] CreateSensors()
    {
        return new ISensor[] { new SlotSensor(this) };
    }

    internal void Fill(float[] buf)
    {
        System.Array.Clear(buf, 0, buf.Length);
        try
        {
            if (body == null) body = GetComponent<ZombieController>();
            var pack = HunterPackTactics.PackOf(body);
            if (pack == null) return;

            buf[5] = Mathf.Clamp01(pack.LastGapDeg / 360f);

            if (!pack.TryGetOrder(body, out Vector3 point, out bool rush)) return;

            Vector3 d = point - transform.position;
            d.y = 0f;
            float dist = d.magnitude;
            Vector3 local = dist > 0.0001f ? transform.InverseTransformDirection(d / dist) : Vector3.zero;

            buf[0] = 1f;
            buf[1] = local.x;
            buf[2] = local.z;
            buf[3] = Mathf.Clamp01(dist / ZombieObservation.ObservationRange);
            buf[4] = rush ? 1f : 0f;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ZombieSlotSensor] {name}: 관측 실패 → 0으로 채움\n{e}", this);
            System.Array.Clear(buf, 0, buf.Length);
        }
    }

    private class SlotSensor : ISensor
    {
        private readonly ZombieSlotSensor owner;
        private readonly float[] buffer = new float[ObservationLength];
        private readonly ObservationSpec spec = ObservationSpec.Vector(ObservationLength);

        public SlotSensor(ZombieSlotSensor owner) { this.owner = owner; }

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
