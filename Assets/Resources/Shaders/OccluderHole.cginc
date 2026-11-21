// 외부 가림 구멍 (2026-11-21) - 카메라와 플레이어 사이 가림 오브젝트에서 "플레이어 근처만" 뚫음.
// OccluderFader가 매 프레임 전역 값을 넣음:
//   _OccHolePlayer (xyz = 플레이어 가슴 높이 월드 위치, w = 플레이어 발 Y)
//   _OccHoleParams (x = 반경 m, y = 페더링 m, z = 발 위 이 높이(m)보다 낮은 부분은 안 뚫음)
// 렌더러별 _HoleAmount(MaterialPropertyBlock, 0~1)로 구멍이 부드럽게 열리고 닫힘.
// 판정: 픽셀이 "카메라→플레이어 원뿔"(플레이어 위치에서 반경 3.5m) 안 + 플레이어보다 카메라 쪽 → 디더링으로 잘라냄(clip).
//  알파 블렌딩이 아니라 불투명 그대로 잘라내서 정렬 문제·반투명 겹침이 없음 (기존 BuildingCutaway와 같은 방식).
//  그림자 패스에서는 안 뚫음 (구멍으로 햇빛이 새어 들어오지 않게) → 호출하는 쪽에서 UNITY_PASS_SHADOWCASTER일 때 건너뜀.
#ifndef OCCLUDER_HOLE_INCLUDED
#define OCCLUDER_HOLE_INCLUDED

float4 _OccHolePlayer;
float4 _OccHoleParams;

// 화면 픽셀 위치로 만든 0~1 노이즈 (Interleaved Gradient Noise) - 페더링 구간을 점묘로 흩뿌림
float OccHoleNoise(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

// amount: 0 = 구멍 없음, 1 = 반경 전체. 잘라낼 픽셀이면 clip
void OccluderHoleClip(float3 worldPos, float2 pixel, float amount)
{
    if (amount <= 0.001 || _OccHoleParams.x <= 0) return;
    if (worldPos.y < _OccHolePlayer.w + _OccHoleParams.z) return; // 발밑 근처(바닥·벽 아래 띠)는 남김

    float3 cam = _WorldSpaceCameraPos;
    float3 d = _OccHolePlayer.xyz - cam;
    float t = dot(worldPos - cam, d) / max(dot(d, d), 1e-4);
    if (t >= 1.0) return; // 플레이어보다 뒤(카메라 반대편)는 안 뚫음

    float dist = length(worldPos - (cam + d * t));       // 시선에서 떨어진 거리 (m)
    // 원뿔: 반경을 t(카메라 0 ~ 플레이어 1)에 비례 → 화면에서 "플레이어 위치 기준 반경 3.5m 원"으로 보임.
    // (원기둥이면 카메라에 가까운 지붕일수록 구멍이 화면에서 거대해짐 - 2026-11-21 Play 확인)
    float tc = max(t, 0.0);
    float radius = _OccHoleParams.x * amount * tc;
    float feather = max(_OccHoleParams.y * amount * tc, 1e-3);
    float keep = saturate((dist - (radius - feather)) / feather); // 0 = 완전히 뚫림, 1 = 그대로
    clip(keep - OccHoleNoise(pixel) - 1e-4);
}

#endif
