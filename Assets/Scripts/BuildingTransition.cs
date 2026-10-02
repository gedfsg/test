using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 건물 진입/이탈 시 화면 검은 페이드 전환 효과.
/// 씬에 아무것도 없어도 BuildingTransition.Play() 최초 호출 시 Canvas+검은 Image를
/// 자동 생성해서 씀 (수동 배치 불필요). BuildingInterior.OnTriggerEnter/Exit에서 호출.
/// </summary>
public class BuildingTransition : MonoBehaviour
{
    public float duration = 0.5f; // 페이드 인+아웃 총 시간

    static BuildingTransition instance;
    Image fadeImage;
    Coroutine routine;

    public static void Play()
    {
        if (instance == null) instance = Create();
        instance.PlayInternal();
    }

    static BuildingTransition Create()
    {
        var canvasGo = new GameObject("BuildingTransitionCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000; // 다른 HUD보다 항상 위에서 화면을 덮음
        canvasGo.AddComponent<CanvasScaler>();

        var imgGo = new GameObject("Fade");
        imgGo.transform.SetParent(canvasGo.transform, false);
        var img = imgGo.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var comp = canvasGo.AddComponent<BuildingTransition>();
        comp.fadeImage = img;
        return comp;
    }

    void PlayInternal()
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Routine());
    }

    IEnumerator Routine()
    {
        float half = duration * 0.5f;

        // 페이드 인(검게)
        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            fadeImage.color = new Color(0f, 0f, 0f, t / half);
            yield return null;
        }
        fadeImage.color = Color.black;

        // 페이드 아웃(다시 밝게)
        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            fadeImage.color = new Color(0f, 0f, 0f, 1f - t / half);
            yield return null;
        }
        fadeImage.color = new Color(0f, 0f, 0f, 0f);
    }
}
