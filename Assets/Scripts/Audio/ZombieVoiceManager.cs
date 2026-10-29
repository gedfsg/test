using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 좀비 목소리 풀 (전역). 좀비 100마리가 각자 소리를 내면 귀가 아프고 CPU도 낭비라서
///  - 동시에 최대 MaxVoices(4)개만 재생 (전용 AudioSource 4개를 돌려씀)
///  - 플레이어(리스너)에서 MaxDistance(20m) 밖 좀비는 재생 안 함
///  - 자리가 없으면 지금 재생 중인 것 중 가장 먼 좀비보다 가까울 때만 그 자리를 뺏음 (가까운 좀비 우선), 아니면 skip
///  - 상태별 쿨타임 (좀비마다 따로): 배회 10~25초 신음 / 추적 5~10초 신음 또는 비명 / 공격 즉시 비명 / 사망 즉시 단말마
/// 사용: ZombieController가 매 프레임 Tick(this, Idle/Chase), 공격·사망 때 Play(this, Attack/Death).
/// 씬 배치 불필요 - 처음 쓸 때 자동 생성.
/// </summary>
public class ZombieVoiceManager : MonoBehaviour
{
    public enum State { Idle, Chase, Attack, Death }

    public const int MaxVoices = 4;
    public const float MaxDistance = 20f;

    class Voice
    {
        public AudioSource src;
        public Transform follow;       // 소리 낸 좀비 (걸어가면 소리도 따라감)
        public ZombieController owner;
        public float distance;         // 리스너까지 거리 (자리 뺏기 판정용)
    }

    static ZombieVoiceManager instance;
    readonly Voice[] voices = new Voice[MaxVoices];
    readonly Dictionary<ZombieController, float> nextVoiceTime = new Dictionary<ZombieController, float>();

    /// <summary>지금 재생 중인 좀비 목소리 수 (검증/디버그용)</summary>
    public static int ActiveVoices
    {
        get
        {
            if (instance == null) return 0;
            int n = 0;
            foreach (var v in instance.voices) if (v.src.isPlaying) n++;
            return n;
        }
    }

    static ZombieVoiceManager Get()
    {
        if (instance != null) return instance;
        var go = new GameObject("ZombieVoiceManager");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ZombieVoiceManager>();
        return instance;
    }

    void Awake()
    {
        for (int i = 0; i < MaxVoices; i++)
        {
            var go = new GameObject("ZombieVoice_" + i);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 1f;                       // 3D
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 2f;
            src.maxDistance = MaxDistance + 5f;          // 20m 경계에서 뚝 끊기지 않게 살짝 여유
            src.dopplerLevel = 0f;
            src.outputAudioMixerGroup = AudioManager.GetGroup(AudioManager.Channel.Zombie);
            voices[i] = new Voice { src = src };
        }
    }

    void Update()
    {
        // 재생 중인 목소리는 좀비를 따라다님 (죽어서 사라지면 마지막 위치에 남음)
        Vector3 ear = AudioManager.ListenerPosition;
        foreach (var v in voices)
        {
            if (!v.src.isPlaying) { v.owner = null; v.follow = null; continue; }
            if (v.follow != null) v.src.transform.position = v.follow.position + Vector3.up * 1.5f;
            v.distance = Vector3.Distance(v.src.transform.position, ear);
        }
    }

    /// <summary>배회/추적 중 매 프레임 호출. 쿨타임이 지났을 때만 소리를 시도함</summary>
    public static void Tick(ZombieController zombie, State state)
    {
        var m = Get();
        float now = Time.time;
        if (!m.nextVoiceTime.TryGetValue(zombie, out float next))
        {
            // 처음 본 좀비: 다 같이 동시에 울지 않게 첫 소리 시각을 흩어놓음
            m.nextVoiceTime[zombie] = now + Cooldown(state) * Random.value;
            return;
        }
        if (now < next) return;
        m.nextVoiceTime[zombie] = now + Cooldown(state); // 재생 못 했어도(멀거나 자리 없음) 다음 쿨타임까지 대기
        Play(zombie, state);
    }

    // static float Cooldown(State state) => state == State.Chase ? Random.Range(2f, 5f) : Random.Range(5f, 15f); // (이전) 좀비가 많으면 시끄러움
    static float Cooldown(State state) => state == State.Chase ? Random.Range(5f, 10f) : Random.Range(10f, 25f);

    /// <summary>즉시 재생 시도 (공격/사망). 멀거나 자리가 없으면 false</summary>
    public static bool Play(ZombieController zombie, State state)
    {
        var lib = AudioManager.Library;
        if (zombie == null || lib == null) return false;
        var m = Get();

        Vector3 pos = zombie.transform.position + Vector3.up * 1.5f;
        float dist = Vector3.Distance(pos, AudioManager.ListenerPosition);
        if (dist > MaxDistance) return false;

        // 상태별 클립/볼륨: 신음 0.4 (배경), 비명/단말마 0.9 (위협)
        bool scream = state == State.Attack || (state == State.Chase && Random.value < 0.5f);
        AudioClip clip = state == State.Death ? SoundLibrary.Pick(lib.zombieDeath)
                       : scream ? SoundLibrary.Pick(lib.zombieScream)
                       : SoundLibrary.Pick(lib.zombieGroan);
        float volume = (state == State.Death || scream) ? AudioManager.Vol.ZombieScream : AudioManager.Vol.ZombieGroan;
        if (clip == null) return false;

        Voice slot = m.FindSlot(zombie, state, dist);
        if (slot == null) return false;

        slot.src.Stop();
        slot.owner = zombie;
        slot.follow = zombie.transform;
        slot.distance = dist;
        slot.src.transform.position = pos;
        slot.src.clip = clip;
        slot.src.volume = volume;
        slot.src.pitch = Random.Range(0.92f, 1.08f); // 같은 클립도 좀비마다 조금씩 다르게
        slot.src.Play();
        return true;
    }

    Voice FindSlot(ZombieController zombie, State state, float dist)
    {
        // 이 좀비가 이미 말하는 중: 공격/사망이면 자기 소리를 끊고 바꿈, 신음은 겹치지 않게 skip
        foreach (var v in voices)
            if (v.owner == zombie && v.src.isPlaying)
                return state >= State.Attack ? v : null;

        // 빈 자리
        foreach (var v in voices) if (!v.src.isPlaying) return v;

        // 자리 없음: 가장 먼 좀비보다 내가 가까우면 그 자리를 뺏음
        Voice farthest = null;
        foreach (var v in voices) if (farthest == null || v.distance > farthest.distance) farthest = v;
        return farthest != null && farthest.distance > dist ? farthest : null;
    }

    /// <summary>좀비가 사라질 때 쿨타임 기록 정리</summary>
    public static void Forget(ZombieController zombie)
    {
        if (instance != null) instance.nextVoiceTime.Remove(zombie);
    }
}
