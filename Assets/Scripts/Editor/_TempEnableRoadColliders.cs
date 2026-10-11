using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

// 일회성 스크립트 - 확인 후 삭제할 것
// Kenney Roads FBX format 폴더 안의 모든 모델(.fbx)의 Import Setting에서
// "Generate Colliders"를 켜서, 이 메시를 쓰는 씬의 모든 인스턴스(이미 배치된 것 +
// 앞으로 배치할 것 전부)에 자동으로 Mesh Collider(convex=false, 실제 모양 그대로)가
// 붙게 함. 인스턴스별로 일일이 콜라이더를 붙이는 것보다 근본적인 해결책.
public static class TempEnableRoadColliders
{
    const string FolderPath = "Assets/KenneyAssets/Roads/FBX format";

    [MenuItem("Tools/Map/Enable Colliders On All Road FBX Models")]
    static void Run()
    {
        var guids = AssetDatabase.FindAssets("t:Model", new[] { FolderPath });
        int changed = 0, alreadyOn = 0, failed = 0;
        var changedNames = new List<string>();

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) { failed++; continue; }

            if (importer.addCollider)
            {
                alreadyOn++;
                continue;
            }

            importer.addCollider = true;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            changed++;
            changedNames.Add(Path.GetFileName(path));
        }

        string msg = $"✅ Road FBX Generate Colliders 켬: {changed}개 (이미 켜져있던 것 {alreadyOn}개, 실패 {failed}개)\n"
                     + string.Join(", ", changedNames);
        Debug.Log(msg);
    }
}
