using UnityEngine;
using TMPro;

public class PickupItem : MonoBehaviour
{
    [Header("Item Data")]
    public ItemData itemData;
    public int amount = 1;

    private GameObject promptObj;
    private TextMeshPro promptText;
    private PlayerController nearbyPlayer;

    private static TMP_FontAsset cachedKoreanFont;
    private static bool fontSearched = false;

    void Start()
    {
        Collider playerCollider = GameObject.FindGameObjectWithTag("Player")?.GetComponent<Collider>();
        Collider boxCollider = GetComponent<BoxCollider>();
        if (playerCollider != null && boxCollider != null)
            Physics.IgnoreCollision(playerCollider, boxCollider);

        if (GetComponent<ItemOutlineGlow>() == null)
            gameObject.AddComponent<ItemOutlineGlow>();

        CreatePrompt();
    }

    void OnDestroy()
    {
        if (promptObj != null) Destroy(promptObj);
    }

    void CreatePrompt()
    {
        LoadKoreanFont();

        // 부모에 묶지 않고 독립 오브젝트로 (스케일 영향 안 받게)
        promptObj = new GameObject("PickupPrompt");
        promptObj.transform.localScale = Vector3.one;

        promptText = promptObj.AddComponent<TextMeshPro>();
        if (cachedKoreanFont != null) promptText.font = cachedKoreanFont;
        promptText.text = "F키로 획득";
        promptText.fontSize = 2f;
        promptText.color = Color.white;
        promptText.alignment = TextAlignmentOptions.Center;
        promptText.fontStyle = FontStyles.Bold;
        promptText.rectTransform.sizeDelta = new Vector2(5f, 1.2f);

        promptObj.AddComponent<FaceCamera>();
        promptObj.SetActive(false);
    }

    void LoadKoreanFont()
    {
        if (fontSearched) return;
        fontSearched = true;

        // TTF 파일에서 직접 TMP_FontAsset 생성 (Dynamic atlas)
        Font ttf = Resources.Load<Font>("H2HDRM");
#if UNITY_EDITOR
        if (ttf == null)
            ttf = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/H2HDRM.TTF");
#endif

        if (ttf == null)
        {
            Debug.LogError("[PickupItem] H2HDRM.TTF 파일을 찾을 수 없어!");
            return;
        }

        cachedKoreanFont = TMP_FontAsset.CreateFontAsset(ttf);
        if (cachedKoreanFont != null)
            cachedKoreanFont.atlasPopulationMode = AtlasPopulationMode.Dynamic;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null) return;

        nearbyPlayer = player;
        player.SetNearbyItem(this);
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null) return;

        nearbyPlayer = null;
        player.ClearNearbyItem(this);
        if (promptObj != null) promptObj.SetActive(false);
    }

    void Update()
    {
        if (promptObj == null) return;

        // 플레이어가 근처에 있고, 이 아이템이 '가장 가까운 아이템'일 때만 표시
        bool show = nearbyPlayer != null && nearbyPlayer.GetClosestNearbyItem() == this;

        if (show)
        {
            bool hotbarFull = itemData is WeaponData && (WeaponHotbarUI.Instance?.IsFull() ?? false);
            promptText.text = hotbarFull ? "핫바 가득 참" : "F키로 획득";
            promptText.color = hotbarFull ? Color.red : Color.white;
            promptObj.SetActive(true);

            // 아이템 옆에 위치 (월드 공간)
            promptObj.transform.position = transform.position + Vector3.up * 0.5f + Vector3.right * 0.5f;
        }
        else
        {
            promptObj.SetActive(false);
        }
    }
}
