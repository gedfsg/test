using TMPro;
using UnityEngine;

/// <summary>한국어 원문을 기억해 두고 언어가 바뀌면 글자를 바꿈 (Localization.Register가 붙임)</summary>
[DisallowMultipleComponent]
public class LocalizedText : MonoBehaviour
{
    [SerializeField] string korean;
    TMP_Text text;

    void Awake() => Init();

    void Init()
    {
        if (text != null) return;
        text = GetComponent<TMP_Text>();
        if (string.IsNullOrEmpty(korean) && text != null) korean = text.text;
    }

    void OnEnable()
    {
        Localization.Changed += Refresh;
        Refresh();
    }

    void OnDisable() => Localization.Changed -= Refresh;

    public void Refresh()
    {
        Init();
        if (text != null) text.text = Localization.T(korean);
    }
}
