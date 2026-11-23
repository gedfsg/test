using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 회복템 사용 딜레이 (2026-11-16). 플레이어에 붙음 (PlayerController.Awake가 자동 부착).
///  - ConsumableData.useTime 동안 진행 바(화면 중앙 하단, 주황) + "회복 중..." 표시, 끝나면 효과 적용 + 1개 차감
///  - 사용 중: 이동 가능, 공격 불가 (PlayerController.HandleFire가 IsUsing 확인)
///  - 좀비에게 맞으면(Health.onHurt + IsZombieHit, 2026-11-21) 취소: 아이템 안 줄어듦, "회복 중단" 토스트, 소리 멈춤. 죽어도 취소
///  - 소리: ConsumableData.useSound를 SFX 믹서 그룹으로 (설정 메뉴 효과음 볼륨 적용)
/// 핫바(3~5키·클릭)와 인벤토리 "사용" 모두 여기를 거침.
/// </summary>
public class ConsumableUseController : MonoBehaviour
{
    public static ConsumableUseController Instance { get; private set; }

    public bool IsUsing => current != null;
    /// <summary>취소됐을 때 (PlayerController가 아이템 사용 동작/IK 차단을 풀기 위해 구독)</summary>
    public event System.Action Cancelled;
    public float Progress01 => current != null && duration > 0f ? Mathf.Clamp01(elapsed / duration) : 0f;

    ConsumableData current;
    System.Action onComplete;
    float duration, elapsed;
    Health health;
    AudioSource audioSrc;

    // UI (런타임 생성)
    GameObject barRoot;
    Image barFill;

    static readonly Color BarColor = HudStyle.Energy; // 주황 #FFA500

    void Awake()
    {
        Instance = this;
        health = GetComponent<Health>();
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;
        audioSrc.spatialBlend = 0f; // 내 캐릭터 소리라 2D
        // SFX 믹서 그룹 (설정의 효과음 볼륨 SFXVolume)
        var mixer = AudioManager.Mixer != null ? AudioManager.Mixer : Resources.Load<UnityEngine.Audio.AudioMixer>("Audio/GameMixer");
        if (mixer != null)
        {
            var groups = mixer.FindMatchingGroups("SFX");
            if (groups.Length > 0) audioSrc.outputAudioMixerGroup = groups[0];
        }
    }

    void OnEnable()
    {
        if (health == null) health = GetComponent<Health>();
        if (health != null)
        {
            if (health.onHurt == null) health.onHurt = new UnityEngine.Events.UnityEvent();
            if (health.onDeath == null) health.onDeath = new UnityEngine.Events.UnityEvent();
            health.onHurt.AddListener(OnHurt);
            health.onDeath.AddListener(OnDied);
        }
    }

    void OnDisable()
    {
        if (health != null) { health.onHurt.RemoveListener(OnHurt); health.onDeath.RemoveListener(OnDied); }
        Cancel(null);
    }

    /// <summary>
    /// 사용 시작. useTime이 0이면 바로 complete. 이미 사용 중이면 false (중복 사용 X).
    /// complete = 효과 적용 + 개수 차감 (끝까지 버텼을 때만 호출됨)
    /// </summary>
    public bool Begin(ConsumableData data, System.Action complete)
    {
        if (data == null || complete == null) return false;
        if (IsUsing) { PickupFeedbackUI.Instance?.ShowWarning("이미 사용 중"); return false; }
        if (data.useTime <= 0f) { complete(); return true; }

        current = data; onComplete = complete;
        duration = data.useTime; elapsed = 0f;
        if (data.useSound != null) { audioSrc.clip = data.useSound; audioSrc.volume = 1f; audioSrc.Play(); }
        ShowBar(true);
        return true;
    }

    void Update()
    {
        if (!IsUsing) return;
        elapsed += Time.deltaTime;
        if (barFill != null) barFill.fillAmount = Progress01;
        if (elapsed < duration) return;

        var done = onComplete;
        current = null; onComplete = null;
        ShowBar(false);
        done?.Invoke();
    }

    // void OnHurt() => Cancel("회복 중단"); // (이전) 어떤 피해든 취소 → 갈증 피해로 물을 못 마시는 문제
    // (2026-11-21) 좀비에게 맞았을 때만 취소. 갈증·배고픔·불길·폭발 피해로는 끊기지 않음
    void OnHurt() { if (health != null && health.IsZombieHit) Cancel("회복 중단"); }
    void OnDied() => Cancel(null);

    /// <summary>사용 취소 (아이템 소비 X). message가 있으면 경고 토스트</summary>
    public void Cancel(string message)
    {
        if (!IsUsing) return;
        current = null; onComplete = null;
        if (audioSrc != null) audioSrc.Stop();
        ShowBar(false);
        if (!string.IsNullOrEmpty(message)) PickupFeedbackUI.Instance?.ShowWarning(message);
        Cancelled?.Invoke();
    }

    // ── 진행 바: 화면 중앙 하단, 주황 채움 + "회복 중..." ──
    void ShowBar(bool on)
    {
        if (on && barRoot == null) BuildBar();
        if (barRoot != null) barRoot.SetActive(on);
        if (on && barFill != null) barFill.fillAmount = 0f;
    }

    void BuildBar()
    {
        var canvasGo = new GameObject("ConsumableUseBar", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        barRoot = canvasGo;

        // 바탕 (중앙 하단, 핫바 위)
        var bg = NewImage("Bg", canvasGo.transform, HudStyle.PanelBg);
        var bgRt = bg.rectTransform;
        bgRt.anchorMin = bgRt.anchorMax = new Vector2(0.5f, 0f);
        bgRt.pivot = new Vector2(0.5f, 0f);
        bgRt.anchoredPosition = new Vector2(0f, 230f);
        bgRt.sizeDelta = new Vector2(320f, 16f);

        barFill = NewImage("Fill", bg.transform, BarColor);
        barFill.type = Image.Type.Filled;
        barFill.fillMethod = Image.FillMethod.Horizontal;
        barFill.fillOrigin = 0;
        // Filled 타입은 스프라이트가 있어야 채움이 적용됨 → 1x1 흰 텍스처 스프라이트
        barFill.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        var fRt = barFill.rectTransform;
        fRt.anchorMin = Vector2.zero; fRt.anchorMax = Vector2.one;
        fRt.offsetMin = new Vector2(2f, 2f); fRt.offsetMax = new Vector2(-2f, -2f);

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(bg.transform, false);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        if (HudStyle.SemiBold != null) label.font = HudStyle.SemiBold;
        // label.text = "회복 중..."; // (이전)
        label.text = "사용 중... (다시 누르면 취소)"; // (2026-11-24) 같은 키·가방 "사용"을 다시 누르면 취소 (PlayerController.BeginConsumable)
        label.fontSize = 22f;
        label.color = HudStyle.Text;
        label.alignment = TextAlignmentOptions.Bottom;
        label.raycastTarget = false;
        var lRt = label.rectTransform;
        lRt.anchorMin = new Vector2(0f, 1f); lRt.anchorMax = new Vector2(1f, 1f);
        lRt.pivot = new Vector2(0.5f, 0f);
        lRt.anchoredPosition = new Vector2(0f, 4f);
        lRt.sizeDelta = new Vector2(0f, 30f);
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }
}
