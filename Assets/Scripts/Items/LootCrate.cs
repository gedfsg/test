using UnityEngine;
using TMPro;

public class LootCrate : MonoBehaviour
{
    [Header("Crate Settings")]
    public LootTable lootTable;
    public Rarity crateTier = Rarity.Common;

    [Tooltip("열리는 사운드/이펙트 (선택)")]
    public GameObject openEffectPrefab;

    private GameObject promptObj;
    private TextMeshPro promptText;
    private PlayerController nearbyPlayer;
    private bool isOpened = false;

    private static TMP_FontAsset cachedKoreanFont;
    private static bool fontSearched = false;

    void Start()
    {
        // 등급별 글로우 (ItemOutlineGlow 재활용 가능)
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
        promptObj = new GameObject("CratePrompt");
        promptObj.transform.localScale = Vector3.one;

        promptText = promptObj.AddComponent<TextMeshPro>();
        if (cachedKoreanFont != null) promptText.font = cachedKoreanFont;
        promptText.text = $"F키로 {crateTier} 상자 열기";
        promptText.fontSize = 2f;
        promptText.color = RarityColors.Get(crateTier);
        promptText.alignment = TextAlignmentOptions.Center;
        promptText.fontStyle = FontStyles.Bold;
        promptText.rectTransform.sizeDelta = new Vector2(6f, 1.2f);

        promptObj.AddComponent<FaceCamera>();
        promptObj.SetActive(false);
    }

    void LoadKoreanFont()
    {
        if (fontSearched) return;
        fontSearched = true;
        Font ttf = Resources.Load<Font>("H2HDRM");
#if UNITY_EDITOR
        if (ttf == null)
            ttf = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/H2HDRM.TTF");
#endif
        if (ttf == null) return;
        cachedKoreanFont = TMP_FontAsset.CreateFontAsset(ttf);
        if (cachedKoreanFont != null)
            cachedKoreanFont.atlasPopulationMode = AtlasPopulationMode.Dynamic;
    }

    void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[LootCrate] OnTriggerEnter: {other.name}, tag={other.tag}");
        if (!other.CompareTag("Player")) return;
        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null) return;
        nearbyPlayer = player;
        player.SetNearbyCrate(this);
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null) return;
        nearbyPlayer = null;
        player.ClearNearbyCrate(this);
        if (promptObj != null) promptObj.SetActive(false);
    }

    void Update()
    {
        if (promptObj == null) return;
        bool show = nearbyPlayer != null && nearbyPlayer.GetClosestNearbyCrate() == this && !isOpened;
        promptObj.SetActive(show);
        if (show)
            promptObj.transform.position = transform.position + Vector3.up * 0.8f;
    }

    /// <summary>PlayerController가 F키 입력 시 호출</summary>
    public void Open(InventoryManager inventory)
    {
        if (isOpened || lootTable == null) return;
        isOpened = true;

        var rolled = lootTable.Roll();
        Debug.Log($"[LootCrate] {crateTier} 상자 열림! {rolled.Count}개 아이템 획득");

        foreach (var (item, amount) in rolled)
        {
            // 무기는 핫바로, 나머지는 인벤토리
            if (item is WeaponData wd)
            {
                var hotbar = WeaponHotbarUI.Instance;
                if (hotbar != null && !hotbar.IsFull())
                    hotbar.AddWeapon(wd);
                else
                    Debug.LogWarning($"{wd.itemName} 핫바 가득 차서 드롭됨");
            }
            else
            {
                inventory.AddItem(item, amount);
            }
            Debug.Log($"  → {item.itemName} x{amount} ({item.rarity})");
        }

        if (openEffectPrefab != null)
            Instantiate(openEffectPrefab, transform.position, Quaternion.identity);

        Destroy(gameObject);
    }
}