using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 미니맵 미션 핀: 메인 목표 = 주황 ◆, 발견된 장소형 서브 = 파랑 🔍.
///  - 위치는 실제 위치에서 ±30m 흐린 곳 (MissionManager.MapPinFuzz). 플레이어가 핀 50m 안에 오면 정확한 위치로 옮김
///  - 미니맵 카메라로 투영 → Q/E 회전/줌을 그대로 따라감. 미니맵 밖이면 둥근 테두리 안쪽에 붙여서 방향만 보여줌
///  - 월드에는 아무것도 그리지 않음 (이전: HUD 방향 화살표 + 탈출/좀비 떼 빛기둥)
/// MissionManager가 AddComponent로 붙임. 미니맵(MinimapController + PlayerMarker)이 없는 씬이면 아무것도 안 함.
/// </summary>
public class MinimapMissionPins : MonoBehaviour
{
    static readonly Color MainColor = new Color32(0xFF, 0xA5, 0x00, 0xFF); // 주황 (미션 HUD와 같은 색)
    static readonly Color SubColor = new Color32(0x4F, 0xA8, 0xFF, 0xFF);  // 파랑
    const float PinSize = 14f;
    const float EdgeInset = 8f; // 미니맵 밖 핀을 테두리에서 이만큼 안쪽에 붙임 (UI px)

    MissionManager mm;
    Camera mapCam;
    RectTransform mapImage;   // 미니맵 그림 (MinimapRawImage) - 투영 기준 영역
    RectTransform pinLayer;   // 핀 부모 (지도 위, 링/플레이어 마커 아래)
    Transform player;
    readonly List<MissionManager.MapPin> pins = new List<MissionManager.MapPin>();
    readonly List<Image> pool = new List<Image>();

    void Start()
    {
        mm = GetComponent<MissionManager>();
        var p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;

        var mc = FindFirstObjectByType<MinimapController>();
        if (mc == null || mc.playerMarker == null) { enabled = false; return; }
        mapCam = mc.GetComponent<Camera>();
        var raw = mc.playerMarker.parent.GetComponentInChildren<RawImage>(true);
        if (mapCam == null || raw == null) { enabled = false; return; }
        mapImage = raw.rectTransform;

        // 핀 레이어: 미니맵 패널 안, 플레이어 마커 바로 앞 순서 (지도/마스크 위, 바깥 링도 위로 둠)
        Transform panel = mc.playerMarker.parent;
        pinLayer = new GameObject("MissionPins", typeof(RectTransform)).GetComponent<RectTransform>();
        pinLayer.SetParent(panel, false);
        pinLayer.anchorMin = pinLayer.anchorMax = pinLayer.pivot = new Vector2(0.5f, 0.5f);
        pinLayer.anchoredPosition = mapImage.anchoredPosition;
        pinLayer.sizeDelta = Vector2.zero;
        pinLayer.SetSiblingIndex(mc.playerMarker.GetSiblingIndex()); // 마커가 핀 위에 그려짐
    }

    void LateUpdate() // MinimapController(LateUpdate)가 카메라를 옮긴 뒤 순서는 상관없음: 투영은 카메라 현재 값 기준
    {
        if (mm == null || pinLayer == null) return;
        mm.GetMapPins(pins);

        float radius = Mathf.Min(mapImage.rect.width, mapImage.rect.height) * 0.5f - EdgeInset;
        for (int i = 0; i < pins.Count; i++)
        {
            var img = GetPin(i);
            Vector3 shown = mm.MapPinFuzz(pins[i].pos);
            // 아이콘 50m 안으로 오면 정확한 위치
            if (player != null && FlatDist(player.position, shown) <= mm.mapPinExactRadius) shown = pins[i].pos;

            // 미니맵 카메라 뷰포트(0~1) → 미니맵 그림 안 UI 좌표 (중심 0,0)
            Vector3 vp = mapCam.WorldToViewportPoint(shown);
            Vector2 ui = new Vector2((vp.x - 0.5f) * mapImage.rect.width, (vp.y - 0.5f) * mapImage.rect.height);
            if (ui.magnitude > radius) ui = ui.normalized * radius; // 원형 미니맵 밖 → 테두리에 붙임

            img.rectTransform.anchoredPosition = ui;
            bool main = pins[i].main;
            img.sprite = main ? HudStyle.Diamond : HudStyle.Search;
            img.color = main ? MainColor : SubColor;
            img.gameObject.SetActive(true);
        }
        for (int i = pins.Count; i < pool.Count; i++) pool[i].gameObject.SetActive(false);
    }

    Image GetPin(int i)
    {
        while (pool.Count <= i)
        {
            var img = HudStyle.NewImage(pinLayer, "Pin" + pool.Count, HudStyle.Diamond, MainColor, Vector2.zero, new Vector2(PinSize, PinSize));
            img.raycastTarget = false;
            var outline = img.gameObject.AddComponent<Outline>(); // 밝은 지도 위에서도 보이게
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(1f, -1f);
            pool.Add(img);
        }
        return pool[i];
    }

    static float FlatDist(Vector3 a, Vector3 b) { a.y = b.y = 0f; return (a - b).magnitude; }
}
