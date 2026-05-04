using UnityEngine;

public class RootMotionFix : MonoBehaviour
{
    private Transform parentTransform;

    void Start()
    {
        parentTransform = transform.parent;
    }

    void LateUpdate()
    {
        // 애니메이션이 자식 오브젝트 위치를 바꿔도
        // 부모(Player) 기준 로컬 위치를 0으로 고정
        transform.localPosition = Vector3.zero;
    }
}