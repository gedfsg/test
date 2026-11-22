using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 미니맵 미션 핀: 메인 목표 = 주황 ◆, 발견된 장소형 서브 = 파랑 🔍.
///  - 위치는 실제 위치에서 ±30m 흐린 곳 (MissionManager.MapPinFuzz). 플레이어가 핀 50m 안에 오면 정확한 위치로 옮김
///  - 미니맵 카메라로 투영 → Q/E 회전/줌을 그대로 따라감. 미니맵 밖이면 둥근 테두리 안쪽에 붙여서 방향만 보여줌
///  - 월드에는 아무것도 그리지 않음 (이전: HUD 방향 화살표 + 탈출/좀비 떼 빛기둥)
/// MissionManager가 AddComponent로 붙임. 미니맵(MinimapController + PlayerMarker)이 없는 씬이면 아무것도 안 함.
///
/// 2026-11-16 서브 미션 범위 원:
///  - 발견된 서브 미션마다 반경 50m 원 (미니맵 축척 적용): 주황 반투명 테두리 2px + 안쪽 알파 0.1 채움, 원 중심에 🔍
///  - 원 중심 = 기존 ±30m 흐린 핀 위치 (실제 장소는 항상 원 안). 서브 핀은 50m 안에 와도 정확한 위치로 옮기지 않음
///  - 플레이어가 원 안에 들어오면 초록으로 바뀜. 미션이 끝나면(보상 받음) 목록에서 빠져서 사라짐
/// </summary>
public class MinimapMissionPins : MonoBehaviour
{
    static readonly Color MainColor = new Color32(0xFF, 0xA5, 0x00, 0xFF); // 주황 (미션 HUD와 같은 색)
    static readonly Color SubColor = new Color32(0x4F, 0xA8, 0xFF, 0xFF);  // 파랑
    const float PinSize = 14f;
    const float EdgeInset = 8f; // 미니맵 밖 핀을 테두리에서 이만큼 안쪽에 붙임 (UI px)
    const float SubAreaRadius = 50f; // 서브 미션 범위 원 반경 (m)
    static readonly Color AreaOrange = new Color(1f, 0.6f, 0.1f, 0.85f);
    static readonly Color AreaGreen = new Color(0.3f, 0.9f, 0.35f, 0.9f);
    RectTransform circleLayer; // 원 부모 (미니맵 마스크 안 → 지도 밖으로 안 삐져나감)
    readonly List<UICircle> circlePool = new List<UICircle>();

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

        // 원 레이어: 미니맵 그림을 자르는 마스크가 있으면 그 안 (원 일부가 지도 밖이면 잘림), 없으면 핀 레이어 바로 아래
        Transform maskT = null;
        var mask = raw.GetComponentInParent<Mask>(true);
        if (mask != null) maskT = mask.transform;
        else { var rm = raw.GetComponentInParent<RectMask2D>(true); if (rm != null) maskT = rm.transform; }
        circleLayer = new GameObject("SubMissionAreas", typeof(RectTransform)).GetComponent<RectTransform>();
        circleLayer.SetParent(maskT != null ? maskT : panel, false);
        circleLayer.anchorMin = circleLayer.anchorMax = circleLayer.pivot = new Vector2(0.5f, 0.5f);
        circleLayer.sizeDelta = Vector2.zero;
        // 지도 그림 중심에 맞춤 (마스크 안이면 지도 그림 바로 위 순서)
        circleLayer.position = mapImage.TransformPoint(mapImage.rect.center);
        if (maskT == raw.transform.parent) circleLayer.SetSiblingIndex(raw.transform.GetSiblingIndex() + 1);
        else if (maskT == null) circleLayer.SetSiblingIndex(pinLayer.GetSiblingIndex());
    }

    UICircle GetCircle(int i)
    {
        while (circlePool.Count <= i)
        {
            var go = new GameObject("Area" + circlePool.Count, typeof(RectTransform), typeof(CanvasRenderer), typeof(UICircle));
            go.transform.SetParent(circleLayer, false);
            var c = go.GetComponent<UICircle>();
            c.raycastTarget = false; c.thickness = 2f;
            circlePool.Add(c);
        }
        return circlePool[i];
    }

    void LateUpdate() // MinimapController(LateUpdate)가 카메라를 옮긴 뒤 순서는 상관없음: 투영은 카메라 현재 값 기준
    {
        if (mm == null || pinLayer == null) return;
        mm.GetMapPins(pins);

        float radius = Mathf.Min(mapImage.rect.width, mapImage.rect.height) * 0.5f - EdgeInset;
        // 미니맵 축척: 50m가 UI에서 몇 px인지 (카메라 오른쪽으로 50m 떨어진 점을 투영해서 비교)
        Vector3 camRight = mapCam.transform.right; camRight.y = 0f; camRight = camRight.sqrMagnitude > 1e-4f ? camRight.normalized : Vector3.right;
        int circles = 0;
        for (int i = 0; i < pins.Count; i++)
        {
            var img = GetPin(i);
            Vector3 shown = mm.MapPinFuzz(pins[i].pos);
            // 아이콘 50m 안으로 오면 정확한 위치 (메인 미션만. 서브는 원 중심 = 흐린 위치 그대로)
            if (pins[i].main && player != null && FlatDist(player.position, shown) <= mm.mapPinExactRadius) shown = pins[i].pos;
            if (pins[i].escape) shown = pins[i].pos; // (2026-11-22) 헬리패드는 흐리지 않고 정확한 위치

            if (!pins[i].main)
            {
                // 서브 미션 범위 원
                var c = GetCircle(circles++);
                Vector3 vp0 = mapCam.WorldToViewportPoint(shown), vp1 = mapCam.WorldToViewportPoint(shown + camRight * SubAreaRadius);
                Vector2 c0 = new Vector2((vp0.x - 0.5f) * mapImage.rect.width, (vp0.y - 0.5f) * mapImage.rect.height);
                Vector2 c1 = new Vector2((vp1.x - 0.5f) * mapImage.rect.width, (vp1.y - 0.5f) * mapImage.rect.height);
                float rpx = (c1 - c0).magnitude;
                c.rectTransform.anchoredPosition = c0;
                c.rectTransform.sizeDelta = Vector2.one * rpx * 2f;
                bool inside = player != null && FlatDist(player.position, shown) <= SubAreaRadius;
                Color border = inside ? AreaGreen : AreaOrange;
                Color fill = border; fill.a = 0.1f;
                c.SetColors(border, fill);
                c.gameObject.SetActive(true);
                img.color = inside ? AreaGreen : SubColor; // 원 중심 🔍도 같이 초록
            }

            // 미니맵 카메라 뷰포트(0~1) → 미니맵 그림 안 UI 좌표 (중심 0,0)
            Vector3 vp = mapCam.WorldToViewportPoint(shown);
            Vector2 ui = new Vector2((vp.x - 0.5f) * mapImage.rect.width, (vp.y - 0.5f) * mapImage.rect.height);
            if (ui.magnitude > radius) ui = ui.normalized * radius; // 원형 미니맵 밖 → 테두리에 붙임

            img.rectTransform.anchoredPosition = ui;
            bool main = pins[i].main;
            img.sprite = main ? HudStyle.Diamond : HudStyle.Search;
            if (main) img.color = MainColor; // (이전) main ? MainColor : SubColor → 서브는 위에서 원 안/밖에 따라 정함
            if (pins[i].escape) img.color = AreaGreen; // (2026-11-22) 탈출 준비 완료된 헬리패드 = 초록 ◆
            img.gameObject.SetActive(true);
        }
        for (int i = pins.Count; i < pool.Count; i++) pool[i].gameObject.SetActive(false);
        for (int i = circles; i < circlePool.Count; i++) circlePool[i].gameObject.SetActive(false);
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
