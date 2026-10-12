using UnityEngine;

/// <summary>
/// 아이템을 월드 바닥에 놓음 (상자/건물 루팅/좀비 드랍 공용).
///  - 탄약: 기존 AmmoDropSpawner 그대로 (튀어오르는 박스 + 2m 자동 획득, amount = 탄 개수)
///  - 무기: 무기 모델을 바닥에 눕혀서 놓음
///  - 그 외: 아이템 아이콘을 카메라를 향한 판으로 띄움
/// 모두 PickupItem(F키 획득/프롬프트)을 붙임. 바닥 높이는 아래로 레이캐스트해서 맞춤.
/// </summary>
public static class WorldItemDrop
{
    public static GameObject Spawn(Vector3 position, ItemData item, int amount)
    {
        if (item == null || amount <= 0) return null;
        if (item is AmmoData ammo) return AmmoDropSpawner.Spawn(position, ammo, amount);

        Vector3 ground = FindGround(position);
        var go = new GameObject(item.itemName + "_Drop");
        go.transform.position = ground + Vector3.up * 0.3f;

        if (item is WeaponData wd && wd.weaponPrefab != null)
        {
            // 무기 모델을 옆으로 눕힘 (총구 방향은 랜덤)
            var model = Object.Instantiate(wd.weaponPrefab, go.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 90f);
        }
        else if (item.icon != null)
        {
            var icon = new GameObject("Icon");
            icon.transform.SetParent(go.transform, false);
            icon.transform.localScale = Vector3.one * 0.6f / Mathf.Max(0.01f, item.icon.bounds.size.x);
            var sr = icon.AddComponent<SpriteRenderer>();
            sr.sprite = item.icon;
            icon.AddComponent<FaceCamera>();
        }

        var pickup = go.AddComponent<PickupItem>();
        pickup.itemData = item;
        pickup.amount = item is WeaponData ? 1 : amount;

        // PickupItem의 F키 감지용. BoxCollider로 하면 PickupItem.Start의 IgnoreCollision(플레이어, BoxCollider)이
        // 이 트리거에 걸려서 트리거 이벤트까지 꺼짐 → Sphere로
        var trigger = go.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 1f;
        return go;
    }

    static Vector3 FindGround(Vector3 p)
    {
        if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
            return hit.point;
        return p;
    }
}
