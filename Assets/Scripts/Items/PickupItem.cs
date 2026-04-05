using UnityEngine;

public class PickupItem : MonoBehaviour
{
    [Header("Item Data")]
    public ItemData itemData; 
    public int amount = 1; // 바닥에 떨어져 있는 개수

    void OnTriggerEnter(Collider other)
    {
        Debug.Log("트리거 감지: " + other.name); // 이거 추가
        if (other.CompareTag("Player"))
        {
            PlayerController player = other.GetComponent<PlayerController>();
            if (player != null) 
            {
                player.SetNearbyItem(this);
                // 아이템 이름과 개수를 함께 표시함.
                Debug.Log(itemData.itemName + " " + amount + "개 획득 가능 (F 키)");
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerController player = other.GetComponent<PlayerController>();
            if (player != null) 
            {
                player.ClearNearbyItem(this);
            }
        }
    }

    void Start()
    {
    // 플레이어 콜라이더 찾아서 Box Collider랑 충돌 무시
        Collider playerCollider = GameObject.FindGameObjectWithTag("Player").GetComponent<Collider>();
        Collider boxCollider = GetComponent<BoxCollider>();
    
        if (playerCollider != null && boxCollider != null)
        {
            Physics.IgnoreCollision(playerCollider, boxCollider);
        }
    }
}