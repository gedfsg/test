using UnityEngine;

public class FieldOfView : MonoBehaviour
{
    [Header("시야 설정")]
    public float viewRadius = 10f;        // 시야 반경
    [Range(0, 360)]
    public float viewAngle = 120f;        // 시야 각도

    [Header("레이어 설정")]
    public LayerMask obstacleMask;        // 장애물 레이어

    [Header("시야 메쉬 설정")]
    public MeshFilter viewMeshFilter;     // 시야 메쉬
    public int rayCount = 90;             // 레이 개수 (많을수록 정밀)

    private Mesh viewMesh;

    void Start()
    {
        viewMesh = new Mesh();
        viewMesh.name = "ViewMesh";
        viewMeshFilter.mesh = viewMesh;
    }

    void LateUpdate()
    {
        DrawFieldOfView();
    }

    void DrawFieldOfView()
{
    float angleStep = viewAngle / rayCount;
    float startAngle = -viewAngle / 2;

    Vector3[] vertices = new Vector3[rayCount + 2];
    int[] triangles = new int[rayCount * 3];

    vertices[0] = Vector3.zero; // 플레이어 위치 (중심점)

    for (int i = 0; i <= rayCount; i++)
    {
        float angle = startAngle + angleStep * i;
        Vector3 direction = DirFromAngle(angle);

        RaycastHit hit;
        if (Physics.Raycast(transform.position, direction, out hit, viewRadius, obstacleMask))
        {
            // 장애물에 맞으면 거기서 멈춤
            vertices[i + 1] = transform.InverseTransformPoint(hit.point);
        }
        else
        {
            // 아무것도 없으면 최대 거리까지
            vertices[i + 1] = transform.InverseTransformPoint(transform.position + direction * viewRadius);
        }

        if (i < rayCount)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i + 2;
        }
    }

    viewMesh.Clear();
    viewMesh.vertices = vertices;
    viewMesh.triangles = triangles;
    viewMesh.RecalculateNormals();
}

// 각도를 방향 벡터로 변환
Vector3 DirFromAngle(float angleDegrees)
{
    float angle = transform.eulerAngles.y + angleDegrees;
    return new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad), 0, Mathf.Cos(angle * Mathf.Deg2Rad));
}
}