using UnityEngine;

/// <summary>항상 메인 카메라를 향하도록 회전</summary>
public class FaceCamera : MonoBehaviour
{
    void LateUpdate()
    {
        if (Camera.main == null) return;
        transform.forward = Camera.main.transform.forward;
    }
}
