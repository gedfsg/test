using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
public class WaterWave : MonoBehaviour
{
    public float waveHeight = 0.15f;
    public float waveSpeed = 1f;
    public float waveScale = 0.3f;

    Mesh mesh;
    Vector3[] baseVerts;
    Vector3[] workVerts;

    void OnEnable()
    {
        // sharedMesh를 직접 건드리면 다른 Plane 오브젝트(MapGuide 등)까지 같이 망가지므로 복제본 사용
        MeshFilter mf = GetComponent<MeshFilter>();
        mesh = Instantiate(mf.sharedMesh);
        mf.mesh = mesh;
        baseVerts = mesh.vertices;
        workVerts = new Vector3[baseVerts.Length];
    }

    void Update()
    {
        if (mesh == null) return;
        float t = Time.realtimeSinceStartup * waveSpeed;
        for (int i = 0; i < baseVerts.Length; i++)
        {
            Vector3 v = baseVerts[i];
            v.y += Mathf.Sin((v.x + v.z) * waveScale + t) * waveHeight;
            workVerts[i] = v;
        }
        mesh.vertices = workVerts;
        mesh.RecalculateNormals();
    }

    void OnDisable()
    {
        if (mesh != null) mesh.vertices = baseVerts;
    }
}
