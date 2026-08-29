using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 일회성 스크립트 - 확인 후 삭제할 것
// Player를 지정된 공원 좌표(X, Z)로 이동. Y는 그 위치의 실제 지형 높이 + 1.
public static class TempMovePlayerToPark
{
    const float ParkX = -158f;
    const float ParkZ = -220f;
    const float HeightOffset = 1f;

    [MenuItem("Tools/Map/Move Player To Park")]
    static void Move()
    {
        GameObject player = GameObject.Find("Player");
        if (player == null)
        {
            Debug.LogError("Player 오브젝트를 찾을 수 없습니다.");
            return;
        }

        GameObject groundGo = GameObject.Find("Ground");
        Terrain terrain = groundGo != null ? groundGo.GetComponent<Terrain>() : null;
        if (terrain == null)
        {
            Debug.LogError("Ground(Terrain)를 찾을 수 없습니다.");
            return;
        }

        float groundY = terrain.SampleHeight(new Vector3(ParkX, 0f, ParkZ)) + terrain.transform.position.y;
        Vector3 newPos = new Vector3(ParkX, groundY + HeightOffset, ParkZ);

        player.transform.position = newPos;

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"✅ Player를 공원 좌표로 이동 완료: {newPos} (지형 높이={groundY})");
    }
}
