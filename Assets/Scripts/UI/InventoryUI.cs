using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem; 

public class InventoryUI : MonoBehaviour
{
    [Header("References")]
    public InventoryManager inventoryManager;
    public GameObject inventoryWindow;
    public Transform slotGrid;
    public GameObject slotPrefab;

    public bool isInventoryOpen = false;
    
    private PlayerInputActions inputActions;

    void Awake()
    {
        // 스크립트가 깨어날 때 인풋 액션 객체를 생성함
        inputActions = new PlayerInputActions();
    }

    void OnEnable()
    {
        // UI가 활성화될 때 입력을 켜고, 탭(Inventory) 키 이벤트에 함수를 구독시킴
        inputActions.Enable();
        inputActions.Player.Inventory.performed += OnInventoryPerformed;
    }

    void OnDisable()
    {
        // UI가 비활성화될 때 입력을 끄고, 구독을 안전하게 해제함
        inputActions.Disable();
        inputActions.Player.Inventory.performed -= OnInventoryPerformed;
    }

    private void OnInventoryPerformed(InputAction.CallbackContext context)
    {
        ToggleInventory();
    }

    public void ToggleInventory()
    {
        isInventoryOpen = !isInventoryOpen;
        inventoryWindow.SetActive(isInventoryOpen);

        if (isInventoryOpen)
        {
            UpdateUI();
        }
    }

    public void UpdateUI()
{
    foreach (Transform child in slotGrid)
        Destroy(child.gameObject);

    // 빈 슬롯도 maxCapacity만큼 전부 생성
    for (int i = 0; i < inventoryManager.maxCapacity; i++)
    {
        GameObject newSlot = Instantiate(slotPrefab, slotGrid);

        // 슬롯 배경색 — 다크 톤
        Image slotBg = newSlot.GetComponent<Image>();
        if (slotBg != null)
            slotBg.color = new Color(0.12f, 0.14f, 0.17f, 1f);

        Image icon = newSlot.transform.Find("Icon").GetComponent<Image>();
        TextMeshProUGUI amountText = newSlot.transform.Find("AmountText").GetComponent<TextMeshProUGUI>();

        if (i < inventoryManager.inventory.Count)
        {
            InventorySlot slotData = inventoryManager.inventory[i];

            // 아이템 있는 슬롯은 살짝 밝게
            if (slotBg != null)
                slotBg.color = new Color(0.18f, 0.22f, 0.27f, 1f);

            if (slotData.item.icon != null)
            {
                icon.sprite = slotData.item.icon;
                icon.color = Color.white;
                icon.enabled = true;
            }
            else
            {
                icon.enabled = false;
            }

            amountText.text = slotData.amount > 1 ? slotData.amount.ToString() : "";
            amountText.color = new Color(0.89f, 0.73f, 0.43f, 1f); // 황금색
        }
        else
        {
            // 빈 슬롯
            icon.enabled = false;
            amountText.text = "";
        }
    }
}
}