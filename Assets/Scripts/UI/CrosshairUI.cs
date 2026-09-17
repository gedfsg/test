using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 마우스를 따라다니는 크로스헤어. 총구에서 조준점까지의 거리에 따라 색이 바뀐다.
///
/// 구간은 Bullet.cs의 사거리 처리와 같은 기준을 쓴다.
///   - 유효 사거리의 절반 이내 : 데미지 100%
///   - 절반 ~ 유효 사거리      : 데미지 50%
///   - 유효 사거리 초과        : 총알이 소멸해 닿지 않음
///
/// Canvas(Screen Space - Overlay) 밑에 Image를 하나 만들고 이 컴포넌트를 붙인 뒤,
/// crosshairRect와 crosshairGraphic에 그 Image를 연결하면 된다.
/// </summary>
public class CrosshairUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("마우스를 따라 움직일 크로스헤어의 RectTransform.")]
    public RectTransform crosshairRect;

    [Tooltip("색을 바꿀 대상. 보통 위와 같은 오브젝트의 Image.")]
    public Graphic crosshairGraphic;

    [Tooltip("비워두면 Player 태그에서 자동으로 찾는다.")]
    public PlayerController playerController;

    [Header("구간별 색")]
    [Tooltip("데미지가 온전히 들어가는 거리.")]
    public Color inRangeColor = Color.white;

    [Tooltip("데미지가 절반으로 줄어드는 거리.")]
    public Color falloffColor = new Color(0.95f, 0.75f, 0.25f, 1f);

    [Tooltip("총알이 닿지 않는 거리.")]
    public Color outOfRangeColor = new Color(0.85f, 0.25f, 0.20f, 1f);

    [Tooltip("무기를 들고 있지 않을 때의 색.")]
    public Color noWeaponColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);

    [Header("옵션")]
    [Tooltip("켜면 시스템 마우스 커서를 숨긴다.")]
    public bool hideSystemCursor = true;

    [Tooltip("색이 바뀔 때 부드럽게 섞이는 속도. 0이면 즉시 바뀐다.")]
    public float colorLerpSpeed = 12f;

    private Camera mainCamera;
    private Color targetColor;

    void Start()
    {
        mainCamera = Camera.main;

        if (playerController == null)
            playerController = GameObject.FindWithTag("Player")?.GetComponent<PlayerController>();

        if (hideSystemCursor) Cursor.visible = false;

        targetColor = noWeaponColor;
        if (crosshairGraphic != null) crosshairGraphic.color = targetColor;
    }

    void OnDisable()
    {
        // 크로스헤어가 꺼지면 커서를 되돌려준다. 안 그러면 커서가 사라진 채로 남는다.
        if (hideSystemCursor) Cursor.visible = true;
    }

    void Update()
    {
        if (crosshairRect == null || mainCamera == null) return;
        if (Mouse.current == null) return;

        // 화면 좌표는 그대로 쓴다. Overlay 캔버스라 스크린 좌표가 곧 UI 좌표다.
        crosshairRect.position = Mouse.current.position.ReadValue();

        UpdateColor();
    }

    private void UpdateColor()
    {
        if (crosshairGraphic == null) return;

        targetColor = EvaluateColor();

        crosshairGraphic.color = colorLerpSpeed > 0f
            ? Color.Lerp(crosshairGraphic.color, targetColor, colorLerpSpeed * Time.deltaTime)
            : targetColor;
    }

    /// <summary>총구에서 조준점까지의 거리를 재서 해당 구간의 색을 고른다.</summary>
    private Color EvaluateColor()
    {
        Weapon weapon = GetActiveWeapon();
        if (weapon == null || weapon.weaponData == null) return noWeaponColor;

        float range = weapon.weaponData.effectiveRange;
        if (range <= 0f) return inRangeColor;

        // 총알이 실제로 출발하는 지점. 없으면 플레이어 위치로 대체한다.
        Vector3 origin = weapon.firePoint != null
            ? weapon.firePoint.position
            : (playerController != null ? playerController.transform.position : Vector3.zero);

        if (!GameUtils.GetMouseWorldPosition(mainCamera, origin.y, out Vector3 aimPoint))
            return inRangeColor;

        float distance = Vector3.Distance(origin, aimPoint);

        if (distance > range) return outOfRangeColor;
        if (distance > range * 0.5f) return falloffColor;
        return inRangeColor;
    }

    /// <summary>현재 들고 있는 총. 없으면 null.</summary>
    private Weapon GetActiveWeapon()
    {
        // AmmoUI가 이미 현재 무기를 참조하고 있으므로 그대로 쓴다.
        Weapon fromAmmoUI = AmmoUI.Instance != null ? AmmoUI.Instance.targetRangedWeapon : null;
        if (fromAmmoUI != null && fromAmmoUI.gameObject.activeInHierarchy) return fromAmmoUI;

        if (playerController == null) return null;

        // 대비책: 자식에서 활성 무기를 찾는다.
        Weapon[] weapons = playerController.GetComponentsInChildren<Weapon>(false);
        foreach (var w in weapons)
            if (w.gameObject.activeInHierarchy) return w;

        return null;
    }
}