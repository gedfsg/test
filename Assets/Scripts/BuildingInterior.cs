using UnityEngine;

/// <summary>
/// 플레이어가 건물 안(축소된 트리거)에 들어가면 1층 높이(groundFloorHeight) 위쪽을
/// 셰이더로 완전히 잘라내서(discard) 안 보이게 함 — 지붕은 물론 2층 이상도 다 사라지고
/// 1층만 남음. 알파 블렌딩이 아니라 진짜로 안 그려지는 방식. 동시에 Fog로 주변을 어둡게
/// 만들어 지금 있는 방 주변만 보이게 함(좀보이드 스타일). 건물 콜라이더(문 있는 벽 1면 +
/// 막힌 벽 3면)/트리거 세팅은 _TempApplyBuildingInterior.cs(에디터 배치 툴)에서 담당.
/// </summary>
[RequireComponent(typeof(Collider))]
public class BuildingInterior : MonoBehaviour
{
    [Header("1층 높이 (건물 바닥에서 이 높이 위는 전부 잘라내서 1층만 보이게 함)")]
    public float groundFloorHeight = 5f;

    public string playerTag = "Player";

    [Header("실내 시야 제한 (좀보이드 스타일 - 안에 들어가면 주변이 어두워짐)")]
    public bool darkenSurroundings = true;
    public float indoorFogStartDistance = 2f;
    public float indoorFogEndDistance = 14f;
    public Color indoorFogColor = Color.black;

    static readonly int CutHeightId = Shader.PropertyToID("_CutHeight");
    static readonly int CutEnabledId = Shader.PropertyToID("_CutEnabled");

    // 여러 건물이 겹쳐 있어도(모서리 등) 안개가 꼬이지 않게 전역 카운터로 관리.
    // 실외 원래 안개 설정은 맨 처음 건물에 들어갈 때 한 번만 저장해뒀다가 마지막에 나갈 때 복원.
    static int globalInsideCount = 0;
    public static bool IsAnyoneIndoors => globalInsideCount > 0;
    static bool originalFogSaved = false;
    static bool originalFogEnabled;
    static FogMode originalFogMode;
    static Color originalFogColor;
    static float originalFogStart, originalFogEnd;

    Renderer[] renderers;
    Material[][] instanceMats; // renderer별 인스턴스 머티리얼 슬롯

    void Awake()
    {
        Collider triggerCol = FindTriggerCollider();
        if (triggerCol != null) triggerCol.isTrigger = true;

        renderers = GetComponentsInChildren<Renderer>(true);
        float cutHeightWorldY = ComputeCutHeight();

        Shader cutawayShader = Shader.Find("Custom/BuildingCutaway");
        if (cutawayShader == null)
        {
            Debug.LogError($"'{name}' — Custom/BuildingCutaway 셰이더를 찾을 수 없습니다.");
            return;
        }

        instanceMats = new Material[renderers.Length][];
        for (int r = 0; r < renderers.Length; r++)
        {
            Material[] originals = renderers[r].sharedMaterials;
            Material[] copies = new Material[originals.Length];
            for (int m = 0; m < originals.Length; m++)
            {
                Material src = originals[m];
                Material cut = new Material(cutawayShader);
                if (src != null)
                {
                    if (src.mainTexture != null) cut.mainTexture = src.mainTexture;
                    if (src.HasProperty("_BaseColor") || src.HasProperty("_Color")) cut.color = src.color;
                }
                cut.SetFloat(CutHeightId, cutHeightWorldY);
                cut.SetFloat(CutEnabledId, 0f);
                copies[m] = cut;
            }
            renderers[r].materials = copies;
            instanceMats[r] = copies;
        }
    }

    Collider FindTriggerCollider()
    {
        foreach (var c in GetComponents<Collider>())
            if (c.isTrigger) return c;
        return GetComponent<Collider>();
    }

    float ComputeCutHeight()
    {
        if (renderers.Length == 0) return transform.position.y + 1000f;
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b.min.y + groundFloorHeight;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        SetCut(true);
        if (darkenSurroundings) EnterIndoorFog(other.transform);
        BuildingTransition.Play();
        Camera.main?.GetComponent<CameraZoom>()?.SetIndoor(true);
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        SetCut(false);
        if (darkenSurroundings) ExitIndoorFog();
        BuildingTransition.Play();
        Camera.main?.GetComponent<CameraZoom>()?.SetIndoor(false);
    }

    void SetCut(bool cut)
    {
        if (instanceMats == null) return;
        float v = cut ? 1f : 0f;
        foreach (var mats in instanceMats)
            foreach (var m in mats)
                if (m != null) m.SetFloat(CutEnabledId, v);
    }

    // 좀보이드 스타일: 실내에 있을 때 Fog로 멀리 있는 바깥/다른 방을 어둡게 가려서
    // 지금 있는 방 주변만 보이게 함. 여러 건물이 겹쳐도 카운터로 관리해서 정상 작동.
    //
    // 주의: Built-in RP의 Fog는 "카메라로부터의 거리" 기준임. 이 게임 카메라는
    // CameraZoom.cs 기준 플레이어에서 20~80유닛(기본 40) 떨어진 탑뷰 카메라라서,
    // fogStartDistance/EndDistance를 0~14 같은 작은 절대값으로 주면 카메라~플레이어
    // 거리 자체가 이미 그 범위를 넘어버려서 아무 효과가 안 생김(또는 화면 전체가
    // 안개색). 그래서 트리거 진입 시점의 "카메라→플레이어 거리"를 구해서 그 위에
    // 상대값(indoorFogStartDistance/EndDistance)을 더하는 방식으로 계산함.
    static void EnterIndoorFogStatic(Transform player, float startMargin, float endMargin, Color color)
    {
        if (!originalFogSaved)
        {
            originalFogEnabled = RenderSettings.fog;
            originalFogMode = RenderSettings.fogMode;
            originalFogColor = RenderSettings.fogColor;
            originalFogStart = RenderSettings.fogStartDistance;
            originalFogEnd = RenderSettings.fogEndDistance;
            originalFogSaved = true;
        }

        float camDist = 0f;
        Camera cam = Camera.main;
        if (cam != null && player != null)
            camDist = Vector3.Distance(cam.transform.position, player.position);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = color;
        RenderSettings.fogStartDistance = camDist + startMargin;
        RenderSettings.fogEndDistance = camDist + endMargin;
        globalInsideCount++;
    }

    static void ExitIndoorFogStatic()
    {
        globalInsideCount = Mathf.Max(0, globalInsideCount - 1);
        if (globalInsideCount == 0 && originalFogSaved)
        {
            RenderSettings.fog = originalFogEnabled;
            RenderSettings.fogMode = originalFogMode;
            RenderSettings.fogColor = originalFogColor;
            RenderSettings.fogStartDistance = originalFogStart;
            RenderSettings.fogEndDistance = originalFogEnd;
        }
    }

    void EnterIndoorFog(Transform player) => EnterIndoorFogStatic(player, indoorFogStartDistance, indoorFogEndDistance, indoorFogColor);
    void ExitIndoorFog() => ExitIndoorFogStatic();
}
