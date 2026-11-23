using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools/Tutorial/Build Tutorial Steps (2026-11-23) - 튜토리얼 단계 에셋(TutorialStep)을 Assets/Resources/Tutorial/Steps에 만듦.
/// 다시 실행하면 같은 이름 에셋을 덮어씀 (stepId·문구·반경·순서).
///
/// 문구 출처:
///  [동료] = GuideHints(화면 위쪽 안내 블록)의 원문 그대로 - 전달 방식만 스포트라이트 말풍선으로 바뀜
///           시작 안내 2개(이동·무기)는 요청대로 이동 / 공격 / 총 / 재장전 / 조준 으로 나눔 (단어는 원문 그대로)
///  [요청] = 튜토리얼 리워크 요청서의 문구
/// 같은 대상에 둘 다 있으면 동료 원문을 씀 (좀비·상자·밤·체력·배고픔).
/// </summary>
public static class TutorialStepBuilder
{
    const string Folder = "Assets/Resources/Tutorial/Steps";

    // 동료 GuideHints와 같은 키 강조 (금색 굵게)
    static string K(string key) => $"<color=#FFD700><b>{key}</b></color>";

    // 스포트라이트 반경 (m): 작은 소품 2 / 중간(자동차·주유기) 3.5 / 건물 입구 4.5
    const float Small = 2f, Medium = 3.5f, Large = 4.5f;

    [MenuItem("Tools/Tutorial/Build Tutorial Steps")]
    public static void Build()
    {
        Directory.CreateDirectory(Folder);
        int n = 0;

        // ── 조작 (동료 상단 블록을 나눔) ──
        Make(ref n, "move", DiscoveryType.GameStart, Small,
            $"{K("WASD")} 이동\n{K("Shift")} 달리기 · {K("Space")} 구르기 (구르는 동안 무적)",
            $"{K("WASD")} move\n{K("Shift")} sprint · {K("Space")} roll (invincible while rolling)");
        Make(ref n, "attack", DiscoveryType.Attack, Small,
            $"{K("마우스")} 조준 · {K("좌클릭")} 공격",
            $"{K("Mouse")} aim · {K("Left click")} attack");
        Make(ref n, "gun", DiscoveryType.GunHeld, 0f,
            $"{K("1·2")} 총 · {K("4")} 도끼",
            $"{K("1·2")} guns · {K("4")} axe");
        Make(ref n, "reload", DiscoveryType.Reload, 0f,
            $"{K("R")} 재장전",
            $"{K("R")} reload");
        Make(ref n, "no_ammo", DiscoveryType.NoAmmo, 0f,                                     // [동료]
            "이 총의 예비 탄약이 떨어졌어요\n상자·좀비·상인에게서 구하고, 그동안은 도끼(4)로",
            "Out of spare ammo for this gun\nFind more in crates, from zombies or traders — use the axe (4) meanwhile");

        // ── 월드 오브젝트 ──
        Make(ref n, "zombie", DiscoveryType.Zombie, Small,                                   // [동료] (요청: 좀비다! 총이나 도끼로 공격하자)
            "좀비가 다가와요! 거리를 두고 쏘세요\n총소리는 다른 좀비도 불러요",
            "A zombie is coming! Keep your distance and shoot\nGunshots attract other zombies too");
        Make(ref n, "aim", DiscoveryType.Aim, Small,
            $"{K("우클릭")}을 누르고 있으면 조준 자세",
            $"Hold {K("Right click")} to aim");
        Make(ref n, "crate", DiscoveryType.LootCrate, Small,                                 // [동료] (요청: 상자다! F키로 열자)
            $"{K("F")}로 상자를 열어 보세요\n좋은 상자일수록 좋은 물건이 나와요",
            $"Press {K("F")} to open the crate\nBetter crates hold better loot");
        Make(ref n, "weapon_item", DiscoveryType.WeaponItem, Small,                          // [요청]
            $"무기를 주웠다. 핫바 {K("1·2")}번, 숫자키/휠로 선택",
            $"A weapon! Hotbar slots {K("1·2")} — select with number keys / wheel");
        Make(ref n, "heal_item", DiscoveryType.HealItem, Small,                              // [요청]
            "회복템이다. 핫바에 등록해서 쓰자",
            "A healing item. Put it on the hotbar to use it");
        Make(ref n, "ammo_item", DiscoveryType.AmmoItem, Small,                              // [요청]
            "탄약이다. 탄약 종류에 맞게 자동 장전",
            "Ammo. It's loaded automatically by ammo type");
        Make(ref n, "throwable_item", DiscoveryType.ThrowableItem, Small,                    // [요청]
            $"투척물. {K("좌클릭")} 꾹 눌러 조준, {K("R")}로 수류탄 핀 뽑기",
            $"Throwable. Hold {K("Left click")} to aim, {K("R")} to pull a grenade pin");
        Make(ref n, "pickup", DiscoveryType.AnyItem, Small,                                  // [동료]
            $"{K("F")} 줍기 · {K("I")} 또는 {K("Tab")} 가방 열기\n가방에서 아이템을 우클릭하면 사용·제작",
            $"{K("F")} pick up · {K("I")} / {K("Tab")} open bag\nRight-click an item in the bag to use or craft");

        // ── 건물 ──
        // [요청] "건물 입구다. F키로 들어가자" → 실제 게임은 F가 아니라 문으로 걸어 들어가면 들어가짐 (BuildingInterior 트리거)
        Make(ref n, "entrance", DiscoveryType.BuildingEntrance, Large,
            "건물 입구다. 문으로 걸어 들어가자",
            "A building entrance. Walk through the door to go in");
        Make(ref n, "police", DiscoveryType.Police, Large, "경찰서다. 열쇠 카드와 무기가 있을 거야", "A police station. There should be key cards and weapons");
        Make(ref n, "hospital", DiscoveryType.Hospital, Large, "병원이다. 회복 물품을 찾을 수 있다", "A hospital. You can find healing supplies");
        Make(ref n, "mart", DiscoveryType.Mart, Large, "마트다. 식량과 물을 챙기자", "A mart. Grab food and water");
        Make(ref n, "warehouse", DiscoveryType.Warehouse, Large, "창고다. 보급품을 찾아보자", "A warehouse. Look for supplies");
        Make(ref n, "church", DiscoveryType.Church, Large, "교회다. 안식처가 되어줄 것이다", "A church. It can be a refuge");
        Make(ref n, "school", DiscoveryType.School, Large, "학교다. 피난처로 쓸 수 있다", "A school. It can serve as a shelter");
        Make(ref n, "gas_station", DiscoveryType.GasStation, Medium, "주유소다. 연료를 수집할 수 있다", "A gas station. You can collect fuel");

        // ── 미니맵 ──
        Make(ref n, "mission", DiscoveryType.MissionPin, 0f,                                 // [동료] (요청: 미션 목표다. 미니맵을 참고하자)
            "미니맵의 핀이 오늘의 미션이에요\n미션을 끝낼수록 7일차 탈출이 가까워져요",
            "The pin on the minimap is today's mission\nFinish missions to get closer to escaping on Day 7");
        Make(ref n, "trader", DiscoveryType.Trader, 0f,                                      // [동료]
            "미니맵의 금색 원 안에 상인이 있어요\n가까이 가서 F로 거래하세요",
            "A trader is somewhere in the gold circle on the minimap\nGet close and press F to trade");

        // ── 상황 ──
        Make(ref n, "night", DiscoveryType.Night, Large,                                     // [동료] 노을 때 (요청: 밤이다. T키로 손전등을 켜자)
            $"곧 밤이에요 — 좀비가 많아지고 더 멀리서 알아채요\n{K("T")}로 손전등을 켜세요",
            $"Night is coming — more zombies, and they notice you from farther away\nPress {K("T")} for the flashlight");
        Make(ref n, "rain", DiscoveryType.Rain, Large, "비가 온다. 소리가 작아져 좀비가 눈치채기 어렵다", "It's raining. Sounds are muffled, so zombies notice you less");
        Make(ref n, "fog", DiscoveryType.Fog, Large, "안개가 꼈다. 좀비 시야가 짧아진다", "Fog rolled in. Zombies can't see as far");
        Make(ref n, "low_health", DiscoveryType.LowHealth, Small,                            // [동료] (요청: 체력이 낮다! 회복템을 써야 한다)
            "체력이 낮아요! 붕대·구급상자로 회복하세요\n좀비에게 맞으면 끊기니 안전한 곳에서",
            "Low health! Heal with bandages or a medkit\nA zombie hit interrupts healing — find a safe spot first");
        Make(ref n, "hunger", DiscoveryType.Hunger, Small,                                   // [동료] (요청: 배가 고프다. 식량이 필요하다)
            "목마르거나 배고프면 체력이 줄어요\n가방에서 물·음식을 우클릭 → 사용",
            "Thirst and hunger drain your health\nRight-click water or food in the bag → Use");
        Make(ref n, "money", DiscoveryType.Money, Small,                                     // [동료]
            "좀비를 잡으면 돈이 생겨요\n상인에게서 탄약·회복템·강화를 살 수 있어요",
            "Killing zombies earns money\nBuy ammo, healing and upgrades from traders");
        Make(ref n, "final_day", DiscoveryType.FinalDay, Large,                              // [요청]
            "마지막 날이다. 100% 달성 후 헬리패드로 가자",
            "The last day. Reach 100%, then head to the helipad");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Tutorial] 단계 에셋 {n}개 생성/갱신: {Folder}");
    }

    static void Make(ref int n, string id, DiscoveryType type, float radius, string ko, string en)
    {
        string path = $"{Folder}/{n:00}_{id}.asset";
        // 순서가 바뀌어 이름이 달라졌으면 옛 에셋 지움 (같은 stepId 중복 방지)
        foreach (var guid in AssetDatabase.FindAssets("t:TutorialStep", new[] { Folder }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var old = AssetDatabase.LoadAssetAtPath<TutorialStep>(p);
            if (old != null && old.stepId == id && p != path) AssetDatabase.DeleteAsset(p);
        }
        var s = AssetDatabase.LoadAssetAtPath<TutorialStep>(path);
        if (s == null) { s = ScriptableObject.CreateInstance<TutorialStep>(); AssetDatabase.CreateAsset(s, path); }
        s.stepId = id;
        s.discoveryType = type;
        s.spotlightRadius = radius;
        s.order = n;
        s.dialogueText = ko;
        s.dialogueTextEn = en;
        s.targetTag = "";
        EditorUtility.SetDirty(s);
        n++;
    }
}
