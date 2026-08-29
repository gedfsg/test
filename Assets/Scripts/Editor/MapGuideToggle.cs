using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;

public static class MapGuideToggle
{
    [MenuItem("Tools/Map/Toggle Map Guide")]
    static void Toggle()
    {
        GameObject guide = FindIncludingInactive("MapGuide");
        if (guide == null)
        {
            Debug.LogError("MapGuide 오브젝트를 찾을 수 없습니다. 먼저 씬을 열고 초기 세팅이 실행됐는지 확인하세요.");
            return;
        }
        guide.SetActive(!guide.activeSelf);
        Debug.Log($"MapGuide {(guide.activeSelf ? "켜짐" : "꺼짐")}");
    }

    static GameObject FindIncludingInactive(string name)
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t.gameObject;
            }
        }
        return null;
    }
}
