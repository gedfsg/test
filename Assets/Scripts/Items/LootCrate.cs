using UnityEngine;
using TMPro;

public class LootCrate : MonoBehaviour
{
    /// <summary>상자가 열릴 때마다 (미션: 상자 열기 수 집계용)</summary>
    public static event System.Action<LootCrate> AnyOpened;

    [Header("Crate Settings")]
    public LootTable lootTable;
    public Rarity crateTier = Rarity.Common;

    [Tooltip("열리는 사운드/이펙트 (선택)")]
    public GameObject openEffectPrefab;

    [Header("열기 방식 (파괴 X - 뚜껑 열리고 아이템은 바닥에 드랍)")]
    [Tooltip("군용 상자처럼 1개를 먼저 보장으로 뽑을 테이블 (비우면 없음)")]
    public LootTable guaranteedTable;
    [Tooltip("열릴 때 회전시킬 뚜껑 경첩 (비우면 애니메이션 없음)")]
    public Transform lidHinge;
    [Tooltip("열면 주변에 스폰할 좀비 수 (최소~최대)")]
    public int zombiesOnOpenMin = 2, zombiesOnOpenMax = 3;
    [Tooltip("둥실거리는 글로우(ItemOutlineGlow) 붙이기. 파란 효과 제거로 기본 끔")]
    public bool addGlow = false;

    private GameObject promptObj;
    private TextMeshPro promptText;
    private PlayerController nearbyPlayer;
    private bool isOpened = false;

    private static TMP_FontAsset cachedKoreanFont;
    private static bool fontSearched = false;

    void Start()
    {
        // 등급별 글로우 (ItemOutlineGlow 재활용 가능)
        if (addGlow && GetComponent<ItemOutlineGlow>() == null)
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
        promptText.text = $"F키로 {TierName(crateTier)} 상자 열기";
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

    public static string TierName(Rarity tier) => tier switch
    {
        Rarity.Common => "나무",
        Rarity.Rare => "금속",
        Rarity.Epic => "군용",
        _ => tier.ToString()
    };

    /// <summary>PlayerController가 F키 입력 시 호출</summary>
    public void Open(InventoryManager inventory)
    {
        if (isOpened || lootTable == null) return;
        AnyOpened?.Invoke(this);
        if (AudioManager.Library != null) AudioManager.PlayUI(SoundLibrary.Pick(AudioManager.Library.crateOpen), AudioManager.Vol.UI);

        // 뚜껑이 있는 상자(월드 스폰 상자) = 새 방식: 파괴 안 하고 뚜껑 열림 + 바닥 드랍 + 좀비
        if (lidHinge != null) { OpenInPlace(); return; }

        isOpened = true;

        var rolled = lootTable.Roll();
        Debug.Log($"[LootCrate] {crateTier} 상자 열림! {rolled.Count}개 아이템 획득");

        foreach (var (item, amount) in rolled)
        {
            // 무기는 핫바로, 나머지는 인벤토리
            if (item is WeaponData wd)
            {
                var hotbar = WeaponHotbarUI.Instance;
                // 총 슬롯(1,2)이 다 차 있으면 버리지 않고 인벤토리로
                if (hotbar == null || !hotbar.AddWeapon(wd))
                    inventory.AddItem(wd, 1);
            }
            else
            {
                inventory.AddItem(item, amount);
            }
            Debug.Log($"  → {item.itemName} x{amount} ({item.rarity})");
        }

        if (openEffectPrefab != null)
            Instantiate(openEffectPrefab, transform.position, Quaternion.identity);

        // 예전 방식 상자는 열면 부서져 사라짐 → 나무 상자 부서지는 소리 (3D)
        if (AudioManager.Library != null)
            AudioManager.PlaySFX(SoundLibrary.Pick(AudioManager.Library.woodBreak), transform.position, AudioManager.Vol.Impact, AudioManager.Channel.SFX, 30f);
        Destroy(gameObject);
    }

    void OpenInPlace()
    {
        isOpened = true;
        if (promptObj != null) promptObj.SetActive(false);

        var rolled = new System.Collections.Generic.List<(ItemData item, int amount)>();
        if (guaranteedTable != null)
        {
            var g = guaranteedTable.Roll();
            if (g.Count > 0) rolled.Add(g[0]);
        }
        rolled.AddRange(lootTable.Roll());
        Debug.Log($"[LootCrate] {TierName(crateTier)} 상자 열림 → 아이템 {rolled.Count}개 바닥 드랍");

        // 상자 앞쪽 반원에 고르게 흩뿌림
        for (int i = 0; i < rolled.Count; i++)
        {
            float ang = Mathf.Lerp(-70f, 70f, rolled.Count == 1 ? 0.5f : (float)i / (rolled.Count - 1));
            Vector3 dir = Quaternion.Euler(0f, ang, 0f) * transform.forward;
            WorldItemDrop.Spawn(transform.position + dir * 1.4f, rolled[i].item, rolled[i].amount);
            Debug.Log($"  → {rolled[i].item.itemName} x{rolled[i].amount} ({rolled[i].item.rarity})");
        }

        // 주변 좀비 (소리 듣고 몰려옴)
        var spawner = FindFirstObjectByType<ZombieSpawner>(FindObjectsInactive.Include);
        if (spawner != null)
        {
            int n = Random.Range(zombiesOnOpenMin, zombiesOnOpenMax + 1);
            for (int i = 0; i < n; i++) spawner.SpawnExtraNear(transform.position, 10f, 16f);
        }

        if (openEffectPrefab != null)
            Instantiate(openEffectPrefab, transform.position, Quaternion.identity);

        StartCoroutine(OpenLid());
    }

    System.Collections.IEnumerator OpenLid()
    {
        Quaternion from = lidHinge.localRotation, to = from * Quaternion.Euler(-110f, 0f, 0f);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.4f)
        {
            lidHinge.localRotation = Quaternion.Slerp(from, to, 1f - (1f - t) * (1f - t)); // 처음 빠르고 끝에 감속
            yield return null;
        }
        lidHinge.localRotation = to;
    }

    // ── 월드 상자 생성 (에셋 없이 Cube로) ──
    static readonly System.Collections.Generic.Dictionary<Rarity, Material> crateMats = new System.Collections.Generic.Dictionary<Rarity, Material>();

    static Color TierColor(Rarity tier) => tier switch
    {
        Rarity.Rare => new Color(0.18f, 0.34f, 0.62f),   // 금속 - 파랑
        Rarity.Epic => new Color(0.22f, 0.36f, 0.16f),   // 군용 - 녹색
        _ => new Color(0.45f, 0.28f, 0.12f),             // 나무 - 갈색
    };

    /// <summary>나무/금속/군용 상자 생성. 몸통 + 경첩 달린 뚜껑 + 상호작용 트리거</summary>
    public static LootCrate CreateCrate(Vector3 groundPos, float yaw, Rarity tier, LootTable table, LootTable guaranteed)
    {
        if (!crateMats.TryGetValue(tier, out var mat) || mat == null)
        {
            mat = new Material(Shader.Find("Standard")) { color = TierColor(tier) };
            mat.SetFloat("_Glossiness", tier == Rarity.Rare ? 0.5f : 0.1f);
            crateMats[tier] = mat;
        }
        Vector3 size = new Vector3(1.0f, 0.55f, 0.65f);

        var root = new GameObject($"Crate_{TierName(tier)}");
        root.transform.SetPositionAndRotation(groundPos, Quaternion.Euler(0f, yaw, 0f));

        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, size.y / 2f, 0f);
        body.transform.localScale = size;
        body.GetComponent<Renderer>().sharedMaterial = mat;

        // 뚜껑: 뒤쪽 위 모서리에 경첩
        var hinge = new GameObject("LidHinge").transform;
        hinge.SetParent(root.transform, false);
        hinge.localPosition = new Vector3(0f, size.y, -size.z / 2f);
        var lid = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lid.name = "Lid";
        Object.Destroy(lid.GetComponent<Collider>());
        lid.transform.SetParent(hinge, false);
        lid.transform.localPosition = new Vector3(0f, 0.05f, size.z / 2f);
        lid.transform.localScale = new Vector3(size.x + 0.04f, 0.1f, size.z + 0.04f);
        lid.GetComponent<Renderer>().sharedMaterial = mat;

        var trigger = root.AddComponent<SphereCollider>(); // 플레이어 근접 감지 (F키)
        trigger.isTrigger = true;
        trigger.radius = 1.8f;
        trigger.center = new Vector3(0f, 0.5f, 0f);

        var crate = root.AddComponent<LootCrate>();
        crate.crateTier = tier;
        crate.lootTable = table;
        crate.guaranteedTable = guaranteed;
        crate.lidHinge = hinge;
        crate.addGlow = false;
        return crate;
    }
}