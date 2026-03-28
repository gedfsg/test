using UnityEngine;
using TMPro;

public class DamageText : MonoBehaviour
{
    public float fadeTime = 1f;
    public float bounceForce = 3f; // 위로 솟구치는 힘임.
    public float gravity = 5f;     // 아래로 끌어당기는 중력임.
    
    private TextMeshPro textMesh;
    private Color textColor;
    private float destroyTimer;
    private Vector3 velocity;

    void Awake()
    {
        textMesh = GetComponent<TextMeshPro>();
        if (textMesh != null)
        {
            textColor = textMesh.color;
        }
        destroyTimer = fadeTime;

        // X축과 Z축으로 퍼져나갈 무작위 방향을 지정하고 초기 속도 벡터를 설정함.
        float randomX = UnityEngine.Random.Range(-1.5f, 1.5f);
        float randomZ = UnityEngine.Random.Range(-1.5f, 1.5f);
        velocity = new Vector3(randomX, bounceForce, randomZ);
    }

    // 생성 직후 데미지 수치를 설정하는 함수임.
    public void Setup(float damageAmount)
    {
        if (textMesh != null)
        {
            textMesh.text = damageAmount.ToString("F0");
        }
    }

    void Update()
    {
        // 텍스트가 항상 카메라를 정면으로 바라보도록 회전값을 일치시킴.
        transform.rotation = Camera.main.transform.rotation;

        // 매 프레임마다 중력을 적용하여 Y축 속도를 지속적으로 감소시킴.
        velocity.y -= gravity * Time.deltaTime;

        // 계산된 속도 벡터를 현재 위치에 적용하여 포물선 이동을 구현함.
        transform.position += velocity * Time.deltaTime;

        // 남은 수명 시간에 비례하여 알파(투명도) 값을 감소시킴.
        destroyTimer -= Time.deltaTime;
        if (textMesh != null)
        {
            textColor.a = destroyTimer / fadeTime;
            textMesh.color = textColor;
        }

        // 수명 시간이 종료되면 오브젝트를 파괴함.
        if (destroyTimer <= 0)
        {
            Destroy(gameObject);
        }
    }
}