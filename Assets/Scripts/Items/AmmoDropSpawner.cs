using UnityEngine;

/// <summary>
/// 좀비가 죽을 때 탄약을 월드에 떨어뜨리는 용도. 작은 박스 메시(프리미엄 큐브, 타입별 색)를
/// 만들고 Rigidbody로 살짝 튀어오르게 한 뒤, PickupItem(F키/프롬프트/글로우 재사용) +
/// AutoPickupRadius(반경 2m 자동 획득)를 붙인다. 별도 메시 에셋 필요 없음.
/// (2026-11-18) AmmoData.worldModel이 있으면(탄종별 탄약 상자 프리팹, Tools/Items/Build Ammo Box Prefabs) 큐브 대신 그 모델.
///   튀어오르기·물리 콜라이더·F키 트리거·자동 획득은 그대로. 바닥 외곽선은 PickupItem이 흰색(일반 등급)으로 붙임
/// </summary>
public static class AmmoDropSpawner
{
    public static GameObject Spawn(Vector3 position, AmmoData ammoData, int amount)
    {
        if (ammoData == null || amount <= 0) return null;

        GameObject go;
        float triggerSize = 2.2f;
        if (ammoData.worldModel != null)
        {
            // (2026-11-18) 탄약 상자 모델: 루트(스케일 1)에 물리 박스 콜라이더를 모델 크기에 맞춰 붙임
            go = new GameObject(ammoData.itemName + "_Drop");
            go.transform.position = position + Vector3.up * 0.4f;
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            var model = Object.Instantiate(ammoData.worldModel, go.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            var box = go.AddComponent<BoxCollider>();
            Bounds b = default; bool first = true;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                // 로컬 공간 크기 (루트 기준)
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                Vector3 c = go.transform.InverseTransformPoint(r.transform.TransformPoint(mb.center));
                Vector3 sz = Vector3.Scale(mb.size, r.transform.lossyScale);
                if (first) { b = new Bounds(c, sz); first = false; } else b.Encapsulate(new Bounds(c, sz));
            }
            if (!first) { box.center = b.center; box.size = b.size; }
            triggerSize = 2.2f * 0.22f; // 아래에서 루트 스케일(1) 기준으로 그대로 쓰도록 (큐브는 스케일 0.22라 2.2 × 0.22 = 실제 0.48m)
        }
        else
        {
        // (이전·대체) 탄약 상자 모델이 없을 때: 탄종별 색 큐브
        go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = ammoData.itemName + "_Drop";
        go.transform.position = position + Vector3.up * 0.4f;
        go.transform.localScale = Vector3.one * 0.22f;

        var renderer = go.GetComponent<Renderer>();
        renderer.material = new Material(Shader.Find("Standard"));
        renderer.material.color = ColorFor(ammoData.weaponType);

        // CreatePrimitive가 붙여준 BoxCollider를 그대로 바닥 충돌(물리)용으로 사용.
        var col = go.GetComponent<BoxCollider>();
        col.isTrigger = false;
        }

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
        // trigger.size = Vector3.one * 2.2f; // (이전) 큐브 스케일 0.22 기준 → 실제 약 0.48m
        trigger.size = Vector3.one * triggerSize;

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
