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
}