using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 좀비가 '틈'을 볼 수 있게 대상(플레이어)의 전투 상태를 한곳에서 읽는다.
/// 학습장의 더미(DummyPlayer)와 게임의 플레이어(PlayerController + Weapon)를 같은 뜻으로 맞춘다.
///
///   aim        : 총구(바라보는) 방향. 더미 = AimForward / 게임 = 플레이어 정면 (마우스 쪽으로 몸을 돌린다)
///   reloading  : 재장전 중인가. 더미 = IsReloading / 게임 = Weapon.onReloadStart ~ 재장전 시간
///   ammo01     : 탄창에 남은 비율
///   velocity   : 이동 속도 (위치 변화로 잰다 —— 게임 플레이어는 NavMeshAgent가 없다)
///   targetsMe  : 지금 나를 표적으로 잡았나. 더미 = AimTarget / 게임 = 정면이 아주 정확히 나를 향할 때
///
/// Weapon.cs(팀원 파일)는 고치지 않는다: 재장전 시작 이벤트를 구독해서 '언제까지 재장전'인지 기억한다.
/// </summary>
public static class ZombieTargetInfo
{
    public struct Info
    {
        public bool    valid;
        public bool    armed;
        public Vector3 aim;
        public bool    reloading;
        public float   ammo01;
        public Vector3 velocity;
    }

    private class Track
    {
        public Vector3 lastPos;
        public float   lastTime = -1f;
        public Vector3 velocity;
        public Weapon  weapon;
        public float   nextSearch;
        public float   reloadUntil;
    }

    private static readonly Dictionary<Transform, Track> tracks = new Dictionary<Transform, Track>();

    public static Info Get(Transform target)
    {
        var info = new Info();
        if (target == null) return info;
        info.valid = true;

        var dummy = target.GetComponent<DummyPlayer>();
        if (dummy != null)
        {
            info.armed     = dummy.ShootingActive;
            info.aim       = dummy.AimForward;
            info.reloading = dummy.IsReloading;
            info.ammo01    = dummy.AmmoFraction;
            var nav = target.GetComponent<UnityEngine.AI.NavMeshAgent>();
            info.velocity  = nav != null ? nav.velocity : Vector3.zero;
            info.velocity.y = 0f;
            return info;
        }

        // 게임 플레이어
        var t = GetTrack(target);
        info.armed     = true;
        info.aim       = target.forward;
        info.reloading = Time.time < t.reloadUntil;
        if (t.weapon != null)
        {
            int max = Mathf.Max(1, t.weapon.GetMaxAmmo());
            info.ammo01 = Mathf.Clamp01(t.weapon.GetCurrentAmmo() / (float)max);
        }
        else info.ammo01 = 1f;
        info.velocity = t.velocity;
        return info;
    }

    /// <summary>나를 지금 표적으로 잡았나 (더미: 표적 / 게임: 정면이 거의 정확히 나를 향함)</summary>
    public static bool TargetsMe(Transform target, Transform me, float aimCos)
    {
        if (target == null) return false;
        var dummy = target.GetComponent<DummyPlayer>();
        if (dummy != null) return dummy.AimTarget == me;
        return aimCos > 0.985f;
    }

    private static Track GetTrack(Transform target)
    {
        if (!tracks.TryGetValue(target, out var t))
        {
            t = new Track();
            tracks[target] = t;
        }

        // 속도: 0.05초 이상 지났을 때만 새로 잰다 (같은 프레임에 여러 좀비가 불러도 0으로 나누지 않게)
        float now = Time.time;
        Vector3 p = target.position;
        if (t.lastTime < 0f) { t.lastPos = p; t.lastTime = now; }
        else if (now - t.lastTime >= 0.05f)
        {
            Vector3 v = (p - t.lastPos) / (now - t.lastTime);
            v.y = 0f;
            t.velocity = v.sqrMagnitude > 400f ? Vector3.zero : v;   // 순간이동(20 m/s 이상)은 무시
            t.lastPos  = p;
            t.lastTime = now;
        }

        // 무기: 찾으면 재장전 시작을 한 번 구독한다 (못 찾았으면 5초마다 다시 찾는다)
        if (t.weapon == null && now >= t.nextSearch)
        {
            t.nextSearch = now + 5f;
            t.weapon = target.GetComponentInChildren<Weapon>(true);
            if (t.weapon != null && t.weapon.onReloadStart != null)
            {
                var captured = t;
                t.weapon.onReloadStart.AddListener(dur => captured.reloadUntil = Time.time + Mathf.Max(0.1f, dur));
                if (t.weapon.onReloadComplete != null)
                    t.weapon.onReloadComplete.AddListener(() => captured.reloadUntil = 0f);
            }
        }
        return t;
    }
}
