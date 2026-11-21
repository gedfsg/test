// 외부 가림 구멍 (2026-11-21) - 카메라와 플레이어 사이 가림 오브젝트에서 "플레이어 근처만" 뚫음.
// OccluderFader가 매 프레임 전역 값을 넣음:
//   _OccHolePlayer (xyz = 플레이어 가슴 높이 월드 위치, w = 플레이어 발 Y)
//   _OccHoleParams (x = 반경 m, y = 페더링 m, z = 발 위 이 높이(m)보다 낮은 부분은 안 뚫음, w = 내부 가림 밝기)
// 렌더러별 _HoleAmount(MaterialPropertyBlock, 0~1)로 구멍이 부드럽게 열리고 닫힘.
// 판정: 픽셀이 "카메라→플레이어 원뿔"(플레이어 위치에서 반경 R) 안 + 플레이어보다 카메라 쪽.
//  그림자 패스에서는 안 뚫음 (구멍으로 햇빛이 새어 들어오지 않게) → 호출하는 쪽에서 UNITY_PASS_SHADOWCASTER일 때 건너뜀.
// (2026-11-21 2차) 디더링 → 그라데이션: 불투명 원본은 페더 구간까지 전부 잘라내고(OccluderHoleClip),
//  페더 구간(가장자리 띠)은 OccluderFader가 붙이는 반투명 "링" 렌더러가 알파 그라데이션으로 그림(OccluderHoleRing).
// (2026-11-21 3차) 피드백 "건물 내부가 보이면 안 됨": 렌더러별 _HoleInteriorFill(MPB) = 1이면 (플레이어가 안 들어간 건물)
//  구멍 안의 바닥 높이 부분(바닥·러그 등)을 안 남기고 어두운 단색(_OccHoleParams.w)으로 칠함 → 방 안이 안 보이고 플레이어·바깥 지면만 보임.
//  벽·지붕·가구(바닥보다 높은 것)는 그대로 잘라냄. 가장자리는 원래 색 → 어두운 색으로 그라데이션.
#ifndef OCCLUDER_HOLE_INCLUDED
#define OCCLUDER_HOLE_INCLUDED

float4 _OccHolePlayer;
float4 _OccHoleParams;

// (이전 1차) 화면 픽셀 노이즈로 페더 구간을 점묘 - 2차부터 안 씀 (남겨 둠)
float OccHoleNoise(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

// 발밑 근처(바닥·벽 아래 띠) 높이인지
bool OccHoleIsLow(float3 worldPos)
{
    return worldPos.y < _OccHolePlayer.w + _OccHoleParams.z;
}

// 높이 조건 없이 원뿔 판정만: 1 = 구멍 밖, 0 = 완전히 뚫림, 그 사이 = 페더 구간 (부드러운 곡선)
float OccHoleKeepCone(float3 worldPos, float amount)
{
    if (amount <= 0.001 || _OccHoleParams.x <= 0) return 1;
    float3 cam = _WorldSpaceCameraPos;
    float3 d = _OccHolePlayer.xyz - cam;
    float t = dot(worldPos - cam, d) / max(dot(d, d), 1e-4);
    if (t >= 1.0) return 1; // 플레이어보다 뒤(카메라 반대편)는 안 뚫음
    // (2026-11-21 3차) 수평(XZ)으로도 플레이어보다 카메라 쪽인 것만: 위에서 비스듬히 보는 카메라라
    //  플레이어 뒤 건물의 높은 지붕은 3D t < 1이 돼서 잘렸음(방 안이 보임) → 플레이어를 실제로 가릴 수 없는 것이니 안 뚫음
    float txz = dot((worldPos - cam).xz, d.xz) / max(dot(d.xz, d.xz), 1e-4);
    if (txz >= 1.0) return 1;

    float dist = length(worldPos - (cam + d * t));       // 시선에서 떨어진 거리 (m)
    // 원뿔: 반경을 t(카메라 0 ~ 플레이어 1)에 비례 → 화면에서 "플레이어 위치 기준 반경 R 원"으로 보임.
    // (원기둥이면 카메라에 가까운 지붕일수록 구멍이 화면에서 거대해짐 - 2026-11-21 Play 확인)
    float tc = max(t, 0.0);
    float radius = _OccHoleParams.x * amount * tc;
    float feather = max(_OccHoleParams.y * amount * tc, 1e-3);
    float k = saturate((dist - (radius - feather)) / feather);
    return k * k * (3 - 2 * k); // smoothstep - 가장자리가 딱딱하지 않게
}

// 1 = 그대로(구멍 밖), 0 = 완전히 뚫림 (발밑 높이는 항상 1)
float OccHoleKeep(float3 worldPos, float amount)
{
    if (OccHoleIsLow(worldPos)) return 1;
    return OccHoleKeepCone(worldPos, amount);
}

// 불투명 원본용: 구멍 + 페더 구간 전부 잘라냄 (페더 구간은 링이 그라데이션으로 채움)
void OccluderHoleClip(float3 worldPos, float2 pixel, float amount)
{
    // (이전 1차) 디더링: clip(keep - OccHoleNoise(pixel) - 1e-4);
    clip(OccHoleKeep(worldPos, amount) - 0.999);
}

// 링(반투명)용: 페더 구간만 남기고 그 알파를 돌려줌
float OccluderHoleRing(float3 worldPos, float amount)
{
    float k = OccHoleKeep(worldPos, amount);
    clip(k - 0.001);   // 완전히 뚫린 곳
    clip(0.999 - k);   // 구멍 밖 (불투명 원본이 그림)
    return k;
}

// (3차) 불투명 원본용 + 내부 가림: 바닥 높이면 잘라내지 않고 "원래 색 비율"(1 = 원래, 0 = 어두운 단색)을 돌려줌
//  fill: 0 = 내부 가림 없음, 1 = 건물 본체(벽·지붕 - 가장자리는 링으로 반투명), 2 = 건물 안 가구·소품(링 없음)
//  바닥 높이는 구멍 범위(페더 포함) 안이면 완전히 어둡게: 위의 반투명 지붕이 그라데이션을 만들어서 경계가 안 보임
//  (이전 3차 초안: 바닥도 k만큼 그라데이션 → 페더 구간에서 방 바닥·러그가 반쯤 보였음 - 2026-11-21 Play 확인)
float OccluderHoleClipFill(float3 worldPos, float amount, float fill)
{
    float r = 1;
    if (fill > 0.5 && OccHoleIsLow(worldPos)) r = OccHoleKeepCone(worldPos, amount) < 0.999 ? 0 : 1;
    else clip(OccHoleKeep(worldPos, amount) - 0.999);
    return r;
}

// (2026-11-22) 링을 못 붙이는 렌더러(정적 배칭 - MeshFilter가 합쳐진 메시를 돌려줌)용: 페더 구간을 디더링으로 (1차 방식)
float OccluderHoleClipFillDither(float3 worldPos, float2 pixel, float amount, float fill, float dither)
{
    float r = 1;
    float th = dither < 0.5 ? 0.999 : OccHoleNoise(pixel) + 1e-4; // 링 있음 = 페더까지 전부 잘라냄 / 링 없음 = 디더링
    if (fill > 0.5 && OccHoleIsLow(worldPos)) r = OccHoleKeepCone(worldPos, amount) < 0.999 ? 0 : 1;
    else clip(OccHoleKeep(worldPos, amount) - th);
    return r;
}

// (3차) 링용 + 내부 가림: 바닥 높이는 불투명 원본이 어둡게 칠하므로 안 그림, 가구·소품(fill 2)은 링 자체를 안 그림
//  (가구 링이 반투명 지붕보다 먼저 그려져서 지붕 너머로 비치던 문제)
float OccluderHoleRingFill(float3 worldPos, float amount, float fill)
{
    if (fill > 1.5) { clip(-1); return 0; }
    if (fill > 0.5 && OccHoleIsLow(worldPos)) { clip(-1); return 0; }
    return OccluderHoleRing(worldPos, amount);
}

// 어두운 단색 (_OccHoleParams.w 밝기)
float3 OccHoleFillColor() { return _OccHoleParams.www; }

#endif
