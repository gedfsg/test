using UnityEngine;

/// <summary>
/// PickupItem이 붙은 오브젝트에 같이 붙이면, 플레이어가 이 반경 안에 들어오는 순간
/// F키 없이 자동으로 주워짐. PickupItem 자체는 그대로 두고(F키 프롬프트/글로우 등 재사용),
/// 여기서는 거리만 체크해서 자동 획득 트리거만 추가함.
///
/// AmmoData는 "amount"를 탄 개수 그대로(배수 아님)로 취급해서 AmmoInventory에 직접 더함 -
/// 좀비 드랍처럼 "5~10발" 같은 랜덤 개수를 AmmoData 에셋(ammoAmount 고정값)과 무관하게
/// 주려면 이 방식이 필요함 (일반 PickupItem.AddItem 경로는 ammoAmount*amount로 계산함).
/// </summary>
[RequireComponent(typeof(PickupItem))]
public class AutoPickupRadius : MonoBehaviour
{
    public float radius = 2f;

    private PickupItem pickup;
    private Transform player;
    private bool collected;

    void Start()
    {
        pickup = GetComponent<PickupItem>();
        var playerGo = GameObject.FindGameObjectWithTag("Player");
        if (playerGo != null) player = playerGo.transform;
    }

    void Update()
    {
        if (collected || player == null || pickup.itemData == null) return;
        if (Vector3.Distance(transform.position, player.position) > radius) return;

        Collect();
    }

    void Collect()
    {
        bool picked;

        if (pickup.itemData is AmmoData ad)
        {
            AmmoInventory.Instance?.AddAmmo(ad.weaponType, pickup.amount);
            PickupFeedbackUI.Instance?.Show($"+{pickup.amount} {AmmoLabel(ad.weaponType)}");
            picked = true;
        }
        else
        {
            var inventory = player.GetComponent<InventoryManager>();
            picked = inventory != null && inventory.AddItem(pickup.itemData, pickup.amount);
            if (picked) PickupFeedbackUI.Instance?.Show($"+{pickup.amount} {pickup.itemData.itemName}");
        }

        if (!picked) return;

        collected = true;
        Destroy(gameObject);
    }

    static string AmmoLabel(WeaponType type) => type switch
    {
        WeaponType.AR      => "AR탄",
        WeaponType.Pistol  => "권총탄",
        WeaponType.Shotgun => "샷건탄",
        WeaponType.Sniper  => "저격탄",
        _ => "탄약"
    };
}
