using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 마우스를 따라다니는 크로스헤어. 총구에서 조준점까지의 거리에 따라 색이 바뀐다.
///
/// 구간 (고정 거리 기준, 인스펙터에서 조정):
///   - nearDistance 이내          : 빨강 (가까움)
///   - nearDistance ~ midDistance : 노랑 (중간)
///   - midDistance ~ 유효 사거리   : 흰색 (멀리)
///   - 유효 사거리 초과            : 회색 (총알이 소멸해 닿지 않음)
///
/// 예전엔 "사거리의 절반/사거리"(AR 기준 20m/40m)로 나눴는데, 탑뷰 카메라 화면 안에서
/// 조준할 수 있는 최대 거리가 실내 약 10m, 실외 약 12m라 구간 경계에 마우스가 닿지 않아
/// 색이 항상 같게 보였음. 그래서 화면 안에서 구분되는 고정 거리로 바꿈.
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

    [Header("구간 거리 (m, 총구 기준)")]
    [Tooltip("이 거리 이내면 '가까움'(빨강).")]
    public float nearDistance = 4f;

    [Tooltip("이 거리 이내면 '중간'(노랑). 그 너머 ~ 유효 사거리는 '멀리'(흰색).")]
    public float midDistance = 8f;

    [Header("구간별 색")]
    [Tooltip("가까움.")]
    public Color nearColor = new Color(0.90f, 0.20f, 0.20f, 1f);

    [Tooltip("중간.")]
    public Color midColor = new Color(0.95f, 0.85f, 0.20f, 1f);

    [Tooltip("멀리 (유효 사거리 이내).")]
    public Color farColor = Color.white;

    [Tooltip("유효 사거리 밖 (총알이 닿지 않음).")]
    public Color beyondRangeColor = new Color(0.5f, 0.5f, 0.5f, 0.8f);

    [Tooltip("무기를 들고 있지 않을 때의 색.")]
    public Color noWeaponColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);

    [Tooltip("켜면 구간이 바뀔 때마다 콘솔에 거리/구간을 출력 (디버그용).")]
    public bool debugLog = false;

    [Header("옵션")]
    [Tooltip("켜면 시스템 마우스 커서를 숨긴다.")]
    public bool hideSystemCursor = true;

    [Tooltip("색이 바뀔 때 부드럽게 섞이는 속도. 0이면 즉시 바뀐다.")]
    public float colorLerpSpeed = 12f;

    [Header("커서 모드")]
    [Tooltip("인벤토리가 열리면 조준선을 숨기고 일반 화살표 커서, 아이템 드래그 중엔 꽉 쥔 손 커서")]
    public bool switchCursorForInventory = true;

    enum CursorMode { Crosshair, Arrow, Grab }
    private CursorMode cursorMode = CursorMode.Crosshair;
    private InventoryUI inventoryUI;
    private static Texture2D grabCursor;

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

        inventoryUI = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
    }

    void OnDisable()
    {
        // 크로스헤어가 꺼지면 커서를 되돌려준다. 안 그러면 커서가 사라진 채로 남는다.
        if (hideSystemCursor) Cursor.visible = true;
        if (cursorMode == CursorMode.Grab) Cursor.SetCursor(null, Vector2.zero, UnityEngine.CursorMode.Auto);
        cursorMode = CursorMode.Crosshair;
    }

    void Update()
    {
        if (crosshairRect == null || mainCamera == null) return;
        if (Mouse.current == null) return;

        // 화면 좌표는 그대로 쓴다. Overlay 캔버스라 스크린 좌표가 곧 UI 좌표다.
        crosshairRect.position = Mouse.current.position.ReadValue();

        UpdateCursorMode();
        UpdateColor();
    }

    /// <summary>평소 = 조준선(시스템 커서 숨김) / 인벤토리 열림 = 화살표 / 드래그 중 = 꽉 쥔 손. 바뀔 때만 적용</summary>
    private void UpdateCursorMode()
    {
        if (!switchCursorForInventory) return;

        CursorMode want = CursorMode.Crosshair;
        if (SlotDragDrop.IsDragging) want = CursorMode.Grab;
        else if (inventoryUI != null && inventoryUI.isInventoryOpen) want = CursorMode.Arrow;
        if (want == cursorMode) return;
        cursorMode = want;

        if (crosshairGraphic != null) crosshairGraphic.enabled = want == CursorMode.Crosshair;
        switch (want)
        {
            case CursorMode.Crosshair:
                Cursor.SetCursor(null, Vector2.zero, UnityEngine.CursorMode.Auto);
                Cursor.visible = !hideSystemCursor;
                break;
            case CursorMode.Arrow:
                Cursor.SetCursor(null, Vector2.zero, UnityEngine.CursorMode.Auto); // OS 기본 화살표
                Cursor.visible = true;
                break;
            case CursorMode.Grab:
                var tex = GetGrabCursor();
                Cursor.SetCursor(tex, new Vector2(tex.width / 2f, tex.height / 2f), UnityEngine.CursorMode.Auto);
                Cursor.visible = true;
                break;
        }
    }

    /// <summary>
    /// 꽉 쥔 손 커서 (32x32)를 코드로 그림: 손바닥(둥근 사각형) + 위쪽 손가락 마디 4개 + 왼쪽 엄지.
    /// 흰 채움 + 검정 테두리 2px + 마디 사이 회색 선. (에셋 파일 없이 쓰려고)
    /// </summary>
    private static Texture2D GetGrabCursor()
    {
        if (grabCursor != null) return grabCursor;
        const int N = 32;
        bool Inside(float x, float y) // y는 위→아래
        {
            // 손바닥: x 8~26, y 12~28, 모서리 반경 4
            float cx = Mathf.Clamp(x, 12f, 22f), cy = Mathf.Clamp(y, 16f, 24f);
            if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= 16f) return true;
            // 손가락 마디 4개 (위쪽 동그란 줄)
            for (int i = 0; i < 4; i++) { float fx = 10.5f + i * 4.6f, fy = 10.5f; if ((x - fx) * (x - fx) + (y - fy) * (y - fy) <= 9.5f) return true; }
            // 엄지 (왼쪽 타원)
            float tx = (x - 7.5f) / 3.2f, ty = (y - 20f) / 4.5f;
            return tx * tx + ty * ty <= 1f;
        }
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "GrabCursor" };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                Color32 c = new Color32(0, 0, 0, 0);
                if (Inside(x + 0.5f, y + 0.5f))
                {
                    bool edge = false; // 2px 안에 바깥이 있으면 테두리
                    for (int dy = -2; dy <= 2 && !edge; dy++)
                        for (int dx = -2; dx <= 2 && !edge; dx++)
                            if (Mathf.Abs(dx) + Mathf.Abs(dy) <= 2 && !Inside(x + dx + 0.5f, y + dy + 0.5f)) edge = true;
                    bool crease = y >= 9 && y <= 14 && (x == 13 || x == 17 || x == 22); // 마디 사이 선
                    c = edge ? new Color32(20, 20, 20, 255) : crease ? new Color32(120, 120, 120, 255) : new Color32(255, 255, 255, 255);
                }
                px[(N - 1 - y) * N + x] = c; // 텍스처는 아래→위
            }
        tex.SetPixels32(px);
        tex.Apply();
        return grabCursor = tex;
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

        // 총알이 실제로 출발하는 지점. 없으면 플레이어 위치로 대체한다.
        Vector3 origin = weapon.firePoint != null
            ? weapon.firePoint.position
            : (playerController != null ? playerController.transform.position : Vector3.zero);

        if (!GameUtils.GetMouseWorldPosition(mainCamera, origin.y, out Vector3 aimPoint))
            return farColor;

        float distance = Vector3.Distance(origin, aimPoint);

        string zone;
        Color color;
        if (range > 0f && distance > range) { zone = "사거리 밖"; color = beyondRangeColor; }
        else if (distance <= nearDistance)  { zone = "가까움"; color = nearColor; }
        else if (distance <= midDistance)   { zone = "중간"; color = midColor; }
        else                                { zone = "멀리"; color = farColor; }

        if (debugLog && zone != lastZone)
            Debug.Log($"[CrosshairUI] {zone} — 거리 {distance:F1}m (사거리 {range}m)");
        lastZone = zone;

        return color;
    }

    private string lastZone;

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