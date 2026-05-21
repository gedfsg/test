using UnityEngine;
using UnityEditor;

public class AddBuildingColliders : EditorWindow
{
    [MenuItem("Tools/Add Building Colliders")]
    static void AddColliders()
    {
        string[] targets = { "STATIC_MODELS", "ENVIRONMENT_OBJECTS" };
        int count = 0;

        foreach (string name in targets)
        {
            GameObject root = GameObject.Find(name);
            if (root == null) continue;

            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.GetComponent<Collider>() == null)
                {
                    MeshCollider mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    count++;
                }
            }
        }

        Debug.Log($"콜라이더 {count}개 추가 완료!");
    }
}
