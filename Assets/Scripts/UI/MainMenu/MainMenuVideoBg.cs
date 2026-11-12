using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 메인 메뉴 배경: 비 내리는 영상 무한 반복 (VideoPlayer → RenderTexture → 전체 화면 RawImage).
/// 영상: StreamingAssets/MainMenu/Seventh_Night_Menu_Background_Loop.mp4 (빌드에 그대로 복사되는 폴더라 URL로 재생).
/// 첫 프레임이 준비되기 전엔 RawImage를 검게 둬서 빈 텍스처(회색/깨진 화면)가 보이지 않게 함
/// </summary>
[RequireComponent(typeof(RawImage))]
public class MainMenuVideoBg : MonoBehaviour
{
    public string fileName = "MainMenu/Seventh_Night_Menu_Background_Loop.mp4";
    public Vector2Int size = new Vector2Int(1920, 1080);

    VideoPlayer player;
    RenderTexture rt;
    RawImage image;

    void Awake()
    {
        image = GetComponent<RawImage>();
        image.color = Color.black;

        rt = new RenderTexture(size.x, size.y, 0) { name = "MainMenuVideo" };
        rt.Create();

        player = gameObject.AddComponent<VideoPlayer>();
        player.playOnAwake = false;
        player.source = VideoSource.Url;
        player.url = System.IO.Path.Combine(Application.streamingAssetsPath, fileName);
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = rt;
        player.isLooping = true;
        player.waitForFirstFrame = true;
        player.skipOnDrop = true;
        player.audioOutputMode = VideoAudioOutputMode.None;
        player.aspectRatio = VideoAspectRatio.FitOutside; // 화면을 꽉 채움 (남는 부분은 잘림)
        player.prepareCompleted += OnPrepared;
        player.errorReceived += (vp, msg) => Debug.LogError("[MainMenuVideoBg] 영상 재생 실패: " + msg);
        player.Prepare();
    }

    void OnPrepared(VideoPlayer vp)
    {
        image.texture = rt;
        image.color = Color.white;
        vp.Play();
    }

    void OnDestroy()
    {
        if (player != null) player.prepareCompleted -= OnPrepared;
        if (rt != null) { rt.Release(); Destroy(rt); }
    }
}
