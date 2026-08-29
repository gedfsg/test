using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 일회성 스크립트 - 확인 후 삭제할 것
public static class TempAddRoadLayer
{
    const string TexPath = "Assets/Textures/RoadPlaceholder.png";
    const string LayerPath = "Assets/Terrain/RoadLayer.terrainlayer";

    [MenuItem("Tools/Map/Add Road Paint Layer")]
    static void Add()
    {
        GameObject groundGo = GameObject.Find("Ground");
        Terrain terrain = groundGo != null ? groundGo.GetComponent<Terrain>() : null;
        if (terrain == null)
        {
            Debug.LogError("Ground(Terrain)를 찾을 수 없습니다.");
            return;
        }

        TerrainLayer roadLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerPath);
        if (roadLayer == null)
        {
            Texture2D tex = new Texture2D(4, 4);
            Color asphalt = new Color(0.32f, 0.32f, 0.34f);
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    tex.SetPixel(x, y, asphalt);
            tex.Apply();
            File.WriteAllBytes(TexPath, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(TexPath);

            roadLayer = new TerrainLayer();
            roadLayer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
            roadLayer.tileSize = new Vector2(10, 10);
            AssetDatabase.CreateAsset(roadLayer, LayerPath);
        }

        TerrainData data = terrain.terrainData;
        if (!data.terrainLayers.Contains(roadLayer))
        {
            var layers = data.terrainLayers.ToList();
            layers.Add(roadLayer);
            data.terrainLayers = layers.ToArray();
        }

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("✅ 도로색(회색) Terrain Layer 추가 완료. Paint Texture 툴에서 'RoadLayer' 선택해서 칠하면 됩니다.");
    }
}
