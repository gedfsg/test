using UnityEngine;

[ExecuteAlways]
public class WaterFlow : MonoBehaviour
{
    public Vector2 flowSpeed = new Vector2(0f, 0.3f);
    Renderer rend;
    Vector2 offset;

    void OnEnable()
    {
        rend = GetComponent<Renderer>();
    }

    void Update()
    {
        if (rend == null) return;
        offset += flowSpeed * Time.deltaTime;
        rend.material.mainTextureOffset = offset; // .material이라 공유 에셋(.mat)은 안 바뀌고 이 오브젝트 전용 복사본만 바뀜
    }
}
