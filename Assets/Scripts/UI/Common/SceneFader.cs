using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 씬 전환 페이드 (게임 시작 / 다시 시작 / 메인 메뉴로).
///  1) 화면이 검게 어두워짐 + 모든 소리가 같이 줄어듦 (AudioListener.volume)
///  2) 검은 화면에서 씬 로드 → 로드 직후 끊김(스폰·초기화)이 검은 화면 뒤에서 지나가도록 잠깐 대기
///  3) 새 씬이 서서히 밝아지며 소리도 돌아옴
/// 전환 중에는 검은 막이 클릭을 막아서 버튼을 두 번 눌러도 한 번만 이동.
/// 일시정지(timeScale 0) 중에도 동작하도록 unscaled 시간 사용.
/// </summary>
public class SceneFader : MonoBehaviour
{
    public float fadeOutTime = 0.6f;
    public float holdTime = 0.35f;   // 로드 후 검은 화면 최소 유지 (첫 프레임 끊김 가리기)
    public float maxHoldTime = 4f;   // 프레임이 계속 끊겨도 이 시간이 지나면 밝아짐
    public float fadeInTime = 1.0f;

    static SceneFader instance;
    CanvasGroup group;

    /// <summary>지금 전환 중</summary>
    public static bool Busy { get; private set; }

    /// <summary>페이드하며 씬 이동. beforeLoad는 화면이 완전히 검어진 뒤 로드 직전에 실행</summary>
    public static void Load(string scene, System.Action beforeLoad = null)
    {
        if (Busy) return;
        Get().StartCoroutine(instance.Run(scene, beforeLoad));
    }

    static SceneFader Get()
    {
        if (instance != null) return instance;
        var go = new GameObject("SceneFader");
        DontDestroyOnLoad(go);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000; // 모든 UI 위
        go.AddComponent<GraphicRaycaster>();
        var img = new GameObject("Black", typeof(RectTransform)).AddComponent<Image>();
        img.transform.SetParent(go.transform, false);
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        img.color = Color.black;
        img.raycastTarget = true;
        instance = go.AddComponent<SceneFader>();
        instance.group = go.AddComponent<CanvasGroup>();
        instance.group.alpha = 0f;
        instance.group.blocksRaycasts = false;
        return instance;
    }

    IEnumerator Run(string scene, System.Action beforeLoad)
    {
        Busy = true;
        group.blocksRaycasts = true;
        float startVolume = AudioListener.volume;
        if (scene != MenuUI.MainMenuScene) UIAudio.FadeOutBgm(fadeOutTime); // 메뉴 BGM이 게임 씬까지 남지 않게

        yield return Fade(0f, 1f, fadeOutTime, startVolume, 0f);

        beforeLoad?.Invoke();
        var op = SceneManager.LoadSceneAsync(scene);
        while (!op.isDone) yield return null;
        // 새 씬 첫 프레임들(스폰·초기화)의 끊김은 검은 화면 뒤에서:
        // 최소 holdTime 기다리고, 프레임이 연속으로 매끄러워질 때까지 더 기다림 (최대 maxHoldTime)
        float held = 0f; int smooth = 0;
        while (held < maxHoldTime && (held < holdTime || smooth < 10))
        {
            yield return null;
            float dt = Time.unscaledDeltaTime;
            held += dt;
            smooth = dt < 0.05f ? smooth + 1 : 0;
        }

        group.blocksRaycasts = false; // 밝아지는 동안은 바로 조작 가능
        yield return Fade(1f, 0f, fadeInTime, 0f, startVolume > 0f ? startVolume : 1f);
        Busy = false;
    }

    IEnumerator Fade(float from, float to, float time, float volFrom, float volTo)
    {
        for (float t = 0f; t < time; )
        {
            // 끊긴 프레임이 있어도 한 번에 건너뛰지 않게 한 프레임 최대 1/30초만 진행
            t += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / time));
            group.alpha = Mathf.Lerp(from, to, k);
            AudioListener.volume = Mathf.Lerp(volFrom, volTo, k);
            yield return null;
        }
        group.alpha = to;
        AudioListener.volume = volTo;
    }
}
