using UnityEngine;

public class PickupItem : MonoBehaviour
{
    [Header("Item Data")]
    public ItemData itemData;
    public int amount = 1;


    void Start()
    {
        // 플레이어 콜라이더 충돌 무시
        Collider playerCollider = GameObject.FindGameObjectWithTag("Player").GetComponent<Collider>();
        Collider boxCollider = GetComponent<BoxCollider>();
        if (playerCollider != null && boxCollider != null)
            Physics.IgnoreCollision(playerCollider, boxCollider);

        // 글로우 아웃라인 추가
        if (GetComponent<ItemOutlineGlow>() == null)
            gameObject.AddComponent<ItemOutlineGlow>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerController player = other.GetComponent<PlayerController>();
            if (player != null)
                player.SetNearbyItem(this);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerController player = other.GetComponent<PlayerController>();
            if (player != null)
                player.ClearNearbyItem(this);
        }
    }
}