using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DayNightCycle : MonoBehaviour
{
    [Header("시간 설정")]
    public float dayDuration = 240f;   // 낮 4분
    public float nightDuration = 300f; // 밤 5분
    public int totalDays = 7;

    [Header("라이팅")]
    public Light directionalLight;
    public Light spotLight;
    private bool flashlightOn = true;

    [Header("UI")]
    public TextMeshProUGUI dayText;

    private float currentTime = 0f;
    private int currentDay = 1;
    private bool isDay = true;
    private bool gameOver = false;

    void Start()
    {
        if (spotLight == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
                spotLight = player.GetComponentInChildren<Light>();
        }
    }

    void Update()
    {
        if (gameOver) return;

        if (UnityEngine.InputSystem.Keyboard.current.qKey.wasPressedThisFrame && spotLight != null)
        {
            flashlightOn = !flashlightOn;
            spotLight.enabled = flashlightOn;
        }

        currentTime += Time.deltaTime;
        float phaseDuration = isDay ? dayDuration : nightDuration;

        if (currentTime >= phaseDuration)
        {
            currentTime = 0f;

            if (!isDay) // 밤이 끝나면 다음 날
            {
                currentDay++;
                if (currentDay > totalDays)
                {
                    Debug.Log("7일 생존 성공!");
                    gameOver = true;
                    return;
                }
            }

            isDay = !isDay;
        }

        float ratio = currentTime / phaseDuration;
        UpdateUI();
    }


    void UpdateUI()
    {
        if (dayText != null)
            dayText.text = $"Day {currentDay} / {(isDay ? "낮" : "밤")}";
    }
}