using UnityEngine;

/// <summary>
/// 게임 시작 시 RangedWeapon을 캐릭터 오른손 뼈(hand.R)에 자동 부착
/// Player 오브젝트에 추가 후 Inspector에서 RangedWeapon 연결
/// </summary>
public class WeaponHandAttacher : MonoBehaviour
{
    [Header("무기 오브젝트")]
    public GameObject rangedWeapon;

    [Header("손 위치/회전 오프셋 (플레이 중 Inspector에서 실시간 조정 가능)")]
    public Vector3 positionOffset = new Vector3(0.04f, 0.02f, 0.08f);
    public Vector3 rotationOffset = new Vector3(-90f, 0f, 0f);

    const string RIGHT_HAND_BONE = "hand.R";

    void Start()
    {
        AttachToHand();
    }

    void AttachToHand()
    {
        if (rangedWeapon == null)
        {
            Debug.LogWarning("[WeaponHandAttacher] rangedWeapon이 비어있어요.");
            return;
        }

        Transform handBone = FindDeep(transform, RIGHT_HAND_BONE);
        if (handBone == null)
        {
            Debug.LogWarning($"[WeaponHandAttacher] '{RIGHT_HAND_BONE}' 뼈대를 찾지 못했어요.");
            return;
        }

        rangedWeapon.transform.SetParent(handBone);
        rangedWeapon.transform.localPosition = positionOffset;
        rangedWeapon.transform.localRotation = Quaternion.Euler(rotationOffset);

        Debug.Log($"[WeaponHandAttacher] {rangedWeapon.name} → {handBone.name} 부착 완료!");
    }

    public void ApplyOffset()
    {
        Transform handBone = FindDeep(transform, RIGHT_HAND_BONE);
        if (handBone != null && rangedWeapon != null &&
            rangedWeapon.transform.parent == handBone)
        {
            rangedWeapon.transform.localPosition = positionOffset;
            rangedWeapon.transform.localRotation = Quaternion.Euler(rotationOffset);
        }
    }

    Transform FindDeep(Transform parent, string boneName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == boneName) return child;
            Transform found = FindDeep(child, boneName);
            if (found != null) return found;
        }
        return null;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying) return;
        Transform handBone = FindDeep(transform, RIGHT_HAND_BONE);
        if (handBone != null && rangedWeapon != null &&
            rangedWeapon.transform.parent == handBone)
        {
            rangedWeapon.transform.localPosition = positionOffset;
            rangedWeapon.transform.localRotation = Quaternion.Euler(rotationOffset);
        }
    }
#endif
}