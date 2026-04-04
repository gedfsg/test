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

    private bool isInventoryOpen = false;
    
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
        {
            Destroy(child.gameObject);
        }

        for (int i = 0; i < inventoryManager.inventory.Count; i++)
        {
            InventorySlot slotData = inventoryManager.inventory[i];
            
            GameObject newSlot = Instantiate(slotPrefab, slotGrid);

            Image icon = newSlot.transform.Find("Icon").GetComponent<Image>();
            TextMeshProUGUI amountText = newSlot.transform.Find("AmountText").GetComponent<TextMeshProUGUI>();

            if (slotData.item.icon != null)
            {
                icon.sprite = slotData.item.icon;
                icon.enabled = true;
            }
            else
            {
                icon.enabled = false;
            }

            if (slotData.amount > 1)
            {
                amountText.text = slotData.amount.ToString();
            }
            else
            {
                amountText.text = "";
            }
        }
    }
}