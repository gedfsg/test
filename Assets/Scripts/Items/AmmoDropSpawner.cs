using UnityEngine;

/// <summary>
/// 좀비가 죽을 때 탄약을 월드에 떨어뜨리는 용도. 작은 박스 메시(프리미엄 큐브, 타입별 색)를
/// 만들고 Rigidbody로 살짝 튀어오르게 한 뒤, PickupItem(F키/프롬프트/글로우 재사용) +
/// AutoPickupRadius(반경 2m 자동 획득)를 붙인다. 별도 메시 에셋 필요 없음.
/// </summary>
public static class AmmoDropSpawner
{
    public static GameObject Spawn(Vector3 position, AmmoData ammoData, int amount)
    {
        if (ammoData == null || amount <= 0) return null;

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = ammoData.itemName + "_Drop";
        go.transform.position = position + Vector3.up * 0.4f;
        go.transform.localScale = Vector3.one * 0.22f;

        var renderer = go.GetComponent<Renderer>();
        renderer.material = new Material(Shader.Find("Standard"));
        renderer.material.color = ColorFor(ammoData.weaponType);

        // CreatePrimitive가 붙여준 BoxCollider를 그대로 바닥 충돌(물리)용으로 사용.
        var col = go.GetComponent<BoxCollider>();
        col.isTrigger = false;

        var rb = go.AddComponent<Rigidbody>();
        rb.linearDamping = 0.5f;
        rb.AddForce(new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(3.5f, 5f), Random.Range(-1.5f, 1.5f)), ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * 2f, ForceMode.Impulse);

        var pickup = go.AddComponent<PickupItem>();
        pickup.itemData = ammoData;
        pickup.amount = amount; // AutoPickupRadius가 AmmoData는 amount를 "탄 개수 그대로"로 처리함

        // PickupItem이 쓰는 F키 감지용 트리거 콜리더를 별도로 추가 (물리용 col은 그대로 둠)
        var trigger = go.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = Vector3.one * 2.2f;

        go.AddComponent<AutoPickupRadius>();
        return go;
    }

    static Color ColorFor(WeaponType type) => type switch
    {
        WeaponType.AR      => new Color(0.32f, 0.38f, 0.2f),
        WeaponType.Pistol  => new Color(0.22f, 0.22f, 0.25f),
        WeaponType.Shotgun => new Color(0.55f, 0.28f, 0.08f),
        WeaponType.Sniper  => new Color(0.15f, 0.18f, 0.28f),
        _ => Color.gray
    };
}
