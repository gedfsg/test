using UnityEngine;

/// <summary>
/// 아이템 주변에 글로우 아웃라인 효과
/// 메시를 살짝 크게 복사해서 발광 머티리얼 적용
/// </summary>
public class ItemOutlineGlow : MonoBehaviour
{
    [Header("Glow 설정")]
    public Color  glowColor        = new Color(0f, 0.8f, 1f, 1f); // 하늘색
    public float  glowIntensityMin = 1.5f;
    public float  glowIntensityMax = 4f;
    public float  glowSpeed        = 2f;
    public float  outlineThickness = 0.06f; // 아웃라인 두께

    private Material  glowMat;
    private GameObject[] outlineObjects;
    private Vector3   startPos;
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    void Start()
    {
        startPos = transform.position;
        CreateOutline();
    }

    void CreateOutline()
    {
        // 글로우용 머티리얼 — 앞면 컬링(Cull Front)으로 테두리만 보이게
        glowMat = new Material(Shader.Find("Standard"));
        glowMat.SetFloat("_Mode", 0f);
        glowMat.color = Color.black;
        glowMat.EnableKeyword("_EMISSION");
        glowMat.SetColor(EmissionColor, glowColor * glowIntensityMin);
        glowMat.SetFloat("_Glossiness", 0f);
        // Cull Front(1) : 앞면을 제거 → 뒤집힌 법선의 뒷면만 렌더
        // 원본 메시보다 살짝 크게 확대되므로 가장자리만 삐져나와 아웃라인처럼 보임
        glowMat.SetInt("_Cull", 1);

        // 자식 MeshFilter 전부 수집
        MeshFilter[] filters = GetComponentsInChildren<MeshFilter>();
        outlineObjects = new GameObject[filters.Length];

        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter mf = filters[i];
            if (mf.sharedMesh == null) continue;

            // 아웃라인 오브젝트 생성
            GameObject outline = new GameObject("_Outline");
            outline.transform.SetParent(mf.transform, false);
            outline.transform.localPosition = Vector3.zero;
            outline.transform.localRotation = Quaternion.identity;
            outline.transform.localScale    = Vector3.one * (1f + outlineThickness);

            MeshFilter omf = outline.AddComponent<MeshFilter>();
            omf.sharedMesh = mf.sharedMesh; // 원본 메시 공유 (읽기 권한 불필요)

            MeshRenderer omr = outline.AddComponent<MeshRenderer>();
            omr.material             = glowMat;
            omr.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
            omr.receiveShadows       = false;

            outlineObjects[i] = outline;
        }
    }

    void Update()
    {
        if (glowMat == null) return;

        // 반짝임
        float t         = (Mathf.Sin(Time.time * glowSpeed) + 1f) * 0.5f;
        float intensity = Mathf.Lerp(glowIntensityMin, glowIntensityMax, t);
        glowMat.SetColor(EmissionColor, glowColor * intensity);

        // 둥실둥실
        float newY = startPos.y + Mathf.Sin(Time.time * 1.5f) * 0.12f;
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);
    }

    void OnDestroy()
    {
        if (glowMat != null) Destroy(glowMat);
    }
}
