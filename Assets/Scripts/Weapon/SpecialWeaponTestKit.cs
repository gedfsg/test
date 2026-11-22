using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// (2026-11-22) 테스트용: Ctrl+Shift+K → 특수 무기 5종 바로 지급 (에디터·개발 빌드에서만, 실제 빌드에선 동작 안 함).
/// 평소엔 상점에서만 구할 수 있어서(상인 30% 확률로 한 자루) 첫날에 전부 써 보기 어려움.
///  - 핫바 1번 화염방사기 / 2번 미니건 / 4번 전기톱 (원래 있던 건 가방으로)
///  - 가방: RPG, 레이저 (탄 가득) → 인벤토리(I)에서 핫바 1·2번으로 끌어다 바꿔 들기
///  - 특수 탄약(공용, Ranged) 200발
/// 게임 씬이 열릴 때마다 플레이어에 자동으로 붙음.
/// </summary>
public class SpecialWeaponTestKit : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        if (!Application.isEditor && !Debug.isDebugBuild) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null && p.GetComponent<SpecialWeaponTestKit>() == null) p.AddComponent<SpecialWeaponTestKit>();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || !kb.kKey.wasPressedThisFrame || !kb.ctrlKey.isPressed || !kb.shiftKey.isPressed) return;
        Give();
    }

    public void Give()
    {
        var db = WeaponDatabase.Instance;
        var hb = WeaponHotbarUI.Instance;
        var inv = GetComponent<InventoryManager>();
        if (db == null || hb == null) { Debug.LogWarning("[TestKit] WeaponDatabase / 핫바 없음"); return; }

        WeaponData Find(SpecialWeaponKind k)
        {
            foreach (var w in db.shopWeapons) if (w != null && w.specialKind == k) return w;
            return null;
        }

        PutInHotbar(hb, inv, 0, Find(SpecialWeaponKind.Flamethrower));
        PutInHotbar(hb, inv, 1, Find(SpecialWeaponKind.Minigun));
        PutInHotbar(hb, inv, 3, Find(SpecialWeaponKind.Chainsaw));
        foreach (var k in new[] { SpecialWeaponKind.RPG, SpecialWeaponKind.Laser })
        {
            var w = Find(k);
            if (w != null && inv != null && !inv.AddItemWithAmmo(w, 1, w.maxAmmo)) Debug.LogWarning("[TestKit] 가방이 가득 차서 못 넣음: " + w.itemName);
        }
        if (AmmoInventory.Instance != null) AmmoInventory.Instance.AddAmmo(WeaponType.Ranged, 200);
        hb.SelectSlot(0);

        var ui = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (ui != null && ui.isInventoryOpen) ui.UpdateUI();
        PickupFeedbackUI.Instance?.Show("테스트: 특수 무기 5종 지급 (RPG·레이저는 가방)");
        Debug.Log("[TestKit] 특수 무기 5종 지급");
    }

    /// <summary>핫바 index 칸에 무기 (원래 있던 건 가방으로)</summary>
    static void PutInHotbar(WeaponHotbarUI hb, InventoryManager inv, int index, WeaponData w)
    {
        if (w == null) return;
        var (old, count) = hb.TakeSlot(index);
        if (old != null && inv != null)
        {
            if (old is WeaponData) inv.AddItemWithAmmo(old, 1, count);
            else inv.AddItem(old, Mathf.Max(1, count));
        }
        hb.PutSlot(index, w, w.maxAmmo);
    }
}
