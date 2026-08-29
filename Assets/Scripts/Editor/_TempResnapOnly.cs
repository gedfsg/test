using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 일회성 스크립트 - 확인 후 삭제할 것. 지형은 건드리지 않고 오브젝트 높이만 재조정.
public static class TempResnapOnly
{
    [MenuItem("Tools/Map/Resnap Objects To Current Terrain (No Flatten)")]
    static void Resnap()
    {
        GameObject groundGo = GameObject.Find("Ground");
        Terrain terrain = groundGo != null ? groundGo.GetComponent<Terrain>() : null;
        if (terrain == null)
        {
            Debug.LogError("Ground(Terrain)를 찾을 수 없습니다.");
            return;
        }

        SnapToGround(terrain, "Player", 1f);
        SnapToGround(terrain, "MapGuide", 0.3f);
        SnapToGround(terrain, "Pistol_K", 0.5f);
        SnapToGround(terrain, "AR_A_1", 0.5f);
        SnapToGround(terrain, "AR_D", 0.5f);
        SnapToGround(terrain, "ShotGun_A", 0.5f);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("✅ 지형 모양은 그대로 두고, 오브젝트들만 현재 땅 높이에 맞춰 재배치 + 저장 완료.");
    }

    static void SnapToGround(Terrain terrain, string name, float heightOffset)
    {
        GameObject go = GameObject.Find(name);
        if (go == null) return;
        float groundY = terrain.SampleHeight(go.transform.position) + terrain.transform.position.y;
        Vector3 p = go.transform.position;
        p.y = groundY + heightOffset;
        go.transform.position = p;
        Debug.Log($"{name}: Y를 {p.y}로 재배치 (현재 지형 기준)");
    }
}
