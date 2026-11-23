using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// (2026-11-23) 시작 장비: 권총 1자루(핫바 1번, 탄창 가득) + 예비 권총탄 90발.
/// 전시회에서 처음 해 본 사람들이 도끼 하나로 시작해 너무 어려워해서 추가.
///  - 게임 씬(PlayerController + ZombieSpawner가 있는 씬)이 열릴 때마다 자동으로 생기고, 지급하면 스스로 사라진다
///  - 권총은 루팅 데이터(LootDatabase)에 있는 권총 중 가장 낮은 등급 (상점 전용 특수 무기 제외)
///  - 핫바 1번이 이미 차 있으면 총은 넣지 않고 탄약만 준다
/// </summary>
public class StarterKit : MonoBehaviour
{
    public const int StartPistolAmmo = 90;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryCreate();
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) => TryCreate();

    static void TryCreate()
    {
        if (FindFirstObjectByType<PlayerController>() == null) return;
        if (FindFirstObjectByType<ZombieSpawner>(FindObjectsInactive.Include) == null) return;   // 게임 씬에서만 (메인 메뉴·학습장 제외)
        if (FindFirstObjectByType<StarterKit>() != null) return;
        new GameObject("StarterKit").AddComponent<StarterKit>();
    }

    IEnumerator Start()
    {
        // 핫바·탄약 창고가 준비될 때까지 기다림 (최대 5초)
        float waited = 0f;
        while ((WeaponHotbarUI.Instance == null || AmmoInventory.Instance == null) && waited < 5f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        // 핫바의 Start(시작 도끼 배치)가 끝난 뒤에 넣도록 두 프레임 더
        yield return null;
        yield return null;
        Give();
        Destroy(gameObject);
    }

    void Give()
    {
        var hotbar = WeaponHotbarUI.Instance;
        var ammo = AmmoInventory.Instance;
        if (hotbar == null) { Debug.LogWarning("[StarterKit] 핫바를 못 찾아 시작 권총을 못 줬습니다"); return; }

        var pistol = FindStarterPistol();
        if (pistol == null) Debug.LogWarning("[StarterKit] 루팅 데이터에서 권총을 못 찾았습니다");
        else if (hotbar.GetItem(0) == null && hotbar.SetSlot(0, pistol, pistol.maxAmmo))
            hotbar.SelectSlot(0);   // 시작부터 권총을 든 상태

        if (ammo != null) ammo.AddAmmo(WeaponType.Pistol, StartPistolAmmo);
    }

    /// <summary>루팅 데이터의 권총 중 가장 낮은 등급 (같으면 이름순 → 매번 같은 총)</summary>
    static WeaponData FindStarterPistol()
    {
        WeaponData best = null;
        foreach (var it in ShopStock.CollectItems())
        {
            if (!(it is WeaponData w) || w.type != WeaponType.Pistol || w.specialKind != SpecialWeaponKind.None) continue;
            if (best == null || (int)w.rarity < (int)best.rarity
                || ((int)w.rarity == (int)best.rarity && string.CompareOrdinal(w.itemName, best.itemName) < 0))
                best = w;
        }
        return best;
    }
}
