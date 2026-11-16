using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;

    [Header("카메라 각도 (방향만 사용, 실제 거리는 distance가 결정)")]
    public Vector3 baseOffset = new Vector3(0f, 30f, -25f);

    // CameraZoom이 조절 (월드 단위 실제 거리)
    [HideInInspector] public float distance = 40f;

    // CameraRotate가 조절 (Q/E 스냅 회전, 도 단위)
    [HideInInspector] public float yawOffset = 0f;

    void LateUpdate()
    {
        if (target == null) return;

        // 오프셋 방향은 baseOffset이 정하고, 실제 거리는 distance, 좌우 회전은 yawOffset이 정함
        Vector3 direction = baseOffset.normalized;
        Vector3 rotatedOffset = Quaternion.Euler(0f, yawOffset, 0f) * (direction * distance);
        transform.position = target.position + rotatedOffset;

        // 항상 플레이어를 바라봄 → 줌/회전해도 캐릭터가 화면 중앙 유지
        transform.LookAt(target.position + Vector3.up * 1f);

        // 화면 흔들림 (2026-11-16, 수류탄 폭발). 설정에서 끄면 안 흔들림
        if (Time.time < shakeUntil && SettingsManager.ScreenShake)
        {
            float k = (shakeUntil - Time.time) / Mathf.Max(0.01f, shakeDuration); // 끝으로 갈수록 약하게
            transform.position += Random.insideUnitSphere * shakeMagnitude * k;
        }
    }

    static float shakeUntil, shakeDuration, shakeMagnitude;

    /// <summary>카메라 흔들기 (초, 세기 m). 겹치면 더 센 쪽</summary>
    public static void Shake(float duration, float magnitude)
    {
        if (Time.time >= shakeUntil) shakeMagnitude = 0f;
        shakeMagnitude = Mathf.Max(shakeMagnitude, magnitude);
        shakeDuration = duration;
        shakeUntil = Mathf.Max(shakeUntil, Time.time + duration);
    }
}
