using UnityEngine;
using UnityEditor;

// 일회성 스크립트 - 확인 후 삭제할 것
public static class TempUnlockMapGuide
{
    [MenuItem("Tools/Map/Unlock Map Guide")]
    static void Unlock()
    {
        GameObject guide = GameObject.Find("MapGuide");
        if (guide == null)
        {
            Debug.LogError("MapGuide를 찾을 수 없습니다.");
            return;
        }
        SceneVisibilityManager.instance.EnablePicking(guide, false);
        Debug.Log("✅ MapGuide 클릭 잠금 해제 완료. 이제 Scene 뷰에서 선택/이동/크기 조절 가능합니다.");
    }
}
