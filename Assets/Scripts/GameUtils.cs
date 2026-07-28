using UnityEngine;

public static class GameUtils
{
    public static bool GetMouseWorldPosition(Camera cam, float targetY, out Vector3 result)
    {
        result = Vector3.zero;
        Ray ray = cam.ScreenPointToRay(UnityEngine.InputSystem.Mouse.current.position.ReadValue());

        float denom = ray.direction.y;
        if (Mathf.Abs(denom) < 0.0001f) return false;

        float t = (targetY - ray.origin.y) / denom;
        if (t < 0) return false;

        result = ray.origin + ray.direction * t;
        return true;
    }

    /// <summary>
    /// 지면 높이와의 교차점을 계산하는 대신, 카메라 화면 좌표에서 "캐릭터가 있는 화면 위치"와
    /// "마우스 화면 위치"의 차이만으로 조준 방향을 구한다. 다른 탑다운 슈팅 게임들이 흔히
    /// 쓰는 방식으로, 월드 좌표 레이캐스트 방식과 달리 캐릭터와 마우스 사이의 실제 거리나
    /// 높이 기준값에 전혀 의존하지 않아서, 마우스가 캐릭터 바로 위에 있어도 방향이 갑자기
    /// 튀거나 떨리는 문제가 생기지 않는다.
    /// </summary>
    /// <param name="cam">기준 카메라</param>
    /// <param name="originWorldPos">방향을 계산할 기준이 되는 월드 좌표 (예: 캐릭터 위치)</param>
    /// <param name="direction">계산된 조준 방향 (수평, Y=0, 정규화됨)</param>
    public static bool GetMouseAimDirection(Camera cam, Vector3 originWorldPos, out Vector3 direction)
    {
        direction = Vector3.forward;
        if (cam == null || UnityEngine.InputSystem.Mouse.current == null) return false;

        Vector3 originScreen = cam.WorldToScreenPoint(originWorldPos);
        Vector2 mouseScreen = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
        Vector2 screenDelta = mouseScreen - new Vector2(originScreen.x, originScreen.y);

        // 마우스가 캐릭터의 화면 좌표와 완전히 겹치는(사실상 있을 수 없는) 경우에만 방향을 못 구함
        if (screenDelta.sqrMagnitude < 0.01f) return false;

        // 카메라가 기울어져 있어도(탑다운/쿼터뷰 모두), 카메라의 좌우/전후 방향을
        // 지면에 투영한 벡터를 기준으로 스크린 델타를 월드 방향으로 환산한다.
        Vector3 camForward = cam.transform.forward;
        camForward.y = 0f;
        camForward.Normalize();

        Vector3 camRight = cam.transform.right;
        camRight.y = 0f;
        camRight.Normalize();

        direction = (camRight * screenDelta.x + camForward * screenDelta.y).normalized;
        return true;
    }
}