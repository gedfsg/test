using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 미션 상호작용 지점 (비상 무전기 / 발전기 / 무기고 / 연료통 / 바리케이드 설치 지점).
/// 플레이어가 radius 안에서 F키를 holdSeconds 동안 누르고 있으면 완료 → onDone.
/// 떼거나 멀어지면 처음부터. 머리 위 안내 문구 + 진행 %, 표시등(빨강 깜빡임 → 완료 시 초록).
/// MissionManager가 MissionProps로 모양을 만든 뒤 AddComponent로 붙임.
/// </summary>
public class MissionInteractable : MonoBehaviour
{
    public string actionText = "상호작용";   // "무전기 가동" 등
    public float holdSeconds = 2f;
    public float radius = 2.4f;
    public float promptHeight = 2.2f;
    public System.Action onDone;
    public Light beacon;                        // 깜빡이는 표시등 (없어도 됨)

    public bool Done { get; private set; }

    Transform player;
    TextMeshPro prompt;
    float held;

    static readonly Color WaitColor = new Color(1f, 0.25f, 0.15f);
    static readonly Color DoneColor = new Color(0.3f, 1f, 0.45f);

    void Start()
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;

        // 머리 위 안내 문구 (항상 카메라를 향함)
        var go = new GameObject("Prompt");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * promptHeight;
        go.AddComponent<FaceCamera>();
        prompt = go.AddComponent<TextMeshPro>();
        prompt.font = HudStyle.SemiBold;
        prompt.fontSize = 2.4f;
        prompt.alignment = TextAlignmentOptions.Center;
        prompt.textWrappingMode = TextWrappingModes.NoWrap;
        prompt.rectTransform.sizeDelta = new Vector2(8f, 1f);
        prompt.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        prompt.gameObject.SetActive(false);
        if (beacon != null) beacon.color = WaitColor;
    }

    void Update()
    {
        if (beacon != null && !Done) beacon.intensity = 1.2f + Mathf.Sin(Time.time * 5f) * 0.8f;
        if (Done || player == null) return;

        Vector3 d = player.position - transform.position;
        bool inRange = Mathf.Abs(d.y) < 2.5f && new Vector2(d.x, d.z).sqrMagnitude <= radius * radius;
        prompt.gameObject.SetActive(inRange);
        if (!inRange) { held = 0f; return; }

        bool holding = Keyboard.current != null && Keyboard.current.fKey.isPressed;
        held = holding ? held + Time.deltaTime : 0f;
        if (held <= 0f)
        {
            prompt.text = $"<color=#FFA500>[F]</color> 길게 눌러 {actionText}";
            prompt.color = Color.white;
            return;
        }

        float t = Mathf.Clamp01(held / Mathf.Max(0.1f, holdSeconds));
        prompt.text = $"{actionText} 중... {Mathf.FloorToInt(t * 100f)}%";
        if (t < 1f) return;

        Done = true;
        prompt.gameObject.SetActive(false);
        if (beacon != null) { beacon.color = DoneColor; beacon.intensity = 1.5f; }
        onDone?.Invoke();
    }
}
