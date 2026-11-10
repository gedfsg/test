using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 알약 모양 ON/OFF 스위치 (설정 - 화면 흔들림 / VSync). Toggle 값에 따라 손잡이 위치·색과 ON/OFF 글자를 바꿈
/// </summary>
[RequireComponent(typeof(Toggle))]
public class UIToggleSwitch : MonoBehaviour
{
    public Image track;
    public RectTransform knob;
    public Image knobImage;
    public TextMeshProUGUI stateLabel;
    public float knobTravel = 13f; // 가운데 기준 좌우 이동 (px)

    public Color onTrack = new Color32(0x6E, 0x55, 0x26, 0xFF);
    public Color offTrack = new Color32(0x2A, 0x2E, 0x35, 0xFF);
    public Color onKnob = new Color32(0xFF, 0xB3, 0x47, 0xFF);
    public Color offKnob = new Color32(0x8A, 0x8F, 0x98, 0xFF);
    public Color onText = new Color32(0xFF, 0xB3, 0x47, 0xFF);
    public Color offText = new Color32(0x9C, 0xA3, 0xAF, 0xFF);

    Toggle toggle;

    void Awake()
    {
        toggle = GetComponent<Toggle>();
        toggle.transition = Selectable.Transition.None;
        toggle.onValueChanged.AddListener(_ => { Refresh(); UIAudio.Play(UIAudio.Sound.Toggle); }); // SetIsOnWithoutNotify(값 채우기)는 소리 없음
    }

    void OnEnable() => Refresh();

    public void Refresh()
    {
        if (toggle == null) toggle = GetComponent<Toggle>();
        bool on = toggle.isOn;
        if (track != null) track.color = on ? onTrack : offTrack;
        if (knob != null) knob.anchoredPosition = new Vector2(on ? knobTravel : -knobTravel, 0f);
        if (knobImage != null) knobImage.color = on ? onKnob : offKnob;
        if (stateLabel != null) { stateLabel.text = on ? "ON" : "OFF"; stateLabel.color = on ? onText : offText; }
    }
}
