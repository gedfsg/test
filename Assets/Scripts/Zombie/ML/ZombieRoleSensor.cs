using System.Collections.Generic;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// 역할 센서. 섞인 무리(일반·달리기·탱커)에서 각자 자기 몫을 고르게 하려고,
/// 나와 동료들의 최고 속도를 알려준다.
///
/// 관측 (5개):
///   [0]    내 최고 속도 / 5
///   [1~4]  가까운 동료 4명의 최고 속도 / 5 (동료 칸 순서는 벡터 관측과 같다. 빈 칸은 0)
///
/// 왜 종류(one-hot)가 아니라 속도인가:
///   학습장에서 종류끼리 다른 건 속도뿐이다 (더미는 체력이 없어서 공격력·체력은 의미가 없다).
///   속도로 주면 게임에서 프로필 수치를 조금 바꿔도 모델이 그대로 읽을 수 있다.
///
/// 역할을 직접 정해주지 않는다. 보상은 지금처럼 팀 공유이고, 속도가 다르면 각자 잘하는 일이
/// 달라서 '빠른 좀비는 앞질러 막고, 느린 좀비는 출구를 지키는' 분담이 생기기를 기대한다.
///
/// ⚠ 지형 센서와 마찬가지로 모델 입력이 바뀐다. 이 센서로 학습한 모델에만 붙일 것.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Zombie/Zombie Role Sensor")]
public class ZombieRoleSensor : SensorComponent
{
    public const int    ObservationLength = 1 + ZombieAgent.TeammateSlots;   // 5
    public const float  SpeedScale        = 5f;
    public const string SensorName        = "ZombieRole";

    private ZombieController body;
    private ZombieAgent      agent;
    private readonly List<ZombieAgent> mates = new List<ZombieAgent>();

    public override ISensor[] CreateSensors()
    {
        return new ISensor[] { new RoleSensor(this) };
    }

    internal void Fill(float[] buf)
    {
        System.Array.Clear(buf, 0, buf.Length);
        try
        {
            if (body  == null) body  = GetComponent<ZombieController>();
            if (agent == null) agent = GetComponent<ZombieAgent>();
            if (body == null || agent == null) return;

            buf[0] = Mathf.Clamp01(body.moveSpeed / SpeedScale);

            agent.GetNearestTeammates(mates);
            for (int i = 0; i < ZombieAgent.TeammateSlots && i < mates.Count; i++)
            {
                var b = mates[i].Body;
                if (b != null) buf[1 + i] = Mathf.Clamp01(b.moveSpeed / SpeedScale);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ZombieRoleSensor] {name}: 관측 실패 → 0으로 채움\n{e}", this);
            System.Array.Clear(buf, 0, buf.Length);
        }
    }

    private class RoleSensor : ISensor
    {
        private readonly ZombieRoleSensor owner;
        private readonly float[] buffer = new float[ObservationLength];
        private readonly ObservationSpec spec = ObservationSpec.Vector(ObservationLength);

        public RoleSensor(ZombieRoleSensor owner) { this.owner = owner; }

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
