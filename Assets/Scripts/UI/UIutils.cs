using UnityEngine;

/// <summary>
/// UI 갱신에서 반복되는 처리를 모아둔 헬퍼.
/// </summary>
public static class UIUtils
{
    /// <summary>
    /// 컨테이너의 자식을 모두 제거한다.
    ///
    /// Destroy는 프레임 끝에 실행되기 때문에, 지운 직후 Instantiate하면
    /// 옛 자식과 새 자식이 한 프레임 동안 같은 부모에 공존한다.
    /// 그 상태로 LayoutGroup이 배치하면 화면에는 곧 사라질 옛 슬롯이 섞여 보이고,
    /// 그 칸을 클릭하면 이벤트가 연결되지 않은 유령 슬롯이 반응한다.
    ///
    /// 그래서 지우기 전에 부모에서 먼저 떼어낸다. SetParent(null)은 즉시 반영되므로
    /// 레이아웃 계산과 클릭 판정에서 곧바로 빠진다.
    /// </summary>
    public static void ClearChildren(Transform container)
    {
        if (container == null) return;

        for (int i = container.childCount - 1; i >= 0; i--)
        {
            Transform child = container.GetChild(i);
            child.SetParent(null, false);
            Object.Destroy(child.gameObject);
        }
    }
}