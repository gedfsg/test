using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 좀비다움 (연출 계층). 뇌(FSM / 조율기 / 강화학습)가 정한 행동을 몸이 실행하기 직전에 살짝 비튼다.
///   Sense → Think → [여기] → Act
///
/// 하는 일
///   1. 반응 지연   : 플레이어를 처음 알아챘을 때(배회 → 추적) 바로 달려들지 않고, 잠깐 멈춰서 고개를 돌린 뒤 움직인다.
///   2. 멍한 좀비   : 일부(dullChance)는 반응이 훨씬 늦고 총소리를 아예 못 듣는다. 맞으면 바로 깬다.
///   3. 소리 반응   : 총소리를 들어도 일부는 무시하고(noiseIgnoreChance), 나머지도 반응 지연만큼 늦게 그쪽으로 간다.
///   4. 가끔 멈춤   : 일부 좀비(서성이는 형, wanderPauseChance)는 배회 중에 멍하니 선다.
///      머뭇형      : 일부 좀비(hesitantChance)는 쫓아올 때 불규칙하게 걷다 서다를 반복한다. 서기 직전 확 느려지고,
///                    선 채로 플레이어 쪽으로 몸을 틀고, 다시 출발할 땐 튀어나간다. 가끔은 멀리서도 짧게 급발진한다.
///                    (예전 '멈칫형'을 대신한다. 플레이어와 가까우면 머뭇거리지 않고 덤빈다)
///   5. 돌진        : 일부 좀비(돌진형)는 평소엔 느릿느릿 다가오다가, 가까워지면 잠깐 멈칫(왁!)한 뒤
///                    엄청난 속도로 달려든다. 몇 초 쉬었다가 다시 할 수 있다.
///                    (예전의 '점점 가속'은 useRamp로 남겨 두었고 기본은 꺼짐)
///   6. 개체 차이   : 좀비마다 속도·반응이 조금씩 다르다.
///   7. 둔한 회전   : 초당 150~240°로만 돈다 (원래 720° = 순식간에 꺾음). 크게 돌아야 하면 속도를 줄이며 돈다.
///   8. 걸음 다양화 : 좀비마다 애니메이션 재생 속도와 시작 지점이 다르다 (군무 방지).
///                    실제 이동 속도에 맞춰 걸음 속도를 조절한다 (천천히 갈 때 발이 미끄러지지 않게).
///   9. 휘청임      : 일부 좀비는 걸으면서 좌우로 조금씩 흔들린다 (일직선으로 오지 않게).
///  10. 덩달아 따라오기 : 플레이어를 직접 보고 알아챈 좀비가 주변 좀비들에게 알린다. 알림을 받은 좀비는 하나둘 시간차로
///                    그쪽을 돌아보고 몰려온다. 알림을 받은 좀비는 다시 알리지 않는다 (직접 봐야 알린다) → 맵 전체로 번지지 않음.
///  11. 무리 지어 배회 : 배회할 때 근처 좀비 쪽으로 끌려서 3~5마리씩 뭉쳐 다닌다. 일부는 혼자 다닌다.
///  (몸짓은 ZombieBodyMotion: 플레이어 쪽으로 고개 돌리기, 부드러운 회전, 몸 기울기, 군중 비켜 가기)
///  12. 노리는 좀비 : ZombieLungeAgent가 붙은 좀비는 '언제 돌진할지'를 규칙(5~9m) 대신 강화학습이 정한다.
///                    (재장전 중, 다른 좀비를 겨누는 중 등 틈을 노림) 이 좀비는 늘 돌진형이고 머뭇형은 아니다.
///
/// 사냥꾼 무리(조율기 / 강화학습)는 포위가 깨지지 않게 약하게만 건다: 짧은 반응 지연 + 짧은 가속만 (멈춤·무시·멍함 없음).
/// 학습장(ZombieTrainingArena 안, 또는 targetOverride가 있는 좀비)에서는 아무것도 하지 않는다 —— 학습 조건이 바뀌면 안 되므로.
///
/// 직접 붙일 필요 없다. ZombieController(zombieQuirks 켜짐)가 Awake에서 붙인다.
/// 값을 바꾸고 싶으면 프리팹에 이 컴포넌트를 미리 붙여 두고 인스펙터에서 조절한다.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Zombie/Zombie Quirks (좀비다움)")]
public class ZombieQuirks : MonoBehaviour
{
    [Header("반응 지연 (처음 알아챘을 때)")]
    [Tooltip("보통 좀비의 반응 지연(초) 범위. 좀비마다 이 사이에서 하나 뽑는다.")]
    public Vector2 reactionDelay = new Vector2(0.3f, 1.2f);
    [Tooltip("한 번 놓쳤다가 다시 알아챌 때는 지연을 이 배율로 줄인다 (이미 흥분한 상태)")]
    [Range(0f, 1f)] public float reReactionScale = 0.5f;

    [Header("멍한 좀비")]
    [Tooltip("멍한 좀비가 될 확률. 반응이 훨씬 늦고 총소리를 못 듣는다. 총에 맞으면 바로 깬다.")]
    [Range(0f, 1f)] public float dullChance = 0.15f;
    [Tooltip("멍한 좀비의 반응 지연(초) 범위")]
    public Vector2 dullReactionDelay = new Vector2(2f, 4.5f);

    [Header("총소리")]
    [Tooltip("보통 좀비가 총소리를 그냥 무시할 확률 (소리 날 때마다 굴린다)")]
    [Range(0f, 1f)] public float noiseIgnoreChance = 0.25f;

    [Header("가끔 멈춤 (일부 좀비만)")]
    [Tooltip("배회 중에 가끔 멍하니 서는 좀비의 비율. 나머지는 배회 중에 안 멈춘다.")]
    [Range(0f, 1f)] public float wanderPauseChance = 0.4f;
    [Tooltip("배회 중 멈추는 간격(초) 범위")]
    public Vector2 wanderPauseEvery = new Vector2(3f, 7f);
    [Tooltip("배회 중 한 번 멈출 때 시간(초) 범위")]
    public Vector2 wanderPauseLength = new Vector2(1f, 3f);

    [Header("머뭇형 (일부 좀비만)")]
    [Tooltip("머뭇형 좀비의 비율. 쫓아올 때 불규칙하게 걷다 서다 하고, 가끔 짧게 급발진한다. 나머지는 쫓을 때 안 멈춘다.")]
    [Range(0f, 1f)] public float hesitantChance = 0.22f;
    [Tooltip("한 번에 걷는 시간(초) 범위 —— 매번 새로 뽑는다 (박자가 일정하면 고장 난 장난감 같다)")]
    public Vector2 hesitantWalk = new Vector2(0.5f, 2.5f);
    [Tooltip("한 번 서 있는 시간(초) 범위")]
    public Vector2 hesitantStop = new Vector2(0.2f, 0.7f);
    [Tooltip("섰다가 다시 걷자마자 한 번 더 서는(연달아 두 번 멈칫) 확률")]
    [Range(0f, 1f)] public float hesitantDoubleStop = 0.25f;
    [Tooltip("서기 직전 이 시간(초) 동안 속도를 확 줄인다")]
    public float hesitantBrakeTime = 0.3f;
    [Tooltip("다시 출발할 때 이 시간(초) 동안 튀어나간다 (속도 ×hesitantJoltScale)")]
    public float hesitantJoltTime = 0.25f;
    public float hesitantJoltScale = 1.4f;
    [Tooltip("걷기가 끝날 때 서는 대신 급발진할 확률")]
    [Range(0f, 1f)] public float hesitantDartChance = 0.3f;
    [Tooltip("급발진 시간(초) 범위")]
    public Vector2 hesitantDartTime = new Vector2(0.4f, 0.8f);
    [Tooltip("급발진 속도 배율 (최고 속도 대비) 범위")]
    public Vector2 hesitantDartSpeed = new Vector2(1.6f, 2.1f);
    [Tooltip("급발진 최소 속도(m/s) 범위 —— 일반 좀비(2 m/s)도 확 튀어나오게")]
    public Vector2 hesitantDartMinSpeed = new Vector2(4.5f, 6f);
    [Tooltip("플레이어와 이 거리(m) 안이면 머뭇거리지 않고 그대로 덤빈다")]
    public float hesitantMinDistance = 3.5f;

    [Header("돌진 (일부 좀비만)")]
    [Tooltip("돌진형 좀비의 비율. 평소엔 느리게 오다가 가까워지면 '왁' 하고 달려든다.")]
    [Range(0f, 1f)] public float lungeChance = 0.35f;
    [Tooltip("돌진형이 평소 다가올 때 속도 (최고 속도 대비) 범위 —— 어슬렁어슬렁")]
    public Vector2 stalkSpeed = new Vector2(0.7f, 0.85f);
    [Tooltip("이 거리(m) 안으로 들어오면 달려든다 (좀비마다 범위 안에서 하나 뽑음)")]
    public Vector2 lungeDistance = new Vector2(5f, 9f);
    [Tooltip("달려들기 직전 멈칫하는 시간(초) 범위 —— '왁!' 하는 순간")]
    public Vector2 lungeWindup = new Vector2(0.25f, 0.45f);
    [Tooltip("달려들 때 속도 배율 (최고 속도 대비) 범위.\n" +
             "예전 2.5~3.2는 너무 빠르다는 의견 (빠른 좀비 4 m/s × 3 = 12 m/s) → 1.8~2.2 (빠른 좀비 7~9 m/s)")]
    public Vector2 lungeSpeed = new Vector2(1.8f, 2.2f);
    [Tooltip("달려들 때 최소 속도(m/s) 범위. 일반 좀비(2.8 m/s)는 배율만으로는 플레이어 걷기(5)를 못 따라잡는다 → 이 속도는 무조건 낸다.\n" +
             "예전 6.5~8 → 5.5~6.5 (걷는 플레이어보다 조금 빠른 정도)")]
    public Vector2 lungeMinSpeed = new Vector2(5.5f, 6.5f);
    [Tooltip("최고 속도가 이보다 느린 좀비(탱커 1 m/s)는 돌진하지 않는다 —— 탱커가 튀어나오면 어색하다")]
    public float lungeMinBaseSpeed = 1.5f;
    [Tooltip("달려드는 시간(초) 범위. 그 안에 사거리에 닿으면 바로 공격으로 넘어간다.\n" +
             "속도를 줄인 만큼 늘려서(예전 1.0~1.8) 닿는 거리는 예전과 비슷하게 (약 10m) —— 노리는 좀비가 배운 출발 거리가 그대로 맞게")]
    public Vector2 lungeDuration = new Vector2(1.25f, 2.2f);
    [Tooltip("한 번 달려든 뒤 다시 달려들 수 있을 때까지(초) 범위")]
    public Vector2 lungeCooldown = new Vector2(4f, 8f);
    [Tooltip("달려들 때 가속도 (NavMeshAgent.acceleration). 크면 '갑자기' 튀어나온다. 원래 12.")]
    public float lungeAcceleration = 60f;
    [Tooltip("달려들 때 걸음 애니메이션 재생 속도 상한 (달리기 애니메이션이 없는 좀비가 미끄러지지 않게)")]
    public float lungeAnimMax = 2f;

    [Header("돌진 예고 (게임에서만)")]
    [Tooltip("돌진할 수 있는 상태가 되면 '노림': 낮게 그르렁거리고, 몸을 웅크린 채 플레이어를 노려본다 (ZombieBodyMotion).\n" +
             "언제 덮칠지는 그대로 —— 덮치기 전에 플레이어가 알아챌 틈만 준다. 학습장에서는 안 한다.")]
    public bool telegraph = true;
    [Tooltip("규칙 돌진형: 돌진 거리보다 이만큼(m) 먼저 노리기 시작한다 (어슬렁 속도로 약 1초)")]
    public float eyeLead = 2.5f;
    [Tooltip("노리는 동안 그르렁거리는 간격(초) 범위")]
    public Vector2 growlInterval = new Vector2(2.5f, 4f);

    [Header("점점 가속 (예전 방식, 기본 꺼짐)")]
    [Tooltip("켜면 쫓기 시작할 때 느리게 출발해서 rampTime 동안 최고 속도까지 올라간다")]
    public bool useRamp = false;
    [Tooltip("출발할 때 속도 (최고 속도 대비)")]
    [Range(0.1f, 1f)] public float rampStartScale = 0.55f;
    [Tooltip("최고 속도까지 걸리는 시간(초) 범위. 좀비마다 이 사이에서 하나 뽑는다.")]
    public Vector2 rampTime = new Vector2(0.8f, 1.6f);

    [Header("개체 차이")]
    [Tooltip("좀비마다 속도 배율 범위 (사냥꾼은 스포너가 속도를 정하므로 적용 안 함)")]
    public Vector2 speedVariation = new Vector2(0.92f, 1.08f);

    [Header("둔한 회전")]
    [Tooltip("좀비가 도는 속도(°/초) 범위. 좀비마다 하나 뽑는다. 원래(학습장)는 720.")]
    public Vector2 turnSpeed = new Vector2(150f, 240f);
    [Tooltip("가려는 방향과 몸 방향이 이 각도(°)보다 벌어지면 속도를 줄이며 돈다")]
    public float turnSlowAngle = 50f;
    [Tooltip("완전히 뒤돌아야 할 때 속도 (최고 속도 대비)")]
    [Range(0.1f, 1f)] public float turnSlowMin = 0.35f;

    [Header("걸음 다양화")]
    [Tooltip("좀비마다 애니메이션 재생 속도 배율 범위")]
    public Vector2 animSpeedVariation = new Vector2(0.85f, 1.15f);
    [Tooltip("실제 이동 속도에 맞춰 걸음 속도를 조절한다 (Animator에 Speed 파라미터가 없을 때만 —— 있으면 Animator가 이미 처리)")]
    public bool  matchAnimToSpeed = true;
    [Tooltip("이동 속도에 맞출 때 재생 속도 배율의 범위")]
    public Vector2 animMatchRange = new Vector2(0.75f, 1.2f);

    [Header("휘청임 (일부 좀비만)")]
    [Tooltip("걸으면서 좌우로 흔들리는 좀비의 비율")]
    [Range(0f, 1f)] public float swayChance = 0.5f;
    [Tooltip("좌우로 흔들리는 폭(m, 한쪽) 범위")]
    public Vector2 swayAmplitude = new Vector2(0.15f, 0.4f);
    [Tooltip("흔들림 빈도(번/초) 범위")]
    public Vector2 swayFrequency = new Vector2(0.35f, 0.75f);
    [Tooltip("플레이어와 이 거리(m) 안이면 흔들리지 않는다 (덤빌 때는 똑바로)")]
    public float swayMinDistance = 4f;

    [Header("덩달아 따라오기")]
    [Tooltip("플레이어를 직접 보고 알아챈 좀비가 이 반경(m) 안의 좀비들에게 알린다. 0이면 끔.")]
    public float alertRadius = 10f;
    [Tooltip("알림을 받은 좀비가 알아들을 확률 (멍한 좀비는 무시)")]
    [Range(0f, 1f)] public float alertHearChance = 0.85f;
    [Tooltip("알림이 도착하기까지 걸리는 시간(초) 범위 —— 하나둘 돌아보게. 그 뒤 각자 반응 지연이 더해진다.")]
    public Vector2 alertDelay = new Vector2(0.1f, 0.8f);

    [Header("무리 지어 배회")]
    [Tooltip("무리를 짓는 좀비의 비율. 나머지는 혼자 다닌다.")]
    [Range(0f, 1f)] public float herdChance = 0.7f;
    [Tooltip("이 반경(m) 안의 좀비들 쪽으로 끌린다")]
    public float herdRadius = 15f;
    [Tooltip("배회 목적지를 무리 가운데 쪽으로 얼마나 당길지 (0 = 안 당김, 1 = 무리 가운데로)")]
    [Range(0f, 1f)] public float herdCohesion = 0.6f;
    [Tooltip("무리 가운데에서 얼마나 흩어져 설지(m) —— 한 점에 겹치지 않게")]
    public float herdSpread = 3f;

    [Header("사냥꾼 무리 (조율기 / 강화학습)")]
    [Tooltip("사냥꾼에게도 반응 지연을 건다 (최대 hunterMaxReaction초)")]
    public bool  hunterReaction    = true;
    public float hunterMaxReaction = 0.4f;
    [Tooltip("사냥꾼에게도 점점 가속을 건다 (hunterRampTime초)")]
    public bool  hunterRamp     = false;
    [Tooltip("켜면 사냥꾼이 무리 덮치기(조율기 Closing / 코앞) 때 '왁' 하고 달려든다. 포획이 쉬워진다.")]
    public bool  hunterLungeOnClose = false;
    public float hunterRampTime = 0.8f;
    [Tooltip("사냥꾼이 포위하는 동안 '포위' 자세(웅크리고 노려봄)를 한다 (ZombieBodyMotion)")]
    public bool  hunterStalkPose = true;
    [Tooltip("사냥꾼의 회전 속도(°/초). 포위 자리로 빨리 꺾어야 해서 일반 좀비보다 빠르게. 감속 회전·휘청임은 걸지 않는다.")]
    public float hunterTurnSpeed = 360f;

    [Header("피격 경직")]
    [Tooltip("맞으면 잠깐 멈칫한다 (이동 정지). 흰 번쩍임·0.3m 넉백·피격 동작(ZombieHitEffect)과 합쳐져 '휘청'으로 보인다.\n" +
             "사냥꾼·노리는 좀비도 학습 때 맞으면 멈칫했으므로(0.25~0.3초) 게임에서도 같게 맞춘다.")]
    public bool hitStagger = true;
    [Tooltip("경직 시간(초) 범위 —— 피해량이 staggerDamageRef일 때 기준")]
    public Vector2 staggerTime = new Vector2(0.18f, 0.32f);
    [Tooltip("이 피해량일 때 기준 시간만큼 멈춘다. 약한 총은 짧게(최소 0.5배), 센 총·도끼는 길게(최대 1.6배)")]
    public float staggerDamageRef = 25f;
    [Tooltip("경직이 끝난 뒤 이 시간(초) 동안은 다시 경직되지 않는다 —— SMG처럼 빠른 총에 영영 묶이지 않게")]
    public float staggerImmunity = 0.5f;
    [Tooltip("느린 좀비(탱커 등, 최고 속도 < lungeMinBaseSpeed)는 경직 시간에 이 배율")]
    [Range(0f, 1f)] public float heavyStaggerScale = 0.4f;

    [Header("노리는 좀비 (ZombieLungeAgent)")]
    [Tooltip("강화학습이 돌진을 고를 수 있는 최대 거리(m). 이보다 멀면 돌진 선택지를 막는다.")]
    public float agentLungeMaxRange = 14f;
    [Tooltip("돌진 학습장(LungeTrainingArena)이 켠다: 학습용 대상(targetOverride)이 있어도 좀비다움을 끄지 않는다.\n" +
             "돌진 학습은 게임과 같은 조건(늦은 반응·어슬렁·돌진 동작)에서 해야 하므로.")]
    public bool trainingMode = false;
    [Tooltip("노리는 좀비의 돌진 쿨다운(초) 범위. 일반 돌진형(4~8초)보다 길다 —— 아무 때나 쓰면 손해가 되도록.")]
    public Vector2 agentLungeCooldown = new Vector2(9f, 12f);
    [Tooltip("노리는 좀비가 돌진에 실패하면(못 닿고 끝남) 헐떡이며 서 있는 시간(초) 범위")]
    public Vector2 agentFailRest = new Vector2(1.2f, 1.8f);

    [Header("디버그")]
    [Tooltip("Scene 뷰에서 이 좀비 위에 지금 상태(반응 중 / 멈칫 / 가속 / 멍함)를 색으로 표시")]
    public bool drawGizmo = true;

    // ── 개체 성격 (Awake에서 정해짐) ──────────────────
    public bool  IsDull        { get; private set; }
    /// <summary>배회 중에 가끔 서는 버릇이 있나</summary>
    public bool  PausesWhenWandering { get; private set; }
    /// <summary>쫓아올 때 걷다 서다 하고 가끔 급발진하나 (머뭇형)</summary>
    public bool  IsHesitant    { get; private set; }
    public float MyReaction    { get; private set; }
    public float MySpeedScale  { get; private set; } = 1f;
    public float MyRampTime    { get; private set; } = 1.5f;
    public float MyTurnSpeed   { get; private set; } = 200f;
    public float MyAnimSpeed   { get; private set; } = 1f;
    public bool  Sways         { get; private set; }
    public bool  IsLunger      { get; private set; }
    public bool  Herds         { get; private set; }
    public float MyStalkSpeed  { get; private set; } = 1f;
    public float MyLungeDistance { get; private set; } = 7f;
    public float MyLungeSpeed  { get; private set; } = 2.8f;
    public float MyLungeMinSpeed { get; private set; } = 7f;
    /// <summary>지금 달려드는 중인가 (애니메이션용)</summary>
    public bool  Lunging => lungePhase == 2;
    /// <summary>지금 급발진 중인가 (머뭇형, 애니메이션용)</summary>
    public bool  Darting => hesPhase == 2;
    /// <summary>지금 돌진을 시작할 수 있는가 (보임 · 쿨다운 끝 · 사거리 밖 · agentLungeMaxRange 안). 강화학습 행동 마스크용.</summary>
    public bool  LungeAvailableNow { get; private set; }
    /// <summary>돌진 예고 중인가 —— 돌진할 수 있는데 아직 안 덮친 상태 (몸짓이 웅크리고 노려본다)</summary>
    public bool  Eyeing { get; private set; }
    private bool  wasEyeing;
    private float nextGrowlAt, lastGrowlAt = -10f;
    /// <summary>마지막으로 돌진을 시작한 시각 (-1 = 아직 없음)</summary>
    public float LastLungeStart { get; private set; } = -1f;
    /// <summary>돌진할 때 낼 속도(m/s) 추정 —— 배율과 최소 속도 중 큰 쪽</summary>
    public float LungeSpeedEstimate => body == null ? MyLungeMinSpeed
        : Mathf.Max(MyLungeSpeed * body.moveSpeed * MySpeedScale, MyLungeMinSpeed);
    /// <summary>돌진을 시작하는 순간 (멈칫 '왁!' 시작). 학습장이 통계를 낸다.</summary>
    public event System.Action<ZombieQuirks> LungeStarted;
    private ZombieLungeAgent lungeAgent;
    private float restUntil = -1f;          // 돌진 실패 뒤 헐떡임 (노리는 좀비)
    private float staggerUntil = -1f, staggerImmuneUntil = -1f;
    /// <summary>돌진이 못 닿고 끝난 순간 (학습장 통계용)</summary>
    public event System.Action<ZombieQuirks> LungeFailed;
    /// <summary>지금 무엇을 하고 있나 (디버그 표시용)</summary>
    public string Label { get; private set; } = "";

    private ZombieController body;
    private NavMeshAgent     agent;
    private Animator         anim;
    private bool  disabledForTraining;
    private int   animParamCheck = -1;   // -1 모름, 0 Speed 파라미터 없음, 1 있음
    private bool  animPhaseDone;
    private int   startFrame;
    private float swayAmp, swayFreq, swayPhase;
    private int   lungePhase;          // 0 없음, 1 멈칫(왁), 2 달려드는 중
    private float lungeUntil;          // 멈칫 / 달려들기 끝나는 시각
    private float nextLungeAt;
    private float baseAcceleration = 12f;
    // 머뭇형
    private int   hesPhase;            // 0 걷기, 1 서 있기, 2 급발진
    private bool  hesStarted;
    private float hesUntil, joltUntil;
    private bool  hesNextIsDart;
    private float dartMulNow = 1.8f, dartMinNow = 5f;
    private Vector3 lastWanderDest, herdDest;
    private bool    hasHerdDest;

    /// <summary>게임에 있는 좀비다움 전부 (알림 / 무리 계산용)</summary>
    private static readonly System.Collections.Generic.List<ZombieQuirks> All =
        new System.Collections.Generic.List<ZombieQuirks>(64);

    void OnEnable()  { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    /// <summary>p에서 r 안에 있는 (살아 있는) 좀비 수. except는 빼고 센다. 노리는 좀비의 관측용.</summary>
    public static int CountNear(Vector3 p, float r, ZombieQuirks except)
    {
        float r2 = r * r;
        int n = 0;
        foreach (var q in All)
        {
            if (q == null || q == except || q.body == null || q.body.IsDead) continue;
            Vector3 d = q.transform.position - p; d.y = 0f;
            if (d.sqrMagnitude <= r2) n++;
        }
        return n;
    }

    private bool  active;          // 지난 프레임에 '쫓는 중'(추적/수색/공격)이었나
    private bool  everAware;       // 한 번이라도 알아챘나 (재반응 지연 단축용)
    private float reactUntil = -1f;
    private float pauseUntil = -1f;
    private float nextPauseAt;
    private float movingTime;      // 이번에 쫓기 시작한 뒤 계속 움직인 시간 (가속용)
    private float pauseRhythm = 1f;   // 멈춤 간격 배율 (개체마다)

    private bool    noisePending;
    private Vector3 noisePos;
    private float   noiseAt;

    void Awake()
    {
        body = GetComponent<ZombieController>();
        disabledForTraining = GetComponentInParent<ZombieTrainingArena>() != null;

        IsDull       = Random.value < dullChance;
        MyReaction   = IsDull ? Range(dullReactionDelay) : Range(reactionDelay);
        MySpeedScale = Range(speedVariation);
        MyRampTime   = Mathf.Max(0.05f, Range(rampTime));
        PausesWhenWandering = Random.value < wanderPauseChance;
        IsHesitant          = Random.value < hesitantChance;
        lungeAgent          = GetComponent<ZombieLungeAgent>();
        if (lungeAgent != null) IsHesitant = false;   // 노리는 좀비: 차분히 다가와 틈을 노린다
        pauseRhythm  = Random.Range(0.7f, 1.5f);   // 버릇이 있어도 얼마나 자주인지는 개체마다 다르다
        nextPauseAt  = Time.time + Range(wanderPauseEvery) * pauseRhythm;

        agent = GetComponent<NavMeshAgent>();
        anim  = GetComponent<Animator>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        MyTurnSpeed = Range(turnSpeed);
        MyAnimSpeed = Range(animSpeedVariation);
        Sways       = Random.value < swayChance;
        swayAmp     = Range(swayAmplitude);
        swayFreq    = Range(swayFrequency);
        swayPhase   = Random.Range(0f, Mathf.PI * 2f);
        startFrame  = Time.frameCount;

        Herds           = Random.value < herdChance;
        IsLunger        = lungeAgent != null
                          || (Random.value < lungeChance && (body == null || body.moveSpeed >= lungeMinBaseSpeed));
        // 노리는 좀비: 게임의 돌진형에게 학습한 '언제 덮칠지' 모델을 붙인다 (Resources/ZombieLungeBrain 설정이 있을 때)
        // 이미 RL 뇌가 있는 좀비(사냥꾼 _RL 프리팹)와 학습장 좀비는 건너뛴다
        if (IsLunger && lungeAgent == null && !disabledForTraining && !trainingMode)
        {
            lungeAgent = ZombieLungeAgent.AttachForGame(gameObject);
            if (lungeAgent != null) IsHesitant = false;
        }
        MyLungeMinSpeed = Range(lungeMinSpeed);
        MyStalkSpeed    = Range(stalkSpeed);
        MyLungeDistance = Range(lungeDistance);
        MyLungeSpeed    = Range(lungeSpeed);
        if (agent != null) baseAcceleration = agent.acceleration;

        // 몸짓 (시선 · 부드러운 회전 · 기울기 · 군중). 학습장에서는 스스로 꺼진다.
        if (!disabledForTraining && GetComponent<ZombieBodyMotion>() == null)
            gameObject.AddComponent<ZombieBodyMotion>();
    }

    // ── 촬영 모드(ZombieShowcase)용: 무작위로 정해진 성격을 원하는 것 하나로 덮어쓴다 ──
    public enum ShowcaseTrait { Plain, Dull, Hesitant, Lunger, Herd }

    public void ForceTraitForShowcase(ShowcaseTrait t)
    {
        IsDull     = t == ShowcaseTrait.Dull;
        MyReaction = IsDull ? Range(dullReactionDelay) : Range(reactionDelay);
        IsHesitant = t == ShowcaseTrait.Hesitant;
        Herds      = t == ShowcaseTrait.Herd;
        IsLunger   = t == ShowcaseTrait.Lunger;
        if (IsLunger && lungeAgent == null && !disabledForTraining && !trainingMode)
            lungeAgent = ZombieLungeAgent.AttachForGame(gameObject);   // 노리는 좀비 모델 (설정이 없으면 규칙 돌진)
        if (IsLunger) IsHesitant = false;
    }

    private void EndLunge(float now, bool cooldown)
    {
        if (lungePhase != 0 && cooldown)
            nextLungeAt = now + Range(lungeAgent != null ? agentLungeCooldown : lungeCooldown);
        lungePhase = 0;
    }

    private static float Range(Vector2 r) => Random.Range(Mathf.Min(r.x, r.y), Mathf.Max(r.x, r.y));

    /// <summary>학습장 좀비거나 꺼져 있으면 아무것도 안 한다.</summary>
    private bool Bypass => disabledForTraining || !isActiveAndEnabled || body == null
                           || (body.targetOverride != null && !trainingMode);

    private bool IsHunter => body != null && HunterPackTactics.PackOf(body) != null;
    /// <summary>게임에서 좀비다움이 켜져 있나 (학습장 · 돌진 학습장이면 false) —— 몸짓(ZombieBodyMotion)이 따른다</summary>
    public bool ActiveInGame => !Bypass && !trainingMode;
    /// <summary>사냥꾼 무리 소속인가</summary>
    public bool IsHunterNow => IsHunter;

    // ── 뇌의 행동을 비튼다 (ZombieController.Update에서 매 프레임) ─────
    public ZombieAction Filter(in ZombieAction action, in ZombieObservation obs, ZombieState state, float dt)
    {
        if (Bypass) { Label = ""; return action; }

        float now    = Time.time;
        bool  hunter = IsHunter;
        LungeAvailableNow = false;
        wasEyeing = Eyeing;
        Eyeing    = false;
        bool  dull   = IsDull && !hunter;

        // 회전 속도 (사냥꾼 편입은 스폰 뒤에 일어나서 매 프레임 맞춘다)
        if (agent != null) agent.angularSpeed = hunter ? hunterTurnSpeed : MyTurnSpeed;

        // 미뤄 둔 총소리
        if (noisePending && now >= noiseAt)
        {
            noisePending = false;
            body.HearNoiseNow(noisePos);
        }

        if (state == ZombieState.Stunned || state == ZombieState.Dead)
        {
            movingTime = 0f;
            EndLunge(now, true);
            ApplyLungeBody();
            Label = now < staggerUntil ? "휘청" : "";
            return action;
        }

        bool nowActive = state == ZombieState.Chase || state == ZombieState.Investigate || state == ZombieState.Attack;

        // 1. 반응 지연: 배회/대기 → 쫓기로 바뀌는 순간
        if (nowActive && !active)
        {
            float delay = MyReaction * (everAware ? reReactionScale : 1f);
            if (hunter) delay = hunterReaction ? Mathf.Min(delay, hunterMaxReaction) : 0f;
            reactUntil = now + delay;
            everAware  = true;
            movingTime = 0f;
            // 직접 보고 알아챘으면 주변에 알린다 (알림·소리로 깬 좀비는 알리지 않는다)
            if (!hunter && obs.hasTarget && obs.targetVisible) AlertNeighbors(obs.targetPosition);
        }
        active = nowActive;

        if (!nowActive) EndLunge(now, false);
        ApplyLungeBody();

        if (nowActive && now < reactUntil)
        {
            // 멈춰서 (보이면) 그쪽을 바라본다 —— '어? 뭐지' 하는 순간
            movingTime = 0f;
            Label = dull ? "멍함" : "반응 중";
            var look = ZombieAction.Stand();
            look.faceTarget = obs.targetVisible;
            return look;
        }

        // 공격, 정지는 그대로.
        // 가속은 처음부터 다시 하지 않는다 —— 예전엔 공격할 때마다 리셋돼서, 플레이어 곁에서
        // 때리고 쫓기를 반복하는 좀비가 늘 40% 속도로 출발해 눈에 띄게 느려졌다.
        // 가속을 다시 하는 건 '알아챈 직후 / 멈칫 뒤 / 경직 뒤'뿐이다.
        if (!action.hasMoveTarget || state == ZombieState.Attack)
        {
            if (state == ZombieState.Attack) EndLunge(now, true);   // 닿았다 → 돌진 끝
            Label = "";
            return action;
        }

        // 무리 지어 배회: 배회 목적지를 근처 좀비들 쪽으로 당긴다
        bool useHerd = !nowActive && !hunter && Herds;
        if (useHerd) UpdateHerdDest(action.moveTarget);
        else hasHerdDest = false;

        // 헐떡임: 노리는 좀비가 돌진에 실패한 뒤 잠깐 서서 숨을 고른다 (실패의 대가 + 좀비다운 틈)
        if (now < restUntil && nowActive)
        {
            Label = "헐떡";
            return ZombieAction.Stand();
        }

        // 2. 돌진: 느릿느릿 → (가까워지면) 멈칫 '왁!' → 엄청난 속도로 달려듦
        bool lungeAllowed = nowActive && (hunter ? hunterLungeOnClose : IsLunger);
        if (!lungeAllowed) EndLunge(now, false);
        float lungeScale = 1f;
        if (lungeAllowed)
        {
            bool sees = obs.hasTarget && obs.targetVisible;
            if (lungePhase == 1)
            {
                if (now < lungeUntil)
                {
                    Label = "왁!";
                    var roar = ZombieAction.Stand();
                    roar.faceTarget = obs.hasTarget;
                    return roar;
                }
                lungePhase = 2;
                lungeUntil = now + Range(lungeDuration);
                ApplyLungeBody();
            }
            if (lungePhase == 2)
            {
                if (now >= lungeUntil || !obs.hasTarget)
                {
                    // 못 닿고 끝났다 = 실패 (닿으면 공격 상태로 넘어가 위에서 끝난다)
                    EndLunge(now, true);
                    ApplyLungeBody();
                    if (lungeAgent != null && !hunter)
                    {
                        restUntil = now + Range(agentFailRest);
                        LungeFailed?.Invoke(this);
                        Label = "헐떡";
                        return ZombieAction.Stand();
                    }
                }
                else
                {
                    // 배율과 최소 속도 중 큰 쪽 (일반 좀비 2 m/s × 3 = 6 → 최소 6.5~8 m/s)
                    float baseSpeed = Mathf.Max(0.1f, body.moveSpeed * (hunter ? 1f : MySpeedScale));
                    lungeScale = Mathf.Max(MyLungeSpeed, MyLungeMinSpeed / baseSpeed);
                    Label = "돌진";
                }
            }
            if (lungePhase == 0)
            {
                bool trigger;
                if (hunter)
                {
                    // 사냥꾼: 조율기가 '덮치라'고 할 때 (무리 덮치기 / 코앞)
                    var pack = HunterPackTactics.PackOf(body);
                    trigger = pack != null && pack.TryGetOrder(body, out _, out bool rush) && rush;
                }
                else if (lungeAgent != null && lungeAgent.isActiveAndEnabled)
                {
                    // 노리는 좀비: 강화학습이 고른다 (고를 수 있는 때만 —— 행동 마스크와 같은 조건)
                    LungeAvailableNow = sees && now >= nextLungeAt
                                        && obs.distance > obs.attackRange + 0.5f && obs.distance <= agentLungeMaxRange;
                    trigger = LungeAvailableNow && lungeAgent.WantsLunge;
                }
                else trigger = obs.distance <= MyLungeDistance;

                // 돌진 예고 '노림': 덮칠 수 있게 됐는데 아직 안 덮친 동안 (사냥꾼 제외)
                if (!hunter && sees && now >= nextLungeAt && obs.distance > obs.attackRange + 0.5f)
                {
                    bool byAgent = lungeAgent != null && lungeAgent.isActiveAndEnabled;
                    Eyeing = byAgent ? LungeAvailableNow : obs.distance <= MyLungeDistance + eyeLead;
                }
                if (Eyeing && telegraph && !trainingMode && body.targetOverride == null)
                {
                    if (!wasEyeing && now - lastGrowlAt > 1.5f) nextGrowlAt = now;   // 막 노리기 시작 → 바로 그르렁
                    if (now >= nextGrowlAt)
                    {
                        ZombieVoiceManager.Play(body, ZombieVoiceManager.State.Growl);
                        lastGrowlAt = now;
                        nextGrowlAt = now + Range(growlInterval);
                    }
                }

                if (sees && trigger && now >= nextLungeAt && obs.distance > obs.attackRange + 0.5f)
                {
                    lungePhase = 1;
                    lungeUntil = now + Range(lungeWindup);
                    LastLungeStart = now;
                    LungeAvailableNow = false;
                    Eyeing = false;
                    LungeStarted?.Invoke(this);
                    // '왁!' —— 팀원 사운드 시스템의 비명 (게임에서만)
                    if (!trainingMode && body.targetOverride == null)
                        ZombieVoiceManager.Play(body, ZombieVoiceManager.State.Attack);
                    Label = "왁!";
                    var roar = ZombieAction.Stand();
                    roar.faceTarget = true;
                    return roar;
                }
                if (!hunter) lungeScale = MyStalkSpeed;   // 평소엔 어슬렁어슬렁
            }
        }

        // 2.5 머뭇형: 불규칙하게 걷다 서다 + 가끔 짧은 급발진 (돌진 중이거나 코앞이면 안 함)
        float hesScale = 1f;
        bool hesitate = !hunter && IsHesitant && nowActive && lungePhase == 0
                        && obs.hasTarget && obs.distance > hesitantMinDistance;
        if (!hesitate)
        {
            hesPhase   = 0;
            hesStarted = false;
        }
        else
        {
            if (!hesStarted) { hesStarted = true; StartHesWalk(now, false); }

            if (hesPhase == 0 && now >= hesUntil)
            {
                if (hesNextIsDart) { hesPhase = 2; hesUntil = now + Range(hesitantDartTime); }
                else               { hesPhase = 1; hesUntil = now + Range(hesitantStop); }
                ApplyLungeBody();
            }
            else if (hesPhase == 1 && now >= hesUntil)
            {
                StartHesWalk(now, Random.value < hesitantDoubleStop);   // 가끔 연달아 두 번 멈칫
                joltUntil = now + hesitantJoltTime;
                ApplyLungeBody();
            }
            else if (hesPhase == 2 && now >= hesUntil)
            {
                StartHesWalk(now, false);
                ApplyLungeBody();
            }

            if (hesPhase == 1)
            {
                // 선 채로 플레이어 쪽으로 몸을 다시 튼다 —— 그냥 굳으면 '버벅이는 AI'처럼 보인다
                Label = "머뭇";
                var hold = ZombieAction.Stand();
                hold.faceTarget = obs.targetVisible;
                return hold;
            }
            if (hesPhase == 2)
            {
                float baseSpeed = Mathf.Max(0.1f, body.moveSpeed * MySpeedScale);
                lungeScale = Mathf.Max(dartMulNow, dartMinNow / baseSpeed);   // 돌진형의 어슬렁 속도 대신
                Label = "급발진";
            }
            else if (now < joltUntil)
            {
                hesScale = hesitantJoltScale;                                  // 다시 출발: 튀어나감
            }
            else if (!hesNextIsDart && hesUntil - now < hesitantBrakeTime)
            {
                hesScale = 0.45f;                                              // 서기 직전: 확 느려짐
            }
        }

        // 3. 가끔 멈춤 (배회 중, 서성이는 형만. 달려드는 중엔 안 멈춘다)
        bool wandering = !nowActive;
        bool canPause  = lungePhase == 0 && wandering && PausesWhenWandering;
        if (now < pauseUntil && canPause)
        {
            movingTime = 0f;
            Label = "멈칫";
            return ZombieAction.Stand();
        }
        if (now >= nextPauseAt)
        {
            if (canPause)
                pauseUntil = now + Range(wanderPauseLength);
            nextPauseAt = (now > pauseUntil ? now : pauseUntil) + Range(wanderPauseEvery) * pauseRhythm;
            if (canPause)
            {
                movingTime = 0f;
                Label = "멈칫";
                return ZombieAction.Stand();
            }
        }

        // 4. 속도: 개체 차이 × 돌진(또는 어슬렁) × (예전 방식) 점점 가속
        ZombieAction result = action;
        if (useHerd && hasHerdDest) result.moveTarget = herdDest;
        float scale = (hunter ? 1f : MySpeedScale) * lungeScale * hesScale;
        if (nowActive && lungePhase == 0 && (hunter ? hunterRamp : useRamp))
        {
            movingTime += dt;
            float ramp = hunter ? Mathf.Max(0.05f, hunterRampTime) : MyRampTime;
            float t = Mathf.Clamp01(movingTime / ramp);
            t = t * t * (3f - 2f * t);   // 부드럽게 (smoothstep)
            scale *= Mathf.Lerp(rampStartScale, 1f, t);
            Label = t < 0.99f ? "가속" : "";
        }
        else
        {
            movingTime = 0f;
            if (lungePhase == 0 && hesPhase != 2) Label = "";
        }

        if (Eyeing && Label == "") Label = "노림";
        else if (hunter && hunterStalkPose && Label == "" && lungePhase == 0 && obs.hasTarget)
        {
            var pk = HunterPackTactics.PackOf(body);
            if (pk != null && pk.CurrentPhase == HunterPackTactics.Phase.Encircling) Label = "포위";
        }

        // 5. 둔한 회전 (달려들 땐 똑바로): 몸 방향과 가려는 방향이 크게 벌어지면 속도를 줄이며 돈다 (사냥꾼 제외)
        bool canMoveNow = agent != null && agent.enabled && agent.isOnNavMesh;
        if (!hunter && canMoveNow && lungePhase == 0 && hesPhase != 2)
        {
            Vector3 want = agent.desiredVelocity; want.y = 0f;
            if (want.sqrMagnitude > 0.01f)
            {
                float ang = Vector3.Angle(transform.forward, want);
                if (ang > turnSlowAngle)
                    scale *= Mathf.Lerp(1f, turnSlowMin, Mathf.Clamp01((ang - turnSlowAngle) / (180f - turnSlowAngle)));
            }
        }
        result.speedScale = action.speedScale * scale;

        // 6. 휘청임: 진행 방향의 옆으로 조금씩 밀었다 당긴다 (경로는 그대로, 사냥꾼 제외, 달려들 땐 안 함)
        if (!hunter && Sways && canMoveNow && lungePhase == 0 && hesPhase != 2 && (!obs.hasTarget || obs.distance > swayMinDistance))
        {
            Vector3 v = agent.velocity; v.y = 0f;
            if (v.sqrMagnitude > 0.25f)
            {
                Vector3 side = new Vector3(-v.z, 0f, v.x).normalized;
                float w = 2f * Mathf.PI * swayFreq;
                float lateral = swayAmp * w * Mathf.Cos(w * now + swayPhase);   // 위치 = amp·sin → 속도 = amp·w·cos
                agent.Move(side * (lateral * dt));
            }
        }
        return result;
    }

    /// <summary>달려드는 동안만 가속도를 높이고 달리기 애니메이션을 켠다.</summary>
    private void ApplyLungeBody()
    {
        bool fast = lungePhase == 2 || hesPhase == 2;
        if (agent != null) agent.acceleration = fast ? Mathf.Max(baseAcceleration, lungeAcceleration) : baseAcceleration;
        if (body != null) body.ForceRunAnim = fast;
    }

    /// <summary>머뭇형: 새 걷기 구간 (shortWalk = 연달아 멈칫하려고 아주 짧게)</summary>
    private void StartHesWalk(float now, bool shortWalk)
    {
        hesPhase      = 0;
        hesUntil      = now + (shortWalk ? Random.Range(0.2f, 0.5f) : Range(hesitantWalk));
        hesNextIsDart = !shortWalk && Random.value < hesitantDartChance;
        dartMulNow    = Range(hesitantDartSpeed);
        dartMinNow    = Range(hesitantDartMinSpeed);
    }

    // ── 걸음 다양화 (애니메이션 재생 속도) ─────────────────
    void LateUpdate()
    {
        if (anim == null || Bypass || body == null) return;
        if (body.IsDead) { anim.speed = 1f; return; }

        // 시작 지점을 흩뜨린다 (컨트롤러가 Start에서 애니메이터를 바꾸므로 몇 프레임 뒤에 한 번)
        if (!animPhaseDone && Time.frameCount > startFrame + 2 && anim.isActiveAndEnabled && anim.runtimeAnimatorController != null)
        {
            animPhaseDone = true;
            var st = anim.GetCurrentAnimatorStateInfo(0);
            anim.Play(st.fullPathHash, 0, Random.value);
        }

        // 공격 동작은 원래 속도로 (공격 판정 시간과 맞춰져 있다)
        if (body.AttackAnimating) { anim.speed = 1f; return; }

        float s = MyAnimSpeed;
        if (matchAnimToSpeed && agent != null && agent.enabled && !HasSpeedParam())
        {
            float v = agent.velocity.magnitude;
            if (v > 0.1f)
            {
                float reference = Mathf.Max(0.5f, body.moveSpeed * MySpeedScale);
                float lo = Mathf.Min(animMatchRange.x, animMatchRange.y), hi = Mathf.Max(animMatchRange.x, animMatchRange.y);
                if (Lunging || Darting) hi = Mathf.Max(hi, lungeAnimMax);
                s *= Mathf.Clamp(v / reference, lo, hi);
            }
        }
        anim.speed = s;
    }

    private bool HasSpeedParam()
    {
        if (animParamCheck < 0)
        {
            if (anim.runtimeAnimatorController == null) return false;   // 아직 없으면 다음에 다시
            animParamCheck = 0;
            foreach (var p in anim.parameters)
                if (p.name == "Speed") { animParamCheck = 1; break; }
        }
        return animParamCheck == 1;
    }

    // ── 덩달아 따라오기 ───────────────────────────────
    private void AlertNeighbors(Vector3 targetPos)
    {
        if (alertRadius <= 0f) return;
        float r2 = alertRadius * alertRadius;
        Vector3 me = transform.position;
        foreach (var q in All)
        {
            if (q == null || q == this || q.Bypass || q.body == null || q.body.IsDead) continue;
            if ((q.transform.position - me).sqrMagnitude > r2) continue;
            q.ReceiveAlert(targetPos);
        }
    }

    /// <summary>동료가 플레이어를 봤다는 알림. 잠깐 뒤 그쪽 소리를 들은 것처럼 반응한다 (반응 지연은 그때 따로 붙는다).</summary>
    private void ReceiveAlert(Vector3 targetPos)
    {
        if (active || IsHunter || IsDull) return;          // 이미 쫓는 중 / 사냥꾼 / 멍한 좀비
        if (Random.value > alertHearChance) return;
        float at = Time.time + Range(alertDelay);
        if (!noisePending || at < noiseAt) noiseAt = at;
        noisePending = true;
        noisePos     = targetPos;
    }

    // ── 무리 지어 배회 ────────────────────────────────
    /// <summary>뇌가 새 배회 목적지를 고를 때마다 근처 좀비들의 가운데 쪽으로 당긴다.</summary>
    private void UpdateHerdDest(Vector3 wanderDest)
    {
        if (hasHerdDest && (wanderDest - lastWanderDest).sqrMagnitude < 0.25f) return;   // 같은 목적지 → 그대로
        lastWanderDest = wanderDest;
        herdDest       = wanderDest;
        hasHerdDest    = true;

        float r2 = herdRadius * herdRadius;
        Vector3 me = transform.position, sum = Vector3.zero;
        int n = 0;
        foreach (var q in All)
        {
            if (q == null || q == this || q.Bypass || q.body == null || q.body.IsDead) continue;
            Vector3 p = q.transform.position;
            if ((p - me).sqrMagnitude > r2) continue;
            sum += p;
            n++;
        }
        if (n == 0) return;   // 근처에 아무도 없으면 원래대로 혼자 배회

        Vector2 j = Random.insideUnitCircle * herdSpread;
        Vector3 center = sum / n + new Vector3(j.x, 0f, j.y);
        Vector3 cand   = Vector3.Lerp(wanderDest, center, herdCohesion);
        if (NavMesh.SamplePosition(cand, out NavMeshHit hit, 3f, NavMesh.AllAreas)) herdDest = hit.position;
    }

    // ── 총소리 (ZombieController.HearNoise가 먼저 묻는다) ─────
    /// <summary>true면 이 소리는 여기서 처리했다 (무시했거나 나중에 전달). false면 바로 반응하라는 뜻.</summary>
    public bool InterceptNoise(Vector3 position)
    {
        if (Bypass) return false;
        if (IsHunter) return false;                       // 사냥꾼은 무리 정보 공유가 우선
        if (IsDull) return true;                          // 멍한 좀비는 못 듣는다
        if (Random.value < noiseIgnoreChance) return true;

        // 듣고 나서 실제로 움직이기까지의 '반응 지연'은 쫓기로 바뀌는 순간 따로 붙으므로 여기선 짧게만 (겹치면 너무 굼뜸)
        float delay = Random.Range(0f, 0.4f);
        // 이미 기다리는 소리가 있으면 위치만 최신으로 (지연은 처음 들은 때 기준)
        if (!noisePending) noiseAt = Time.time + delay;
        noisePending = true;
        noisePos     = position;
        return true;
    }

    // ── 피격: 무조건 깬다 ─────────────────────────────
    public void NotifyDamaged()
    {
        if (Bypass) return;
        IsDull = false;                                    // 한 번 맞으면 더는 멍하지 않다
        float wake = Time.time + 0.15f;
        if (reactUntil > wake) reactUntil = wake;
        pauseUntil = -1f;

        // 피격 경직: 잠깐 멈칫 (돌진 중이면 돌진도 끊긴다 —— 학습 때와 같은 규칙)
        float now = Time.time;
        if (hitStagger && now >= staggerImmuneUntil && body != null && !body.IsDead)
        {
            float dmgScale = staggerDamageRef > 0f ? Mathf.Clamp(body.LastDamage / staggerDamageRef, 0.5f, 1.6f) : 1f;
            float heavy    = body.moveSpeed < lungeMinBaseSpeed ? heavyStaggerScale : 1f;
            float d        = Range(staggerTime) * dmgScale * heavy;
            if (d > 0.02f)
            {
                body.Stun(d);
                staggerUntil       = now + d;
                staggerImmuneUntil = now + d + staggerImmunity;
            }
        }
    }

    /// <summary>학습 리셋 / 재사용 때.</summary>
    public void ResetState()
    {
        active = everAware = noisePending = false;
        lungePhase = 0;
        nextLungeAt = 0f;
        hesPhase = 0;
        hesStarted = false;
        LastLungeStart = -1f;
        restUntil = -1f;
        staggerUntil = staggerImmuneUntil = -1f;
        LungeAvailableNow = false;
        hasHerdDest = false;
        ApplyLungeBody();
        reactUntil = pauseUntil = -1f;
        movingTime = 0f;
        nextPauseAt = Time.time + Range(wanderPauseEvery) * pauseRhythm;
        Label = "";
    }

    void OnDrawGizmos()
    {
        if (!drawGizmo || !Application.isPlaying || Bypass) return;
        Color c;
        switch (Label)
        {
            case "반응 중": c = Color.yellow; break;
            case "멍함":   c = new Color(0.6f, 0.6f, 0.6f); break;
            case "멈칫":   c = Color.cyan; break;
            case "가속":   c = new Color(1f, 0.5f, 0f); break;
            case "왁!":    c = Color.red; break;
            case "노림":   c = new Color(1f, 0.55f, 0.55f); break;
            case "포위":   c = new Color(0.9f, 0.3f, 0.3f); break;
            case "돌진":   c = new Color(1f, 0f, 1f); break;
            case "머뭇":   c = new Color(0.4f, 0.8f, 1f); break;
            case "급발진": c = new Color(1f, 0.3f, 0.6f); break;
            case "헐떡":   c = new Color(0.5f, 0.5f, 1f); break;
            case "휘청":   c = Color.white; break;
            default: return;
        }
        Gizmos.color = c;
        Gizmos.DrawCube(transform.position + Vector3.up * 2.6f, Vector3.one * 0.3f);
    }
}
