using UnityEngine;

/// <summary>
/// MinimapCamera에 붙임. 미니맵도 메인 카메라와 같은 씬 조명/안개를 그대로 받기 때문에
/// 밤이 되면(DayNightCycle이 directionalLight를 거의 끄고 안개를 짙게 깔아서) 미니맵이
/// 같이 캄캄해져서 안 보이던 문제를 고침.
///
/// 미니맵 카메라가 렌더링하는 그 순간에만 안개를 끄고 앰비언트/조명을 밝게 올렸다가,
/// 렌더링이 끝나자마자 원래 값으로 되돌림 - 메인 카메라가 보여주는 실제 게임 화면의
/// 밤 분위기는 전혀 건드리지 않음.
/// </summary>
[RequireComponent(typeof(Camera))]
public class MinimapNightFix : MonoBehaviour
{
    [Header("미니맵 렌더링 중에만 적용할 밝기")]
    public float minAmbientIntensity = 1.3f;
    public float minDirectionalLightIntensity = 0.9f;

    bool savedFog;
    Color savedAmbient;
    float savedAmbientIntensity;
    float savedLightIntensity;
    Color savedLightColor;

    void OnPreRender()
    {
        savedFog = RenderSettings.fog;
        savedAmbient = RenderSettings.ambientLight;
        savedAmbientIntensity = RenderSettings.ambientIntensity;
        RenderSettings.fog = false;
        RenderSettings.ambientLight = Color.white;
        RenderSettings.ambientIntensity = Mathf.Max(RenderSettings.ambientIntensity, minAmbientIntensity);

        var dnc = DayNightCycle.Instance;
        if (dnc != null && dnc.directionalLight != null)
        {
            savedLightIntensity = dnc.directionalLight.intensity;
            savedLightColor = dnc.directionalLight.color;
            dnc.directionalLight.intensity = Mathf.Max(savedLightIntensity, minDirectionalLightIntensity);
            dnc.directionalLight.color = Color.Lerp(Color.white, savedLightColor, 0.4f);
        }
    }

    void OnPostRender()
    {
        RenderSettings.fog = savedFog;
        RenderSettings.ambientLight = savedAmbient;
        RenderSettings.ambientIntensity = savedAmbientIntensity;

        var dnc = DayNightCycle.Instance;
        if (dnc != null && dnc.directionalLight != null)
        {
            dnc.directionalLight.intensity = savedLightIntensity;
            dnc.directionalLight.color = savedLightColor;
        }
    }
}
