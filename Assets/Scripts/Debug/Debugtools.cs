#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 개발 중 테스트용 단축키를 한곳에 모아둔 스크립트.
/// 씬의 아무 오브젝트(예: Player)에 붙여두고 쓰면 된다.
///
/// 에디터에서만 동작한다. 빌드에는 아무 영향이 없다.
///
/// 기본 키:
///   F1 - 피해 (잔상 체력바 확인용)
///   F2 - 회복
///   F3 - 탄약 지급 (네 종류 전부)
///   F4 - 지정한 아이템을 인벤토리에 지급
/// </summary>
public class DebugTools : MonoBehaviour
{
    [Header("피해 / 회복")]
    public float damageAmount = 15f;
    public float healAmount = 15f;

    [Header("탄약")]
    public int ammoAmount = 30;

    [Header("아이템 지급")]
    [Tooltip("F4로 지급할 아이템. 비워두면 아무 일도 일어나지 않는다.")]
    public ItemData itemToGive;
    public int itemAmount = 1;

    [Header("키 설정")]
    public Key damageKey = Key.F1;
    public Key healKey = Key.F2;
    public Key ammoKey = Key.F3;
    public Key itemKey = Key.F4;

    private Health playerHealth;
    private InventoryManager inventory;

    void Start()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) return;

        playerHealth = player.GetComponent<Health>();
        inventory = player.GetComponent<InventoryManager>();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb[damageKey].wasPressedThisFrame) Damage();
        if (kb[healKey].wasPressedThisFrame) Heal();
        if (kb[ammoKey].wasPressedThisFrame) GiveAmmo();
        if (kb[itemKey].wasPressedThisFrame) GiveItem();
    }

    private void Damage()
    {
        if (playerHealth == null) { Debug.LogWarning("[Debug] Health를 찾지 못했다."); return; }
        playerHealth.TakeDamage(damageAmount);
        Debug.Log($"[Debug] 피해 {damageAmount} — 남은 체력 {playerHealth.GetCurrentHealth()}");
    }

    private void Heal()
    {
        if (playerHealth == null) { Debug.LogWarning("[Debug] Health를 찾지 못했다."); return; }
        playerHealth.Heal(healAmount);
        Debug.Log($"[Debug] 회복 {healAmount} — 현재 체력 {playerHealth.GetCurrentHealth()}");
    }

    private void GiveAmmo()
    {
        if (AmmoInventory.Instance == null) { Debug.LogWarning("[Debug] AmmoInventory가 없다."); return; }

        AmmoInventory.Instance.AddAmmo(WeaponType.Pistol, ammoAmount);
        AmmoInventory.Instance.AddAmmo(WeaponType.AR, ammoAmount);
        AmmoInventory.Instance.AddAmmo(WeaponType.Shotgun, ammoAmount);
        AmmoInventory.Instance.AddAmmo(WeaponType.Sniper, ammoAmount);
        Debug.Log($"[Debug] 탄약 {ammoAmount}씩 지급");
    }

    private void GiveItem()
    {
        if (itemToGive == null) { Debug.LogWarning("[Debug] 지급할 아이템이 지정되지 않았다."); return; }
        if (inventory == null) { Debug.LogWarning("[Debug] InventoryManager를 찾지 못했다."); return; }

        inventory.AddItem(itemToGive, itemAmount);
        Debug.Log($"[Debug] {itemToGive.itemName} {itemAmount}개 지급");
    }
}
#endif