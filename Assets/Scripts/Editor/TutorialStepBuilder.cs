using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools/Tutorial/Build Tutorial Steps (2026-11-23) - 튜토리얼 단계 에셋(TutorialStep)을 Assets/Resources/Tutorial/Steps에 만듦.
/// 다시 실행하면 같은 이름 에셋을 덮어쓰고, 목록에서 빠진 단계 에셋은 지움.
///
/// (2026-11-24) 문구 전면 수정 (사용자 요청): "~다/~자" 같은 딱딱한 말투 대신 "~할 수 있어요" 존댓말로, 자세하게.
///   동료 GuideHints 원문의 내용(무엇을 알려 주는지)은 그대로 두고 말투·설명만 풀어 씀. (이전 문구는 git c396720)
/// (2026-11-24) 건물 안내는 하나로 합침: 입구 접근·경찰서·병원·마트·창고·교회·학교·주유소 개별 안내 삭제
///   → 처음 화면에 보이는 건물 하나만 "○○가 보이죠? 들어가면 ~가 있어요"로 한 번 (TutorialManager가 {building}/{inside}를 채움)
/// </summary>
public static class TutorialStepBuilder
{
    const string Folder = "Assets/Resources/Tutorial/Steps";

    // 키 강조 (금색 굵게)
    static string K(string key) => $"<color=#FFD700><b>{key}</b></color>";

    // 스포트라이트 반경 (m): 작은 소품 2 / 중간 3.5 / 건물 입구 4.5
    const float Small = 2f, Large = 4.5f;

    static readonly HashSet<string> made = new HashSet<string>();

    [MenuItem("Tools/Tutorial/Build Tutorial Steps")]
    public static void Build()
    {
        Directory.CreateDirectory(Folder);
        made.Clear();
        int n = 0;

        // ── 조작 ──
        Make(ref n, "move", DiscoveryType.GameStart, Small,
            $"{K("WASD")}로 걸어 다닐 수 있어요.\n{K("Shift")}를 누르고 있으면 달리고, {K("Space")}를 누르면 구르면서 피할 수 있어요. 구르는 동안에는 공격을 받지 않아요.",
            $"Use {K("WASD")} to walk around.\nHold {K("Shift")} to sprint, and press {K("Space")} to roll away. You can't be hit while rolling.");
        Make(ref n, "attack", DiscoveryType.Attack, Small,
            $"{K("마우스")}로 조준하고 {K("좌클릭")}으로 공격할 수 있어요.\n캐릭터는 언제나 마우스 커서가 있는 쪽을 바라봐요.",
            $"Aim with the {K("mouse")} and attack with {K("Left click")}.\nYour character always faces the mouse cursor.");
        Make(ref n, "gun", DiscoveryType.GunHeld, 0f,
            $"지금 손에 권총을 들고 있어요. 핫바 {K("1")}·{K("2")}번 칸은 총, {K("4")}번 칸은 도끼예요.\n숫자키를 누르면 언제든 무기를 바꿔 들 수 있어요.",
            $"You're holding a pistol. Hotbar slots {K("1")}·{K("2")} are guns and slot {K("4")} is the axe.\nPress a number key to switch weapons any time.");
        Make(ref n, "reload", DiscoveryType.Reload, 0f,
            $"탄창이 거의 비었어요! {K("R")}을 누르면 재장전할 수 있어요.\n재장전하는 동안에는 쏠 수 없으니, 좀비와 거리를 벌린 뒤에 하는 게 좋아요.",
            $"Your magazine is almost empty! Press {K("R")} to reload.\nYou can't shoot while reloading, so back away from zombies first.");
        Make(ref n, "no_ammo", DiscoveryType.NoAmmo, 0f,
            $"이 총에 넣을 예비 탄약이 다 떨어졌어요.\n상자를 열거나, 좀비를 잡거나, 상인에게 사서 구할 수 있어요. 그동안은 {K("4")}번 도끼로 싸워 보세요.",
            $"You're out of spare ammo for this gun.\nGet more from crates, zombies or traders. Use the axe ({K("4")}) in the meantime.");

        // ── 월드 오브젝트 ──
        Make(ref n, "zombie", DiscoveryType.Zombie, Small,
            "좀비가 다가오고 있어요! 바짝 붙기 전에 거리를 두고 쏘는 게 안전해요.\n총소리를 들으면 주변 좀비들도 몰려오니 조심하세요.",
            "A zombie is coming! It's safer to keep your distance and shoot before it gets close.\nGunshots draw in nearby zombies too, so be careful.");
        Make(ref n, "aim", DiscoveryType.Aim, Small,
            $"{K("우클릭")}을 누르고 있으면 총을 겨누는 자세가 되고, 화면이 앞으로 가까이 당겨져요.\n멀리 있는 좀비를 노릴 때 써 보세요.",
            $"Hold {K("Right click")} to raise your gun and pull the view in closer.\nTry it when aiming at zombies far away.");
        Make(ref n, "crate", DiscoveryType.LootCrate, Small,
            $"상자를 발견했어요! 가까이 가서 {K("F")}를 누르면 열 수 있어요.\n좋은 상자일수록 좋은 물건이 들어 있어요. 열 때 나는 소리에 좀비가 올 수 있으니 주변을 살펴 주세요.",
            $"You found a crate! Get close and press {K("F")} to open it.\nBetter crates hold better loot. Opening one can attract zombies, so check your surroundings.");
        Make(ref n, "weapon_item", DiscoveryType.WeaponItem, Small,
            $"새 무기가 떨어져 있어요! {K("F")}로 주우면 핫바 {K("1")}·{K("2")}번 칸에 들어가요.\n숫자키나 {K("Ctrl")}+마우스 휠로 바꿔 들 수 있어요.",
            $"A new weapon! Pick it up with {K("F")} and it goes into hotbar slot {K("1")} or {K("2")}.\nSwitch with number keys or {K("Ctrl")}+mouse wheel.");
        Make(ref n, "heal_item", DiscoveryType.HealItem, Small,
            $"회복 아이템이에요. {K("F")}로 주워 두면 체력이 떨어졌을 때 쓸 수 있어요.\n핫바 칸에 넣어 두면 숫자키로 바로 쓸 수 있고, 체력이 가득 찼을 때는 쓸 수 없어요.",
            $"A healing item. Pick it up with {K("F")} and use it when your health drops.\nPut it on the hotbar to use it with a number key. It can't be used at full health.");
        Make(ref n, "ammo_item", DiscoveryType.AmmoItem, Small,
            $"탄약이에요! {K("F")}로 주우면 총 종류에 맞게 예비 탄약으로 자동으로 들어가요.\n권총탄은 권총에, 저격탄은 저격총에 쓰여요.",
            $"Ammo! Pick it up with {K("F")} and it's added to the matching gun's reserve automatically.\nPistol rounds go to pistols, sniper rounds to sniper rifles.");
        Make(ref n, "throwable_item", DiscoveryType.ThrowableItem, Small,
            $"수류탄이나 화염병 같은 투척 무기예요.\n손에 들고 {K("좌클릭")}을 꾹 누르면 날아갈 길이 보이고, 손을 떼면 던져요. 수류탄은 {K("R")}로 핀을 먼저 뽑아 둘 수 있어요.",
            $"A throwable, like a grenade or molotov.\nHold {K("Left click")} to see the throw arc and release to throw. Press {K("R")} to pull a grenade's pin early.");
        Make(ref n, "pickup", DiscoveryType.AnyItem, Small,
            $"바닥에 떨어진 물건은 가까이 가서 {K("F")}로 주울 수 있어요.\n{K("I")}나 {K("Tab")}으로 가방을 열고, 아이템을 우클릭하면 사용하거나 다른 물건을 만들 수 있어요.",
            $"Walk up to items on the ground and press {K("F")} to pick them up.\nOpen your bag with {K("I")} or {K("Tab")}, and right-click an item to use it or craft.");

        // ── 건물 (처음 보이는 건물 하나만, 한 번) ── {building} = 건물 이름+조사, {inside} = 안에 있는 것
        Make(ref n, "building", DiscoveryType.BuildingEntrance, Large,
            "저기 {building} 보이죠? 표시된 입구로 걸어 들어가면 안을 둘러볼 수 있어요.\n{inside} 건물마다 들어 있는 물건이 달라요.",
            "See that {building}? Walk in through the marked entrance to explore inside.\n{inside} Each building holds different things.");

        // ── 미니맵 ──
        Make(ref n, "mission", DiscoveryType.MissionPin, 0f,
            "미니맵에 보이는 핀이 오늘의 미션 장소예요.\n미션을 끝낼수록 진행률이 올라가고, 7일차에 탈출할 수 있게 돼요.",
            "The pin on the minimap marks today's mission.\nFinishing missions raises your progress, so you can escape on Day 7.");
        Make(ref n, "trader", DiscoveryType.Trader, 0f,
            $"미니맵의 금색 원 안 어딘가에 상인이 있어요.\n가까이 가서 {K("F")}를 누르면 탄약·회복템·강화를 살 수 있어요.",
            $"A trader is somewhere inside the gold circle on the minimap.\nGet close and press {K("F")} to buy ammo, healing and upgrades.");

        // ── 상황 ──
        Make(ref n, "night", DiscoveryType.Night, Large,
            $"곧 밤이 와요. 밤에는 좀비가 더 많아지고, 더 멀리서도 나를 알아채요.\n{K("T")}를 누르면 손전등을 켤 수 있어요.",
            $"Night is coming. There will be more zombies, and they'll notice you from farther away.\nPress {K("T")} to turn on your flashlight.");
        Make(ref n, "rain", DiscoveryType.Rain, Large,
            "비가 내리기 시작했어요. 빗소리에 소리가 묻혀서 좀비가 나를 잘 알아채지 못해요.\n조용히 움직이며 지나가기 좋은 때예요.",
            "It started raining. The rain muffles sounds, so zombies have a harder time noticing you.\nIt's a good time to move quietly.");
        Make(ref n, "fog", DiscoveryType.Fog, Large,
            "안개가 끼었어요. 좀비가 멀리 있는 나를 잘 보지 못해요.\n대신 나도 멀리 보기 어려우니 갑자기 나타나는 좀비를 조심하세요.",
            "Fog rolled in. Zombies can't see you from far away.\nBut you can't see far either, so watch out for zombies appearing suddenly.");
        Make(ref n, "low_health", DiscoveryType.LowHealth, Small,
            "체력이 많이 떨어졌어요! 붕대나 구급상자로 회복할 수 있어요.\n회복하는 도중에 좀비에게 맞으면 끊기니까, 안전한 곳으로 피한 다음에 쓰세요.",
            "Your health is low! You can heal with bandages or a medkit.\nA zombie hit interrupts healing, so get somewhere safe first.");
        Make(ref n, "hunger", DiscoveryType.Hunger, Small,
            "배가 고프거나 목이 마르면 체력이 조금씩 줄어들어요.\n가방에서 음식이나 물을 우클릭해서 먹고 마실 수 있어요.",
            "When you're hungry or thirsty, your health slowly drops.\nRight-click food or water in your bag to eat or drink.");
        Make(ref n, "money", DiscoveryType.Money, Small,
            "좀비를 잡으면 돈을 벌 수 있어요.\n모은 돈으로 상인에게서 탄약·회복템·강화를 살 수 있어요.",
            "You earn money by killing zombies.\nSpend it at traders on ammo, healing and upgrades.");
        Make(ref n, "final_day", DiscoveryType.FinalDay, Large,
            "드디어 마지막 날이에요! 진행률을 100%까지 채우면 헬리콥터로 탈출할 수 있어요.\n100%가 되면 미니맵에 헬리패드가 표시되니, 그곳으로 가세요.",
            "It's the last day! Fill your progress to 100% to escape by helicopter.\nAt 100%, the helipad shows up on the minimap. Head there.");

        // 목록에서 빠진 옛 단계 에셋 삭제 (입구·경찰서·병원 등 건물별 안내)
        foreach (var guid in AssetDatabase.FindAssets("t:TutorialStep", new[] { Folder }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var s = AssetDatabase.LoadAssetAtPath<TutorialStep>(p);
            if (s != null && !made.Contains(p)) AssetDatabase.DeleteAsset(p);
        }

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
        made.Add(path);
        n++;
    }
}
