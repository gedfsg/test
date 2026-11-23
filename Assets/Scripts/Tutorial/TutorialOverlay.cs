using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// (2026-11-23) 튜토리얼 화면: 어두운 스포트라이트 오버레이 + 말풍선 + "튜토리얼 끄기 ×" 버튼.
/// TutorialManager가 만들고 조종함 (로직은 매니저, 이 클래스는 그리기·애니메이션만).
///  - 오버레이: 전체 화면 Image + Custom/SpotlightMask (검정 α0.7, 원형 구멍, 가장자리 페더)
///  - 말풍선: 하단 중앙, #0a0a0a α0.85, 모서리 8px, 얇은 흰 테두리, 흰 22pt 굵게, 오른쪽 아래 "▶ SPACE 계속" 깜빡임. 초상화 없음
///  - 애니메이션 (DOTween 대신 코루틴, 일시정지 중에도 움직이게 unscaled 시간):
///      말풍선 등장 fade + scale 0.95→1 (0.3s) / 사라짐 fade (0.2s)
///      스포트라이트 등장 반경 0→목표 (0.4s ease out) / 사라짐 역순 (0.3s)
///      SPACE 깜빡임 alpha 1↔0.5 (1.5s 왕복)
/// </summary>
public class TutorialOverlay : MonoBehaviour
{
    const float OverlayAlpha = 0.7f;
    const float DialogIn = 0.3f, DialogOut = 0.2f, SpotIn = 0.4f, SpotOut = 0.3f, BlinkPeriod = 1.5f;
    static readonly Color DialogBg = new Color32(0x0A, 0x0A, 0x0A, 0xD9); // #0a0a0a α0.85

    /// <summary>스포트라이트 위치 계산 (화면 픽셀 중심, 반경 px, 페더 px). 매 프레임 불러서 해상도가 바뀌어도 맞음</summary>
    public delegate bool SpotFn(out Vector2 center, out float radius, out float feather);

    /// <summary>"계속" 버튼을 눌렀을 때</summary>
    public System.Action onContinueClicked;
    /// <summary>"튜토리얼 끄기 ×"를 눌렀을 때</summary>
    public System.Action onSkipAllClicked;

    public bool IsShowing { get; private set; }
    public bool IsAnimating => anim != null;

    RectTransform root;
    Image overlay;
    Material spotMat;
    CanvasGroup dialogGroup;
    RectTransform dialogRt;
    TextMeshProUGUI dialogText, continueText;
    CanvasGroup continueGroup;
    RectTransform skipRt;
    TextMeshProUGUI skipText;

    SpotFn spot;
    float spotScale;        // 0~1 (반경 애니메이션)
    Coroutine anim;

    static readonly int CenterId = Shader.PropertyToID("_Center");
    static readonly int RadiusId = Shader.PropertyToID("_Radius");
    static readonly int FeatherId = Shader.PropertyToID("_Feather");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    // ── 만들기 ─────────────────────────────────────
    public static TutorialOverlay Create(Transform parent)
    {
        // 게임 HUD 위, 생존 종료(1000)·일시정지(1100) 아래
        var canvas = MenuUI.CreateOverlayCanvas("TutorialCanvas", 900);
        canvas.transform.SetParent(parent, false);
        var o = canvas.gameObject.AddComponent<TutorialOverlay>();
        o.Build((RectTransform)canvas.transform);
        return o;
    }

    void Build(RectTransform canvasRt)
    {
        root = canvasRt;

        // 1) 오버레이 (전체 화면, 뒤 클릭 막기)
        overlay = NewImage(root, "SpotlightOverlay", null, Color.white);
        Stretch(overlay.rectTransform);
        var shader = Shader.Find("Custom/SpotlightMask");
        if (shader == null) Debug.LogWarning("[Tutorial] Custom/SpotlightMask 셰이더를 찾을 수 없습니다 - 구멍 없이 어둡게만 표시");
        spotMat = shader != null ? new Material(shader) : null;
        if (spotMat != null) { spotMat.SetColor(ColorId, new Color(0f, 0f, 0f, OverlayAlpha)); overlay.material = spotMat; }
        else overlay.color = new Color(0f, 0f, 0f, OverlayAlpha);
        overlay.raycastTarget = true;
        overlay.gameObject.SetActive(false);

        // 2) 말풍선 (하단 중앙)
        var dlg = new GameObject("Dialog", typeof(RectTransform), typeof(CanvasGroup));
        dialogRt = (RectTransform)dlg.transform;
        dialogRt.SetParent(root, false);
        dialogRt.anchorMin = dialogRt.anchorMax = dialogRt.pivot = new Vector2(0.5f, 0f);
        dialogRt.anchoredPosition = new Vector2(0f, 190f);   // 핫바(하단) 위
        dialogRt.sizeDelta = new Vector2(900f, 150f);
        dialogGroup = dlg.GetComponent<CanvasGroup>();

        var bg = NewImage(dialogRt, "Bg", HudStyle.RoundedRect, PerceivedAlpha(DialogBg));
        Stretch(bg.rectTransform);
        bg.pixelsPerUnitMultiplier = 2f;                       // 스프라이트 모서리 16px → 8px
        var frame = NewImage(dialogRt, "Frame", HudStyle.RoundedFrame, new Color(1f, 1f, 1f, 0.55f));
        Stretch(frame.rectTransform);
        frame.pixelsPerUnitMultiplier = 2f;                    // 테두리 얇게 (반투명이라 1px 정도로 보임)

        dialogText = HudStyle.NewText(dialogRt, "Text", "", HudStyle.Bold, 22f, Color.white,
            TextAlignmentOptions.Center, new Vector2(0f, 8f), new Vector2(820f, 100f));
        dialogText.richText = true;
        dialogText.textWrappingMode = TextWrappingModes.Normal;
        dialogText.lineSpacing = 12f;

        // "▶ SPACE 계속" (오른쪽 아래, 누를 수 있음) - 폰트에 ▶가 없어서 삼각형 스프라이트를 돌려 씀
        var cont = new GameObject("Continue", typeof(RectTransform), typeof(Image), typeof(Button), typeof(CanvasGroup));
        var crt = (RectTransform)cont.transform;
        crt.SetParent(dialogRt, false);
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0f);
        crt.anchoredPosition = new Vector2(-16f, 10f);
        crt.sizeDelta = new Vector2(150f, 28f);
        var hit = cont.GetComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);                 // 투명 클릭 영역
        cont.GetComponent<Button>().onClick.AddListener(() => onContinueClicked?.Invoke());
        continueGroup = cont.GetComponent<CanvasGroup>();
        var tri = NewImage(crt, "Arrow", HudStyle.Triangle, Color.white);
        tri.rectTransform.anchorMin = tri.rectTransform.anchorMax = tri.rectTransform.pivot = new Vector2(0f, 0.5f);
        tri.rectTransform.anchoredPosition = new Vector2(4f, 0f);
        tri.rectTransform.sizeDelta = new Vector2(11f, 11f);
        tri.rectTransform.localEulerAngles = new Vector3(0f, 0f, -90f); // 위 ▲ → 오른쪽 ▶
        continueText = HudStyle.NewText(crt, "Label", "", HudStyle.SemiBold, 15f, new Color(1f, 1f, 1f, 0.85f),
            TextAlignmentOptions.MidlineLeft, Vector2.zero, new Vector2(130f, 28f));
        continueText.rectTransform.anchorMin = continueText.rectTransform.anchorMax = continueText.rectTransform.pivot = new Vector2(0f, 0.5f);
        continueText.rectTransform.anchoredPosition = new Vector2(20f, 0f);
        dlg.SetActive(false);

        // 3) "튜토리얼 끄기 ×" (미니맵 바로 아래 - 위치는 매 프레임 미니맵에 맞춤)
        var skip = new GameObject("SkipTutorialButton", typeof(RectTransform), typeof(Image), typeof(Button));
        skipRt = (RectTransform)skip.transform;
        skipRt.SetParent(root, false);
        skipRt.anchorMin = skipRt.anchorMax = new Vector2(0.5f, 0.5f);
        skipRt.pivot = new Vector2(0.5f, 1f);
        skipRt.sizeDelta = new Vector2(150f, 30f);
        var sbg = skip.GetComponent<Image>();
        sbg.sprite = HudStyle.RoundedRect;
        sbg.type = Image.Type.Sliced;
        sbg.pixelsPerUnitMultiplier = 2.5f;
        sbg.color = new Color(0f, 0f, 0f, 0.6f);
        var sbtn = skip.GetComponent<Button>();
        var cb = sbtn.colors; cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f); cb.pressedColor = new Color(0.8f, 0.8f, 0.8f); sbtn.colors = cb;
        sbtn.onClick.AddListener(() => onSkipAllClicked?.Invoke());
        var sframe = NewImage(skipRt, "Frame", HudStyle.RoundedFrame, new Color(1f, 1f, 1f, 0.35f));
        Stretch(sframe.rectTransform);
        sframe.pixelsPerUnitMultiplier = 2.5f;
        skipText = HudStyle.NewText(skipRt, "Label", "", HudStyle.SemiBold, 15f, new Color(1f, 1f, 1f, 0.85f),
            TextAlignmentOptions.Center, Vector2.zero, new Vector2(150f, 30f));
        RefreshTexts();
    }

    // ── 표시 ───────────────────────────────────────
    public void Show(string text, SpotFn spotFn)
    {
        spot = spotFn;
        RefreshTexts();
        dialogText.text = text;
        // (2026-11-24) 문구가 길어져서(자세한 설명) 줄 수에 맞춰 말풍선 높이를 늘림 (최소 150)
        float textH = dialogText.GetPreferredValues(text, dialogText.rectTransform.sizeDelta.x, 0f).y;
        dialogText.rectTransform.sizeDelta = new Vector2(dialogText.rectTransform.sizeDelta.x, textH + 8f);
        dialogRt.sizeDelta = new Vector2(dialogRt.sizeDelta.x, Mathf.Max(150f, textH + 78f));
        PlaceDialog();
        IsShowing = true;
        if (anim != null) StopCoroutine(anim);
        anim = StartCoroutine(ShowRoutine());
    }

    /// <summary>기본은 하단 중앙. 비추는 곳이 화면 아래쪽(핫바 등)이면 말풍선이 가리지 않게 위쪽(날짜 HUD 아래)으로</summary>
    void PlaceDialog()
    {
        bool top = false;
        if (spot != null && spot(out Vector2 c, out float r, out _))
            top = c.y - r < Screen.height * 0.32f;
        var a = top ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0f);
        dialogRt.anchorMin = dialogRt.anchorMax = dialogRt.pivot = a;
        dialogRt.anchoredPosition = top ? new Vector2(0f, -170f) : new Vector2(0f, 190f);
    }

    public void Hide(System.Action done)
    {
        if (!IsShowing) { done?.Invoke(); return; }
        IsShowing = false;
        if (anim != null) StopCoroutine(anim);
        anim = StartCoroutine(HideRoutine(done));
    }

    public void SetSkipButtonVisible(bool on)
    {
        if (skipRt != null && skipRt.gameObject.activeSelf != on) skipRt.gameObject.SetActive(on);
    }

    void RefreshTexts()
    {
        bool en = Localization.IsEnglish;
        continueText.text = en ? "SPACE Continue" : "SPACE 계속";
        skipText.text = en ? "Skip Tutorial ×" : "튜토리얼 끄기 ×";   // 폰트에 ✕가 없어서 ×
    }

    IEnumerator ShowRoutine()
    {
        overlay.gameObject.SetActive(true);
        dialogRt.gameObject.SetActive(true);
        for (float t = 0f; t < SpotIn; t += Time.unscaledDeltaTime)
        {
            float k = t / SpotIn;
            spotScale = 1f - (1f - k) * (1f - k) * (1f - k);              // ease out (cubic)
            SetOverlayAlpha(Mathf.Clamp01(t / 0.15f));                    // 처음 0.15초 동안 어둠이 깔림
            float d = Mathf.Clamp01(t / DialogIn);
            dialogGroup.alpha = d;
            dialogRt.localScale = Vector3.one * Mathf.Lerp(0.95f, 1f, 1f - (1f - d) * (1f - d));
            yield return null;
        }
        spotScale = 1f; SetOverlayAlpha(1f);
        dialogGroup.alpha = 1f; dialogRt.localScale = Vector3.one;
        anim = null;
    }

    IEnumerator HideRoutine(System.Action done)
    {
        float dur = Mathf.Max(SpotOut, DialogOut);
        for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
        {
            dialogGroup.alpha = 1f - Mathf.Clamp01(t / DialogOut);
            float k = Mathf.Clamp01(t / SpotOut);
            spotScale = 1f - k * k;                                       // 역순 (ease in)
            SetOverlayAlpha(1f - k);                                      // 반경이 줄면서 어둠도 걷힘
            yield return null;
        }
        overlay.gameObject.SetActive(false);
        dialogRt.gameObject.SetActive(false);
        spot = null;
        anim = null;
        done?.Invoke();
    }

    void SetOverlayAlpha(float a)
    {
        var c = overlay.color; c.a = a; overlay.color = c;               // 셰이더가 Image 색을 곱함 (_Color α0.7 × a)
    }

    void LateUpdate()
    {
        // 스포트라이트 위치 (해상도·창 크기가 바뀌어도 맞게 매 프레임)
        if (overlay.gameObject.activeSelf && spotMat != null)
        {
            Vector2 c = Vector2.zero; float r = 0f, f = 0f;
            bool has = spot != null && spot(out c, out r, out f);
            spotMat.SetVector(CenterId, c);
            spotMat.SetFloat(RadiusId, has ? r * spotScale : 0f);
            spotMat.SetFloat(FeatherId, Mathf.Max(1f, f * spotScale));
        }
        // "SPACE 계속" 깜빡임 1↔0.5 (1.5초 왕복)
        if (dialogRt.gameObject.activeSelf)
            continueGroup.alpha = Mathf.Lerp(0.5f, 1f, 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f / BlinkPeriod));
        if (skipRt.gameObject.activeSelf) PlaceSkipButton();
    }

    // ── 미니맵 ─────────────────────────────────────
    RectTransform minimapRect;

    /// <summary>미니맵 그림 영역 (화면 픽셀). 미니맵 크기 설정으로 바뀌므로 매번 계산</summary>
    public bool TryGetMinimapScreenRect(out Rect rect)
    {
        rect = default;
        if (minimapRect == null)
        {
            var mc = FindFirstObjectByType<MinimapController>();
            if (mc == null || mc.playerMarker == null || mc.playerMarker.parent == null) return false;
            var raw = mc.playerMarker.parent.GetComponentInChildren<RawImage>(true);
            minimapRect = raw != null ? raw.rectTransform : (RectTransform)mc.playerMarker.parent;
        }
        if (!minimapRect.gameObject.activeInHierarchy) return false;
        return TryGetScreenRect(minimapRect, out rect);
    }

    /// <summary>아무 UI의 화면 픽셀 영역 (Overlay / Camera 캔버스 둘 다)</summary>
    public static bool TryGetScreenRect(RectTransform rt, out Rect rect)
    {
        rect = default;
        if (rt == null) return false;
        var canvas = rt.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
        rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        return rect.width > 1f;
    }

    void PlaceSkipButton()
    {
        Vector2 screen;
        if (TryGetMinimapScreenRect(out Rect mm)) screen = new Vector2(mm.center.x, mm.yMin - 10f); // 미니맵 바로 아래 가운데
        else screen = new Vector2(150f, Screen.height - 330f);                                        // 미니맵이 없으면 왼쪽 위
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out Vector2 local))
            skipRt.anchoredPosition = local;
    }

    // ── 헬퍼 ───────────────────────────────────────
    /// <summary>Linear 색 공간에서 어두운 반투명 배경이 사양(감마 기준 알파)보다 옅게 보이는 것 보정</summary>
    static Color PerceivedAlpha(Color c)
    {
        if (QualitySettings.activeColorSpace == ColorSpace.Linear) c.a = 1f - Mathf.Pow(1f - c.a, 2.2f);
        return c;
    }

    static Image NewImage(Transform parent, string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        if (sprite != null && sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
        return img;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    void OnDestroy()
    {
        if (spotMat != null) Destroy(spotMat);
    }
}
