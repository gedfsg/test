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

        // ── Kenney Survival (공용) ──
        { "box",           KSur("box", true) },
        { "boxLarge",      KSur("box-large", true, true) },
        { "boxOpen",       KSur("box-open", true) },
        { "barrel",        KSur("barrel", true) },
        { "barrelOpen",    KSur("barrel-open", true) },
        { "planks",        KSur("resource-planks", false) },
        { "barricade",     KSur("fence-fortified", true, true) },
        { "bedroll",       KSur("bedroll", false) },
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
        { "snackShelf",    S(IA + "Mart/snack_aisle_shelf_3d_model.glb", null, Fit.Height, 1.8f, 90f, true, true) },  // 진열면이 ±X → 90°

        // ── 경찰서 / 사무실 ──
        { "pcDesk",        S(IA + "Police/LowPolyComputerDesk.fbx", null, Fit.None, 1f, 0f, true, true) },
        { "officeDesk",    S(IA + "Police/LowPolyOffice.fbx", "desk", Fit.Scale, 10f, 0f, true, true) },
        { "officeChair",   S(IA + "Police/LowPolyOffice.fbx", "chair", Fit.Scale, 10f, 180f, true) },
        { "officeDrawer",  S(IA + "Police/LowPolyOffice.fbx", "drawer", Fit.Scale, 10f, 0f, true) },
        { "retroChair",    S(IA + "Police/90s_retro_office_pack.glb", "Chair 1.001_65", Fit.None, 1f, 90f, true) },
        { "cubicleWall",   S(IA + "Police/90s_retro_office_pack.glb", "Cubicle_Wall.004_97", Fit.None, 1f, 0f, true, true) },
        { "printer",       S(IA + "Police/90s_retro_office_pack.glb", "Printer Base.004_48", Fit.None, 1f, 0f, true) },
        { "cage",          S(IA + "Police/low-poly_prison_cage__free_game_asset.glb", null, Fit.Height, 2.6f, 0f, true, true) },
        { "waterCooler",   S(IA + "Police/water_cooler.glb", null, Fit.Height, 1.3f, 0f, true) },
        { "policeCar",     S(IA + "Police/police_car_-_low_poly.glb", null, Fit.Length, 4.9f, 0f, true, true) },

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
        { "biohazard",     S(IA + "street/street_signs__stoplight.glb", "Biohazard Sign_0", Fit.Height, 2.1f, 0f, false) },
    };

    // ───────────────────────── 배치 핵심 ─────────────────────────

    /// <summary>이 툴이 만들고 다시 실행 시 지우는 구역 그룹 이름</summary>
    static readonly string[] GroupNames = { "Living", "Bedroom", "Kitchen", "Bathroom", "Clutter", "Lights", "Nave", "Altar", "Ward", "Reception", "Corridor", "Office", "Cells", "Armory", "Aisles", "Checkout", "Storage", "Classroom", "Staffroom", "Hallway", "Exterior", "Partitions" };

    /// <summary>씬 루트의 Building_Interior_[name] (없으면 생성)</summary>
    public static Transform Root(string name)
    {
        string full = "Building_Interior_" + name;
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
        // 경로 끝부분 일치 ("a/b" → .../a/b), 이름 정확히 일치를 먼저
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
            if (!mf.name.StartsWith("Zone_") || mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
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
        var hits = Physics.RaycastAll(from, dir.normalized, maxDist, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        foreach (var h in hits)
        {
            if (IsInteriorObject(h.collider.transform)) continue;
            if (h.distance < best) { best = h.distance; point = h.point; normal = h.normal; }
        }
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
        public GameObject OnWall(string group, string key, float x, float z, string dir, float scaleMul = 1f, float heightAboveFloor = 0f)
        {
            var w = W(x, z);
            float fy = FloorAt(w.x, w.z, floorGuess);
            if (!WallHit(new Vector3(w.x, fy + 1.5f, w.z), Dir(dir), 8f, out var p, out var n)) { Debug.LogWarning($"[Interior] 벽을 못 찾음: {key} @ ({x},{z}) {dir}"); return null; }
            n.y = 0; n.Normalize();
            float yaw = Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg;
            var g = Group(root, group);
            var pos = p + n * 0.05f;
            pos.y = fy + heightAboveFloor;
            var go = Place(g, key, pos, yaw, default, scaleMul, key + "@wall");   // @wall → 검사에서 "공중에 뜸" 제외
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
            var report = Validate(root, bi, true);
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
    public static string Validate(Transform root, BuildingInterior bi, bool fix)
    {
        var sb = new System.Text.StringBuilder();
        bool hasDoor = bi.TryGetDoor(out var door, out var outDir);
        var trig = bi.GetComponent<Collider>();
        Vector3 center = trig != null ? trig.bounds.center : bi.transform.position;
        int issues = 0, fixedCount = 0;
        foreach (Transform group in root)
        {
            if (group.name == "Lights" || group.name == "Exterior") continue;
            foreach (Transform piece in group)
            {
                if (!WorldBounds(piece.gameObject, out var wb)) continue;
                string nm = group.name + "/" + piece.name;

                // 1) 입구
                if (hasDoor)
                {
                    for (int step = 0; step <= 6; step++)
                    {
                        WorldBounds(piece.gameObject, out wb);
                        var cp = wb.ClosestPoint(new Vector3(door.x, wb.center.y, door.z));
                        float d = new Vector2(cp.x - door.x, cp.z - door.z).magnitude;
                        if (d >= DoorClear) break;
                        if (step == 0) { issues++; sb.AppendLine($"  - 입구 {d:F2}m: {nm}"); }
                        if (!fix || step == 6) break;
                        piece.position += -outDir * 0.5f; // 실내 쪽으로 50cm
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
                        if (!fix || step == 6) break;
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

        // 조명: 접수대 / 병실 A 2 / 병실 B 2 / 가운데 홀 2
        c.Light("Reception", -300f, 24f);
        c.Light("Lights", -285.5f, 29f);
        c.Light("Lights", -272.5f, 29f);
        c.Light("Lights", -327.5f, -22f);
        c.Light("Lights", -314.5f, -22f);
        c.Light("Lights", -284f, 8f, 3.0f);
        c.Light("Lights", -300f, -10f, 3.0f);
    });
}
