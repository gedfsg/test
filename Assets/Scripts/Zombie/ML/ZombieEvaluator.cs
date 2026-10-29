using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.MLAgents.Policies;
using UnityEngine;

/// <summary>
/// FSM / 휴리스틱 / RL을 같은 조건에서 비교하는 평가 도구. 결과는 CSV로 저장한다.
///
/// 사용법:
///   1. 학습용 씬(열린 3x3)을 연다. 9구역이 동시에 돌아서 1구역보다 9배 빠르다.
///   2. 빈 GameObject를 만들고 이 컴포넌트를 붙인다.
///   3. RL Model 칸에 평가할 .onnx를 넣는다 (Assets 안에 있어야 한다).
///   4. Play. 끝나면 자동으로 멈추고, 프로젝트 폴더/EvalResults 에 CSV 두 개가 생긴다.
///
/// 공정성:
///   같은 (더미 속도, 시드) 조합을 모든 뇌가 똑같이 치른다. 시드가 같으면 장애물 배치,
///   더미와 좀비의 시작 위치가 같다. 이후 진행은 각 뇌의 판단에 따라 갈라진다.
///
/// 뇌별 정보 조건 (논문에 명시할 것):
///   FSM       — 본 게임의 좀비 AI. 자기 눈과 귀만 쓴다 (팀 공유 정보 없음).
///   Heuristic — 직선 추격 + 팀 공유 정보. "같은 정보를 받은 단순 추격"의 기준선.
///   RL        — 학습된 정책. 팀 공유 정보 + 동료 위치.
/// </summary>
public class ZombieEvaluator : MonoBehaviour
{
    /// <summary>
    /// RLDeterministic: 같은 모델이지만 행동을 확률적으로 뽑지 않고 평균값만 쓴다.
    /// RL의 '예측 불가능성'이 전략에서 오는지, 단순한 행동 무작위성에서 오는지 가르는 대조군.
    /// (값을 추가할 때는 반드시 맨 뒤에 —— 인스펙터에 저장된 목록이 밀린다)
    /// </summary>
    public enum BrainKind { FSM, Heuristic, RL, RLDeterministic }

    [Header("비교할 뇌 (순서대로 실행)")]
    public List<BrainKind> brains = new List<BrainKind>
        { BrainKind.FSM, BrainKind.Heuristic, BrainKind.RL, BrainKind.RLDeterministic };

    [Tooltip("평가할 RL 모델(.onnx). 비우면 좀비들의 Behavior Parameters에 이미 들어 있는 모델을 쓴다.")]
    public Object rlModel;

    [Header("조건")]
    [Tooltip("더미 최고 속도(고정). 조건마다 아래 판 수만큼 돈다. 좀비 최고 속도는 3.5")]
    public float[] evaderSpeeds = { 2.5f, 3.0f, 3.3f, 3.5f, 3.8f };
    [Tooltip("조건(뇌 × 속도)마다 판 수. 50이면 포획률 오차가 대략 ±7%p")]
    public int episodesPerCondition = 50;
    [Tooltip("시드 기준값. 바꾸면 다른 맵/배치 묶음으로 평가한다")]
    public int baseSeed = 20261014;
    [Tooltip("평가용 포획 거리(m, 중심 거리). 0이면 씬 설정(학습 때 1.2)을 그대로 쓴다.\n" +
             "기본값 1.8 = 좀비 공격 사거리. FSM은 이 거리에 들어오면 멈춰서 공격하므로\n" +
             "포획 거리가 이보다 짧으면 '때렸는데 포획으로 안 치는' 불이익을 받는다.\n" +
             "실제 게임에서 '잡혔다' = '공격이 닿았다'이므로, 셋 다 같은 규칙으로 비교한다.")]
    public float captureRadius = 1.8f;

    [Header("실행")]
    [Tooltip("게임 배속. 너무 높으면 프레임당 물리 스텝이 밀려 실제로는 덜 빨라진다")]
    public float timeScale = 10f;

    [Header("총 설정")]
    [Tooltip("켜면 모든 더미의 총을 게임 총에 맞춘다 (shoot4 학습과 같은 값):\n" +
             "연사 간격 0.1~0.5초, 총알 70~150 m/s (매 판 무작위), 탄창 30, 재장전 1.5초, 사거리 25m, 탄 퍼짐 15°.\n" +
             "끄면 씬에 저장된 더미 값을 그대로 쓴다 (예전 평가와 같은 조건).")]
    public bool gameGuns = false;

    [Header("영상 촬영용")]
    [Tooltip("화면 위쪽에 지금 어떤 AI가 움직이는지 크게 표시한다 (영상 녹화용)")]
    public bool showLabel = false;
    public int labelFontSize = 30;
    public string outputFolder = "EvalResults";
    [Tooltip("끝나면 Play를 자동으로 멈춘다 (에디터 전용)")]
    public bool stopWhenDone = true;

    // ─────────────────────────────────────────────
    private struct Job { public float speed; public int seed; public int index; }

    private readonly List<ZombieTrainingArena> arenas = new List<ZombieTrainingArena>();
    private readonly Dictionary<ZombieTrainingArena, Job?> running = new Dictionary<ZombieTrainingArena, Job?>();
    private readonly Queue<Job> pending = new Queue<Job>();

    private int  phase = -1;
    private int  phaseTotal;
    private int  phaseDone;
    private bool switchRequested;
    private bool finished;

    private StreamWriter episodeCsv;
    private string summaryPath;
    private readonly StringBuilder summary = new StringBuilder();
    private readonly Dictionary<float, List<ZombieTrainingArena.EpisodeReport>> phaseResults =
        new Dictionary<float, List<ZombieTrainingArena.EpisodeReport>>();

    private float startRealtime;

    // ─────────────────────────────────────────────
    void Start()
    {
        // 안전장치: mlagents-learn에 연결된 상태(=학습 중)라면 아무것도 하지 않는다.
        // 평가 오브젝트가 남은 채로 학습 빌드를 만들어도 학습을 망치지 않도록.
        if (Unity.MLAgents.Academy.Instance.IsCommunicatorOn)
        {
            Debug.LogWarning("[평가] 학습기에 연결되어 있어 평가를 건너뜁니다.");
            enabled = false;
            return;
        }

        arenas.AddRange(FindObjectsByType<ZombieTrainingArena>(FindObjectsSortMode.InstanceID));
        if (arenas.Count == 0)
        {
            Debug.LogError("[평가] 씬에 ZombieTrainingArena가 없습니다.");
            enabled = false;
            return;
        }

        if ((brains.Contains(BrainKind.RL) || brains.Contains(BrainKind.RLDeterministic)) && !HasUsableModel())
        {
            Debug.LogError("[평가] RL 모델이 없습니다. RL Model 칸에 .onnx를 넣거나 좀비 Behavior Parameters에 모델을 넣으세요. RL은 건너뜁니다.");
            brains.RemoveAll(b => b == BrainKind.RL || b == BrainKind.RLDeterministic);
        }
        if (brains.Count == 0) { enabled = false; return; }

        foreach (var a in arenas)
        {
            a.adaptiveSpeed     = false;   // 속도는 평가 도구가 정한다
            if (gameGuns && a.dummy != null) a.dummy.SetGun(0.1f, 0.5f, 70f, 150f, 30, 1.5f, 25f, 15f);
            a.evaderSpeedJitter = 0f;      // 조건을 정확히 고정
            if (captureRadius > 0f) a.captureRadius = captureRadius;
            a.EpisodeFinished  += OnEpisodeFinished;
            a.BeforeReset      += OnBeforeReset;
            running[a] = null;
        }

        // Assets 바깥(프로젝트 폴더)에 저장한다 —— Assets 안에 두면 Unity가 매번 임포트하려 든다.
        // 그래서 Project 창에는 보이지 않는다. 경로는 Console과 완료 시 열리는 탐색기 창으로 확인.
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", outputFolder));
        Directory.CreateDirectory(dir);
        string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string episodePath = Path.Combine(dir, $"eval_{stamp}_episodes.csv");
        summaryPath = Path.Combine(dir, $"eval_{stamp}_summary.csv");

        episodeCsv = new StreamWriter(episodePath, false, new UTF8Encoding(true));
        episodeCsv.WriteLine("brain,evader_speed,seed,episode,arena,captured,capture_sec,steps," +
                             "escape_gap_deg,encircle_mean,evader_mean_speed,evader_flee_speed," +
                             "zombie_mean_speed,prediction_error_m,shots_fired,shots_hit,hit_rate,hits_per_sec," +
                             "oblique_aimed_deg,oblique_free_deg,steer_aimed_deg,steer_free_deg,aimed_fraction");
        summary.AppendLine("brain,evader_speed,episodes,capture_rate,capture_sec_mean,escape_gap_mean," +
                           "encircle_mean,evader_flee_speed_mean,zombie_speed_mean,prediction_error_mean," +
                           "hits_per_episode,hit_rate,hits_per_sec," +
                           "oblique_aimed_deg,oblique_free_deg,steer_aimed_deg,steer_free_deg,aimed_fraction");

        Application.runInBackground = true;
        Time.timeScale = timeScale;
        startRealtime  = Time.realtimeSinceStartup;

        Debug.Log($"[평가] 시작: 구역 {arenas.Count}개, 뇌 {string.Join("/", brains)}, " +
                  $"속도 {string.Join("/", evaderSpeeds)}, 조건당 {episodesPerCondition}판, " +
                  $"포획 거리 {arenas[0].captureRadius:0.0}m, 총 {(gameGuns ? "게임 총" : "씬 값")}\n결과: {episodePath}");

        BeginPhase(0);
    }

    private static string BrainLabel(BrainKind k)
    {
        switch (k)
        {
            case BrainKind.FSM:             return "기존 규칙 AI (FSM)";
            case BrainKind.Heuristic:       return "규칙 기반 협동 (조율기)";
            case BrainKind.RL:              return "조율기 + 강화학습";
            case BrainKind.RLDeterministic: return "조율기 + 강화학습 (결정적)";
        }
        return k.ToString();
    }

    private GUIStyle labelStyle, subStyle;
    void OnGUI()
    {
        if (!showLabel || phase < 0 || phase >= brains.Count) return;
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = labelFontSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            subStyle   = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(labelFontSize * 0.55f), alignment = TextAnchor.UpperCenter };
            labelStyle.normal.textColor = Color.white;
            subStyle.normal.textColor   = new Color(1f, 1f, 1f, 0.85f);
        }
        float speed = -1f;
        foreach (var kv in running) if (kv.Value.HasValue) { speed = kv.Value.Value.speed; break; }
        string sub = (speed > 0f ? $"플레이어 속도 {speed:0.0} m/s · " : "") + $"{Mathf.Min(phaseDone + 1, phaseTotal)} / {phaseTotal}판";

        float w = Screen.width, h = labelFontSize * 2.6f;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(w * 0.2f, 8f, w * 0.6f, h), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(0f, 12f, w, labelFontSize * 1.4f), BrainLabel(brains[phase]), labelStyle);
        GUI.Label(new Rect(0f, 12f + labelFontSize * 1.35f, w, labelFontSize), sub, subStyle);
    }

    void Update()
    {
        if (finished || !switchRequested) return;
        switchRequested = false;

        WritePhaseSummary();
        if (phase + 1 < brains.Count) BeginPhase(phase + 1);
        else                          Finish();
    }

    void OnDestroy()
    {
        foreach (var a in arenas)
        {
            if (a == null) continue;
            a.EpisodeFinished -= OnEpisodeFinished;
            a.BeforeReset     -= OnBeforeReset;
        }
        episodeCsv?.Dispose();
        episodeCsv = null;
        Time.timeScale = 1f;
    }

    // ── 단계(뇌) 전환 ─────────────────────────────
    private void BeginPhase(int index)
    {
        phase = index;
        var kind = brains[phase];
        ApplyBrain(kind);

        pending.Clear();
        phaseResults.Clear();
        for (int s = 0; s < evaderSpeeds.Length; s++)
            for (int e = 0; e < episodesPerCondition; e++)
                pending.Enqueue(new Job
                {
                    speed = evaderSpeeds[s],
                    seed  = baseSeed + s * 100003 + e * 7919,   // 뇌가 바뀌어도 같은 목록
                    index = e,
                });

        phaseTotal = pending.Count;
        phaseDone  = 0;

        // 진행 중인 판은 기록 없이 끊고, 모든 구역이 새 작업을 받아 시작하게 한다
        foreach (var a in arenas)
        {
            running[a] = null;
            a.RequestAbort();
        }

        Debug.Log($"[평가] ▶ {kind} 시작 ({phaseTotal}판)");
    }

    private bool warnedNoDeterministic;

    private void ApplyBrain(BrainKind kind)
    {
        var modelProp = typeof(BehaviorParameters).GetProperty("Model");
        // ML-Agents 버전에 따라 없을 수도 있어서 이름으로 찾는다
        var detProp   = typeof(BehaviorParameters).GetProperty("DeterministicInference");
        bool isRL     = kind == BrainKind.RL || kind == BrainKind.RLDeterministic;

        if (kind == BrainKind.RLDeterministic && detProp == null && !warnedNoDeterministic)
        {
            warnedNoDeterministic = true;
            Debug.LogError("[평가] 이 ML-Agents 버전에는 Deterministic Inference가 없습니다. RLDeterministic은 RL과 같게 돈다.");
        }

        foreach (var a in arenas)
        foreach (var z in a.zombies)
        {
            if (z == null) continue;

            var fsm = z.GetComponent<ZombieFSMBrain>();
            var ml  = z.GetComponent<ZombieMLBrain>();
            z.brain = kind == BrainKind.FSM ? (ZombieBrain)fsm : ml;

            var bp = z.GetComponent<BehaviorParameters>();
            if (bp == null) continue;

            if (isRL && rlModel != null && modelProp != null)
            {
                if (modelProp.PropertyType.IsInstanceOfType(rlModel)) modelProp.SetValue(bp, rlModel);
                else Debug.LogError($"[평가] RL Model 칸의 파일 형식이 맞지 않습니다: {rlModel.GetType().Name}");
            }

            // 정책을 다시 만들기 전에 설정해야 반영된다 (아래 BehaviorType 대입이 정책을 다시 만든다)
            if (detProp != null && detProp.CanWrite)
                detProp.SetValue(bp, kind == BrainKind.RLDeterministic);

            // FSM도 에이전트는 계속 돌아야 한다 —— 에피소드 진행(TickEpisode)을 에이전트가 구동하기 때문.
            // 휴리스틱으로 두면 계산이 가볍고, FSM 모드에선 그 행동이 쓰이지 않는다.
            bp.BehaviorType = isRL ? BehaviorType.InferenceOnly : BehaviorType.HeuristicOnly;
        }
    }

    private bool HasUsableModel()
    {
        if (rlModel != null) return true;
        foreach (var a in arenas)
        foreach (var z in a.zombies)
        {
            var bp = z != null ? z.GetComponent<BehaviorParameters>() : null;
            if (bp != null && bp.Model != null) return true;
        }
        return false;
    }

    // ── 구역 콜백 ─────────────────────────────────
    private void OnBeforeReset(ZombieTrainingArena arena)
    {
        if (finished || switchRequested || pending.Count == 0)
        {
            running[arena] = null;   // 할 일 없음: 판은 돌지만 기록하지 않는다
            return;
        }

        var job = pending.Dequeue();
        running[arena] = job;
        arena.defaultEvaderSpeed = job.speed;
        Random.InitState(job.seed);  // 바로 이어지는 ResetArena가 이 시드로 맵과 배치를 뽑는다
    }

    private void OnEpisodeFinished(ZombieTrainingArena arena, ZombieTrainingArena.EpisodeReport r)
    {
        if (finished || !running.TryGetValue(arena, out var maybeJob) || maybeJob == null) return;
        var job = maybeJob.Value;
        running[arena] = null;

        var kind = brains[phase];
        var ci   = CultureInfo.InvariantCulture;
        episodeCsv.WriteLine(string.Join(",",
            kind.ToString(),
            job.speed.ToString("0.00", ci),
            job.seed.ToString(ci),
            job.index.ToString(ci),
            arena.name,
            r.captured ? "1" : "0",
            r.captured ? r.seconds.ToString("0.00", ci) : "",
            r.steps.ToString(ci),
            F(r.escapeGapDeg), F(r.encircleMean), F(r.evaderMeanSpeed), F(r.evaderFleeSpeed),
            F(r.zombieMeanSpeed), F(r.predictionError),
            r.shotsFired >= 0 ? r.shotsFired.ToString(ci) : "",
            r.shotsHit   >= 0 ? r.shotsHit.ToString(ci)   : "",
            F(HitRate(r)), F(HitsPerSec(r)),
            F(r.obliqueAimedDeg), F(r.obliqueFreeDeg), F(r.steerAimedDeg), F(r.steerFreeDeg), F(r.aimedFraction)));
        episodeCsv.Flush();   // 중간에 멈춰도 그때까지의 결과는 남는다

        if (!phaseResults.TryGetValue(job.speed, out var list))
            phaseResults[job.speed] = list = new List<ZombieTrainingArena.EpisodeReport>();
        list.Add(r);

        phaseDone++;
        if (phaseDone % 25 == 0 || phaseDone == phaseTotal)
        {
            float min = (Time.realtimeSinceStartup - startRealtime) / 60f;
            Debug.Log($"[평가] {kind}: {phaseDone}/{phaseTotal}판 ({min:0.0}분 경과)");
        }

        // 뇌 교체는 에이전트 스텝 밖(Update)에서 한다 —— 스텝 도중 정책을 바꾸면 위험하다
        if (phaseDone >= phaseTotal) switchRequested = true;
    }

    // ── 요약 ──────────────────────────────────────
    private void WritePhaseSummary()
    {
        var kind = brains[phase];
        var ci   = CultureInfo.InvariantCulture;
        var log  = new StringBuilder($"[평가] ■ {kind} 결과\n");

        foreach (float speed in evaderSpeeds)
        {
            if (!phaseResults.TryGetValue(speed, out var list) || list.Count == 0) continue;

            int caught = 0;
            float secSum = 0f, gapSum = 0f; int gapN = 0;
            var enc = new Mean(); var flee = new Mean(); var zsp = new Mean(); var pe = new Mean();
            // 사격: 판당 맞은 수 / 명중률(전체 맞은 수 ÷ 전체 쏜 수) / 초당 맞은 수 (판 길이가 달라도 비교되게)
            var hitsEp = new Mean(); var hps = new Mean(); int firedSum = 0, hitSum = 0;
            // 회피: 판별 평균을 다시 평균 (판마다 표본 수가 달라도 판 하나를 한 표로)
            var oA = new Mean(); var oF = new Mean(); var sA = new Mean(); var sF = new Mean(); var aF = new Mean();
            foreach (var r in list)
            {
                if (r.captured)
                {
                    caught++;
                    secSum += r.seconds;
                    if (!float.IsNaN(r.escapeGapDeg)) { gapSum += r.escapeGapDeg; gapN++; }
                }
                enc.Add(r.encircleMean); flee.Add(r.evaderFleeSpeed); zsp.Add(r.zombieMeanSpeed); pe.Add(r.predictionError);
                if (r.shotsFired >= 0)
                {
                    hitsEp.Add(r.shotsHit); hps.Add(HitsPerSec(r));
                    firedSum += r.shotsFired; hitSum += r.shotsHit;
                }
                oA.Add(r.obliqueAimedDeg); oF.Add(r.obliqueFreeDeg); sA.Add(r.steerAimedDeg); sF.Add(r.steerFreeDeg); aF.Add(r.aimedFraction);
            }

            float rate   = caught / (float)list.Count;
            float secAvg = caught > 0 ? secSum / caught : float.NaN;
            float gapAvg = gapN > 0 ? gapSum / gapN : float.NaN;
            float hitRate = firedSum > 0 ? hitSum / (float)firedSum : float.NaN;

            summary.AppendLine(string.Join(",",
                kind.ToString(), speed.ToString("0.00", ci), list.Count.ToString(ci),
                rate.ToString("0.000", ci), F(secAvg), F(gapAvg),
                F(enc.Value), F(flee.Value), F(zsp.Value), F(pe.Value),
                F(hitsEp.Value), F(hitRate), F(hps.Value),
                F(oA.Value), F(oF.Value), F(sA.Value), F(sF.Value), F(aF.Value)));

            log.AppendLine($"  더미 {speed:0.0}: 포획률 {rate:P0} ({caught}/{list.Count}) | 포획 {secAvg:0.0}초 | " +
                           $"빈틈 {gapAvg:0}° | 포위 {enc.Value:0.00} | 더미 실속 {flee.Value:0.00} | " +
                           $"좀비 {zsp.Value:0.00} | 예측오차 {pe.Value:0.00}m" +
                           (firedSum > 0 ? $" | 피격 {hitsEp.Value:0.0}발/판 (명중 {hitRate:P0}, {hps.Value:0.00}발/초)" : "") +
                           (oA.Count > 0 ? $" | 비스듬함 조준당함 {oA.Value:0}° / 아님 {oF.Value:0}°" : "") +
                           (sA.Count > 0 ? $" | RL 옆보정 조준당함 {sA.Value:0}° / 아님 {sF.Value:0}°" : ""));
        }

        File.WriteAllText(summaryPath, summary.ToString(), new UTF8Encoding(true));
        Debug.Log(log.ToString());
    }

    private static float HitRate(ZombieTrainingArena.EpisodeReport r)
        => r.shotsFired > 0 ? r.shotsHit / (float)r.shotsFired : float.NaN;
    private static float HitsPerSec(ZombieTrainingArena.EpisodeReport r)
        => r.shotsHit >= 0 && r.seconds > 0f ? r.shotsHit / r.seconds : float.NaN;

    private void Finish()
    {
        finished = true;
        episodeCsv?.Dispose();
        episodeCsv = null;
        Time.timeScale = 1f;

        float min = (Time.realtimeSinceStartup - startRealtime) / 60f;
        Debug.Log($"[평가] 완료 ({min:0.0}분). 요약: {summaryPath}");

#if UNITY_EDITOR
        // 결과 폴더는 Assets 바깥이라 Project 창에 안 보인다 → 탐색기로 바로 연다
        UnityEditor.EditorUtility.RevealInFinder(summaryPath);
        if (stopWhenDone) UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    private static string F(float v) =>
        float.IsNaN(v) || float.IsInfinity(v) ? "" : v.ToString("0.000", CultureInfo.InvariantCulture);

    /// <summary>NaN을 건너뛰는 평균.</summary>
    private struct Mean
    {
        private float sum; private int n;
        public void Add(float v) { if (!float.IsNaN(v) && !float.IsInfinity(v)) { sum += v; n++; } }
        public float Value => n > 0 ? sum / n : float.NaN;
        public int Count => n;
    }
}
