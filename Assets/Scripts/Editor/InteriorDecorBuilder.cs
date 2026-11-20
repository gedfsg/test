using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 건물 실내 인테리어 배치 툴 (2026-11-18). 메뉴 Tools → Interior → Build ... (건물별 / 전체)
///
/// 구조
///  - 씬 루트에 "Building_Interior_[건물명]" 빈 오브젝트를 만들고, 그 아래 방/구역별 그룹(Living, Bedroom ...)에 가구를 넣음
///  - 다시 실행하면 이 툴이 만든 "구역 그룹"만 지우고 새로 만듦. 사용자가 루트 바로 아래나 다른 이름 그룹에
///    수동으로 추가한 오브젝트는 그대로 둠 (GroupNames에 있는 이름만 지움)
///  - 건물 오브젝트의 자식으로 넣지 않는 이유: LootDatabase.Classify가 건물 자식 메시 이름으로 건물 종류를 판별해서
///    (예: 경찰차 메시 "police"가 들어가면 민가가 경찰서로 바뀜) 판별이 꼬일 수 있음
///
/// 배치 규칙 (Place)
///  - 에셋(팩)에서 node 경로에 해당하는 부분만 꺼내 래퍼 오브젝트에 담고, fit 규칙(높이/길이/배율)으로 실제 크기에 맞춤
///  - 래퍼의 피벗 = 바닥 중심 → 회전(yaw)·넘어뜨리기(tilt) 후 렌더러 바운드 최저점을 바닥 높이에 다시 맞춤 (공중에 뜨거나 묻히지 않게)
///  - solid: BoxCollider (플레이어·총알 막음, NavMesh 굽기에도 장애물로 반영), obstacle: NavMeshObstacle(Carve)
///  - 모든 가구 Static(Batching/Occludee) 체크. 2.3m 넘는 큰 가구는 Occludable 레이어(10) → OccluderFader가 카메라 가림 시 반투명 처리
///  - 바닥 높이: 건물마다 Play 모드에서 실측한 값(FloorY)을 씀. 런타임에만 생기는 벽 콜라이더(BuildingWallCollider)를
///    에디터에서 임시로 만들어 벽 겹침 검사에도 씀
/// </summary>
public static class InteriorDecorBuilder
{
    const string IA = "Assets/Interior Assets/";
    const string KF = IA + "_Kenney_Furniture/FBX format/";
    const string KS = IA + "_Kenney_Survival/FBX format/";

    // ───────────────────────── 에셋 라이브러리 ─────────────────────────

    public enum Fit { None, Height, Length, Scale }

    public class Spec
    {
        public string path;        // 에셋 경로
        public string node;        // 팩 안에서 꺼낼 노드 경로(끝부분 일치, '/' 구분). null = 통째로
        public string[] exclude;   // 이름에 이 문자열이 들어간 노드는 버림 (예: 팩에 딸린 스카이 구체)
        public Fit fit;            // 크기 맞춤 방식
        public float size;         // Height/Length: 목표 m, Scale: 배율
        public float yawOffset;    // 모델 정면을 +Z(래퍼 forward)로 맞추는 보정 각
        public Vector3 baseEuler;  // 모델을 세우는 보정 회전 (누워 있는 평면 등)
        public bool solid = true;  // BoxCollider
        public bool obstacle;      // NavMeshObstacle(Carve)
        public bool decal;         // 바닥 데칼 (콜라이더·그림자 없음, 바닥 위 1.5cm)
        public bool noShadow;      // 그림자 안 드리움 (작은 소품)
        public bool meshCollider;  // 렌더 메시 그대로 MeshCollider (방 통째 모델: 교실 벽·책상)
        public bool resetYaw;      // 팩 안에서 노드가 돌아가 있는 차(Proop3): 회전을 (270,0,0)으로 되돌려 길이축 = Z
        public Color tint;         // a>0이면 모든 재질을 이 색 Standard 재질로 교체 (텍스처 없는 흰 모델용: 교회 벤치·설교대)
    }

    static Spec S(string path, string node, Fit fit, float size, float yawOffset = 0f, bool solid = true, bool obstacle = false)
        => new Spec { path = path, node = node, fit = fit, size = size, yawOffset = yawOffset, solid = solid, obstacle = obstacle };

    /// <summary>작은 소품 (콜라이더 없음, 그림자 없음)</summary>
    static Spec Small(string path, string node, Fit fit, float size, float yawOffset = 0f)
        => new Spec { path = path, node = node, fit = fit, size = size, yawOffset = yawOffset, solid = false, noShadow = true };

    static Spec Decal(string path, string node, float length)
        => new Spec { path = path, node = node, fit = Fit.Length, size = length, solid = false, decal = true, noShadow = true };

    // Kenney Furniture Kit는 모델끼리 비율이 맞음 → 한 배율로 통일 (원본이 약 5배 큼: 소파 폭 1.86m·변기 0.81m·의자 0.89m가 되는 배율, 카탈로그 실측)
    // Kenney 모델은 정면이 -Z (소파·침대·의자 등받이, 변기 물탱크가 +Z 쪽 — 메시 분석 실측) → 기본 180° 보정
    const float KenneyFurnitureScale = 0.19f;
    static Spec KFur(string name, bool solid = true, bool obstacle = false, float yawOffset = 180f)
        => new Spec { path = KF + name + ".fbx", fit = Fit.Scale, size = KenneyFurnitureScale, solid = solid, obstacle = obstacle, yawOffset = yawOffset, noShadow = !solid };
    const float KenneySurvivalScale = 2.5f;
    static Spec KSur(string name, bool solid = true, bool obstacle = false)
        => new Spec { path = KS + name + ".fbx", fit = Fit.Scale, size = KenneySurvivalScale, solid = solid, obstacle = obstacle, noShadow = !solid };

    public static readonly Dictionary<string, Spec> Lib = new Dictionary<string, Spec>
    {
        // ── Kenney Furniture (공용) ──
        { "sofa",          KFur("loungeSofa", true, true) },
        { "sofaLong",      KFur("loungeSofaLong", true, true) },
        { "armchair",      KFur("loungeChair", true) },
        { "bedDouble",     KFur("bedDouble", true, true) },
        { "bedSingle",     KFur("bedSingle", true, true) },
        { "bedBunk",       KFur("bedBunk", true, true) },
        { "sideTable",     KFur("sideTableDrawers", true) },
        { "table",         KFur("table", true, true) },
        { "tableCoffee",   KFur("tableCoffee", true) },
        { "tableRound",    KFur("tableRound", true, true) },
        { "kitchenBar",    KFur("kitchenBar", true, true) },
        { "bookcaseClosed", KFur("bookcaseClosed", true, true) },
        { "washer",        KFur("washer", true) },
        { "plantSmall",    KFur("plantSmall1", false) },
        { "chair",         KFur("chair", true) },
        { "chairDesk",     KFur("chairDesk", true) },
        { "desk",          KFur("desk", true, true) },
        { "bookcase",      KFur("bookcaseOpen", true, true) },
        { "bookcaseWide",  KFur("bookcaseClosedWide", true, true) },
        { "kitchenCab",    KFur("kitchenCabinet", true, true) },
        { "kitchenDrawer", KFur("kitchenCabinetDrawer", true, true) },
        { "kitchenSink",   KFur("kitchenSink", true, true) },
        { "kitchenStove",  KFur("kitchenStove", true, true) },
        { "fridge",        KFur("kitchenFridgeLarge", true, true) },
        { "toilet",        KFur("toilet", true) },
        { "bathSink",      KFur("bathroomSink", true) },
        { "bathtub",       KFur("bathtub", true, true) },
        { "shower",        KFur("shower", true, true) },
        { "tvVintage",     KFur("televisionVintage", false) },
        { "lampFloor",     KFur("lampRoundFloor", false) },
        { "lampTable",     KFur("lampRoundTable", false) },
        { "trashcan",      KFur("trashcan", false) },
        { "books",         KFur("books", false) },
        { "plant",         KFur("pottedPlant", false) },
        { "coatRack",      KFur("coatRackStanding", false) },
        { "kBoxClosed",    KFur("cardboardBoxClosed", false) },
        { "kBoxOpen",      KFur("cardboardBoxOpen", false) },
        { "radio",         KFur("radio", false) },
        { "laptop",        KFur("laptop", false) },
        { "pillow",        KFur("pillow", false) },
        { "rugDoormat",    new Spec { path = KF + "rugDoormat.fbx", fit = Fit.Scale, size = KenneyFurnitureScale, solid = false, decal = true, noShadow = true } },
        { "wall",          KFur("wall", true, true) },    // 칸막이 벽 (민가 방 나누기, NavMesh Carve)
        { "wallDoorway",   KFur("wallDoorway", true) },   // 문 뚫린 칸막이 벽
        { "wallHalf",      KFur("wallHalf", true) },
        // 학교: 칠판 = Kenney 벽을 짙은 녹색으로 얇게 (칠판 에셋 없음), 사물함 = 닫힌 책장을 철제 회청색으로
        { "blackboard",    new Spec { path = KF + "wall.fbx", fit = Fit.Scale, size = KenneyFurnitureScale, yawOffset = 180f, solid = false, noShadow = true, tint = new Color(0.12f, 0.24f, 0.18f) } },
        { "locker",        new Spec { path = KF + "bookcaseClosedDoors.fbx", fit = Fit.Scale, size = KenneyFurnitureScale, yawOffset = 180f, obstacle = true, tint = new Color(0.42f, 0.50f, 0.56f) } },
        { "cellBed",       KFur("bedSingle", false) },   // 유치장 안 간이침대 (철창 콜라이더 안이라 콜라이더 없음)

        // ── Kenney Survival (공용) ──
        { "box",           KSur("box", true) },
        { "boxLarge",      KSur("box-large", true, true) },
        { "boxOpen",       KSur("box-open", true) },
        { "barrel",        KSur("barrel", true) },
        { "barrelOpen",    KSur("barrel-open", true) },
        { "planks",        KSur("resource-planks", false) },
        { "barricade",     KSur("fence-fortified", true, true) },
        { "bedroll",       KSur("bedroll", false) },
        { "campfire",      KSur("campfire-pit", false) },
        { "chest",         KSur("chest", true) },
        { "workbench",     KSur("workbench", true, true) },
        { "bucket",        KSur("bucket", false) },
        { "bottle",        KSur("bottle-large", false) },

        // ── 민가 (House) ──
        { "closet",        S(IA + "House/old_shelves_tv_stand_and_closets.glb", "closet", Fit.None, 1f, 0f, true, true) },
        { "bookShelfOld",  S(IA + "House/old_shelves_tv_stand_and_closets.glb", "book shelf", Fit.None, 1f, 0f, true, true) },
        { "tvStand",       S(IA + "House/old_shelves_tv_stand_and_closets.glb", "Object010", Fit.None, 1f, 0f, true, true) },
        { "lowCabinet",    S(IA + "House/old_shelves_tv_stand_and_closets.glb", "closet004", Fit.None, 1f, 0f, true) },
        { "rugA",          Decal(IA + "House/low-poly_psx_style_vintage_rugs_pack.glb", "Vintage Rugs_001", 2.83f) },
        { "rugB",          Decal(IA + "House/low-poly_psx_style_vintage_rugs_pack.glb", "Vintage Rugs_004", 2.83f) },
        { "rugC",          Decal(IA + "House/low-poly_psx_style_vintage_rugs_pack.glb", "Vintage Rugs_008", 2.83f) },
        { "rugD",          Decal(IA + "House/low-poly_psx_style_vintage_rugs_pack.glb", "Vintage Rugs_012", 2.83f) },

        // ── 교회 ──
        { "churchBench",   new Spec { path = IA + "church/ChurchbenchSet.fbx", fit = Fit.Length, size = 2.6f, yawOffset = 90f, obstacle = true, tint = new Color(0.36f, 0.23f, 0.14f) } },   // 등받이 +X → 90°, 텍스처 없는 흰색 → 짙은 나무색
        { "tribune",       new Spec { path = IA + "church/tribune.fbx", fit = Fit.Height, size = 1.5f, obstacle = true, tint = new Color(0.30f, 0.19f, 0.11f) } },
        { "statue",        S(IA + "church/hl._brigitta.glb", null, Fit.Height, 2.2f, 0f, true, true) },
        { "candles",       Small(IA + "church/revenant__crosses_and_candles.glb", "Object_5", Fit.Height, 0.35f) },
        { "crossA",        S(IA + "church/revenant__crosses_and_candles.glb", "Object_3", Fit.Height, 1.6f, 0f, true) },

        // ── 병원 ──
        { "medCart",       S(IA + "Hospital/medical_props.glb", null, Fit.Height, 1.0f, 0f, true) },          // 트롤리(prop) + 수술 도구(Circle.007) 통째로
        { "curtain",       S(IA + "Hospital/Curtains.fbx", null, Fit.Height, 2.4f, 90f, false) },

        // ── 마트 ──
        { "martFreezer",   S(IA + "Mart/low_poly_store_shelves_and_supermarket_rack_pack.glb", "Cube.049_3", Fit.None, 1f, 0f, true, true) },
        { "martShelfLow",  S(IA + "Mart/low_poly_store_shelves_and_supermarket_rack_pack.glb", "Cube.027_28", Fit.None, 1f, 0f, true, true) },
        { "martGondola",   S(IA + "Mart/low_poly_store_shelves_and_supermarket_rack_pack.glb", "Cube.031_22", Fit.None, 1f, 0f, true, true) },
        { "martWallShelf", S(IA + "Mart/low_poly_store_shelves_and_supermarket_rack_pack.glb", "Cube.016_39", Fit.None, 1f, 0f, true, true) },
        { "martFridge",    S(IA + "Mart/low_poly_store_shelves_and_supermarket_rack_pack.glb", "Cube.018_37", Fit.None, 1f, 0f, true, true) },
        { "martTall",      S(IA + "Mart/low_poly_store_shelves_and_supermarket_rack_pack.glb", "Cube.007_51", Fit.None, 1f, 0f, true, true) },
        { "martCounter",   S(IA + "Mart/low_poly_store_shelves_and_supermarket_rack_pack.glb", "Cube_61", Fit.None, 1f, 0f, true, true) },
        { "martCounter2",  S(IA + "Mart/low_poly_store_shelves_and_supermarket_rack_pack.glb", "Cube.005_53", Fit.None, 1f, 0f, true, true) },
        { "martRack",      S(IA + "Mart/low_poly_store_shelves_and_supermarket_rack_pack.glb", "Cube.011_46", Fit.None, 1f, 0f, true) },
        { "martBin",       S(IA + "Mart/low_poly_store_shelves_and_supermarket_rack_pack.glb", "Cube.036_18", Fit.None, 1f, 0f, true) },
        { "martSmall",     S(IA + "Mart/low_poly_store_shelves_and_supermarket_rack_pack.glb", "Cube.040_12", Fit.None, 1f, 0f, true) },
        { "martTall2",     S(IA + "Mart/low_poly_store_shelves_and_supermarket_rack_pack.glb", "Cube.020_35", Fit.None, 1f, 0f, true, true) },
        // Kenney Food Kit (마트 바닥에 흩어진 상품)
        { "foodCan",       Small(IA + "Mart/food_kit/FBX format/can.fbx", null, Fit.Height, 0.13f) },
        { "foodBag",       Small(IA + "Mart/food_kit/FBX format/bag.fbx", null, Fit.Height, 0.30f) },
        { "foodCarton",    Small(IA + "Mart/food_kit/FBX format/carton.fbx", null, Fit.Height, 0.24f) },
        { "foodBottle",    Small(IA + "Mart/food_kit/FBX format/bottle-oil.fbx", null, Fit.Height, 0.28f) },
        { "foodCandy",     Small(IA + "Mart/food_kit/FBX format/candy-bar-wrapper.fbx", null, Fit.Length, 0.16f) },
        { "snackShelf",    S(IA + "Mart/snack_aisle_shelf_3d_model.glb", null, Fit.Height, 1.8f, 90f, true, true) },  // 진열면이 ±X → 90°

        // ── 경찰서 / 사무실 ──
        { "pcDesk",        S(IA + "Police/LowPolyComputerDesk.fbx", null, Fit.None, 1f, 0f, true, true) },
        { "officeDesk",    new Spec { path = IA + "Police/LowPolyOffice.fbx", node = "desk", fit = Fit.Scale, size = 10f, obstacle = true, tint = new Color(0.47f, 0.33f, 0.21f) } },  // 텍스처 없는 회색 → 나무색
        { "officeChair",   S(IA + "Police/LowPolyOffice.fbx", "chair", Fit.Scale, 10f, 180f, true) },
        { "officeDrawer",  new Spec { path = IA + "Police/LowPolyOffice.fbx", node = "drawer", fit = Fit.Scale, size = 10f, tint = new Color(0.40f, 0.43f, 0.46f) } },   // 철제 서류함 색
        { "retroChair",    S(IA + "Police/90s_retro_office_pack.glb", "Chair 1.001_65", Fit.None, 1f, 90f, true) },
        { "cubicleWall",   S(IA + "Police/90s_retro_office_pack.glb", "Cubicle_Wall.004_97", Fit.None, 1f, 0f, true, true) },
        { "printer",       S(IA + "Police/90s_retro_office_pack.glb", "Printer Base.004_48", Fit.None, 1f, 0f, true) },
        { "cage",          S(IA + "Police/low-poly_prison_cage__free_game_asset.glb", null, Fit.Height, 2.6f, 0f, true, true) },
        { "waterCooler",   S(IA + "Police/water_cooler.glb", null, Fit.Height, 1.3f, 0f, true) },
        { "policeCar",     S(IA + "Police/police_car_-_low_poly.glb", null, Fit.Length, 10.5f, 0f, true, true) },   // 맵 축척(기존 차 11.7m)에 맞춤

        // ── 학교 ──
        { "classroom",     new Spec { path = IA + "school/clasroom.fbx", fit = Fit.None, size = 1f, solid = false, meshCollider = true } }, // 벽·책상·칠판이 메시 1개인 교실 통째 → 메시 콜라이더

        // ── 창고 ──
        { "rack",          new Spec { path = IA + "Warehouse/warehouse_storage_racking_fbx_low_poly_free.glb", exclude = new[] { "Sphere" }, fit = Fit.Height, size = 3.2f, solid = true, obstacle = true } },
        { "rackSmall",     S(IA + "Warehouse/warehouse_low_poly_game_assets.glb", "shelf", Fit.None, 1f, 0f, true, true) },
        { "pallet",        S(IA + "Warehouse/warehouse_low_poly_game_assets.glb", "pallet", Fit.None, 1f, 0f, false) },
        { "wBarrel",       S(IA + "Warehouse/warehouse_low_poly_game_assets.glb", "barrel", Fit.None, 1f, 0f, true) },
        { "wBox",          S(IA + "Warehouse/warehouse_low_poly_game_assets.glb", "box", Fit.None, 1f, 0f, true) },
        { "container",     S(IA + "Warehouse/Cargo Container.fbx", null, Fit.Length, 6.1f, 0f, true, true) },
        { "drum",          S(IA + "Warehouse/Бочки.fbx", "Cylinder", Fit.Height, 0.9f, 0f, true) },
        { "forklift",      S(IA + "Warehouse/warehouse_forklift_gameready.glb", null, Fit.Length, 3.0f, 0f, true, true) },
        { "exhaustFan",    new Spec { path = IA + "Warehouse/low_poly_exhaust_fan_with_motor.glb", fit = Fit.Height, size = 1.3f, solid = false } },
        { "chainFence",    S(IA + "Warehouse/chain_link_fence_pack_low_poly_game_ready.glb", null, Fit.None, 1f, 0f, true) },

        // ── 거리 / 미션 ──
        { "generator",     S(IA + "street/Generator.fbx", null, Fit.Length, 1.4f, 0f, true, true) },
        { "jerrycan",      Small(IA + "street/Jerrycan_Rusted.fbx", null, Fit.Height, 0.47f) },
        { "gasPump",       S(IA + "street/gas_station_stand__high-quality_3d_model.glb", null, Fit.Height, 1.9f, 0f, true, true) },
        { "doorPanel",     new Spec { path = IA + "street/digital_door_lock_access_panel.glb", node = "Plane_0", fit = Fit.Length, size = 0.25f, baseEuler = new Vector3(-90, 0, 0), solid = false, noShadow = true } },
        { "keyCard",       new Spec { path = IA + "street/digital_door_lock_access_panel.glb", node = "Plane.001_1", fit = Fit.Length, size = 0.09f, solid = false, noShadow = true } },
        // WallVine.fbx는 텍스처가 없어 흰 줄기로만 보임 → 텍스처 있는 vines.glb를 크게 써서 대신함 (원본 키 이름 유지)
        { "wallVine",      new Spec { path = IA + "street/vines.glb", fit = Fit.Height, size = 3.0f, solid = false, noShadow = true } },
        { "vines",         new Spec { path = IA + "street/vines.glb", fit = Fit.Height, size = 2.4f, solid = false, noShadow = true } },
        { "pillarDebris",  new Spec { path = IA + "street/15043711d14f454dbe3fc30552a040d5.fbx.fbx", exclude = new[] { "pPlane1" }, fit = Fit.Length, size = 1.6f, solid = true } }, // 바닥 회색판 제외
        { "deadBody",      S(IA + "street/low_poly_dead_body_covered_game_ready.glb", null, Fit.Length, 1.9f, 0f, false) },
        { "blood",         Decal(IA + "street/blood_decals_psx.glb", "Plane_0", 1.6f) },
        { "blood2",        Decal(IA + "street/blood_decals_psx.glb", "Plane.001_1", 1.6f) },
        { "bloodWall",     new Spec { path = IA + "street/small_blood_splatter.glb", fit = Fit.Height, size = 0.8f, solid = false, noShadow = true } },
        { "jersey",        S(IA + "street/stylized_concrete_barriers__game_ready.glb", "barrier", Fit.Height, 0.85f, 0f, true, true) },
        { "jerseyCaution", S(IA + "street/stylized_concrete_barriers__game_ready.glb", "barrier_caution", Fit.Height, 0.85f, 0f, true, true) },
        { "jerseySmall",   S(IA + "street/stylized_concrete_barriers__game_ready.glb", "barrier_small", Fit.Height, 0.85f, 0f, true, true) },
        { "cBarrier",      S(IA + "street/construction_100_props_asset_pack.glb", "Concrete_Barrier", Fit.None, 1f, 0f, true, true) },
        { "cSign",         S(IA + "street/construction_100_props_asset_pack.glb", "Danger_Sign", Fit.None, 1f, 0f, false) },
        { "cTrafficLight", S(IA + "street/construction_100_props_asset_pack.glb", "Portable_Traffic_Light", Fit.None, 1f, 0f, true) },
        { "cBarrel",       S(IA + "street/construction_100_props_asset_pack.glb", "Plastic_Barrel", Fit.None, 1f, 0f, true) },
        { "cTape",         S(IA + "street/construction_100_props_asset_pack.glb", "Warning_Tape", Fit.None, 1f, 0f, false) },
        { "cFence",        S(IA + "street/construction_100_props_asset_pack.glb", "Mesh_Fencing_Part_2", Fit.None, 1f, 0f, true) },
        { "cityBench",     S(IA + "street/citypack.fbx", "Bench", Fit.None, 1f, 0f, true) },
        { "dumpster",      S(IA + "street/citypack.fbx", "Dumpster", Fit.None, 1f, 0f, true, true) },
        { "hydrant",       S(IA + "street/citypack.fbx", "Fire Hydrant", Fit.Height, 0.8f, 0f, true) },
        { "mailbox",       S(IA + "street/citypack.fbx", "Mail", Fit.Height, 1.2f, 0f, true) },
        { "phoneBooth",    S(IA + "street/citypack.fbx", "Phone", Fit.Height, 2.1f, 0f, true) },
        { "trashBin",      S(IA + "street/citypack.fbx", "TrashBin", Fit.Height, 0.9f, 0f, true) },
        { "newsStand",     S(IA + "street/citypack.fbx", "NewspaperStand", Fit.Height, 1.4f, 0f, true) },
        // Low Poly Cars Pack: Object_2~46 = 차 한 대씩 (차종 5 x 색), Object_47~ = 전시용 바닥판
        { "carA",          S(IA + "street/low_poly_cars_pack.glb", "Object_2", Fit.Length, 4.2f, 0f, true, true) },
        { "carB",          S(IA + "street/low_poly_cars_pack.glb", "Object_4", Fit.Length, 4.2f, 0f, true, true) },
        { "carC",          S(IA + "street/low_poly_cars_pack.glb", "Object_9", Fit.Length, 3.8f, 0f, true, true) },
        { "carD",          S(IA + "street/low_poly_cars_pack.glb", "Object_14", Fit.Length, 4.6f, 0f, true, true) },
        { "carE",          S(IA + "street/low_poly_cars_pack.glb", "Object_20", Fit.Length, 4.4f, 0f, true, true) },
        { "carF",          S(IA + "street/low_poly_cars_pack.glb", "Object_29", Fit.Length, 3.4f, 0f, true, true) },
        { "carG",          S(IA + "street/low_poly_cars_pack.glb", "Object_30", Fit.Length, 2.0f, 0f, true, true) },
        // 거리 차: 기존 씬 차(Proop3, 맵 축척 2.5배 = 길이 약 11.5m)와 같은 모델·크기 (Low Poly Cars Pack은 바퀴·유리가 전시판 메시에 합쳐져 있고 차체가 검게 나와 못 씀)
        { "streetCar0",    new Spec { path = "Assets/Props/Cars/Proop3.fbx", node = "car 1", fit = Fit.Scale, size = 2.5f, resetYaw = true, obstacle = true } },
        { "streetCar1",    new Spec { path = "Assets/Props/Cars/Proop3.fbx", node = "car 2", fit = Fit.Scale, size = 2.5f, resetYaw = true, obstacle = true } },
        { "streetCar2",    new Spec { path = "Assets/Props/Cars/Proop3.fbx", node = "car 3", fit = Fit.Scale, size = 2.5f, resetYaw = true, obstacle = true } },
        { "streetCar3",    new Spec { path = "Assets/Props/Cars/Proop3.fbx", node = "car 1.RC", fit = Fit.Scale, size = 2.5f, resetYaw = true, obstacle = true } },
        { "streetCar4",    new Spec { path = "Assets/Props/Cars/Proop3.fbx", node = "car 2.RC", fit = Fit.Scale, size = 2.5f, resetYaw = true, obstacle = true } },
        { "streetCar5",    new Spec { path = "Assets/Props/Cars/Proop3.fbx", node = "car 1,/2", fit = Fit.Scale, size = 2.5f, resetYaw = true, obstacle = true } },
        { "streetCar6",    new Spec { path = "Assets/Props/Cars/Proop3.fbx", node = "car 2./2", fit = Fit.Scale, size = 2.5f, resetYaw = true, obstacle = true } },
        { "streetCar7",    new Spec { path = "Assets/Props/Cars/Proop3.fbx", node = "car ./", fit = Fit.Scale, size = 2.5f, resetYaw = true, obstacle = true } },
        { "trafficLight",  S(IA + "street/citypack.fbx", "Traffic Light2", Fit.Height, 7.0f, 0f, true) },
        { "antenna",       new Spec { path = KF + "televisionAntenna.fbx", fit = Fit.Height, size = 4.0f, solid = false } },
        { "biohazard",     S(IA + "street/street_signs__stoplight.glb", "Biohazard Sign_0", Fit.Height, 2.1f, 0f, false) },
    };

    // ───────────────────────── 배치 핵심 ─────────────────────────

    /// <summary>이 툴이 만들고 다시 실행 시 지우는 구역 그룹 이름</summary>
    static readonly string[] GroupNames = { "Living", "Bedroom", "Kitchen", "Bathroom", "Clutter", "Lights", "Nave", "Altar", "Ward", "Reception", "Corridor", "Office", "Cells", "Armory", "Aisles", "Checkout", "Storage", "Classroom", "Staffroom", "Hallway", "Exterior", "Partitions", "Cars", "CityProps", "Ruins", "MissionProps", "GasStation", "Wall", "Center" };

    /// <summary>씬 루트의 Building_Interior_[name] (없으면 생성)</summary>
    public static Transform Root(string name) => RootFull("Building_Interior_" + name);

    /// <summary>씬 루트의 이름 그대로 (없으면 생성)</summary>
    public static Transform RootFull(string full)
    {
        var scene = EditorSceneManager.GetActiveScene();
        foreach (var g in scene.GetRootGameObjects()) if (g.name == full) return g.transform;
        var go = new GameObject(full);
        Undo.RegisterCreatedObjectUndo(go, "Build Interior");
        return go.transform;
    }

    /// <summary>루트 아래 이 툴이 만든 구역 그룹을 지움 (수동 추가분은 보존)</summary>
    public static void ClearGenerated(Transform root)
    {
        var names = new HashSet<string>(GroupNames);
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            var c = root.GetChild(i);
            if (names.Contains(c.name)) Object.DestroyImmediate(c.gameObject);
        }
    }

    public static Transform Group(Transform root, string name)
    {
        var t = root.Find(name);
        if (t != null) return t;
        var g = new GameObject(name);
        g.transform.SetParent(root, false);
        return g.transform;
    }

    static Transform FindNode(Transform root, string path)
    {
        // 이름에 '/'가 들어간 노드(Proop3 "car 1,/2")가 있어서 전체 이름 정확히 일치를 먼저, 그다음 경로 끝부분 일치 ("a/b" → .../a/b)
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == path) return t;
        string[] parts = path.Split('/');
        string last = parts[parts.Length - 1];
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != last) continue;
            var p = t.parent; bool ok = true;
            for (int i = parts.Length - 2; i >= 0 && ok; i--, p = p != null ? p.parent : null)
                ok = p != null && p.name == parts[i];
            if (ok) return t;
        }
        return null;
    }

    /// <summary>렌더러 바운드 (월드)</summary>
    public static bool WorldBounds(GameObject go, out Bounds b)
    {
        b = default; bool has = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue; // 연기·불 파티클은 바운드가 커서 제외 (차 크기 계산이 틀어짐)
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        return has;
    }

    /// <summary>렌더러 메시 바운드를 relativeTo 로컬 공간으로 (콜라이더 크기용)</summary>
    static Bounds LocalBounds(GameObject go, Transform relativeTo)
    {
        bool has = false; Bounds lb = default;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = relativeTo.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!has) { lb = new Bounds(p, Vector3.zero); has = true; } else lb.Encapsulate(p);
            }
        }
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var b = smr.bounds;
            var c = relativeTo.InverseTransformPoint(b.center);
            if (!has) { lb = new Bounds(c, Vector3.zero); has = true; }
            lb.Encapsulate(relativeTo.InverseTransformPoint(b.min)); lb.Encapsulate(relativeTo.InverseTransformPoint(b.max));
        }
        return lb;
    }

    /// <summary>
    /// 가구 1개 배치. pos = 바닥 위 중심(y = 바닥 높이), yaw = 정면 방향(0 = +Z), tilt = 넘어뜨리기(로컬 오일러, 예: (0,0,90) 옆으로 넘어짐).
    /// stretch = 모델 로컬 축별 추가 배율 (예: 벽 길이). 반환값 = 래퍼 오브젝트 (이름 = key)
    /// </summary>
    public static GameObject Place(Transform parent, string key, Vector3 pos, float yaw, Vector3 tilt = default, float scaleMul = 1f, string name = null, Vector3? stretch = null)
    {
        if (!Lib.TryGetValue(key, out var s)) { Debug.LogError("[Interior] 라이브러리에 없는 키: " + key); return null; }
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(s.path);
        if (src == null) { Debug.LogError("[Interior] 에셋 없음: " + s.path); return null; }

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
        PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        inst.transform.position = Vector3.zero;

        GameObject content = inst;
        if (!string.IsNullOrEmpty(s.node))
        {
            var t = FindNode(inst.transform, s.node);
            if (t == null) { Debug.LogError($"[Interior] '{s.path}'에 노드 '{s.node}' 없음"); Object.DestroyImmediate(inst); return null; }
            t.SetParent(null, true);
            content = t.gameObject;
            Object.DestroyImmediate(inst);
        }
        if (s.exclude != null)
            foreach (var t in content.GetComponentsInChildren<Transform>(true))
                foreach (var ex in s.exclude)
                    if (t != null && t != content.transform && t.name.Contains(ex)) { Object.DestroyImmediate(t.gameObject); break; }

        // 에셋에 딸려온 콜라이더/조명/카메라/애니메이션 제거 (콜라이더는 아래에서 박스로 새로 붙임)
        foreach (var c in content.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (var c in content.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(c);
        foreach (var c in content.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(c);
        foreach (var c in content.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(c);
        foreach (var c in content.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(c);

        var wrapper = new GameObject(name ?? key);
        content.transform.SetParent(wrapper.transform, true);
        if (s.resetYaw) content.transform.rotation = Quaternion.Euler(270f, 0f, 0f);   // Proop3 차: 메시 Y(길이) → 월드 Z, 메시 Z(위) → 월드 Y
        if (s.baseEuler != Vector3.zero)
            content.transform.rotation = Quaternion.Euler(s.baseEuler) * content.transform.rotation;

        if (!WorldBounds(content, out var b)) { Debug.LogError("[Interior] 렌더러 없음: " + key); Object.DestroyImmediate(wrapper); return null; }
        // 피벗 = 바닥 중심
        content.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);

        float k = 1f;
        switch (s.fit)
        {
            case Fit.Height: k = s.size / Mathf.Max(0.0001f, b.size.y); break;
            case Fit.Length: k = s.size / Mathf.Max(0.0001f, Mathf.Max(b.size.x, b.size.z)); break;
            case Fit.Scale: k = s.size; break;
        }
        // stretch: 로컬 축별 늘리기 (칸막이 벽 길이 맞춤 등, 회전 전 모델 축 기준)
        wrapper.transform.localScale = Vector3.Scale(Vector3.one * (k * scaleMul), stretch ?? Vector3.one);
        wrapper.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(tilt) * Quaternion.Euler(0f, s.yawOffset, 0f);
        wrapper.transform.position = pos;

        // 회전/넘어뜨린 뒤 다시 바닥에 붙이고 중심을 목표 위치로
        WorldBounds(wrapper, out var wb);
        float lift = s.decal ? 0.015f : 0f;
        wrapper.transform.position += new Vector3(pos.x - wb.center.x, pos.y + lift - wb.min.y, pos.z - wb.center.z);

        // 텍스처 없는 흰 모델 → 공용 색 재질 (Assets/Materials/Interior/Tint_RRGGBB.mat, 같은 색이면 재사용 → 배칭)
        if (s.tint.a > 0f)
        {
            var tm = TintMaterial(s.tint);
            foreach (var r in wrapper.GetComponentsInChildren<Renderer>())
            {
                var arr = r.sharedMaterials;
                for (int i = 0; i < arr.Length; i++) arr[i] = tm;
                r.sharedMaterials = arr;
            }
        }

        // 렌더 설정
        foreach (var r in wrapper.GetComponentsInChildren<Renderer>())
        {
            if (s.noShadow || s.decal) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // 방 통째 모델: 렌더 메시 그대로 충돌 (벽·책상 사이로만 다님, NavMesh 굽기에도 반영)
        if (s.meshCollider)
            foreach (var mf in wrapper.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;

        // 콜라이더 / 내비 장애물
        if (s.solid)
        {
            var lb = LocalBounds(wrapper, wrapper.transform);
            var bc = wrapper.AddComponent<BoxCollider>();
            bc.center = lb.center;
            bc.size = new Vector3(lb.size.x * 0.95f, lb.size.y, lb.size.z * 0.95f);
            if (s.obstacle)
            {
                var ob = wrapper.AddComponent<NavMeshObstacle>();
                ob.shape = NavMeshObstacleShape.Box;
                ob.center = bc.center;
                ob.size = bc.size;
                ob.carving = true;
                ob.carveOnlyStationary = true;
            }
        }

        // Static + 큰 가구는 Occludable 레이어 (카메라 가림 시 반투명)
        WorldBounds(wrapper, out wb);
        int occ = LayerMask.NameToLayer("Occludable");
        bool tall = wb.size.y > 2.3f && s.solid && occ >= 0;
        foreach (var t in wrapper.GetComponentsInChildren<Transform>(true))
        {
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            if (tall) t.gameObject.layer = occ;
        }

        wrapper.transform.SetParent(parent, true);
        Undo.RegisterCreatedObjectUndo(wrapper, "Place " + key);
        return wrapper;
    }

    /// <summary>색 재질 에셋 (없으면 생성). GPU Instancing 켬</summary>
    static Material TintMaterial(Color c)
    {
        const string dir = "Assets/Materials/Interior";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Materials", "Interior");
        string path = $"{dir}/Tint_{ColorUtility.ToHtmlStringRGB(c)}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("Standard")) { color = c, enableInstancing = true };
        m.SetFloat("_Glossiness", 0.15f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    /// <summary>방 조명 (Point, intensity 0.8, range 6m, #FFE8B0). 바닥에서 2.6m 높이</summary>
    public static Light RoomLight(Transform parent, Vector3 floorPos, float height = 2.6f, string name = "RoomLight")
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = floorPos + Vector3.up * height;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.intensity = 0.8f;
        l.range = 6f;
        l.color = new Color(1f, 0xE8 / 255f, 0xB0 / 255f);
        l.shadows = LightShadows.None;
        l.renderMode = LightRenderMode.Auto;
        return l;
    }

    // ───────────────────────── 임시 벽 콜라이더 (에디터 검사용) ─────────────────────────

    /// <summary>런타임 벽 콜라이더를 에디터에서 임시 생성 (RebakeNavMeshWithWalls와 같은 방식). 다 쓰면 RemoveTempWalls</summary>
    public static List<MeshCollider> MakeTempWalls()
    {
        var temp = new List<MeshCollider>();
        foreach (var w in Object.FindObjectsByType<BuildingWallCollider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (w.GetComponent<MeshCollider>() != null) continue;
            var mc = w.Build();
            if (mc != null) temp.Add(mc);
        }
        return temp;
    }

    public static void RemoveTempWalls(List<MeshCollider> temp)
    {
        foreach (var mc in temp)
        {
            if (mc == null) continue;
            var mesh = mc.sharedMesh;
            Object.DestroyImmediate(mc);
            if (mesh != null) Object.DestroyImmediate(mesh);
        }
    }

    // ───────────────────────── 빌드 세션 (임시 벽 + 바닥판 임시 콜라이더) ─────────────────────────

    static List<MeshCollider> sessionWalls;
    static readonly List<MeshCollider> sessionPlates = new List<MeshCollider>();

    /// <summary>
    /// 배치 전 준비: 런타임 벽 콜라이더 + 장식 바닥판(Zone_*, 콜라이더 없음, 지면보다 0.2m 높음)에 임시 MeshCollider.
    /// 바닥 높이·벽 위치를 Play 때와 같게 잴 수 있음. 반드시 EndSession으로 정리 (씬에 저장되면 안 됨)
    /// </summary>
    public static void BeginSession()
    {
        EndSession();
        sessionWalls = MakeTempWalls();
        foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
        {
            // 장식 바닥판(Zone_*) + 콜라이더 없는 주유소 모델(사용자가 놓은 low_poly_gas_station: 벽 찾기·겹침 검사용)
            bool gas = mf.transform.root.name == "low_poly_gas_station";
            if ((!mf.name.StartsWith("Zone_") && !gas) || mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            sessionPlates.Add(mc);
        }
        Physics.SyncTransforms();
    }

    public static void EndSession()
    {
        if (sessionWalls != null) RemoveTempWalls(sessionWalls);
        sessionWalls = null;
        foreach (var mc in sessionPlates) if (mc != null) Object.DestroyImmediate(mc);
        sessionPlates.Clear();
    }

    static bool IsInteriorObject(Transform t)
    {
        // 루트 자체(Building_Interior_House 등) 또는 묶음 루트(Building_Interior_Auto) 아래면 이 툴이 놓은 것
        for (; t != null; t = t.parent) if (t.parent == null && t.name.StartsWith("Building_Interior_")) return true;
        return false;
    }

    /// <summary>(x,z)의 바닥 높이: guessY+1.6m에서 아래로 쏴서 처음 닿는 면 (트리거·이 툴이 놓은 가구 제외)</summary>
    public static float FloorAt(float x, float z, float guessY)
    {
        var hits = Physics.RaycastAll(new Vector3(x, guessY + 1.6f, z), Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (var h in hits)
        {
            if (IsInteriorObject(h.collider.transform)) continue;
            if (h.point.y > best) best = h.point.y;
        }
        return float.IsNegativeInfinity(best) ? guessY : best;
    }

    /// <summary>from에서 dir로 쏴서 벽(이 툴 가구 제외)에 닿은 점과 법선</summary>
    public static bool WallHit(Vector3 from, Vector3 dir, float maxDist, out Vector3 point, out Vector3 normal)
    {
        point = normal = Vector3.zero;
        // 학교처럼 벽 메시가 한쪽 면만 있는 모델은 안쪽에서 쏘면 뒷면이라 안 맞음 → 이 호출 동안만 뒷면도 맞게 (바로 원래 값 복원)
        bool prevBackfaces = Physics.queriesHitBackfaces;
        RaycastHit[] hits;
        try
        {
            Physics.queriesHitBackfaces = true;
            hits = Physics.RaycastAll(from, dir.normalized, maxDist, ~0, QueryTriggerInteraction.Ignore);
        }
        finally { Physics.queriesHitBackfaces = prevBackfaces; }
        float best = float.MaxValue;
        foreach (var h in hits)
        {
            if (IsInteriorObject(h.collider.transform)) continue;
            if (h.distance < best) { best = h.distance; point = h.point; normal = h.normal; }
        }
        if (Vector3.Dot(normal, dir) > 0f) normal = -normal; // 뒷면에 맞으면 법선이 진행 방향 → 뒤집어서 "벽에서 나오는 쪽"으로
        return best < float.MaxValue;
    }

    /// <summary>
    /// 건물 하나 꾸밀 때 쓰는 도우미. 모든 좌표는 "건물 프레임" 기준:
    /// 건물이 조금 돌아가 있으면(예: 민가 181.79° → 1.79°) 설계는 축 정렬 좌표로 하고, frameCenter 둘레로 frameYaw만큼 돌려서 월드에 놓음.
    /// snap: "N"/"S"/"E"/"W" = 프레임 +Z/-Z/+X/-X 쪽 벽에 닿을 때까지 밀착 (벽 위치를 정확히 몰라도 벽에 딱 붙음)
    /// </summary>
    public class Ctx
    {
        public Transform root;
        public float floorGuess;
        public BuildingInterior building;
        public int count;
        public Vector3 frameCenter;
        public readonly List<Vector3> doors = new List<Vector3>();   // 검사용 내부 문 틈 (Door로 등록)

        /// <summary>내부 문 틈 등록 (프레임 좌표) → 배치 후 검사에서 1.5m 반경 비워둠</summary>
        public void Door(float x, float z) { var w = W(x, z); doors.Add(new Vector3(w.x, FloorAt(w.x, w.z, floorGuess), w.z)); }
        public float frameYaw;

        /// <summary>프레임 좌표 → 월드 (y=0)</summary>
        public Vector3 W(float x, float z)
        {
            var d = new Vector3(x - frameCenter.x, 0, z - frameCenter.z);
            var r = Quaternion.Euler(0, frameYaw, 0) * d;
            return new Vector3(frameCenter.x + r.x, 0, frameCenter.z + r.z);
        }

        /// <summary>프레임 방향 문자 → 월드 방향</summary>
        public Vector3 Dir(string s)
        {
            Vector3 d = s == "N" ? Vector3.forward : s == "S" ? Vector3.back : s == "E" ? Vector3.right : Vector3.left;
            return Quaternion.Euler(0, frameYaw, 0) * d;
        }

        public GameObject P(string group, string key, float x, float z, float yaw, Vector3 tilt = default, float scaleMul = 1f, Vector3? stretch = null, string snap = null)
        {
            var g = Group(root, group);
            var w = W(x, z);
            var go = Place(g, key, new Vector3(w.x, FloorAt(w.x, w.z, floorGuess), w.z), yaw + frameYaw, tilt, scaleMul, null, stretch);
            if (go == null) return null;
            count++;
            if (!string.IsNullOrEmpty(snap)) foreach (char ch in snap) SnapToWall(go, Dir(ch.ToString()));
            return go;
        }

        /// <summary>이미 놓은 가구(baseGo) 윗면 가운데(+offset)에 소품 올리기 (스냅으로 옮겨진 뒤 위치 기준)</summary>
        public GameObject OnTopOf(string group, string key, GameObject baseGo, float yaw, float offX = 0f, float offZ = 0f)
        {
            if (baseGo == null || !WorldBounds(baseGo, out var bb)) return null;
            var off = Quaternion.Euler(0, frameYaw, 0) * new Vector3(offX, 0, offZ);
            var go = Place(Group(root, group), key, new Vector3(bb.center.x + off.x, bb.max.y, bb.center.z + off.z), yaw + frameYaw, default, 1f, key + "@top");
            if (go != null) count++;
            return go;
        }

        /// <summary>월드 좌표 그대로 배치 (다른 가구 바운드 기준 위치용), scaleMul로 크기 조절</summary>
        public GameObject Root_P(string group, string key, float wx, float wz, float yaw, float scaleMul = 1f)
        {
            var go = Place(Group(root, group), key, new Vector3(wx, FloorAt(wx, wz, floorGuess), wz), yaw + frameYaw, default, scaleMul);
            if (go != null) count++;
            return go;
        }

        /// <summary>벽에 붙여 놓는 가구 (snap 생략형)</summary>
        public GameObject PW(string group, string key, float x, float z, float yaw, string snap) => P(group, key, x, z, yaw, default, 1f, null, snap);

        /// <summary>다른 가구 위에 올리는 소품 (y = 바닥 + height). 이름 끝에 @top → 검사에서 "공중에 뜸" 제외</summary>
        public GameObject OnTop(string group, string key, float x, float z, float height, float yaw)
        {
            var g = Group(root, group);
            var w = W(x, z);
            var go = Place(g, key, new Vector3(w.x, FloorAt(w.x, w.z, floorGuess) + height, w.z), yaw + frameYaw, default, 1f, key + "@top");
            if (go != null) count++;
            return go;
        }

        /// <summary>
        /// 벽에 붙이는 평면 소품(덩굴 등): 프레임 좌표 (x,z)에서 dir 쪽 벽을 찾아 표면 바로 앞에 세움, 정면 = 벽 법선 방향
        /// </summary>
        public GameObject OnWall(string group, string key, float x, float z, string dir, float scaleMul = 1f, float heightAboveFloor = 0f, Vector3? stretch = null, float probeY = 1.5f)
        {
            var w = W(x, z);
            float fy = FloorAt(w.x, w.z, floorGuess);
            if (!WallHit(new Vector3(w.x, fy + probeY, w.z), Dir(dir), 25f, out var p, out var n)) { Debug.LogWarning($"[Interior] 벽을 못 찾음: {key} @ ({x},{z}) {dir}"); return null; }
            n.y = 0; n.Normalize();
            float yaw = Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg;
            var g = Group(root, group);
            var pos = p + n * 0.05f;
            pos.y = fy + heightAboveFloor;
            var go = Place(g, key, pos, yaw, default, scaleMul, key + "@wall", stretch);   // @wall → 검사에서 "공중에 뜸" 제외
            if (go != null)
            {
                // Place는 바운드 중심을 pos에 맞추므로, 두께 절반만큼 바깥으로 밀어 벽 쪽 면이 표면에 닿게
                WorldBounds(go, out var wb);
                float depth = Mathf.Abs(wb.extents.x * n.x) + Mathf.Abs(wb.extents.z * n.z);
                go.transform.position += n * depth;
                count++;
            }
            return go;
        }

        /// <summary>
        /// 칸막이 벽 줄: 프레임 좌표 a→b 선 위에서 실제 벽까지 양 끝을 늘리고, gaps(선 위 거리 비율이 아니라 프레임 좌표의 그 축 값) 자리는 gapWidth만큼 비움.
        /// Kenney wall(폭 1.9m)을 구간 길이에 맞게 늘려서 구간마다 1개씩 놓음
        /// </summary>
        public void Partition(string group, float ax, float az, float bx, float bz, float[] gaps, float gapWidth = 2.0f)
        {
            var A = W(ax, az); var B = W(bx, bz);
            var u = (B - A).normalized;
            float fy = FloorAt((A.x + B.x) * 0.5f, (A.z + B.z) * 0.5f, floorGuess);
            var mid = (A + B) * 0.5f + Vector3.up * (fy + 1.2f);
            float len = (B - A).magnitude;
            // 양 끝을 실제 벽까지 (못 찾으면 주어진 끝점)
            float t0 = -len * 0.5f, t1 = len * 0.5f;
            float reach = len * 0.5f + 0.8f; // 주어진 끝점에서 최대 0.8m까지만 늘림 (빈 공간 쪽으로 끝없이 늘어나지 않게)
            if (WallHit(mid, -u, reach, out var pa, out _)) t0 = Vector3.Dot(pa - mid, u);   // 음수 (mid 뒤쪽)
            if (WallHit(mid, u, reach, out var pb, out _)) t1 = Vector3.Dot(pb - mid, u);
            // 구간 나누기 (gaps = 프레임 좌표에서 선 방향 축 값 → mid 기준 t로 변환)
            bool alongX = Mathf.Abs(bx - ax) > Mathf.Abs(bz - az);
            float midF = alongX ? (ax + bx) * 0.5f : (az + bz) * 0.5f;
            float sign = alongX ? Mathf.Sign(bx - ax) : Mathf.Sign(bz - az);
            var cuts = new List<(float, float)>();
            float cur = t0;
            var gs = new List<float>(gaps ?? new float[0]); gs.Sort((p1, p2) => (sign * p1).CompareTo(sign * p2));
            foreach (var gv in gs)
            {
                float tc = (gv - midF) * sign;
                cuts.Add((cur, tc - gapWidth * 0.5f));
                cur = tc + gapWidth * 0.5f;
            }
            cuts.Add((cur, t1));
            float yaw = -Mathf.Atan2(u.z, u.x) * Mathf.Rad2Deg;
            foreach (var (s0, s1) in cuts)
            {
                float segLen = s1 - s0;
                if (segLen < 0.3f) continue;
                var c = mid + u * ((s0 + s1) * 0.5f);
                var go = Place(Group(root, group), "wall", new Vector3(c.x, fy, c.z), yaw, default, 1f, null, new Vector3(segLen / 1.9f, 1f, 1f));
                if (go != null) count++;
            }
        }

        public void Light(string group, float x, float z, float height = 2.6f)
        {
            var w = W(x, z);
            RoomLight(Group(root, group), new Vector3(w.x, FloorAt(w.x, w.z, floorGuess), w.z), height);
        }
    }

    /// <summary>가구를 dir 쪽 벽(이 툴 가구 제외)에 닿을 때까지 밀착 (최대 2m, 2cm 남김)</summary>
    public static void SnapToWall(GameObject go, Vector3 dir)
    {
        Physics.SyncTransforms();
        if (!WorldBounds(go, out var wb)) return;
        dir.y = 0; dir.Normalize();
        // 진행 방향 두께는 얇게, 나머지는 조금 줄인 박스로 쏨 (바닥·천장 안 닿게 높이 60%)
        var half = new Vector3(wb.extents.x * 0.9f, wb.extents.y * 0.6f, wb.extents.z * 0.9f);
        var hits = Physics.BoxCastAll(wb.center, half, dir, Quaternion.identity, 2f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        foreach (var h in hits)
        {
            if (IsInteriorObject(h.collider.transform) || h.collider is TerrainCollider || sessionPlates.Contains(h.collider as MeshCollider)) continue;
            if (h.distance <= 0f) continue; // 이미 겹침 (Validate에서 처리)
            if (h.distance < best) best = h.distance;
        }
        if (best < float.MaxValue) go.transform.position += dir * Mathf.Max(0f, best - 0.02f);
    }

    /// <summary>건물 1개 꾸미기 실행 (세션 준비 → 기존 생성분 지우기 → 배치 → 검사/자동 수정 → 씬 dirty)</summary>
    static void Run(string name, string buildingPath, float floorGuess, float frameYaw, System.Action<Ctx> content)
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[Interior] Play 모드에서는 실행 불가"); return; }
        var bgo = GameObject.Find(buildingPath);
        var bi = bgo != null ? bgo.GetComponent<BuildingInterior>() : null;
        if (bi == null) { Debug.LogError($"[Interior] 건물 '{buildingPath}' (BuildingInterior) 없음"); return; }
        if (name == "House") SetMarkerTheme(bi, "home"); else if (name == "Warehouse") SetMarkerTheme(bi, "warehouse"); // (2026-11-20) 회색 대신 테마 색 입구

        BeginSession();
        try
        {
            var root = Root(name);
            ClearGenerated(root);
            Physics.SyncTransforms();
            var trig = bi.GetComponent<Collider>();
            var ctx = new Ctx { root = root, floorGuess = floorGuess, building = bi, frameYaw = frameYaw, frameCenter = trig != null ? trig.bounds.center : bi.transform.position };
            content(ctx);
            Physics.SyncTransforms();
            var report = Validate(root, bi, true, ctx.doors);
            InteriorShadowsOff(); // (2026-11-20) 성능
            Debug.Log($"[Interior] {name}: 가구/소품 {ctx.count}개 배치 (조명 {root.GetComponentsInChildren<Light>().Length}개)\n{report}");
        }
        finally { EndSession(); }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    // ───────────────────────── 검사 / 자동 수정 ─────────────────────────

    public const float DoorClear = 1.5f;   // 입구 반경 비워두기 (m)

    /// <summary>
    /// 배치 검사 (+fix면 자동 수정):
    ///  1) 문 중심 1.5m 안(가로 거리)에 가구·데칼이 있으면 → 실내 쪽으로 50cm씩 밀어냄 (최대 3m)
    ///  2) 콜라이더 가구가 벽에 묻혀 있으면 → 방 안쪽(트리거 중심 쪽)으로 10cm씩 밀어냄 (최대 60cm)
    ///  3) 바닥에서 5cm 넘게 떠 있으면 → 바닥으로 내림 (@top 소품·벽 부착물 제외)
    /// 반환: 사람이 읽을 보고서
    /// </summary>
    public static string Validate(Transform root, BuildingInterior bi, bool fix, List<Vector3> extraDoors = null, bool fixWalls = true)
    {
        var sb = new System.Text.StringBuilder();
        // 검사할 문: 건물 정문(BuildingWallCollider 문 구멍) + 내부 문 틈(칸막이·교실 문, Ctx.Door로 등록)
        var doors = new List<Vector3>();
        if (bi.TryGetDoor(out var mainDoor, out _)) doors.Add(mainDoor);
        if (extraDoors != null) doors.AddRange(extraDoors);
        var trig = bi.GetComponent<Collider>();
        Vector3 center = trig != null ? trig.bounds.center : bi.transform.position;
        int issues = 0, fixedCount = 0;
        foreach (Transform group in root)
        {
            if (group.name == "Lights" || group.name == "Exterior" || group.name == "Partitions") continue;
            foreach (Transform piece in group)
            {
                if (!WorldBounds(piece.gameObject, out var wb)) continue;
                string nm = group.name + "/" + piece.name;

                // 1) 입구 (문마다): 문 중심에서 가구 쪽으로 50cm씩 밀어냄
                foreach (var door in doors)
                {
                    for (int step = 0; step <= 6; step++)
                    {
                        WorldBounds(piece.gameObject, out wb);
                        var cp = wb.ClosestPoint(new Vector3(door.x, wb.center.y, door.z));
                        float d = new Vector2(cp.x - door.x, cp.z - door.z).magnitude;
                        if (d >= DoorClear) break;
                        if (step == 0) { issues++; sb.AppendLine($"  - 입구 {d:F2}m: {nm} (문 {door.x:F1},{door.z:F1})"); }
                        if (!fix || step == 6) break;
                        var away = wb.center - door; away.y = 0;
                        if (away.sqrMagnitude < 0.01f) away = center - door; away.y = 0;
                        piece.position += away.normalized * 0.5f;
                        if (step == 0) fixedCount++;
                    }
                }

                // 2) 벽 겹침 (콜라이더 있는 가구만)
                var bc = piece.GetComponent<BoxCollider>();
                if (bc != null)
                {
                    for (int step = 0; step <= 6; step++)
                    {
                        Physics.SyncTransforms();
                        var half = Vector3.Scale(bc.size * 0.5f, piece.lossyScale) * 0.9f;
                        var c = piece.TransformPoint(bc.center);
                        bool hit = false;
                        foreach (var col in Physics.OverlapBox(c, new Vector3(Mathf.Abs(half.x), Mathf.Abs(half.y) * 0.8f, Mathf.Abs(half.z)), piece.rotation, ~0, QueryTriggerInteraction.Ignore))
                        {
                            if (IsInteriorObject(col.transform)) continue;
                            if (col is TerrainCollider) continue;
                            if (sessionPlates.Contains(col as MeshCollider)) continue;
                            hit = true; break;
                        }
                        if (!hit) break;
                        if (step == 0) { issues++; sb.AppendLine($"  - 벽/건물에 겹침: {nm}"); }
                        if (!fix || !fixWalls || step == 6) break;   // 검수(AuditAll)에선 보고만: 매번 밀면 같은 가구가 조금씩 계속 밀려남
                        var toC = center - c; toC.y = 0;
                        piece.position += toC.normalized * 0.1f;
                        if (step == 0) fixedCount++;
                    }
                }

                // 3) 공중에 뜸
                if (!piece.name.EndsWith("@top") && !piece.name.EndsWith("@wall") && group.name != "Exterior")
                {
                    WorldBounds(piece.gameObject, out wb);
                    float fy = FloorAt(wb.center.x, wb.center.z, wb.min.y);
                    float gap = wb.min.y - fy;
                    if (gap > 0.05f && gap < 1.5f)
                    {
                        issues++; sb.AppendLine($"  - 공중 {gap:F2}m: {nm}");
                        if (fix) { piece.position -= new Vector3(0, gap, 0); fixedCount++; }
                    }
                }
            }
        }
        return $"검사: 문제 {issues}건, 자동 수정 {fixedCount}건" + (sb.Length > 0 ? "\n" + sb : "");
    }

    // ───────────────────────── 1. 민가 (Kenney building-a (4), 15x15m 박스 방) ─────────────────────────
    // 실측(Play, 벽 콜라이더): 실내 x 65.7~80.3, z 14.7~29.3, 바닥 ≈ 15.0~15.2, 문 = 북쪽 벽 x≈77.4 (바깥 +Z)
    // 칸막이 벽(Kenney wall)으로 4구역: 북동 거실(입구) / 북서 주방 / 남동 침실 / 남서 욕실·다용도실
    // 분위기: 급히 떠난 가정집 — 넘어진 의자, 바닥 박스, 핏자국, 입구 바깥 벽 덩굴

    [MenuItem("Tools/Interior/Build 1. House (민가)")]
    public static void BuildHouse() => Run("House", "Buildings/building-a (4)", 15.0f, 1.79f, c =>
    {
        // 프레임 실측(벽 콜라이더 레이): 서 x65.5 / 동 x80.9 / 남 z14.3 / 북 z29.65, 문 = 북벽 x≈77.4
        // 칸막이: z=22 동서 벽 (틈 = 남서방 x70.5, 침실 x76.3) + x=73 남북 벽 (남쪽 절반)
        c.Partition("Partitions", 66f, 22f, 80f, 22f, new[] { 70.5f, 76.3f });
        c.Partition("Partitions", 73f, 15f, 73f, 21.9f, null);

        // 거실 (입구 → 침실 틈으로 이어지는 x75.3~77.3 통로는 비워둠)
        c.PW("Living", "sofa", 80.3f, 24.6f, 270f, "E");
        c.P("Living", "tableCoffee", 78.9f, 24.6f, 90f);
        c.P("Living", "rugA", 78.0f, 24.6f, 0f);
        var tvStand = c.P("Living", "tvStand", 74.2f, 24.6f, 90f);
        c.OnTopOf("Living", "tvVintage", tvStand, 90f);
        c.PW("Living", "bookShelfOld", 74.6f, 29.0f, 180f, "N");
        c.PW("Living", "lampFloor", 80.4f, 26.7f, 0f, "E");
        c.P("Living", "chair", 79.4f, 22.9f, 30f, new Vector3(0, 0, 90));        // 넘어진 의자 (통로 밖 남동 구석)

        // 주방 (북쪽 벽을 따라 냉장고 + 싱크대 줄, 가운데 식탁)
        c.PW("Kitchen", "fridge", 66.05f, 29.0f, 180f, "NW");
        c.PW("Kitchen", "kitchenCab", 66.96f, 29.0f, 180f, "N");
        c.PW("Kitchen", "kitchenSink", 67.78f, 29.0f, 180f, "N");
        c.PW("Kitchen", "kitchenStove", 68.60f, 29.0f, 180f, "N");
        c.PW("Kitchen", "kitchenDrawer", 69.42f, 29.0f, 180f, "N");
        c.PW("Kitchen", "kitchenCab", 70.24f, 29.0f, 180f, "N");
        c.P("Kitchen", "table", 69.0f, 25.6f, 0f);
        c.P("Kitchen", "chair", 69.0f, 24.7f, 0f);
        c.P("Kitchen", "chair", 70.8f, 25.4f, 70f, new Vector3(0, 0, 90));     // 넘어진 의자
        c.PW("Kitchen", "trashcan", 71.4f, 29.0f, 0f, "N");
        c.P("Kitchen", "kBoxOpen", 72.2f, 23.0f, 25f);

        // 침실 (침대 머리맡 = 남쪽 벽)
        c.PW("Bedroom", "bedDouble", 77.8f, 15.6f, 0f, "S");
        var side = c.PW("Bedroom", "sideTable", 79.55f, 14.8f, 0f, "S");
        c.OnTopOf("Bedroom", "lampTable", side, 0f, 0.25f);
        c.PW("Bedroom", "closet", 80.4f, 19.4f, 270f, "E");
        c.P("Bedroom", "rugB", 77.6f, 18.8f, 90f);
        c.P("Bedroom", "box", 74.0f, 15.0f, 15f);
        c.P("Bedroom", "boxOpen", 74.3f, 15.9f, -20f);

        // 욕실·다용도실 (변기·세면대 = 남쪽 벽, 욕조 = 서쪽 벽)
        c.PW("Bathroom", "toilet", 66.4f, 14.9f, 0f, "S");
        c.PW("Bathroom", "bathSink", 67.9f, 14.7f, 0f, "S");
        c.PW("Bathroom", "bathtub", 66.1f, 18.6f, 90f, "W");
        c.PW("Bathroom", "barrel", 72.4f, 14.9f, 0f, "S");
        c.P("Bathroom", "box", 71.6f, 15.0f, 40f);
        c.P("Bathroom", "planks", 70.6f, 20.9f, 80f);

        // 어질러진 흔적
        c.P("Clutter", "blood", 77.2f, 20.6f, 30f);
        c.P("Clutter", "blood2", 69.6f, 17.4f, 110f);

        // 입구 바깥 북쪽 외벽 덩굴 (문 x75.9~78.9는 피함): 바깥(z=33)에서 남쪽으로 쏴서 외벽 표면에 붙임
        c.OnWall("Exterior", "wallVine", 72.0f, 33f, "S", 0.8f);
        c.OnWall("Exterior", "wallVine", 68.5f, 33f, "S", 0.8f);
        c.OnWall("Exterior", "vines", 80.6f, 33f, "S", 0.9f, 0.6f);

        // 조명 (방마다 1개)
        c.Light("Lights", 76.8f, 25.8f);
        c.Light("Lights", 69.0f, 26.0f);
        c.Light("Lights", 76.8f, 18.2f);
        c.Light("Lights", 69.0f, 18.2f);
    });

    // ───────────────────────── 2. 교회 (Church by Poly by Google, 신랑 59x28m) ─────────────────────────
    // 실측: 신랑 x -346.3~-287.1, z -96.5~-68.5, 바닥 15.0, 입구 = 동쪽 벽 가운데 틈(z -85~-80) → 현관 → 문(x≈-279)
    // 제단 = 서쪽 끝. 가운데 통로(z=-82.5, 폭 3m) 양쪽 벤치 4열씩 (열 간격 1.2m), 통로엔 러그, 양초
    // 분위기: 신성한 폐허 — 넘어진 벤치, 기둥 잔해, 벽 덩굴, 제단 앞 핏자국

    [MenuItem("Tools/Interior/Build 2. Church (교회)")]
    public static void BuildChurch() => Run("Church", "Buildings/Enterable_Church by Poly by Google - 2-J0Y-8AQOG", 15.0f, 0.48f, c =>
    {
        const float aisleZ = -82.5f;
        // 제단: 설교대(Tribune) + 양옆 성녀상, 뒷벽 십자가
        c.P("Altar", "tribune", -341.5f, aisleZ, 90f);
        c.PW("Altar", "statue", -344.8f, aisleZ + 5.0f, 90f, "W");
        c.PW("Altar", "statue", -344.8f, aisleZ - 5.0f, 90f, "W");
        c.OnWall("Altar", "crossA", -340f, aisleZ, "W", 1f, 1.1f);
        c.P("Altar", "candles", -340.2f, aisleZ + 2.2f, 90f);
        c.P("Altar", "candles", -340.2f, aisleZ - 2.2f, 90f);

        // 벤치: 정면 = 제단(서쪽) → yaw 270, 길이 2.6m가 통로와 직각. 열 간격 = 벤치 폭 1.05 + 1.2
        float[] rows = { -335.0f, -332.75f, -330.5f, -328.25f };
        foreach (var x in rows)
        {
            c.P("Nave", "churchBench", x, aisleZ - 1.5f - 1.3f, 270f);   // 남쪽 (통로 왼쪽)
            c.P("Nave", "churchBench", x, aisleZ + 1.5f + 1.3f, 270f);   // 북쪽 (통로 오른쪽)
        }
        c.P("Nave", "churchBench", -324.5f, aisleZ + 3.6f, 250f, new Vector3(80, 0, 0));  // 넘어진 벤치 (뒤쪽 열, 길이 축 기준으로 뒤로 넘어짐)
        // 통로 양옆 양초 (벤치 끝 통로 쪽)
        c.P("Nave", "candles", -333.9f, aisleZ - 1.25f, 0f);
        c.P("Nave", "candles", -329.4f, aisleZ + 1.25f, 0f);

        // 통로 러그 (긴 쪽 = 통로 방향)
        for (int i = 0; i < 4; i++) c.P("Nave", i % 2 == 0 ? "rugC" : "rugD", -338.0f + i * 2.85f, aisleZ, 90f);

        // 폐허: 기둥 잔해, 덩굴, 핏자국
        c.P("Clutter", "pillarDebris", -312f, -73.5f, 20f);
        c.P("Clutter", "pillarDebris", -301f, -92.0f, 75f);
        c.P("Clutter", "blood", -339.0f, aisleZ + 0.6f, 15f);   // 제단 앞
        c.OnWall("Clutter", "wallVine", -318f, -75f, "N", 0.9f);
        c.OnWall("Clutter", "wallVine", -303f, -75f, "N", 0.8f);
        c.OnWall("Clutter", "wallVine", -310f, -90f, "S", 0.9f);

        // 조명 (긴 공간: 제단 / 벤치 / 가운데 / 입구 쪽)
        c.Light("Lights", -340.5f, aisleZ);
        c.Light("Lights", -331.5f, aisleZ);
        c.Light("Lights", -316f, aisleZ, 3.0f);
        c.Light("Lights", -299f, aisleZ, 3.0f);
    });

    // ───────────────────────── 3. 병원 (low_poly_hospital, L자 큰 홀) ─────────────────────────
    // 실측: 동쪽 동 x -305.9~-254.3 × z 2.7~33.0 / 남쪽 동 x -336.2~-254.3 × z -27.0~2.7 (두 동 사이 벽 없음), 바닥 15.54
    // 정문 = 동쪽 동 서쪽 벽 x≈-306, z 14~23 (바깥은 안뜰). 회전 0.88°
    // 구역: 정문 앞 접수대 / 북쪽 벽 병실 A (침대 4) / 남쪽 벽 병실 B (침대 4) / 가운데 복도(넘어진 트롤리·서류·핏자국)

    [MenuItem("Tools/Interior/Build 3. Hospital (병원)")]
    public static void BuildHospital() => Run("Hospital", "Buildings/Enterable_scene (3)", 15.54f, 0.88f, c =>
    {
        // 접수대 (정문 정면, 문 1.5m 밖), 정수기 = 북서 모서리
        c.P("Reception", "pcDesk", -299.5f, 26.5f, 270f);
        c.P("Reception", "retroChair", -297.8f, 27.2f, 270f);
        c.P("Reception", "officeDrawer", -296.5f, 31.9f, 180f, default, 1f, null, "N");
        c.PW("Reception", "waterCooler", -305.3f, 32.4f, 90f, "NW");
        c.P("Reception", "cityBench", -299f, 9.0f, 90f);        // 대기 의자 (정문 남쪽)

        // 병실 A: 북쪽 벽에 머리맡 (yaw 180 → 머리 +Z), 침대 옆 트롤리
        float[] ax = { -289f, -282.5f, -276f, -269.5f };
        foreach (var x in ax)
        {
            c.PW("Ward", "bedSingle", x, 31.8f, 180f, "N");
            c.PW("Ward", "medCart", x + 1.35f, 32.4f, 180f, "N");
        }
        // 병실 B: 남쪽 벽에 머리맡 (yaw 0 → 머리 -Z)
        float[] bx = { -331f, -324.5f, -318f, -311.5f };
        for (int i = 0; i < bx.Length; i++)
        {
            c.PW("Ward", "bedSingle", bx[i], -25.8f, 0f, "S");
            if (i != 2) c.PW("Ward", "medCart", bx[i] + 1.35f, -26.3f, 0f, "S");
        }
        // 병실 칸막이: A = 북동 (z=24 줄 + x=-296 줄), B = 남서 (z=-17.5 줄 + x=-305 줄), 문 틈 2곳씩
        c.Partition("Partitions", -296f, 24f, -255f, 24f, new[] { -286f, -271f }, 2.2f);
        c.Partition("Partitions", -296f, 24.1f, -296f, 32.6f, null);
        c.Partition("Partitions", -335.6f, -17.5f, -305f, -17.5f, new[] { -327f, -315f }, 2.2f);
        c.Partition("Partitions", -305f, -26.5f, -305f, -17.6f, null);

        // 창가 커튼 (병실 A 동쪽 벽 창문, 병실 B 서쪽 벽 창문)
        c.OnWall("Ward", "curtain", -258f, 28.5f, "E");
        c.OnWall("Ward", "curtain", -258f, 22.0f, "E");
        c.OnWall("Ward", "curtain", -333f, -22f, "W");

        // 복도/홀: 넘어진 트롤리, 서류·박스, 핏자국, 덮인 시신
        c.P("Corridor", "medCart", -286f, 12.5f, 35f, new Vector3(0, 0, 90));
        c.P("Corridor", "medCart", -318.5f, -20.0f, 110f, new Vector3(0, 0, 85));
        c.P("Corridor", "kBoxOpen", -279f, 6.5f, 20f);
        c.P("Corridor", "kBoxClosed", -278.2f, 7.1f, -15f);
        c.P("Corridor", "books", -283.5f, 9.2f, 60f);
        c.P("Corridor", "books", -303.5f, -14.5f, -30f);
        c.P("Corridor", "box", -262f, -20f, 10f);
        c.P("Corridor", "blood", -284.0f, 11.0f, 40f);
        c.P("Corridor", "blood2", -321.0f, -21.5f, 100f);
        c.P("Corridor", "blood", -295.0f, -8.0f, -70f);
        c.P("Corridor", "deadBody", -308.0f, -22.5f, 15f);
        // (2026-11-20) 가운데 큰 홀 대기실: 벤치 3줄 + 화분 + 쓰레기통 + 넘어진 트롤리 + 서류 상자
        foreach (var bz in new[] { 14.0f, 16.6f, 19.2f }) { c.P("Corridor", "cityBench", -276f, bz, 180f); c.P("Corridor", "cityBench", -272f, bz, 180f); }
        c.PW("Corridor", "plant", -254.9f, 12.0f, 270f, "E");
        c.PW("Corridor", "plant", -254.9f, 20.5f, 270f, "E");
        c.P("Corridor", "trashcan", -268.5f, 12.5f, 0f);
        c.P("Corridor", "officeDesk", -263.0f, 2.5f, 90f);
        c.P("Corridor", "retroChair", -261.9f, 2.5f, 270f);
        c.P("Corridor", "kBoxClosed", -265.0f, -4.0f, 20f);
        c.P("Corridor", "medCart", -300.0f, -5.0f, 60f);
        c.P("Corridor", "bedSingle", -290.0f, -3.0f, 75f);                  // 복도에 끌어낸 침대
        c.P("Corridor", "blood2", -289.0f, -5.5f, 30f);
        c.Light("Lights", -273f, 16f, 3.0f);

        // 조명: 접수대 / 병실 A 2 / 병실 B 2 / 가운데 홀 2
        c.Light("Reception", -300f, 24f);
        c.Light("Lights", -285.5f, 29f);
        c.Light("Lights", -272.5f, 29f);
        c.Light("Lights", -327.5f, -22f);
        c.Light("Lights", -314.5f, -22f);
        c.Light("Lights", -284f, 8f, 3.0f);
        c.Light("Lights", -300f, -10f, 3.0f);
    });

    // ───────────────────────── 4. 경찰서 (low_poly_police_station, 본관 홀 22x28m) ─────────────────────────
    // 실측: 홀 x -109.1~-86.9, z -147.2~-119.2, 바닥 15.85 (지면보다 0.85m 높음 → NavBake가 문 링크로 연결)
    //       입구 = 서쪽 벽 틈 z -138~-129 (가운데 -133.3), 남·북 날개는 문 없는 막힌 방이라 제외
    // 구역: 북쪽 사무실(책상 5 + 의자) / 남동 유치장(철창 3칸 + 간이침대) / 남서 무기고(상자 쌓기) / 바깥 경찰차 2대
    // 분위기: 급히 비운 사무실 — 넘어진 의자, 바닥 서류, 핏자국

    [MenuItem("Tools/Interior/Build 4. Police (경찰서)")]
    public static void BuildPolice() => Run("Police", "Buildings/Enterable_scene (4)", 15.85f, 0f, c =>
    {
        c.Door(-109.1f, -133.3f);   // 홀 서쪽 벽의 실제 입구 (건물 문 좌표는 트리거 끝이라 4m 바깥)

        // 사무실: 1열 = LowPolyOffice 책상 3 + 레트로 의자(남쪽에 앉아 북쪽을 봄), 2열 = 컴퓨터 책상 2 (의자 포함, 남쪽)
        var desk0 = c.P("Office", "officeDesk", -102.5f, -121.4f, 0f);
        c.P("Office", "officeDesk", -98.0f, -121.4f, 0f);
        c.P("Office", "officeDesk", -93.5f, -121.4f, 0f);
        c.P("Office", "retroChair", -102.5f, -122.6f, 0f);
        c.P("Office", "retroChair", -98.0f, -122.7f, 15f);
        c.P("Office", "pcDesk", -101.5f, -126.0f, 180f);
        c.P("Office", "pcDesk", -95.5f, -126.0f, 180f);
        c.OnTopOf("Office", "radio", desk0, 0f, 0.3f);                     // 무전기 (라디오 송신기 역할)
        c.PW("Office", "waterCooler", -108.6f, -119.7f, 135f, "NW");       // 정수기 = 북서 모서리
        c.PW("Office", "officeDrawer", -89.5f, -119.6f, 180f, "N");        // 서류 캐비닛
        c.P("Office", "retroChair", -93.0f, -124.0f, 60f, new Vector3(0, 0, 90));    // 넘어진 의자
        c.P("Office", "retroChair", -105.0f, -125.5f, -40f, new Vector3(0, 0, 90));  // 넘어진 의자
        c.P("Office", "books", -97.0f, -128.6f, 35f);
        c.P("Office", "books", -91.5f, -127.5f, -60f);

        // 유치장: 동쪽 벽에 철창 3칸 (문 = 서쪽), 안에 간이침대
        float[] cz = { -145.4f, -142.5f, -139.6f };
        foreach (var z in cz)
        {
            var cage = c.PW("Cells", "cage", -88.6f, z, 270f, "E");
            if (cage != null && WorldBounds(cage, out var cb))
                c.Root_P("Cells", "cellBed", cb.center.x + 0.3f, cb.center.z, 90f, 0.9f);
        }

        // 무기고 (남서): 상자 쌓기 + 금속 상자 — 무기를 숨겨둔 느낌
        var b1 = c.PW("Armory", "boxLarge", -107.8f, -146.3f, 0f, "SW");
        c.OnTopOf("Armory", "box", b1, 10f);
        var b2 = c.PW("Armory", "box", -106.4f, -146.5f, 5f, "S");
        c.OnTopOf("Armory", "box", b2, -12f);
        c.PW("Armory", "chest", -105.2f, -146.6f, 0f, "S");
        c.P("Armory", "boxOpen", -104.2f, -144.8f, 30f);
        c.P("Armory", "workbench", -102.0f, -146.4f, 180f, default, 1f, null, "S");

        // 핏자국
        c.P("Clutter", "blood", -98.0f, -131.5f, 20f);
        c.P("Clutter", "blood2", -92.0f, -136.0f, 140f);

        // 바깥: 경찰차 2대 (입구 양옆, 건물과 나란히), 입구 남쪽 드럼통 (창고 에셋 혼용)
        c.P("Exterior", "policeCar", -119.5f, -123.0f, 90f);
        c.P("Exterior", "policeCar", -120.0f, -144.0f, 95f);
        c.P("Exterior", "drum", -112.3f, -147.5f, 0f);
        c.P("Exterior", "drum", -113.0f, -146.7f, 0f);
        c.P("Exterior", "drum", -112.4f, -146.0f, 0f, new Vector3(90, 0, 0));

        c.Light("Lights", -101f, -123.5f);
        c.Light("Lights", -94f, -123.5f);
        c.Light("Lights", -90.5f, -142.5f);
        c.Light("Lights", -105f, -144.5f);
        c.Light("Lights", -103f, -133.3f);
    });

    // ───────────────────────── 5. 학교 (low_poly_school "scene (2)") ─────────────────────────
    // 실측(Play, 콜라이더 854개): 앞 복도(베란다) x -315~-249, z 129.5~139.5 / 교실 줄 z 140.5~150.5 (방 폭 6~8m, 복도 쪽 문 틈 2m)
    //       교실 A x -307.5~-299.5 (문 x -305.5) / E x -274.5~-267.5 (문 -269.5) / F x -266.5~-258.5 (문 -260.5), 바닥 ≈ 16.5
    //       뒤쪽 큰 홀(z 152~166)은 사방이 막혀 못 들어감 → 제외
    // 교실 2칸(책상 6·4) + 교무실 1칸 + 복도 사물함. Japanese Classroom(clasroom.fbx)은 벽·책상이 메시 1개(8.5x18.9m)라
    // 분해도 안 되고 교실 크기(7x10m)에 안 맞아서 Kenney 책상/의자로 그리드 구성

    /// <summary>
    /// 학교를 "들어갈 수 있는 건물"로: Buildings/Enterable_School(교실 줄 트리거 + BuildingInterior) 아래로 scene (2)를 옮김 (월드 위치 유지).
    /// 학교 콜라이더(854개)는 그대로 둠 — 문 정보(BuildingWallCollider)가 없으므로 트리거에 들어오면 실내로 침 (BuildingInterior 예전 방식).
    /// 다시 실행해도 안전 (이미 있으면 건너뜀)
    /// </summary>
    [MenuItem("Tools/Interior/Make School Enterable")]
    public static BuildingInterior EnsureSchoolEnterable()
    {
        var existing = GameObject.Find("Buildings/Enterable_School");
        if (existing != null) { var eb = existing.GetComponent<BuildingInterior>(); SetSchoolDoor(eb); return eb; }
        var school = GameObject.Find("scene (2)");
        var buildings = GameObject.Find("Buildings");
        if (school == null || buildings == null) { Debug.LogError("[Interior] 학교(scene (2)) 또는 Buildings 없음"); return null; }
        var wrap = new GameObject("Enterable_School");
        Undo.RegisterCreatedObjectUndo(wrap, "Make School Enterable");
        wrap.transform.SetParent(buildings.transform, false);
        wrap.transform.position = new Vector3(-283f, 18.8f, 145.5f);
        var bc = wrap.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.size = new Vector3(66f, 5f, 11f);   // 교실 줄 (x -316~-250, z 140~151, y 16.3~21.3)
        SetSchoolDoor(wrap.AddComponent<BuildingInterior>());
        Undo.SetTransformParent(school.transform, wrap.transform, "Make School Enterable");
        EditorSceneManager.MarkSceneDirty(wrap.scene);
        Debug.Log("[Interior] 학교를 Enterable_School 아래로 옮기고 교실 트리거를 붙였습니다 (콜라이더는 그대로)");
        return wrap.GetComponent<BuildingInterior>();
    }

    /// <summary>
    /// 꾸미는 교실 3칸(A·E·F)의 미닫이 나무문(Component_43_N/…Wood_Cherry, 높이 8m짜리 문짝 + MeshCollider)을 끔 → 문틈이 열림.
    /// 닫힌 문 때문에 복도↔교실 NavMesh가 끊겨 있었음 (Play 실측 PathPartial). 끈 뒤 Rebake NavMesh 필요. 다시 실행해도 안전
    /// </summary>
    static void OpenClassroomDoors()
    {
        var school = GameObject.Find("scene (2)");
        if (school == null) return;
        int opened = 0;
        // foreach (var id in new[] { "Component_43_15", "Component_43_13", "Component_43_10" })   // (이전) 교실 A, E, F 문
        foreach (var id in new[] { "Component_43_15", "Component_43_13", "Component_43_10", "Component_43_14", "Component_43_16", "Component_43_11" })   // (2026-11-20) + B, C, G (D는 원래 문 없음)
        {
            foreach (var t in school.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != id) continue;
                foreach (var leaf in t.GetComponentsInChildren<Transform>(true))
                {
                    if (!leaf.name.Contains("Wood_Cherry") || !leaf.gameObject.activeSelf) continue;
                    Undo.RecordObject(leaf.gameObject, "Open Classroom Door");
                    leaf.gameObject.SetActive(false);
                    opened++;
                }
            }
        }
        if (opened > 0) Debug.Log($"[Interior] 교실 문짝 {opened}개를 열었음(끔) → Tools/Map/Rebake NavMesh With Building Walls 필요");
    }

    /// <summary>(2026-11-20) 학교는 벽 콜라이더(BuildingWallCollider)가 없어 부모(Buildings)의 다른 건물 문을 가져왔음 → 실제 정문(마당 계단, 가운데 홀 앞) 수동 지정</summary>
    static void SetSchoolDoor(BuildingInterior b)
    {
        if (b == null) return;
        Undo.RecordObject(b, "School Door");
        b.useManualDoor = true;
        b.manualDoorWorld = new Vector3(-279f, 16.5f, 128.5f);
        b.manualDoorOut = Vector3.back;
        b.markerLabel = "학교";
        b.markerColor = new Color(0.3f, 0.9f, 0.75f);
        EditorUtility.SetDirty(b);
    }

    [MenuItem("Tools/Interior/Build 5. School (학교)")]
    public static void BuildSchool()
    {
        if (EnsureSchoolEnterable() == null) return;
        OpenClassroomDoors();
        Run("School", "Buildings/Enterable_School", 16.4f, 0f, c =>
        {
            c.Door(-305.5f, 140.0f); c.Door(-269.5f, 140.0f); c.Door(-260.5f, 140.0f);
            c.Door(-297.5f, 140.0f); c.Door(-288.5f, 140.0f); c.Door(-252.5f, 140.0f); c.Door(-278.5f, 140.0f);   // (2026-11-20) B·C·G 문 + D 입구

            // (2026-11-20) 나머지 교실도 전부: B·C = 교실 (책상 2x2·칠판·교탁), D = 보건실 (침대·트롤리·정수기), G = 도서실 (책장·책상)
            ClassroomGrid(c, new[] { -296.3f, -293.8f }, new[] { 144.3f, 146.6f });
            c.OnWall("Classroom", "blackboard", -295f, 148f, "N", 1f, 0.9f, new Vector3(1.4f, 0.5f, 1f), 1.0f);
            c.P("Classroom", "officeDesk", -295f, 149.3f, 180f);
            c.P("Classroom", "chair", -292.6f, 142.2f, 120f, new Vector3(0, 0, 90));
            ClassroomGrid(c, new[] { -288.3f, -285.8f }, new[] { 144.3f, 146.6f });
            c.OnWall("Classroom", "blackboard", -287f, 148f, "N", 1f, 0.9f, new Vector3(1.4f, 0.5f, 1f), 1.0f);
            c.P("Classroom", "officeDesk", -287f, 149.3f, 180f);
            c.P("Classroom", "books", -284.6f, 142.0f, 30f);
            c.PW("Classroom", "bedSingle", -280.3f, 148.6f, 180f, "N");          // D 보건실
            c.P("Classroom", "medCart", -278.6f, 149.6f, 180f);
            c.PW("Classroom", "waterCooler", -276.2f, 150.0f, 225f, "NE");
            c.P("Classroom", "blood", -279.0f, 145.0f, 20f);
            c.PW("Classroom", "bookcaseWide", -255.5f, 150.0f, 180f, "N");        // G 도서실
            c.PW("Classroom", "bookcaseWide", -252.6f, 150.0f, 180f, "N");
            c.PW("Classroom", "bookcase", -250.9f, 146.5f, 270f, "E");
            c.PW("Classroom", "bookcase", -257.1f, 146.5f, 90f, "W");
            c.P("Classroom", "table", -254.0f, 145.6f, 0f);
            c.P("Classroom", "chair", -254.0f, 144.7f, 0f);
            c.P("Classroom", "chair", -254.0f, 146.5f, 180f, new Vector3(0, 0, 90));
            c.P("Classroom", "books", -255.6f, 143.2f, 70f);
            c.Light("Lights", -295f, 145.5f); c.Light("Lights", -287f, 145.5f); c.Light("Lights", -278.5f, 145.5f); c.Light("Lights", -254f, 145.5f);

            // 교실 A: 칠판 = 북쪽 벽, 학생 책상 3x2 (의자 남쪽, 북쪽을 봄), 교탁 = 칠판 앞
            ClassroomGrid(c, new[] { -305.2f, -303.3f, -301.4f }, new[] { 144.3f, 146.6f });
            c.OnWall("Classroom", "blackboard", -303.5f, 148f, "N", 1f, 0.9f, new Vector3(1.6f, 0.5f, 1f), 1.0f);   // 창문 높이(1.5m)를 피해 1m에서 벽 찾기
            c.P("Classroom", "officeDesk", -303.5f, 149.3f, 180f);
            c.P("Classroom", "trashcan", -300.1f, 141.2f, 0f);
            c.P("Classroom", "books", -302.4f, 143.2f, 40f);                          // 바닥에 떨어진 책

            // 교실 E: 책상 2x2 + 넘어진 의자
            ClassroomGrid(c, new[] { -272.6f, -270.0f }, new[] { 144.3f, 146.6f });
            c.OnWall("Classroom", "blackboard", -271f, 148f, "N", 1f, 0.9f, new Vector3(1.6f, 0.5f, 1f), 1.0f);   // 창문 높이(1.5m)를 피해 1m에서 벽 찾기
            c.P("Classroom", "officeDesk", -271f, 149.3f, 180f);
            c.P("Classroom", "chair", -268.4f, 143.4f, 70f, new Vector3(0, 0, 90));
            c.P("Classroom", "kBoxOpen", -273.8f, 149.6f, 15f);

            // 교무실 F: 사무 책상 3 + 의자, 서랍장, 정수기
            c.P("Staffroom", "pcDesk", -264.3f, 145.2f, 180f);
            c.P("Staffroom", "officeDesk", -261.0f, 144.6f, 0f);
            c.P("Staffroom", "retroChair", -261.0f, 143.5f, 0f);
            c.P("Staffroom", "officeDesk", -263.0f, 148.8f, 180f);
            c.P("Staffroom", "retroChair", -263.0f, 149.9f, 180f);
            c.PW("Staffroom", "officeDrawer", -259.5f, 147.5f, 270f, "E");
            c.PW("Staffroom", "waterCooler", -259.0f, 150.0f, 225f, "NE");

            // 복도: 사물함(문 틈 피해서 북쪽 벽), 쓰레기통, 떨어진 책·박스, 핏자국
            foreach (var x in new[] { -294.5f, -286.5f, -279.0f, -256.0f })
                c.PW("Hallway", "locker", x, 139.0f, 180f, "N");
            c.P("Hallway", "trashcan", -297.5f, 138.9f, 0f, default, 1f, null, "N");
            c.P("Hallway", "trashcan", -265.0f, 138.9f, 0f, default, 1f, null, "N");
            c.P("Hallway", "books", -288.0f, 134.0f, 120f);
            c.P("Hallway", "books", -275.5f, 132.5f, 10f);
            c.P("Hallway", "kBoxClosed", -252.5f, 137.0f, 30f);
            c.P("Hallway", "blood", -283.0f, 134.5f, 60f);

            c.Light("Lights", -303.5f, 145.5f);
            c.Light("Lights", -271f, 145.5f);
            c.Light("Lights", -262.5f, 146.5f);
            c.Light("Lights", -298f, 134.5f, 2.8f);
            c.Light("Lights", -270f, 134.5f, 2.8f);
        });
    }

    /// <summary>학생 책상(Kenney desk를 가로세로 60%로 줄임) + 의자(책상 남쪽, 북쪽 칠판을 봄) 그리드</summary>
    static void ClassroomGrid(Ctx c, float[] xs, float[] zs)
    {
        foreach (var z in zs)
            foreach (var x in xs)
            {
                c.P("Classroom", "desk", x, z, 0f, default, 1f, new Vector3(0.6f, 1f, 0.6f));
                c.P("Classroom", "chair", x, z - 0.55f, 0f);
            }
    }

    // ───────────────────────── 6. 마트 (Building by Kay Lousberg - bbH2Bg73qM, 매장 23x43m) ─────────────────────────
    // 실측: 매장 x -108.4~-85.5, z -85.6~-42.65, 바닥 15.1, 회전 -0.76°. 입구 = 서쪽 벽 틈(z -65~-58, 가운데 -64.1)
    //       동쪽 뒤 작은 방(x -84~-78.7)은 계단/창고 구조라 제외. 서남쪽(x -108~-104, z -73~-68)에 기둥 구조물
    // 구역: 입구 옆 계산대 / 가운데 진열대 3줄(선반 간격 2m) / 북쪽 벽 냉장 쇼케이스 / 동쪽 벽 벽선반
    // 분위기: 약탈당한 편의점 — 넘어진 진열대 + 바닥에 흩어진 상품, 박스·드럼통, 핏자국, 입구 바깥 콘크리트 방벽

    [MenuItem("Tools/Interior/Build 6. Mart (마트)")]
    public static void BuildMart() => Run("Mart", "Buildings/Building by Kay Lousberg - bbH2Bg73qM", 15.1f, -0.76f, c =>
    {
        c.Door(-108.4f, -61.5f);   // 서쪽 벽의 실제 입구 틈

        // 계산대 (입구 들어와서 북쪽), 계산대 뒤 담배·잡화 선반
        c.P("Checkout", "martCounter", -103.6f, -56.2f, 90f);
        c.P("Checkout", "martRack", -101.6f, -54.4f, 270f);

        // 진열대 3줄 (동서로 길게, 줄 간격 = 깊이 1.28 + 통로 2m), 한 줄 = 곤돌라 3개
        float[] rowZ = { -69.5f, -72.8f, -76.1f };
        float[] colX = { -100.2f, -97.5f, -94.8f };
        for (int r = 0; r < rowZ.Length; r++)
            for (int i = 0; i < colX.Length; i++)
            {
                if (r == 2 && i == 2) continue;                                   // 넘어진 자리
                string key = (r == 1 && i == 1) ? "snackShelf" : "martGondola";
                c.P("Aisles", key, colX[i], rowZ[r], 0f);
            }
        // 넘어진 진열대 (마지막 줄 끝, 옆으로 75°) + 흩어진 상품
        c.P("Aisles", "martGondola", -94.6f, -77.2f, 8f, new Vector3(0, 0, 75));
        string[] goods = { "foodCan", "foodBag", "foodCarton", "foodBottle", "foodCan", "foodCandy", "foodBag" };
        float[,] gp = { { -93.0f, -75.4f }, { -92.3f, -76.6f }, { -95.9f, -74.9f }, { -91.8f, -78.0f }, { -96.6f, -78.4f }, { -93.7f, -79.0f }, { -90.9f, -75.8f } };
        for (int i = 0; i < goods.Length; i++) c.P("Aisles", goods[i], gp[i, 0], gp[i, 1], i * 47f, i % 3 == 0 ? new Vector3(90, 0, 0) : default);

        // 북쪽 벽 냉장 쇼케이스 (정면 = 남쪽) + 가운데 냉동 진열대
        c.PW("Storage", "martTall2", -104.5f, -43.4f, 180f, "N");
        c.PW("Storage", "martTall2", -102.9f, -43.4f, 180f, "N");
        c.PW("Storage", "martFridge", -100.6f, -43.4f, 180f, "N");
        c.PW("Storage", "martFridge", -98.4f, -43.4f, 180f, "N");
        c.P("Storage", "martFreezer", -94.5f, -49.5f, 180f);
        // 동쪽 벽 벽선반 (정면 = 서쪽)
        foreach (var z in new[] { -48.5f, -52.0f, -66.5f, -70.0f, -79.0f })
            c.PW("Storage", "martWallShelf", -86.2f, z, 270f, "E");
        c.P("Storage", "martCounter2", -90.5f, -60.5f, 270f);           // 농산물 칸 진열대

        // 약탈 흔적: 박스·드럼통, 핏자국
        c.P("Clutter", "boxOpen", -88.6f, -83.8f, 20f);
        c.P("Clutter", "box", -89.4f, -84.4f, -10f);
        c.P("Clutter", "barrel", -87.4f, -84.6f, 0f);
        c.P("Clutter", "barrelOpen", -101.5f, -83.9f, 0f, new Vector3(90, 0, 0));
        c.P("Clutter", "kBoxOpen", -97.0f, -60.5f, 35f);
        c.P("Clutter", "blood", -97.4f, -66.4f, 50f);
        c.P("Clutter", "blood2", -91.5f, -56.0f, 160f);

        // 입구 바깥 콘크리트 방벽 2개 (약탈 저지 흔적, 문 앞 1.5m는 비움)
        c.P("Exterior", "jersey", -114.8f, -59.0f, 70f);
        c.P("Exterior", "jerseyCaution", -114.6f, -69.0f, 110f);

        c.Light("Lights", -102f, -58f);
        c.Light("Lights", -97.5f, -73f);
        c.Light("Lights", -98f, -48f);
        c.Light("Lights", -89f, -66f);
    });

    // ───────────────────────── 7. 창고 (Structure by Quaternius - ilWoURnbZW, 43x23m) ─────────────────────────
    // 실측: x -25.1~18.1, z -329.4~-306.5, 바닥 15.0, 회전 -2.07°. 문 = 남쪽 벽 x≈-9.3
    //       기둥: (-17,-312.5) (14.5,-316.5) (15.5,-322) (5.5,-324) (-11,-327) 근처, 서쪽 작은 방들(x -33~-27)은 제외
    // 북쪽 벽 = 큰 랙 4개, 동쪽 벽 = 랙 2개, 남서 = 컨테이너 2개, 남동 모서리 = 드럼통 5개, 가운데 통로 = 지게차, 벽 위 환풍기 3

    [MenuItem("Tools/Interior/Build 7. Warehouse (창고)")]
    public static void BuildWarehouse() => Run("Warehouse", "Buildings/Enterable_Structure by Quaternius - ilWoURnbZW", 15.0f, -2.07f, c =>
    {
        c.Door(-9.3f, -329.4f);

        // 랙 (상자 실린 철제 랙 묶음, 길이 4.45m): 북쪽 벽 4 + 동쪽 벽 2
        foreach (var x in new[] { -21.0f, -16.3f, -11.6f, -6.9f })
            c.PW("Storage", "rack", x, -308.0f, 90f, "N");
        c.PW("Storage", "rack", 16.6f, -309.5f, 0f, "E");
        c.PW("Storage", "rack", 16.6f, -314.2f, 0f, "E");
        // 작은 선반 2 (서쪽 벽)
        c.PW("Storage", "rackSmall", -24.3f, -318.0f, 90f, "W");
        c.PW("Storage", "rackSmall", -24.3f, -320.6f, 90f, "W");

        // 컨테이너 2 (남서, 남쪽 벽에 붙임, 문 열린 쪽이 동쪽)
        c.PW("Storage", "container", -21.4f, -327.8f, 90f, "SW");
        c.P("Storage", "container", -21.4f, -323.9f, 90f, default, 1f, null, "W");

        // 드럼통 5 (남동 모서리)
        float[,] dp = { { 17.2f, -328.6f }, { 16.5f, -328.7f }, { 15.8f, -328.6f }, { 17.1f, -327.9f }, { 16.4f, -327.9f } };
        for (int i = 0; i < 5; i++) c.P("Storage", "drum", dp[i, 0], dp[i, 1], i * 30f);

        // 바닥 팔레트 + 상자 더미 3
        foreach (var p in new[] { new Vector2(-2.5f, -310.5f), new Vector2(3.5f, -311.0f), new Vector2(8.5f, -327.5f) })
        {
            var pal = c.P("Storage", "pallet", p.x, p.y, 10f);
            var bx = c.OnTopOf("Storage", "wBox", pal, 5f, -0.25f, 0.1f);
            c.OnTopOf("Storage", "wBox", bx, -15f);
        }
        c.P("Storage", "wBarrel", 9.6f, -327.0f, 0f);

        // 지게차 (가운데 통로)
        c.P("Storage", "forklift", -1.5f, -318.5f, 35f);

        // 환풍기 3 (벽 위쪽, 랙보다 높이)
        c.OnWall("Storage", "exhaustFan", 12f, -312f, "E", 1f, 3.6f);
        c.OnWall("Storage", "exhaustFan", 12f, -319.5f, "E", 1f, 3.0f);
        c.OnWall("Storage", "exhaustFan", 3f, -312f, "N", 1f, 3.6f);

        c.P("Clutter", "blood", 1.5f, -322.0f, 25f);
        c.P("Clutter", "boxOpen", -4.0f, -325.5f, 40f);

        c.Light("Lights", -14f, -311f, 3.4f);
        c.Light("Lights", 0f, -318f, 3.4f);
        c.Light("Lights", 13f, -314f, 3.4f);
        c.Light("Lights", -9f, -325f, 3.0f);
    });

    // ───────────────────────── 8. 거리 / 차량 / 폐허 / 미션 소품 / 주유소 ─────────────────────────
    // 씬 루트 "Street_Decor" 아래 그룹(Cars, CityProps, Ruins, MissionProps, GasStation). 고정 시드라 다시 실행해도 같은 배치.
    // 축척: 이 맵은 도로 폭 38m, 기존 차(Proop3) 길이 11.7m인 약 2.5배 축척 → 차·신호등·방벽은 맵 축척, 사람 손 소품(시신·제리캔)은 실제 크기
    // 길가 배치 기준: Kenney 도로 조각(로컬 X = 도로 방향), 차선(주차) = 중심에서 ±11m, 인도 = ±14m
    // 이미 씬에 있는 것은 새로 안 만듦: 가로등 183개(light-square, StreetLightManager가 관리), 기존 차 70대, 런타임 장식(WorldFX 차 불,
    // WorldFallenProps 쓰러진 가로등·쓰레기통, WorldDecals 도로 균열·핏자국)


    static readonly string[] CarNodes = { "car 1", "car 2", "car 3", "car 1.RC", "car 2.RC", "car 1,/2", "car 2./2", "car ./" };

    [MenuItem("Tools/Interior/Build 8. Street (거리·차량·미션 소품·주유소)")]
    public static void BuildStreet()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[Interior] Play 모드에서는 실행 불가"); return; }
        BeginSession();
        var log = new System.Text.StringBuilder();
        try
        {
            var root = RootFull("Street_Decor");   // 건물 실내(Building_Interior_*)와 구분되는 거리 장식 루트
            ClearGenerated(root);
            Physics.SyncTransforms();
            var c = new Ctx { root = root, floorGuess = 15.0f, frameYaw = 0f, frameCenter = Vector3.zero };
            var rng = new System.Random(20261119);
            var roads = RoadPieces();
            var doors = AllDoors();

            int cars = StreetCars(c, rng, roads, doors, log);
            int props = CityProps(c, rng, roads, doors, log);
            int ruins = Ruins(c, rng, roads, doors, log);
            MissionProps(c, log);
            GasStation(c, log);
            log.Insert(0, $"[Interior] Street: 차 {cars}대, 도시 소품 {props}개, 폐허 {ruins}개, 전체 {c.count}개\n");
        }
        finally { EndSession(); }
        Debug.Log(log.ToString());
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    struct RoadPiece { public Vector3 c; public Vector3 along; public Vector3 side; public string kind; }

    /// <summary>씬 Roads 아래 Kenney 도로 조각 (직선·횡단보도 = 차/소품 자리, 교차로 = 신호등 자리). 다리·경사 조각은 제외</summary>
    static List<RoadPiece> RoadPieces()
    {
        var list = new List<RoadPiece>();
        var roads = GameObject.Find("Roads");
        if (roads == null) return list;
        foreach (Transform t in roads.transform)
        {
            string n = t.name;
            string kind = n.StartsWith("road-straight") ? "straight" : n.StartsWith("road-crossing") ? "crossing" : (n.StartsWith("road-intersection") || n.StartsWith("road-crossroad")) ? "intersection" : null;
            if (kind == null || !WorldBounds(t.gameObject, out var b) || b.size.y > 1.5f) continue; // 경사/다리(높이 있음) 제외
            var along = t.rotation * Vector3.right; along.y = 0; along.Normalize();
            list.Add(new RoadPiece { c = new Vector3(b.center.x, b.max.y, b.center.z), along = along, side = Vector3.Cross(Vector3.up, along), kind = kind });
        }
        list.Sort((a, b2) => (a.c.x * 1000 + a.c.z).CompareTo(b2.c.x * 1000 + b2.c.z)); // 순서 고정 (시드 재현)
        return list;
    }

    /// <summary>모든 건물 문 위치 (문 앞은 비워둠)</summary>
    static List<Vector3> AllDoors()
    {
        var l = new List<Vector3>();
        foreach (var b in Object.FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None))
            if (b.TryGetDoor(out var d, out _)) l.Add(d);
        return l;
    }

    static bool NearAny(Vector3 p, List<Vector3> pts, float r)
    {
        foreach (var q in pts) if (new Vector2(p.x - q.x, p.z - q.z).sqrMagnitude < r * r) return true;
        return false;
    }

    /// <summary>이 오브젝트 콜라이더가 도로·지면 말고 다른 것(건물, 기존 차, 가로등, 앞서 놓은 소품)과 겹치는지</summary>
    static bool Blocked(GameObject go)
    {
        Physics.SyncTransforms();
        var bc = go.GetComponent<BoxCollider>();
        Vector3 c; Vector3 half; Quaternion rot;
        if (bc != null) { c = go.transform.TransformPoint(bc.center); half = Vector3.Scale(bc.size * 0.5f, go.transform.lossyScale); rot = go.transform.rotation; }
        else { if (!WorldBounds(go, out var wb)) return false; c = wb.center; half = wb.extents; rot = Quaternion.identity; }
        half = new Vector3(Mathf.Abs(half.x), Mathf.Abs(half.y) * 0.8f, Mathf.Abs(half.z));
        foreach (var col in Physics.OverlapBox(c + Vector3.up * half.y * 0.2f, half, rot, ~0, QueryTriggerInteraction.Ignore))
        {
            if (col.transform.IsChildOf(go.transform) || col is TerrainCollider || sessionPlates.Contains(col as MeshCollider)) continue;
            string n = col.name.ToLower();
            if (n.StartsWith("road") || n.StartsWith("ground") || n.Contains("asphalt") || n.Contains("walking")) continue;
            return true;
        }
        return false;
    }

    /// <summary>놓고 겹치면 along 방향으로 조금씩 옮겨 다시 시도, 끝내 겹치면 지움</summary>
    static GameObject PlaceFree(Ctx c, string group, string key, Vector3 pos, float yaw, Vector3 along, Vector3 tilt = default, float scaleMul = 1f)
    {
        float[] shifts = { 0f, 6f, -6f, 12f, -12f };
        foreach (var s in shifts)
        {
            var p = pos + along * s;
            var go = Place(Group(c.root, group), key, new Vector3(p.x, FloorAt(p.x, p.z, pos.y), p.z), yaw, tilt, scaleMul);
            if (go == null) return null;
            if (!Blocked(go)) { c.count++; return go; }
            Object.DestroyImmediate(go);
        }
        return null;
    }

    static void Tint(GameObject go, Color col)
    {
        var tm = TintMaterial(col);
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var arr = r.sharedMaterials;
            for (int i = 0; i < arr.Length; i++) arr[i] = tm;
            r.sharedMaterials = arr;
        }
    }

    // ── 차량: 정상 40% / 녹슴·파손 40% / 연기·불 20%, 뒤집힌 차 3, 도로 가운데 사고 2쌍 ──
    static int StreetCars(Ctx c, System.Random rng, List<RoadPiece> roads, List<Vector3> doors, System.Text.StringBuilder log)
    {
        var existing = new List<Vector3>();
        var proop = GameObject.Find("Proop3");
        if (proop != null) foreach (Transform t in proop.transform) existing.Add(t.position);
        var cand = roads.FindAll(r => r.kind == "straight" || r.kind == "crossing");
        int placed = 0, target = 18;
        var used = new List<Vector3>();
        for (int guard = 0; placed < target && guard < 200 && cand.Count > 0; guard++)
        {
            var r = cand[rng.Next(cand.Count)];
            float sideSign = rng.Next(2) == 0 ? -1f : 1f;
            bool accident = placed < 4 && placed % 2 == 0 && guard % 3 == 0;   // 사고 쌍은 도로 가운데
            Vector3 pos = r.c + r.side * (accident ? rng.Range(-2f, 2f) : 11f * sideSign) + r.along * rng.Range(-12f, 12f);
            if (NearAny(pos, existing, 10f) || NearAny(pos, used, 14f) || NearAny(pos, doors, 18f)) continue;
            float yaw = Mathf.Atan2(r.along.x, r.along.z) * Mathf.Rad2Deg + (rng.Next(2) == 0 ? 0f : 180f); // 차 길이축 = 래퍼 Z (resetYaw)
            int state = placed % 5;    // 0,1 정상 / 2,3 녹슴·파손 / 4 연기·불
            bool flipped = placed == 5 || placed == 11 || placed == 16;
            Vector3 tilt = flipped ? new Vector3(0, 0, placed == 11 ? 92f : 180f) : default;
            if (accident) yaw += rng.Range(25f, 50f) * (rng.Next(2) == 0 ? 1 : -1);
            var key = "streetCar" + rng.Next(CarNodes.Length);
            var car = PlaceFree(c, "Cars", key, pos, yaw, r.along, tilt);
            if (car == null) continue;
            used.Add(car.transform.position);
            placed++;
            if (state == 2 || state == 3 || flipped) Tint(car, state == 2 ? new Color(0.42f, 0.27f, 0.17f) : new Color(0.30f, 0.29f, 0.27f)); // 녹슨 / 그을린 파손
            if (state == 4)
            {
                Tint(car, new Color(0.13f, 0.12f, 0.11f));                                   // 불탄 차체
                WorldBounds(car, out var cb);
                var hood = cb.center + Vector3.up * cb.extents.y * 0.9f + (Quaternion.Euler(0, yaw, 0) * Vector3.right) * cb.extents.magnitude * 0.25f;
                AddSmoke(car.transform, hood);
                if (placed % 2 == 0) AddFire(car.transform, hood);
            }
            if (accident)
            {
                // 부딪친 두 번째 차: 첫 차 옆으로 비스듬히
                var p2 = car.transform.position + r.along * 9f + r.side * 3f;
                var car2 = PlaceFree(c, "Cars", "streetCar" + rng.Next(CarNodes.Length), p2, yaw + 75f, r.along);
                if (car2 != null) { Tint(car2, new Color(0.30f, 0.29f, 0.27f)); AddSmoke(car2.transform, car2.transform.position + Vector3.up * 3f); used.Add(car2.transform.position); placed++; }
            }
        }
        return placed;
    }

    static Material FxMaterial(string name, string shader)
    {
        const string dir = "Assets/Materials/Interior";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Materials", "Interior");
        string path = $"{dir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find(shader));
        m.mainTexture = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    /// <summary>연기: 회색, 위로, rate 5, lifetime 3s (보닛에서 피어오름)</summary>
    static void AddSmoke(Transform parent, Vector3 pos)
    {
        var go = new GameObject("Smoke");
        go.transform.SetParent(parent, true);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 3f; main.startSpeed = 2.0f; main.startSize = new ParticleSystem.MinMaxCurve(1.5f, 3.0f);
        main.startColor = new Color(0.35f, 0.35f, 0.35f, 0.55f); main.maxParticles = 40; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 5f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = 0.6f;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.8f));
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0.8f, 0), new GradientAlphaKey(0f, 1) });
        col.color = g;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = FxMaterial("FX_Smoke", "Legacy Shaders/Particles/Alpha Blended");
    }

    /// <summary>불: 주황 파티클 + Point Light (intensity 2, range 3m)</summary>
    static void AddFire(Transform parent, Vector3 pos)
    {
        var go = new GameObject("Fire");
        go.transform.SetParent(parent, true);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f); main.startSpeed = 1.5f; main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startColor = new Color(1f, 0.55f, 0.15f, 0.9f); main.maxParticles = 60;
        var em = ps.emission; em.rateOverTime = 25f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8f; sh.radius = 0.7f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.8f, 0.3f), 0), new GradientColorKey(new Color(0.8f, 0.2f, 0.05f), 1) }, new[] { new GradientAlphaKey(1f, 0), new GradientAlphaKey(0f, 1) });
        col.color = g;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = FxMaterial("FX_Fire", "Legacy Shaders/Particles/Additive");
        var lgo = new GameObject("FireLight");
        lgo.transform.SetParent(go.transform, false);
        lgo.transform.localPosition = new Vector3(0, 0, 0.8f);
        var l = lgo.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.55f, 0.2f); l.intensity = 2f; l.range = 3f; l.shadows = LightShadows.None;
    }

    // ── 도시 소품: 인도(±14m)에 벤치·쓰레기통·공중전화·소화전·우체통, 교차로 신호등, 공사 콘·바리케이드로 도로 일부 막기, 콘크리트 방벽 3세트 ──
    static int CityProps(Ctx c, System.Random rng, List<RoadPiece> roads, List<Vector3> doors, System.Text.StringBuilder log)
    {
        int n = 0;
        string[] cycle = { "cityBench", "trashBin", "hydrant", "phoneBooth", "mailbox", "newsStand", "dumpster" };
        int k = 0;
        var straights = roads.FindAll(r => r.kind == "straight");
        for (int i = 0; i < straights.Count; i += 2)
        {
            var r = straights[i];
            float s = (i / 2) % 2 == 0 ? 1f : -1f;
            var pos = r.c + r.side * (14.2f * s) + r.along * rng.Range(-10f, 10f);
            if (NearAny(pos, doors, 8f)) continue;
            float yaw = Mathf.Atan2(-r.side.x * s, -r.side.z * s) * Mathf.Rad2Deg;   // 도로 쪽을 봄
            if (PlaceFree(c, "CityProps", cycle[k % cycle.Length], pos, yaw, r.along, default, 1.6f) != null) { n++; k++; }
        }
        // 신호등: 교차로마다 한쪽 모서리
        foreach (var r in roads.FindAll(x => x.kind == "intersection"))
        {
            var corner = r.c + r.side * 15.5f + r.along * 15.5f;
            float yaw = Mathf.Atan2(r.c.x - corner.x, r.c.z - corner.z) * Mathf.Rad2Deg;
            if (PlaceFree(c, "CityProps", "trafficLight", corner, yaw, r.side) != null) n++;
        }
        // 공사 구간 2곳: 도로 한쪽 차선을 콘·바리케이드·작업등으로 막음 (좀비가 다른 차선으로 몰리게)
        foreach (var target in new[] { new Vector3(-61f, 15f, -278f), new Vector3(-149f, 15f, -281f) })
        {
            var r = Nearest(straights, target);
            for (int i = -2; i <= 2; i++)
            {
                var p = r.c + r.side * 6f + r.along * (i * 4f);
                string key = i == 0 ? "cTrafficLight" : (i % 2 == 0 ? "cBarrier" : "cBarrel");
                if (PlaceFree(c, "CityProps", key, p, Mathf.Atan2(r.along.x, r.along.z) * Mathf.Rad2Deg, r.along, default, 2.2f) != null) n++;
            }
            if (PlaceFree(c, "CityProps", "cSign", r.c + r.side * 10f + r.along * -11f, 0f, r.along, default, 2.2f) != null) n++;
        }
        // 콘크리트 방벽 3세트 (중요 길목: 경찰서 앞 도로 북쪽, 병원 앞 도로, 다리 남쪽 끝) — 가운데는 사람 한 명 지나갈 틈
        foreach (var target in new[] { new Vector3(-145f, 15f, -80f), new Vector3(-250f, 15f, -163f), new Vector3(-8f, 15f, 64f) })
        {
            var r = Nearest(roads, target);
            float yaw = Mathf.Atan2(r.side.x, r.side.z) * Mathf.Rad2Deg + 90f;   // 길이 방향 = 도로를 가로지름
            foreach (var off in new[] { -9f, -4.5f, 4.5f, 9f })
                if (PlaceFree(c, "CityProps", rng.Next(2) == 0 ? "jersey" : "jerseyCaution", r.c + r.side * off, yaw + rng.Range(-8f, 8f), r.along, default, 2.0f) != null) n++;
        }
        return n;
    }

    static RoadPiece Nearest(List<RoadPiece> list, Vector3 p)
    {
        RoadPiece best = default; float bd = float.MaxValue;
        foreach (var r in list) { float d = (r.c - p).sqrMagnitude; if (d < bd) { bd = d; best = r; } }
        return best;
    }

    // ── 폐허: 건물 외벽 덩굴 25, 기둥 잔해 8, 덮인 시신 6, 거리 핏자국 12, 판자·박스 더미 6 ──
    static int Ruins(Ctx c, System.Random rng, List<RoadPiece> roads, List<Vector3> doors, System.Text.StringBuilder log)
    {
        int n = 0;
        var blds = new List<BuildingInterior>(Object.FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None));
        blds.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        int vines = 0;
        for (int guard = 0; vines < 25 && guard < 120 && blds.Count > 0; guard++)
        {
            var b = blds[rng.Next(blds.Count)];
            var tc = b.GetComponent<Collider>();
            if (tc == null) continue;
            var bb = tc.bounds;
            Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
            var d = dirs[rng.Next(4)];
            float ext = Mathf.Abs(Vector3.Dot(bb.extents, new Vector3(Mathf.Abs(d.x), 0, Mathf.Abs(d.z))));
            var lat = Vector3.Cross(Vector3.up, d) * rng.Range(-0.35f, 0.35f) * (Mathf.Abs(d.x) > 0 ? bb.size.z : bb.size.x);
            var from = new Vector3(bb.center.x, 0, bb.center.z) + d * (ext + 6f) + lat;
            float fy = FloorAt(from.x, from.z, 15f);
            from.y = fy + 1.5f;
            if (!WallHit(from, -d, 12f, out var hp, out var hn)) continue;
            if (!hits_building(hp, b) || NearAny(hp, doors, 5f)) continue;
            hn.y = 0; hn.Normalize();
            var go = Place(Group(c.root, "Ruins"), rng.Next(3) == 0 ? "vines" : "wallVine", new Vector3(hp.x + hn.x * 0.05f, fy, hp.z + hn.z * 0.05f), Mathf.Atan2(hn.x, hn.z) * Mathf.Rad2Deg, default, rng.Range(0.9f, 1.4f), "wallVine@wall");
            if (go == null) continue;
            WorldBounds(go, out var vb);
            go.transform.position += hn * (Mathf.Abs(vb.extents.x * hn.x) + Mathf.Abs(vb.extents.z * hn.z));
            c.count++; vines++; n++;
        }
        // 길가 잔해·시신·핏자국·더미
        var straights = roads.FindAll(r => r.kind == "straight" || r.kind == "crossing");
        for (int i = 0; i < 8; i++)
        {
            var r = straights[rng.Next(straights.Count)];
            var p = r.c + r.side * (rng.Next(2) == 0 ? 15.5f : -15.5f) + r.along * rng.Range(-14f, 14f);
            if (PlaceFree(c, "Ruins", "pillarDebris", p, rng.Range(0f, 360f), r.along, default, 1.8f) != null) n++;
        }
        // 덮인 시신: 경찰서 앞 2, 병원 안뜰 2, 거리 2
        var bodies = new List<Vector3> { new Vector3(-115.5f, 15f, -129f), new Vector3(-116f, 15f, -139.5f), new Vector3(-318f, 15f, 12f), new Vector3(-322f, 15f, 24f) };
        for (int i = 0; i < 2; i++) { var r = straights[rng.Next(straights.Count)]; bodies.Add(r.c + r.side * rng.Range(-7f, 7f) + r.along * rng.Range(-10f, 10f)); }
        foreach (var p in bodies) if (PlaceFree(c, "Ruins", "deadBody", p, rng.Range(0f, 360f), Vector3.right) != null) n++;
        for (int i = 0; i < 12; i++)
        {
            var r = straights[rng.Next(straights.Count)];
            var p = r.c + r.side * rng.Range(-9f, 9f) + r.along * rng.Range(-14f, 14f);
            if (NearAny(p, doors, 4f)) continue;
            var go = Place(Group(c.root, "Ruins"), rng.Next(2) == 0 ? "blood" : "blood2", new Vector3(p.x, FloorAt(p.x, p.z, 15f), p.z), rng.Range(0f, 360f), default, rng.Range(1.2f, 2.0f));
            if (go != null) { c.count++; n++; }
        }
        for (int i = 0; i < 6; i++)
        {
            var r = straights[rng.Next(straights.Count)];
            var p = r.c + r.side * (rng.Next(2) == 0 ? 13f : -13f) + r.along * rng.Range(-12f, 12f);
            var pl = PlaceFree(c, "Ruins", "planks", p, rng.Range(0f, 360f), r.along, default, 1.6f);
            if (pl == null) continue;
            n++;
            var bx = PlaceFree(c, "Ruins", rng.Next(2) == 0 ? "boxOpen" : "box", p + r.along * 1.4f, rng.Range(0f, 360f), r.along, default, 1.3f);
            if (bx != null) n++;
        }
        return n;
    }

    static bool hits_building(Vector3 p, BuildingInterior b)
    {
        // 맞은 점이 이 건물 트리거 근처(바운드 + 3m)인지 → 옆 건물·나무에 붙는 것 방지
        var bb = b.GetComponent<Collider>().bounds; bb.Expand(6f);
        return bb.Contains(new Vector3(p.x, bb.center.y, p.z));
    }

    // ── 미션 소품 (보이는 모형만 — 실제 미션 판정은 MissionManager가 그날 위치에 따로 만듦) ──
    static void MissionProps(Ctx c, System.Text.StringBuilder log)
    {
        // 발전기: 창고 동쪽 바깥 (전력 복구 미션 분위기)
        var gen = Place(Group(c.root, "MissionProps"), "generator", new Vector3(22.8f, FloorAt(22.8f, -316f, 15f), -316f), 88f, default, 2.0f);
        if (gen != null) { c.count++; if (Blocked(gen)) { gen.transform.position += Vector3.right * 2f; } }
        c.P("MissionProps", "jerrycan", 21.0f, -313.8f, 30f, default, 1.4f);
        // 도어락 패널: 경찰서 입구 옆 벽(바깥), 병원 정문 옆 벽(안뜰 쪽) / 카드: 경찰서 접수 책상 옆 바닥
        c.OnWall("MissionProps", "doorPanel", -114f, -127.4f, "E", 1f, 1.3f, null, 1.3f);
        c.OnWall("MissionProps", "doorPanel", -309f, 24.6f, "E", 1f, 1.3f, null, 1.3f);
        c.P("MissionProps", "keyCard", -100.2f, -129.4f, 25f);
        // 라디오 송신기 역할: 경찰서 옥상에 TV 안테나(크게) + 무전기
        float roofY = RoofAt(-100f, -138f);
        if (roofY > 20f)
        {
            var ant = Place(Group(c.root, "MissionProps"), "antenna", new Vector3(-100f, roofY, -138f), 0f, default, 1f, "antenna@top");
            if (ant != null) c.count++;
            var rad = Place(Group(c.root, "MissionProps"), "radio", new Vector3(-98.2f, roofY, -138f), 270f, default, 4f, "radio@top");
            if (rad != null) c.count++;
        }
        else log.AppendLine("  - 경찰서 옥상 높이를 못 찾아 송신기 생략");
    }

    static float RoofAt(float x, float z)
    {
        bool prev = Physics.queriesHitBackfaces;
        try
        {
            Physics.queriesHitBackfaces = false;
            if (Physics.Raycast(new Vector3(x, 150f, z), Vector3.down, out var h, 150f, ~0, QueryTriggerInteraction.Ignore)) return h.point.y;
        }
        finally { Physics.queriesHitBackfaces = prev; }
        return 0f;
    }

    // ── 주유소 (사용자가 놓은 low_poly_gas_station, 앞마당 = 북쪽 도로 z≈-202 쪽) ──
    static void GasStation(Ctx c, System.Text.StringBuilder log)
    {
        var gs = GameObject.Find("low_poly_gas_station");
        if (gs == null) { log.AppendLine("  - 주유소 없음"); return; }
        // 앞마당 주유기 3 (캐노피 앞 한 줄)
        foreach (var x in new[] { -114.5f, -108.5f, -102.5f })
            c.P("GasStation", "gasPump", x, -236.5f, 180f, default, 1.7f);
        // 제리캔 5 (바닥에 흩어짐)
        float[,] jp = { { -117f, -233.5f }, { -111.2f, -239.5f }, { -105.5f, -232.8f }, { -99.0f, -240.2f }, { -96.5f, -235.0f } };
        for (int i = 0; i < 5; i++) c.P("GasStation", "jerrycan", jp[i, 0], jp[i, 1], i * 63f, i == 3 ? new Vector3(90, 0, 0) : default, 1.4f);
        // 입구 콘크리트 방벽 2 (방어 흔적, 앞마당과 도로 사이)
        c.P("GasStation", "jersey", -121.0f, -229.8f, 12f, default, 2.0f);
        c.P("GasStation", "jerseyCaution", -95.5f, -230.4f, -14f, default, 2.0f);
        // 앞 도로: 뒤집힌 차 1 + 사고 차 1
        var flipped = Place(Group(c.root, "GasStation"), "streetCar2", new Vector3(-111f, FloorAt(-111f, -205f, 15f), -205f), 8f, new Vector3(0, 0, 180f));
        if (flipped != null) { c.count++; Tint(flipped, new Color(0.30f, 0.29f, 0.27f)); AddSmoke(flipped.transform, flipped.transform.position + Vector3.up * 2.5f); }
        var crash = Place(Group(c.root, "GasStation"), "streetCar0", new Vector3(-97f, FloorAt(-97f, -199f, 15f), -199f), 128f);
        if (crash != null) { c.count++; Tint(crash, new Color(0.42f, 0.27f, 0.17f)); }
        // 핏자국 1, 외벽 덩굴 3
        c.P("GasStation", "blood", -107.0f, -242.0f, 35f, default, 1.5f);
        c.OnWall("GasStation", "wallVine", -126f, -268f, "E", 1.2f);   // 가게 서쪽 벽 (가게 = z -273~-263)
        c.OnWall("GasStation", "wallVine", -82f, -268f, "W", 1.2f);    // 가게 동쪽 벽
        c.OnWall("GasStation", "vines", -108f, -279f, "N", 1.3f);      // 가게 뒷벽
    }

    // ───────────────────────── 입구 검수 (전체) ─────────────────────────
    // 각 Building_Interior_* 루트를 그 건물 정문 + 내부 문 틈(칸막이·교실·홀 입구) 기준으로 다시 검사·자동 수정.
    // 바깥 장식(Exterior 그룹, Street_Decor 전체)은 씬의 "모든" 건물 문 1.5m 반경에서 밀어냄.
    // 검사 항목: 문 앞 1.5m 비움(가구·데칼·소품 발밑 겹침), 벽 겹침(콜라이더 가구), 공중에 뜸
    // (문짝 여닫이 궤적: 이 맵 건물 문은 열린 구멍/미닫이라 여닫이 문짝이 없음 → 해당 없음)

    static readonly (string root, string building, Vector2[] innerDoors)[] AuditTargets =
    {
        ("Building_Interior_House",     "Buildings/building-a (4)",                                    new Vector2[0]),
        ("Building_Interior_Church",    "Buildings/Enterable_Church by Poly by Google - 2-J0Y-8AQOG",  new[] { new Vector2(-287.1f, -82.5f) }),
        ("Building_Interior_Hospital",  "Buildings/Enterable_scene (3)",                               new[] { new Vector2(-286f, 24f), new Vector2(-271f, 24f), new Vector2(-327f, -17.5f), new Vector2(-315f, -17.5f) }),
        ("Building_Interior_Police",    "Buildings/Enterable_scene (4)",                               new[] { new Vector2(-109.1f, -133.3f) }),
        ("Building_Interior_School",    "Buildings/Enterable_School",                                  new[] { new Vector2(-305.5f, 140f), new Vector2(-269.5f, 140f), new Vector2(-260.5f, 140f) }),
        ("Building_Interior_Mart",      "Buildings/Building by Kay Lousberg - bbH2Bg73qM",            new[] { new Vector2(-108.4f, -61.5f) }),
        ("Building_Interior_Warehouse", "Buildings/Enterable_Structure by Quaternius - ilWoURnbZW",   new[] { new Vector2(-9.3f, -329.4f) }),
    };

    [MenuItem("Tools/Interior/Audit All Entrances (입구 검수)")]
    public static void AuditAll()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[Interior] Play 모드에서는 실행 불가"); return; }
        var sb = new System.Text.StringBuilder("[Interior] 입구 검수\n");
        BeginSession();
        try
        {
            foreach (var t in AuditTargets)
            {
                var rootGo = GameObject.Find(t.root);
                var bgo = GameObject.Find(t.building);
                var bi = bgo != null ? bgo.GetComponent<BuildingInterior>() : null;
                if (rootGo == null || bi == null) { sb.AppendLine($"  {t.root}: 없음 (건너뜀)"); continue; }
                var extra = new List<Vector3>();
                foreach (var d in t.innerDoors) extra.Add(new Vector3(d.x, FloorAt(d.x, d.y, 15.5f), d.y));
                sb.AppendLine($"  {t.root}: {Validate(rootGo.transform, bi, true, extra, false)}");
            }
            // 바깥 장식: 모든 건물 문
            var doors = AllDoors();
            int moved = 0;
            var outside = new List<Transform>();
            var street = GameObject.Find("Street_Decor");
            if (street != null) foreach (Transform g in street.transform) foreach (Transform p in g) outside.Add(p);
            foreach (var t in AuditTargets)
            {
                var ext = GameObject.Find(t.root + "/Exterior");
                if (ext != null) foreach (Transform p in ext.transform) outside.Add(p);
            }
            foreach (var p in outside)
            {
                if (p.name.EndsWith("@wall") || p.name.EndsWith("@top")) continue;   // 벽 덩굴·옥상 소품은 바닥 통로를 안 막음
                foreach (var door in doors)
                    for (int step = 0; step < 6; step++)
                    {
                        if (!WorldBounds(p.gameObject, out var wb)) break;
                        var cp = wb.ClosestPoint(new Vector3(door.x, wb.center.y, door.z));
                        if (new Vector2(cp.x - door.x, cp.z - door.z).magnitude >= DoorClear) break;
                        var away = wb.center - door; away.y = 0;
                        p.position += away.normalized * 0.5f;
                        if (step == 0) { moved++; sb.AppendLine($"  - 바깥 소품 문 앞 → 밀어냄: {p.parent.name}/{p.name}"); }
                    }
            }
            sb.AppendLine($"  바깥 장식 {outside.Count}개 검사, 문 앞에서 밀어낸 것 {moved}개 (건물 문 {doors.Count}곳)");
        }
        finally { EndSession(); }
        Debug.Log(sb.ToString());
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    /// <summary>전체 다시 만들기 (1~8) → 입구 검수 → NavMesh 재굽기(벽 반영, 씬 저장)</summary>
    [MenuItem("Tools/Interior/Build All + Audit + Rebake NavMesh")]
    public static void BuildAll()
    {
        BuildHouse(); BuildChurch(); BuildHospital(); BuildPolice(); BuildSchool(); BuildMart(); BuildWarehouse(); BuildStreet();
        AuditAll();
        RebakeNavMeshWithWalls.Run();
    }

    // ───────────────────────── 9. 나머지 건물 전부 (자동 테마) ─────────────────────────
    // 위 7곳(손으로 꾸민 건물)을 뺀 들어갈 수 있는 건물 전부(약 91채)를 테마별로 자동 배치. 씬 루트 Building_Interior_Auto 아래
    // Building_Interior_[건물 이름]으로 묶음. 고정 시드(건물 이름)라 다시 실행해도 같은 배치, 툴이 만든 구역 그룹만 지우고 새로 만듦.
    //  - 방 크기: 게임이 런타임에 벽에 맞춰 줄이는 트리거(BuildingInterior.FitTriggerToWalls)를 에디터에서 같은 방식으로 재고 원래대로 되돌림
    //  - 벽 가구: 벽을 따라 차례로 놓고 벽에 밀착(문 3.2m 안은 비움), 가운데: 테마별 그리드, 잡동사니: 빈 바닥 무작위(겹치면 다른 자리)
    //  - 테마: 민가(home) / 상점(shop) / 식당(cafe) / 사무실(office) / 창고(storage) / 야영(camp, 벽 없는 헛간) / 큰 창고(warehouse)

    static readonly HashSet<string> HandBuilt = new HashSet<string>
    {
        "building-a (4)", "Enterable_Church by Poly by Google - 2-J0Y-8AQOG", "Enterable_scene (3)", "Enterable_scene (4)",
        "Enterable_School", "Building by Kay Lousberg - bbH2Bg73qM", "Enterable_Structure by Quaternius - ilWoURnbZW",
    };

    [MenuItem("Tools/Interior/Build 9. All Other Buildings (나머지 건물 전부)")]
    public static void BuildAllOthers()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[Interior] Play 모드에서는 실행 불가"); return; }
        var auto = RootFull("Building_Interior_Auto");
        var log = new System.Text.StringBuilder();
        int total = 0, built = 0, issues = 0;
        BeginSession();
        try
        {
            var list = new List<BuildingInterior>(Object.FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None));
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var bi in list)
            {
                if (HandBuilt.Contains(bi.name)) continue;
                if (!FittedRoom(bi, out var c, out float hx, out float hz, out float fyaw)) { log.AppendLine($"  - {bi.name}: 방 크기를 못 잼 (건너뜀)"); continue; }
                string rootName = "Building_Interior_" + bi.name;
                var rt = auto.Find(rootName);
                if (rt == null) { rt = new GameObject(rootName).transform; rt.SetParent(auto, false); Undo.RegisterCreatedObjectUndo(rt.gameObject, "Build Interior"); }
                ClearGenerated(rt);
                Physics.SyncTransforms();
                var ctx = new Ctx { root = rt, building = bi, frameYaw = fyaw, frameCenter = c };
                ctx.floorGuess = FloorAt(c.x, c.z, 15.3f);
                string theme = ThemeFor(bi, hx, hz);
                SetMarkerTheme(bi, theme);
                var rng = new System.Random(StableHash(bi.name));
                FurnishRoom(ctx, theme, c, hx, hz, rng);
                Physics.SyncTransforms();
                string rep = Validate(rt, bi, true, ctx.doors);
                if (!rep.StartsWith("검사: 문제 0건")) issues++;
                total += ctx.count; built++;
                log.AppendLine($"  {bi.name}: {theme} {hx * 2:F0}x{hz * 2:F0}m → {ctx.count}개 ({rep.Split('\n')[0]})");
            }
        }
        finally { EndSession(); }
        InteriorShadowsOff(); // (2026-11-20) 성능
        Debug.Log($"[Interior] 나머지 건물 {built}채 자동 배치, 소품 {total}개 (검사에서 고친 건물 {issues}채)\n{log}");
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    /// <summary>(2026-11-20) 입구 표시(EntranceMarkers) 이름·색을 테마에 맞춤 — 회색 매트만 있던 일반 건물도 무슨 건물인지 보이게</summary>
    static void SetMarkerTheme(BuildingInterior bi, string theme)
    {
        (string label, Color col) = theme switch
        {
            "shop" => ("상점", new Color(1f, 0.52f, 0.12f)),
            "cafe" => ("식당", new Color(1f, 0.42f, 0.35f)),
            "office" => ("사무실", new Color(0.35f, 0.8f, 1f)),
            "storage" => ("창고", new Color(0.8f, 0.58f, 0.32f)),
            "warehouse" => ("창고", new Color(0.8f, 0.58f, 0.32f)),
            "camp" => ("야영지", new Color(0.45f, 0.9f, 0.4f)),
            _ => ("민가", new Color(1f, 0.8f, 0.4f)),   // 호박색 (연한 베이지는 흰 인도와 구분이 안 됨)
        };
        Undo.RecordObject(bi, "Marker Theme");
        bi.markerLabel = label;
        bi.markerColor = col;
        EditorUtility.SetDirty(bi);
    }

    static int StableHash(string s) { unchecked { int h = 17; foreach (char ch in s) h = h * 31 + ch; return h & 0x7fffffff; } }

    /// <summary>
    /// 런타임과 같은 방식(BuildingInterior.FitTriggerToWalls, 벽 콜라이더 기준)으로 트리거를 벽 안쪽에 맞춘 상자 = 방.
    /// 씬의 트리거 값은 재고 나서 원래대로 되돌림. 프레임 = 트리거 회전을 90° 단위로 뺀 나머지 (-45~45°)
    /// </summary>
    static bool FittedRoom(BuildingInterior bi, out Vector3 center, out float hx, out float hz, out float frameYaw)
    {
        center = Vector3.zero; hx = hz = frameYaw = 0f;
        var bc = bi.GetComponent<BoxCollider>();
        if (bc == null) return false;
        Vector3 size0 = bc.size, center0 = bc.center;
        var fit = typeof(BuildingInterior).GetMethod("FitTriggerToWalls", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        try { fit?.Invoke(bi, null); } catch { }
        var t = bc.transform;
        Vector3 c = t.TransformPoint(bc.center);
        Vector3 s = Vector3.Scale(bc.size, t.lossyScale);
        bc.size = size0; bc.center = center0;   // 되돌림 (씬 저장값 그대로)
        float rot = t.eulerAngles.y;
        int k = Mathf.RoundToInt(rot / 90f);
        frameYaw = rot - k * 90f;
        bool swap = (k & 1) == 1;
        hx = Mathf.Abs(swap ? s.z : s.x) * 0.5f;
        hz = Mathf.Abs(swap ? s.x : s.z) * 0.5f;
        center = new Vector3(c.x, 0f, c.z);
        return hx > 2f && hz > 2f;
    }

    static string ThemeFor(BuildingInterior bi, float hx, float hz)
    {
        string n = bi.name;
        int h = StableHash(n);
        if (n.Contains("Houses_FirstAge")) return "camp";
        if (n.Contains("Structure")) return "warehouse";
        if (n.Contains("skyscraper") || n.Contains("low-detail") || n.StartsWith("building-l") || n.StartsWith("building-m")) return "office";
        if (n.StartsWith("building-a") || n.StartsWith("building-b") || n.StartsWith("building-f") || n.StartsWith("building-h") || n.StartsWith("building-i") || n.StartsWith("building-j")) return "home";
        if (n.StartsWith("building-k")) return "shop";
        if (n.StartsWith("building-c") || n.StartsWith("building-e") || n.StartsWith("building-g")) return (h % 2 == 0) ? "shop" : "cafe";
        string[] mix = { "home", "office", "shop", "cafe", "storage" };
        return mix[h % mix.Length];
    }

    // 테마별 벽 가구 순서 (벽을 따라 차례로, 공간이 남으면 처음부터 다시)
    static readonly Dictionary<string, string[]> WallSets = new Dictionary<string, string[]>
    {
        { "home",      new[] { "bedDouble", "sideTable", "closet", "sofa", "tvStand", "bookShelfOld", "fridge", "kitchenCab", "kitchenSink", "kitchenStove", "kitchenDrawer", "washer" } },   // 한 번씩 (반복은 WallFillers)
        { "shop",      new[] { "martWallShelf", "martFridge", "martWallShelf", "martTall2", "martWallShelf", "martTall", "martFridge", "martWallShelf" } },
        { "cafe",      new[] { "kitchenCab", "kitchenSink", "kitchenStove", "kitchenDrawer", "fridge", "kitchenBar", "kitchenBar", "bookcase", "plant", "trashcan" } },
        { "office",    new[] { "officeDrawer", "bookcaseClosed", "waterCooler", "printer", "bookcaseWide", "plant", "officeDrawer", "bookcaseClosed" } },
        { "storage",   new[] { "rackSmall", "rackSmall", "workbench", "rackSmall", "drum", "rackSmall", "chest" } },
        { "warehouse", new[] { "rack", "rack", "container", "rack", "rackSmall", "rackSmall" } },
        { "camp",      new string[0] },
    };

    const int MaxPerBuilding = 46;

    // 테마별 벽 가구: 주 가구(한 번씩) → 남는 벽은 채움 가구로
    static readonly Dictionary<string, string[]> WallFillers = new Dictionary<string, string[]>
    {
        { "home",      new[] { "bookcase", "plant", "lowCabinet", "box", "bookcaseClosed", "coatRack", "kBoxClosed" } },
        { "shop",      new[] { "martWallShelf", "martFridge", "martWallShelf", "martTall" } },
        { "cafe",      new[] { "plant", "bookcase", "kitchenBar", "trashcan", "kitchenCab" } },
        { "office",    new[] { "officeDrawer", "bookcaseClosed", "plant", "printer" } },
        { "storage",   new[] { "rackSmall", "drum", "boxLarge", "rackSmall" } },
        { "warehouse", new[] { "rackSmall", "rack", "drum" } },
        { "camp",      new string[0] },
    };

    // const int WallBudget = 22, CenterBudget = 17; // (이전) 고정 → 큰 건물(400m² 이상)은 가장자리·옆방이 비었음
    // (2026-11-20) 넓이 비례 예산: 전체 = 넓이/8 (46~110), 벽 45% / 가운데 40% / 나머지 잡동사니
    static int Budget(float hx, float hz) => Mathf.Clamp(Mathf.RoundToInt(hx * hz * 4f / 8f), 46, 110);

    /// <summary>
    /// (프레임 좌표) 이 지점 위에 이 건물의 천장·지붕이 있는지 = 실내인지.
    /// ㄱ자·ㄷ자 건물은 방 상자(트리거)가 건물 밖 빈 땅까지 덮어서, 지붕 없는 곳엔 안 놓음
    /// </summary>
    static bool Indoor(Ctx c, float x, float z)
    {
        var w = c.W(x, z);
        float fy = FloorAt(w.x, w.z, c.floorGuess);
        bool prev = Physics.queriesHitBackfaces;
        try
        {
            Physics.queriesHitBackfaces = true;
            foreach (var h in Physics.RaycastAll(new Vector3(w.x, fy + 1.2f, w.z), Vector3.up, 80f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider.transform.IsChildOf(c.building.transform)) return true;
        }
        finally { Physics.queriesHitBackfaces = prev; }
        return false;
    }

    /// <summary>
    /// 실내 가구 겹침: 바닥에서 0.45m 위부터만 검사 (벽 콜라이더의 두께용 안쪽 면이 바닥 바로 위에 생겨서 바닥까지 재면 늘 겹침으로 나옴).
    /// 낮은 물건(0.45m 미만)은 이 툴이 놓은 다른 가구와만 비교
    /// </summary>
    static bool InteriorBlocked(GameObject go, float floorY)
    {
        Physics.SyncTransforms();
        if (!WorldBounds(go, out var b)) return false;
        float bottom = Mathf.Max(b.min.y, floorY + 0.45f), top = b.max.y - 0.05f;
        if (top <= bottom) return OverlapsFurniture(go);
        var bc = go.GetComponent<BoxCollider>();
        Vector3 half; Quaternion rot; Vector3 cxz;
        if (bc != null) { cxz = go.transform.TransformPoint(bc.center); var hs = Vector3.Scale(bc.size * 0.5f, go.transform.lossyScale); half = new Vector3(Mathf.Abs(hs.x) * 0.92f, 0f, Mathf.Abs(hs.z) * 0.92f); rot = go.transform.rotation; }
        else { cxz = b.center; half = new Vector3(b.extents.x * 0.92f, 0f, b.extents.z * 0.92f); rot = Quaternion.identity; }
        // 회전이 기울어진(넘어진) 물건은 AABB로
        if (Vector3.Dot(go.transform.up, Vector3.up) < 0.9f) { cxz = b.center; half = new Vector3(b.extents.x * 0.92f, 0f, b.extents.z * 0.92f); rot = Quaternion.identity; }
        half.y = (top - bottom) * 0.5f;
        var center = new Vector3(cxz.x, (top + bottom) * 0.5f, cxz.z);
        foreach (var col in Physics.OverlapBox(center, half, rot, ~0, QueryTriggerInteraction.Ignore))
        {
            if (col.transform.IsChildOf(go.transform) || col is TerrainCollider || sessionPlates.Contains(col as MeshCollider)) continue;
            return true;
        }
        return false;
    }

    static void FurnishRoom(Ctx c, string theme, Vector3 ctr, float hx, float hz, System.Random rng)
    {
        float x0 = ctr.x - hx, x1 = ctr.x + hx, z0 = ctr.z - hz, z1 = ctr.z + hz;
        float floorY = c.floorGuess;
        Vector2 doorF = new Vector2(float.NaN, float.NaN);
        if (c.building.TryGetDoor(out var door, out _))
        {
            var d = Quaternion.Euler(0, -c.frameYaw, 0) * new Vector3(door.x - ctr.x, 0, door.z - ctr.z);
            doorF = new Vector2(ctr.x + d.x, ctr.z + d.z);
            c.doors.Add(new Vector3(door.x, FloorAt(door.x, door.z, c.floorGuess), door.z));
        }
        bool NearDoor(float x, float z, float r) => !float.IsNaN(doorF.x) && (new Vector2(x, z) - doorF).sqrMagnitude < r * r;

        // ── 벽 가구: 벽 길이 비례로 예산을 나눠 네 벽에 고르게 ──
        var sides = new List<(string dir, float yaw)> { ("N", 180f), ("S", 0f), ("E", 270f), ("W", 90f) };
        if (!float.IsNaN(doorF.x))
            sides.Sort((a, b) => DistToSide(b.dir, doorF, x0, x1, z0, z1).CompareTo(DistToSide(a.dir, doorF, x0, x1, z0, z1)));
        var main = new List<string>(WallSets[theme]);
        if (theme == "home" && hx * hz * 4f > 320f) main.AddRange(new[] { "bedSingle", "closet", "sideTable", "sofa", "bookShelfOld" }); // 큰 집: 방이 더 있는 느낌
        var fill = WallFillers[theme];
        int mi = 0, fi = rng.Next(Mathf.Max(1, fill.Length));
        float perim = 2f * (hx + hz) * 2f;
        foreach (var (dir, yaw) in sides)
        {
            if (main.Count == 0 && fill.Length == 0) break;
            bool alongX = dir == "N" || dir == "S";
            float a0 = (alongX ? x0 : z0) + 0.9f, a1 = (alongX ? x1 : z1) - 0.9f;
            int quota = Mathf.Max(2, Mathf.RoundToInt(Budget(hx, hz) * 0.45f * (a1 - a0 + 1.8f) / perim));
            float wallLine = dir == "N" ? z1 - 0.6f : dir == "S" ? z0 + 0.6f : dir == "E" ? x1 - 0.6f : x0 + 0.6f;
            float cursor = a0;
            int placed = 0, guard = 0;
            while (cursor < a1 - 0.6f && placed < quota && guard++ < 60)
            {
                float px = alongX ? cursor + 0.5f : wallLine, pz = alongX ? wallLine : cursor + 0.5f;
                if (NearDoor(px, pz, 3.4f) || !Indoor(c, px, pz)) { cursor += 1.0f; continue; }
                string key = mi < main.Count ? main[mi] : (fill.Length > 0 ? fill[fi++ % fill.Length] : null);
                if (key == null) break;
                var go = c.P("Wall", key, px, pz, yaw, default, 1f, null, dir);
                if (go == null) { mi++; cursor += 1f; continue; }
                WorldBounds(go, out var wb);
                Vector3 wdir = c.Dir(alongX ? "E" : "N");
                float half = Mathf.Abs(wb.extents.x * wdir.x) + Mathf.Abs(wb.extents.z * wdir.z);
                go.transform.position += wdir * (half - 0.5f);
                float end = cursor + half * 2f;
                float midA = cursor + half;
                bool bad = end > a1 || NearDoor(alongX ? midA : wallLine, alongX ? wallLine : midA, 3.2f) || !Indoor(c, alongX ? midA : wallLine, alongX ? wallLine : midA);
                if (bad) { Object.DestroyImmediate(go); c.count--; cursor += 1f; continue; }
                if (mi < main.Count) mi++;
                placed++;
                cursor = end + 0.35f + (float)rng.NextDouble() * 0.5f;
                if (key == "tvStand") c.OnTopOf("Wall", "tvVintage", go, yaw);
                if (key == "sideTable") c.OnTopOf("Wall", "lampTable", go, yaw);
            }
        }

        // ── 가운데: 테마별 그리드 (실내 + 문 4m 밖) ──
        float cx0 = x0 + 2.8f, cx1 = x1 - 2.8f, cz0 = z0 + 2.8f, cz1 = z1 - 2.8f;
        int centerStart = c.count;
        if (cx1 > cx0 && cz1 > cz0)
        {
            float stepX = theme == "shop" ? 3.4f : theme == "office" ? 3.0f : theme == "warehouse" ? 5.0f : 4.2f;
            float stepZ = theme == "shop" ? 3.4f : theme == "office" ? 3.2f : theme == "warehouse" ? 5.0f : 4.2f;
            int nx = Mathf.Max(1, Mathf.FloorToInt((cx1 - cx0) / stepX) + 1), nz = Mathf.Max(1, Mathf.FloorToInt((cz1 - cz0) / stepZ) + 1);
            float ox = (cx0 + cx1) * 0.5f - (nx - 1) * stepX * 0.5f, oz = (cz0 + cz1) * 0.5f - (nz - 1) * stepZ * 0.5f;
            // 가운데부터 바깥으로 (예산이 모자라면 가운데가 먼저 참)
            var cells = new List<Vector2>();
            for (int ix = 0; ix < nx; ix++) for (int iz = 0; iz < nz; iz++) cells.Add(new Vector2(ox + ix * stepX, oz + iz * stepZ));
            var mid = new Vector2(ctr.x, ctr.z);
            cells.Sort((p, q) => (p - mid).sqrMagnitude.CompareTo((q - mid).sqrMagnitude));
            // (2026-11-20) 가운데 2칸(식탁·계산대 등)만 중앙 우선, 나머지는 섞어서 옆방·가장자리까지 고르게
            for (int k = cells.Count - 1; k > 2; k--) { int j = 2 + rng.Next(k - 1); var tmp = cells[k]; cells[k] = cells[j]; cells[j] = tmp; }
            int cell = 0;
            foreach (var p in cells)
            {
                if (c.count - centerStart >= Mathf.RoundToInt(Budget(hx, hz) * 0.4f)) break;
                if (NearDoor(p.x, p.y, 4.0f) || !Indoor(c, p.x, p.y)) continue;
                CenterCell(c, theme, p.x, p.y, cell++, rng, floorY);
            }
        }

        // ── 잡동사니 ──
        string[] clutter = theme switch
        {
            "home" => new[] { "box", "kBoxOpen", "chair:fallen", "blood", "books", "planks", "rugB", "trashcan" },
            "shop" => new[] { "foodCan", "foodBag", "foodCarton", "boxOpen", "blood", "foodBottle", "barrel", "foodCandy" },
            "cafe" => new[] { "chair:fallen", "chair:fallen", "trashcan", "blood", "kBoxOpen", "bottle" },
            "office" => new[] { "books", "retroChair:fallen", "kBoxClosed", "blood", "books", "trashcan" },
            "storage" => new[] { "box", "boxLarge", "barrel", "pallet", "blood", "planks" },
            "warehouse" => new[] { "drum", "drum", "pallet", "wBarrel", "blood", "boxOpen" },
            _ => new[] { "planks", "box", "barrel", "blood", "bucket" },
        };
        int want = Mathf.Clamp(Mathf.RoundToInt(hx * hz * 4f / 50f), 4, 16);   // (2026-11-20) 큰 건물은 잡동사니도 더 (최대 7 → 16)
        for (int i = 0, tries = 0; i < want && tries < want * 8 && c.count < Budget(hx, hz); tries++)
        {
            float px = Mathf.Lerp(x0 + 1.2f, x1 - 1.2f, (float)rng.NextDouble());
            float pz = Mathf.Lerp(z0 + 1.2f, z1 - 1.2f, (float)rng.NextDouble());
            if (NearDoor(px, pz, 3.0f) || !Indoor(c, px, pz)) continue;
            string entry = clutter[(i + rng.Next(clutter.Length)) % clutter.Length];
            bool fallen = entry.EndsWith(":fallen");
            string key = fallen ? entry.Substring(0, entry.Length - 7) : entry;
            var go = c.P("Clutter", key, px, pz, (float)rng.NextDouble() * 360f, fallen ? new Vector3(0, 0, 90) : default);
            if (go == null) continue;
            if (InteriorBlocked(go, floorY) || OverlapsFurniture(go)) { Object.DestroyImmediate(go); c.count--; continue; }
            i++;
        }

        // ── 조명 (넓으면 2개, 실내 지점에) ──
        if (hx * hz * 4f > 450f)
        {
            bool longX = hx >= hz;
            for (int s = -1; s <= 1; s += 2)
            {
                float lx = ctr.x + (longX ? hx * 0.45f * s : 0f), lz = ctr.z + (longX ? 0f : hz * 0.45f * s);
                if (!Indoor(c, lx, lz)) { lx = Mathf.Lerp(lx, ctr.x, 0.6f); lz = Mathf.Lerp(lz, ctr.z, 0.6f); }
                c.Light("Lights", lx, lz, 2.8f);
            }
        }
        else c.Light("Lights", ctr.x, ctr.z, 2.7f);
    }

    static float DistToSide(string dir, Vector2 p, float x0, float x1, float z0, float z1) =>
        dir == "N" ? Mathf.Abs(z1 - p.y) : dir == "S" ? Mathf.Abs(p.y - z0) : dir == "E" ? Mathf.Abs(x1 - p.x) : Mathf.Abs(p.x - x0);

    /// <summary>콜라이더 없는 소품(데칼·작은 물건)이 다른 가구 바운드 안에 박히는지</summary>
    static bool OverlapsFurniture(GameObject go)
    {
        if (!WorldBounds(go, out var b)) return false;
        b.Expand(new Vector3(-0.05f, 0f, -0.05f));
        foreach (var col in Physics.OverlapBox(b.center, new Vector3(b.extents.x, Mathf.Max(0.3f, b.extents.y), b.extents.z), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            if (IsInteriorObject(col.transform) && !col.transform.IsChildOf(go.transform)) return true;
        return false;
    }

    /// <summary>가운데 칸 하나 (테마별 세트)</summary>
    static void CenterCell(Ctx c, string theme, float x, float z, int cell, System.Random rng, float floorY)
    {
        GameObject Try(string key, float px, float pz, float yaw, Vector3 tilt = default)
        {
            var go = c.P("Center", key, px, pz, yaw, tilt);
            if (go != null && InteriorBlocked(go, floorY)) { Object.DestroyImmediate(go); c.count--; return null; }
            return go;
        }
        switch (theme)
        {
            case "home":
                if (cell == 0) { c.P("Center", rng.Next(2) == 0 ? "rugA" : "rugC", x, z, 0f); Try("table", x, z, 0f); Try("chair", x, z - 0.85f, 0f); Try("chair", x, z + 0.85f, 180f); }
                else if (cell == 1) { c.P("Center", "rugD", x, z, 90f); Try("tableCoffee", x, z, 0f); Try("armchair", x + 1.4f, z, 270f); }
                else if (cell % 3 == 2) { var p = c.P("Center", "rugB", x, z, rng.Next(2) * 90f); if (p != null && OverlapsFurniture(p)) { Object.DestroyImmediate(p); c.count--; } Try(rng.Next(2) == 0 ? "armchair" : "lampFloor", x + 0.8f, z, rng.Next(4) * 90f); }   // 소파 반복 대신 러그 + 안락의자/스탠드
                break;
            case "shop":
                if (cell == 0) Try("martCounter", x, z, 0f);
                else Try(cell % 4 == 3 ? "snackShelf" : "martGondola", x, z, 0f);
                break;
            case "cafe":
                Try("tableRound", x, z, 0f);
                Try("chair", x, z - 0.95f, 0f);
                Try("chair", x, z + 0.95f, 180f);
                if (cell % 2 == 0) Try("chair", x + 0.95f, z, 270f, rng.Next(3) == 0 ? new Vector3(0, 0, 90) : default);
                break;
            case "office":
                if (cell % 4 == 3) { Try("pcDesk", x, z, 180f); }
                else { Try("officeDesk", x, z, 0f); Try("retroChair", x, z - 1.05f, 0f); }
                break;
            case "storage":
                { var pal = Try("pallet", x, z, rng.Next(4) * 90f); if (pal != null) { var b1 = c.OnTopOf("Center", "wBox", pal, 0f, -0.25f, 0f); c.OnTopOf("Center", "wBox", pal, 20f, 0.25f, 0.1f); if (b1 != null && rng.Next(2) == 0) c.OnTopOf("Center", "wBox", b1, -10f); } }
                break;
            case "warehouse":
                if (cell == 1) Try("forklift", x, z, rng.Next(360));
                else { var pal = Try("pallet", x, z, 0f); if (pal != null) { c.OnTopOf("Center", "wBox", pal, 0f, -0.25f, 0f); c.OnTopOf("Center", "wBox", pal, 15f, 0.25f, 0f); } }
                break;
            case "camp":
                if (cell == 0) { c.P("Center", "campfire", x, z, 0f); c.P("Center", "bedroll", x + 1.6f, z + 0.4f, 80f); c.P("Center", "bedroll", x - 1.6f, z - 0.3f, 260f); }
                else if (cell == 1) { Try("chest", x, z, 0f); Try("barrel", x + 1.0f, z + 0.3f, 0f); Try("box", x - 0.9f, z, 20f); }
                else if (cell == 2) { Try("workbench", x, z, 0f); c.P("Center", "planks", x + 1.2f, z - 0.6f, 40f); }
                else Try(rng.Next(2) == 0 ? "boxLarge" : "barrelOpen", x, z, rng.Next(360));
                break;
        }
    }

    // ───────────────────────── 실내 그림자 끄기 (성능, 2026-11-20) ─────────────────────────
    /// <summary>
    /// 건물 안 가구는 지붕 아래라 햇빛 그림자가 의미 없는데 그림자 패스 비용만 큼 → 실내 소품 그림자 끔 (바깥 Exterior 그룹은 그대로).
    /// 건물 꾸미기(Run / Build 9) 끝에 자동 실행. 메뉴로 따로 실행 가능
    /// </summary>
    [MenuItem("Tools/Interior/Turn Off Interior Shadows (성능)")]
    public static void InteriorShadowsOff()
    {
        int n = 0;
        foreach (var g in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (!g.name.StartsWith("Building_Interior_")) continue;
            foreach (var r in g.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                bool exterior = false;
                for (var t = r.transform; t != null; t = t.parent) if (t.name == "Exterior") { exterior = true; break; }
                if (exterior || r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off) continue;
                Undo.RecordObject(r, "Interior Shadows Off");
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                n++;
            }
        }
        Debug.Log($"[Interior] 실내 소품 그림자 끔: {n}개");
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }
}
