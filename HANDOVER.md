# 🎮 좀비 생존 게임 프로젝트 - 인수인계 문서

> **작성일:** 2026-08-01
> **팀:** 트리거 (Trigger) - 2인 (도의준, 김호영)
> **엔진:** Unity 6 (6000.3.11f1) + URP
> **저장소:** https://github.com/gedfsg/test

---

## 🆕 2026-08-29 세션 진행 상황

**맵:** 참고 지도(MapGuide) 배치, Kenney City Kit(Roads/Suburban/Commercial) 임포트, Simple Water Shader로 강 제작, 도로 배치 완료, 건물 일부 배치 및 신호등/가로등 배치 시작.

**이번 세션에서 작업한 것 (6가지 중):**
- ✅ **C. PlayerXRay** — Player 오브젝트에 이미 부착되어 있음 확인 (추가 작업 불필요)
- ✅ **D. 카메라 각도/줌아웃** — `CameraFollow.cs`의 baseOffset을 (0,21,-30)→(0,40,-18)로 변경 (내려다보는 각도 약 66°, 거리 약 +7 증가)
- ✅ **E. Q/E 카메라 회전** — `CameraRotate.cs` 신규 작성, Main Camera에 부착. 0/90/180/270도 스냅 + 0.3초 Lerp. `PlayerController.cs`의 이동 계산을 카메라 기준(camera-relative)으로 변경해 회전해도 W가 항상 화면 위쪽으로 이동하게 함
- ✅ **F. 우클릭 홀드 줌 + 휠 줌** — `CameraZoom.cs`를 거리 기반(min 15~max 40)으로 리팩터링, 우클릭 홀드 시 12% 확대 추가
- ✅ Input System에 RotateLeft(Q)/RotateRight(E)/AimZoom(우클릭)/Zoom(휠) 액션 추가 (`PlayerInputActions.inputactions`)
- 🔧 **A. 모래 바닥 구역 배치** — 코드로는 완료 (`Assets/Scripts/Editor/_TempPlaceGroundZones.cs`), **Unity 에디터에서 `Tools → Map → Place Ground Zones (Sand)` 실행 필요**
- 🔧 **B. BuildingInterior 일괄 적용** — 코드로는 완료 (`Assets/Scripts/Editor/_TempApplyBuildingInterior.cs`), **Unity 에디터에서 `Tools → Map → Apply Building Interior (Batch)` 실행 필요**

**⚠️ Claude Code는 Unity 에디터 GUI/Play 모드를 직접 조작할 수 없어서 위 두 메뉴 실행과 Play 테스트(모래 바닥, 건물 지붕, Q/E 회전, 줌, X-Ray)는 사용자가 직접 확인 필요.** 확인 후 문제 있으면 알려주면 바로 수정.

### 🔧 Play 테스트 후 버그 수정 (같은 세션)
1. **Q/E 회전 겹침** — `CameraRotate.cs`에 "회전 중 입력 무시" 가드 추가, Input Action에 명시적 Press interaction 추가
2. **카메라 각도 조정** — baseOffset (0,40,-18)→(0,30,-25), 각도 약 50°로 낮춤
3. **건물/도로 콜라이더** — `_TempAddBlockingColliders.cs` 신규 작성. `Tools → Map → Add Blocking Colliders` 실행 필요 (건물/담장/방호벽/나무=충돌, 신호등/가로등=얇은 충돌, 도로/Zone_*=통과)
4. **건물 완전 사라짐 → 반투명(알파 0.3)** — `BuildingInterior.cs`를 알파 블렌딩 방식으로 재작성. **모래 바닥 안 보임** — `_TempPlaceGroundZones.cs`가 Terrain 높이를 지점별로 샘플링하도록 수정 (기존엔 평평하다고 가정해서 지형에 파묻힘), Y 여유값도 0.05로. **재실행 필요**: `Tools → Map → Place Ground Zones (Sand)`

### 🔧 조명/콜라이더/Hierarchy 정리 (같은 날, 추가 작업)
1. **그림자/조명이 너무 어두움** — Directional Light(Sun) Intensity 1→1.3, Shadow Strength 1→0.6 (Soft Shadows는 이미 켜져 있었음). Environment Ambient는 Source가 이미 Color 모드였는데 색상이 검정(0,0,0)이라 어두웠던 게 원인 — RGB(100,100,110), Intensity 1.2로 변경 (main.unity 직접 수정, 실행할 도구 없음)
2. **건물 콜라이더 부정확** — `_TempFixBuildingColliders.cs` 신규: 건물(building-/house-)의 뭉뚱그린 BoxCollider를 실제 메시 모양대로 MeshCollider(Convex=false)로 교체, 여러 파츠 있으면 파츠별로 적용. 도로/Zone_*은 콜라이더 있으면 제거. **`Tools → Map → Fix Building Colliders (Mesh)` 실행 완료됨** (main.unity에 반영됨)
3. **Hierarchy 정리** — `_TempOrganizeHierarchy.cs` 신규: Roads/Buildings/Water/Props/Nature/Zones/SketchfabAssets 부모로 카테고리 정리 (월드 위치 유지). 기존 GroundZones는 이름만 Zones로 변경해 재사용. **`Tools → Map → Organize Hierarchy` 실행 완료됨** (main.unity에 반영됨)
4. **Sketchfab/Poly Pizza 에셋 다운로드** — `Assets/SketchfabAssets/`에 여러 건물(학교/병원/경찰서/교회/폐가/농장 등) + **City Park at Sunset**(공원) 임포트됨. **아직 씬에 배치 안 됨.** 가장 큰 파일이 50MB에 달해 `.gitattributes`에 `Assets/SketchfabAssets/**` LFS 트래킹 추가함
5. **Player 스폰 위치를 공원으로 이동** — 요청받았으나 공원 오브젝트가 씬에 없어서(에셋만 임포트, 미배치) 보류. `_TempMovePlayerToPark.cs`는 만들어뒀지만 **공원 좌표(-158, -220)가 씬에 배치되기 전 값이라 재확인 필요** — City Park를 배치한 뒤 그 오브젝트의 실제 좌표로 갱신해서 실행할 것

⚠️ Unity 에디터가 이미 열려있는 상태에서 외부(Claude Code)가 파일을 수정했으므로, 씬을 한 번 닫았다가 다시 열어야 최신 변경사항이 반영됨.

### 🔧 카메라 줌 범위 확장 (같은 세션, 추가 조정)
- `CameraZoom.cs`: minDistance 20 / maxDistance 80 / defaultDistance 40 (기존 15~40, 기본 35에서 확장)
- zoomStep 2→3 (범위가 넓어져서 노치당 이동량도 비례 조정)

---

## 🔀 팀원과 병합 시 주의사항 (2026-08-29)

**담당 분리:** 김호영 = 맵/카메라/시각효과, 도의준 = UI/인벤토리/무기/애니메이션. 지금까지는 서로 다른 파일 위주라 충돌이 적었지만 아래 파일들은 겹칠 가능성이 있음.

### ⚠️ 병합 위험도 높은 파일
| 파일 | 위험도 | 이유 |
|---|---|---|
| `Assets/Main Project/main.unity` | **매우 높음** | Unity 씬은 YAML이지만 사실상 자동 병합 불가급. 팀원이 UI Canvas/캐릭터 프리팹을 씬에서 직접 만지면 거의 확실히 충돌. **UnityYAMLMerge(Smart Merge)가 설정 안 되어 있음** — 팀원과 함께 `.gitconfig`에 설정 권장 |
| `Assets/PlayerController.cs` | 높음 | 이동/조준/발사 담당이라 팀원도 만질 수 있음. 이번에 바뀐 부분은 `// === MAP DEV ===` ~ `// === MAP DEV END ===` 주석으로 표시해둠 (이동 계산 로직, `CameraRelativeMove()` 메서드) |
| `Assets/PlayerInputActions.inputactions` + 자동생성 `PlayerInputActions.cs` | 중간 | 새 액션(RotateLeft/RotateRight/AimZoom/Zoom)을 배열 맨 끝에 추가하는 방식으로 넣어서, 팀원이 다른 액션을 추가해도 append라면 충돌 적음. **직접 수정하지 말고 Unity Input Actions 에디터로 열어서 추가할 것** (수동 JSON 편집은 위험) |
| `ProjectSettings/TagManager.asset` | 낮음~중간 | 태그/레이어 추가 시 겹치면 충돌 (현재 diff는 이번 세션 작업 아님, 이미 변경돼 있던 상태) |

### ✅ 상대적으로 안전한 파일 (맵 전용 영역)
`CameraFollow.cs` / `CameraZoom.cs` / `CameraRotate.cs` / `BuildingInterior.cs`, `Assets/KenneyAssets/*`, `Assets/IgniteCoders/*`, `Assets/Terrain/*`, `Assets/Textures/*`, `Assets/Materials/*Water*`, `Assets/Materials/MapGuideMaterial.mat`, `Assets/Scripts/Editor/_Temp*.cs` — 팀원 작업(UI/인벤토리/무기)과 겹칠 이유가 없는 파일들.

**`_Temp*.cs` 스크립트들은 "일회성, 확인 후 삭제" 용도로 만들어진 것.** 커밋 전에 실행 결과를 확인하고, 계속 필요 없으면 삭제 후 커밋하는 게 좋음 (안 그러면 팀원이 헷갈릴 수 있음).

### 병합 실전 팁
1. **자주 커밋/푸시**, 특히 `main.unity`를 건드린 날은 그날 안에 푸시해서 diverge 기간을 최소화
2. 씬을 동시에 만지는 시간대를 서로 피하거나, 미리 Slack/카톡 등으로 "지금 씬 작업 중" 알리기
3. 가능하면 UI/캐릭터 관련 작업은 **Prefab으로 분리**해서 씬에는 프리팹 참조만 남게 하면 충돌 표면적이 줄어듦
4. `main.unity` 충돌이 나면 `git checkout --ours/--theirs`로 섣불리 해결하지 말고, Unity Editor에서 두 버전을 열어보고 수동으로 합치거나 UnityYAMLMerge 사용

---

## 📌 프로젝트 개요

### 게임 컨셉
- **장르:** 탑뷰(Top-down) 좀비 생존 슈팅 게임
- **목표:** 바이러스 오염 도시에서 7일(7사이클) 이내 탈출
- **핵심 차별점:** ML-Agents 기반 강화학습(PPO) AI 도입 예정 (현재 FSM 기반)

### 팀 역할 분담
| 담당자 | 담당 영역 |
|--------|----------|
| **김호영 (본인)** | 맵 디자인, 좀비 AI/스폰, 시각 효과 (X-Ray, 건물 차폐), Git LFS 협업 환경, 보고서/논문 |
| **도의준 (동료)** | 인벤토리, 아이템/탄약 데이터, 루팅 상자 시스템, 무기 데이터, 무기 애니메이션 |

---

## 🛠️ 기술 스택

| 구분 | 사용 기술 |
|------|----------|
| **게임 엔진** | Unity 6 (6000.3.11f1) |
| **렌더링** | URP (Universal Render Pipeline) |
| **개발 언어** | C# (게임), Python (학습 예정) |
| **AI/ML** | ML-Agents Toolkit (예정), PPO 알고리즘 |
| **버전 관리** | GitHub + **Git LFS** (100MB+ 씬 파일용) |
| **주요 에셋** | Polygon City Pack (Wand and Circles) |
| **UI/폰트** | TextMeshPro + H2HDRM.TTF (한글) |

---

## 📁 내가 만든 파일들 (본인 작업)

### 🧟 좀비 시스템
- `Assets/Scripts/ZombieController.cs` — 좀비 AI (배회/추적/공격, NavMesh 자가복구, 가짜 죽음 모션)
- `Assets/Scripts/ZombieSpawner.cs` — 실내/실외 분리 스폰, 낮밤 차등 강도, 버스트 스폰
- `Assets/Scripts/Editor/ZombieSetup.cs` — 좀비 자동 셋업 에디터 도구

### 🎨 시각 효과 시스템
- `Assets/Shaders/PlayerXRay.shader` — 벽 너머 캐릭터 텍스처 표시 (Stencil 기반)
- `Assets/Shaders/PlayerStencilWriter.shader` — X-Ray 마스킹용 (자기 자신 제외)
- `Assets/Scripts/PlayerXRay.cs` — X-Ray 고스트 자동 생성 (무기 장착 자동 감지)
- `Assets/Scripts/BuildingInterior.cs` — 건물 진입 시 천장/벽 자동 제거 (Project Zomboid 스타일)
- `Assets/Scripts/CameraObstacleFader.cs` — 백업 카메라 차폐 시스템
- `Assets/Scripts/FaceCamera.cs` — World Space UI가 항상 카메라 향하게

### 🎁 리소스
- `Assets/Prefab/Female_A.prefab`, `Female_B.prefab`, `Male_B.prefab` — 좀비 프리팹 3종
- `Assets/Resources/KoreanFont.asset` — 한글 폰트

### 📊 보고서/PPT
- `Report/generate.js` — 주간 보고서 PPT 생성 스크립트
- `Report/generate_map.js` — 맵 디자인 보고서 PPT
- `Report/주간보고서_2026_05.pptx`
- `C:/Users/HY/OneDrive/바탕 화면/맵디자인_보고서.pptx`
- `C:/Users/HY/OneDrive/바탕 화면/캡스톤 디자인 기말 논문.hwpx` — 논문

---

## 🤝 동료가 만든 파일들 (참고용)

### 인벤토리/아이템 시스템
- `Assets/Scripts/Items/InventoryManager.cs`
- `Assets/Scripts/Items/AmmoInventory.cs`
- `Assets/Scripts/Items/ItemData.cs`
- `Assets/Scripts/Items/PickupItem.cs`
- `Assets/Scripts/Items/ThrowableItem.cs`

### 무기 시스템
- `Assets/Scripts/Weapon/Weapon.cs`
- `Assets/Scripts/Weapon/MeleeWeapon.cs`
- `Assets/Scripts/Weapon/WeaponData.cs`
- `Assets/Scripts/Weapon/Bullet.cs`
- `Assets/Scripts/WeaponHandAttacher.cs` — **최근 대폭 개선** (0726 애니메이션 업데이트)

### 루팅 상자
- `Assets/Scripts/Items/LootCrate.cs`
- `Assets/Scripts/Items/LootTable.cs`
- `Assets/Scripts/Items/Rarity.cs`
- `Assets/Prefab/LootCrate_Common.prefab`
- `Assets/Items/LootTable/LootTable_Common.asset`

### UI
- `Assets/Scripts/UI/WeaponHotbarUI.cs` — 5칸 무기 핫바
- `Assets/Scripts/UI/InventoryUI.cs`
- `Assets/Scripts/UI/AmmoUI.cs`

---

## 🎯 지금까지 정한 것 (설계 결정)

### 게임 규칙
- **낮/밤 사이클:** 15분 (낮 8분 → 노을 1분 → 밤 5분 → 새벽 1분)
- **총 플레이 시간:** 7일 × 15분 = 약 105분
- **좀비 유형:** 일반 / 달리기 / 탱커 3종
- **적:** 좀비 + 적대적 생존자 (후반부)

### 좀비 스폰 (현재 설정값)
- **낮 실외:** 6초마다 3~4마리 (최대 15~30마리)
- **밤 실외:** 3초마다 3~4마리 (최대 30마리)
- **밤 실내:** 2초마다 2~3마리 (건물 안에서 폭주)
- **스폰 지점 분산:** 4m 반경 랜덤

### 무기 스탯
| 무기 | 데미지 | 장탄수 | 특징 |
|------|--------|--------|------|
| AR | 25 | 30 | 자동 연사, 중거리 |
| 권총 | 20 | 12 | 단발, 정확도 |
| 샷건 | 60 | 6 | 산탄 8발, 근거리 |
| 근접 | 50 | - | 무한, 1.5m |

### 시스템 아키텍처 (3계층)
1. **Game Layer** — Unity 6 (플레이어, 환경, 게임 로직)
2. **AI Layer** — FSM + ML-Agents (병행 운용, 폴백 구조)
3. **Training Layer** — Python/PyTorch + TensorBoard (예정)

### 맵 워크플로우 (2단계)
1. **손그림 스케치** — 공원, 강, 다리, 시설 배치 결정
2. **Unity 씬 적용** — Polygon City Pack 배치 + NavMesh Bake
   - 지역: 학교, 종합병원, 경찰서, 소방서, 식료품점, 공원 2개
   - 강 = 자연 장벽, 다리 = 병목 전투 지점

---

## 🚨 진행 중 이슈 / 미해결 문제

### ⚠️ 좀비 애니메이션
- **문제:** Walk 애니메이션이 재생 안 됨 (좀비가 미끄러지듯 이동)
- **원인:** ZombieAnimator 컨트롤러의 Walk State에 Motion(walk_64f 클립) 미할당 가능성
- **해결 방법:** `Assets/Character/Zombie/ZombieAnimator.controller` 열어서 Walk State → Motion 필드에 `walk_64f` 클립 드래그
- **대안:** `Tools → Setup Zombie (Selected)` 다시 실행

### ⚠️ 좀비 죽음 애니메이션
- **현재:** 코루틴으로 절차적 처리 (뒤로 쓰러짐 + 땅 아래로 가라앉음)
- **개선안:** Mixamo에서 무료 좀비 죽음 애니메이션 다운로드 → 정식 애니메이션으로 교체

### ⚠️ X-Ray 시안색 잔상 (Mac에서만)
- 커스텀 셰이더가 Mac Metal에서 완벽 호환 안 됨
- Windows는 정상 작동

---

## 📋 다음 할 일 (우선순위별)

### 🥇 1순위 — 완성도 마무리
1. **좀비 Walk 애니메이션 수정** (Motion 클립 할당)
2. **미니맵 UI 구현**
   - 방식: 두 번째 카메라 + RenderTexture, 또는 정적 맵 이미지 + 좌표 변환
   - 스타일: Project Zomboid 스타일 종이 지도 or 사이버펑크 HUD
   - 요소: 나침반, 플레이어(초록), 좀비(빨강), POI 마커
3. **PlayerHUD 정리** (체력바, 탄약, 낮/밤 인디케이터)

### 🥈 2순위 — 콘텐츠 확장
4. **강 + 다리 구현**
   - 에셋: Low Poly Water - Poseidon (추천)
   - Spline 기반 강 모델링, Polygon City Bridge 프리팹
5. **적대적 생존자 AI**
   - 총기 사용, 엄폐 행동
   - Day 3부터 등장
6. **스토리 시스템**
   - 맵 곳곳 메모/쪽지 배치
   - 4개 열쇠 카드 수집 → 탈출 조건

### 🥉 3순위 — ML-Agents 학습 (핵심 목표)
7. **ML-Agents 환경 구축**
   - Python 학습 환경 세팅
   - PPO 하이퍼파라미터 설정
8. **강화학습 AI 학습 실행**
   - Unity 가속 시뮬레이션 (수만 에피소드)
   - TensorBoard로 보상 곡선 모니터링
9. **FSM vs RL 성능 비교**
   - 정량 지표: 처치율, 추적 시간, 회피 성공률
10. **Day별 난이도 파라미터 튜닝**
    - ScriptableObject 기반 데이터화
    - 플레이 테스트

### 🏅 4순위 — 마무리
11. **캡스톤 결과 보고서** 완성
12. **논문** 최종 마무리 (실험 결과 포함)
13. **발표 자료** (PPT) 최종본

---

## 🔧 개발 환경 설정

### Git 설정
- **Git LFS 필수** (씬 파일 100MB+)
- 처음 클론 시: `git lfs install && git lfs pull`
- `.gitattributes`에 `*.unity, *.fbx, *.psd, *.wav, *.mp3, *.mp4, *.blend` 등록됨

### 주요 폴더 구조
```
Assets/
├── Character/Zombie/      — 좀비 FBX + 애니메이션
├── Items/                 — ScriptableObject (Ammo, Heal, LootTable 등)
├── Main Project/          — 메인 씬 (main.unity, LFS로 관리)
├── Prefab/                — 프리팹 (좀비, 무기, 아이템, 상자)
├── Scripts/               — C# 스크립트
│   ├── Editor/           — 에디터 도구 (ZombieSetup)
│   ├── Items/            — 인벤토리, 상자, 픽업
│   ├── UI/               — 핫바, 인벤토리 UI
│   └── Weapon/           — 무기, 총알, 근접
├── Shaders/               — 커스텀 셰이더 (X-Ray, Stencil)
├── Wand and Circles/      — Polygon City Pack
└── Fonts/                 — H2HDRM.TTF (한글 폰트)
```

---

## 📊 산출물 (지금까지)

### 코드 통계
- C# 스크립트: 30+ 개
- 커스텀 셰이더: 2개
- 좀비 프리팹: 3종
- 무기 프리팹: 4종 (AR_A_1, AR_D, Pistol_K, ShotGun_A)

### 문서
- 캡스톤 디자인 기말 논문 (진행 중, 약 60% 완성)
- 주간 보고서 PPT 2개 (2026.05, 2026.06)
- 이 인수인계 문서

---

## 🎨 참고 자료 / 에셋 링크

### 사용 중
- **Polygon City Pack** (Wand and Circles) — 도시 환경
- **H2HDRM.TTF** — 한글 폰트

### 추후 도입 예정
- **Low Poly Water - Poseidon** — 강/물 셰이더
- **Mixamo Zombie Death** — 죽음 애니메이션
- **Kevin Iglesias Human Animations** — 이미 임포트됨

### 참고 게임 (레퍼런스)
- Project Zomboid (탑뷰 좀비 생존, 시각 시스템)
- 7 Days to Die (낮밤 사이클)
- State of Decay (맵 UI)

---

## 💬 Claude Code 사용 팁

### 전역 규칙 (이미 적용됨)
- `C:\Users\HY\.claude\CLAUDE.md` — Karpathy 스타일 가이드라인
  - Think Before Coding
  - Simplicity First
  - Surgical Changes
  - Goal-Driven Execution

### 이 프로젝트에서 자주 쓸 명령
```
지금 프로젝트 상태 봐줘
좀비 [파일명] 수정해줘
동료가 뭐 새로 올렸는지 확인해줘
GitHub에 커밋해줘 (메시지: ...)
```

### 주의사항
- **동료 작업 있으면 반드시 pull 먼저** (특히 WeaponHandAttacher.cs, Weapon.cs)
- **씬 파일 수정 시 반드시 Unity 저장 → git commit**
- **큰 에셋 추가 시 Git LFS 확인** (`.gitattributes` 매치되는지)

---

## 🎯 이 문서의 활용

**Claude Code에서 새 세션 시작 시 첫 명령:**
```
HANDOVER.md 읽어봐
```

그러면 지금까지의 컨텍스트를 그대로 이어받아 작업 가능!

---

## 📞 긴급 참고

- **원격 저장소:** https://github.com/gedfsg/test
- **씬 파일 열 때:** `Assets/Main Project/main.unity`
- **문제 발생 시:** 이 문서의 "진행 중 이슈" 섹션 확인

---

*이 문서는 Claude.ai 웹에서 작성됨. 이후 작업은 Claude Code 터미널에서 진행 예정.*
