using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 메뉴 UI 언어 (설정 → 접근성 → 언어). 0 한국어 / 1 English.
///  - 메뉴 프리팹 글자는 한국어로 만들어져 있고, Register(root)가 그 아래 한국어 글자마다 LocalizedText를 붙여
///    원문을 기억한 뒤 현재 언어로 바꿔 줌 (프리팹을 다시 빌드할 필요 없음)
///  - 적용을 누르면 SettingsManager가 Changed를 보내고, 열려 있는 메뉴 글자가 바로 바뀜
///  - 대상: 메인 메뉴·설정·조작 방법·일시정지·생존 종료. 게임 HUD(미션·인벤토리 등)는 아직 한국어만
/// </summary>
public static class Localization
{
    public const int Korean = 0, English = 1;

    /// <summary>언어가 바뀌었을 때 (적용 버튼)</summary>
    public static event System.Action Changed;

    public static bool IsEnglish => SettingsManager.Language == English;

    public static void NotifyChanged() => Changed?.Invoke();

    /// <summary>현재 언어로 바꾼 글자 (영어 사전에 없으면 원문 그대로)</summary>
    public static string T(string korean)
    {
        if (!IsEnglish || string.IsNullOrEmpty(korean)) return korean;
        return En.TryGetValue(korean, out var en) ? en : korean;
    }

    public static bool HasTranslation(string korean) => !string.IsNullOrEmpty(korean) && En.ContainsKey(korean);

    /// <summary>root 아래 번역할 한국어 글자에 LocalizedText를 붙이고 현재 언어로 맞춤.
    /// 드롭다운 안 글자(선택값·목록)는 SettingsPanel이 직접 바꾸므로 제외</summary>
    public static void Register(GameObject root)
    {
        if (root == null) return;
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (t.GetComponentInParent<TMP_Dropdown>(true) != null) continue;
            var lt = t.GetComponent<LocalizedText>();
            if (lt == null)
            {
                if (!HasTranslation(t.text)) continue;
                lt = t.gameObject.AddComponent<LocalizedText>();
            }
            lt.Refresh();
        }
    }

    // 한국어 원문 → 영어 (메뉴 빌더 MenuUIBuilder의 글자와 정확히 같아야 함)
    static readonly Dictionary<string, string> En = new Dictionary<string, string>
    {
        // 공통
        { "뒤로", "Back" },
        { "<cspace=0.12em>TRIGGER</cspace>  <color=#FFA500><i>/</i></color>  <size=74%>일곱 번째 밤</size>",
          "<cspace=0.12em>TRIGGER</cspace>  <color=#FFA500><i>/</i></color>  <size=74%>The Seventh Night</size>" },
        { "<cspace=0.12em>TRIGGER</cspace>  /  일곱 번째 밤", "<cspace=0.12em>TRIGGER</cspace>  /  The Seventh Night" },
        { "일곱 번째 밤", "The Seventh Night" },
        { "설정", "Settings" },
        { "조작 방법", "Controls" },
        { "게임 종료", "Quit Game" },
        { "다시 시작", "Restart" },
        { "메인 메뉴로", "Main Menu" },

        // 메인 메뉴
        { "게임 시작", "Start Game" },

        // 설정
        { "사운드", "Sound" },
        { "마스터 볼륨", "Master Volume" },
        { "BGM 볼륨", "BGM Volume" },
        { "효과음 볼륨", "SFX Volume" },
        { "좀비 사운드", "Zombie Sounds" },
        { "UI 사운드", "UI Sounds" },
        { "게임플레이", "Gameplay" },
        { "마우스 민감도", "Mouse Sensitivity" },
        { "카메라 회전", "Camera Rotation" },
        { "화면 흔들림", "Screen Shake" },
        { "그래픽", "Graphics" },
        { "해상도", "Resolution" },
        { "화면 모드", "Display Mode" },
        { "품질", "Quality" },
        { "그림자 품질", "Shadow Quality" },
        { "접근성", "Accessibility" },
        { "언어", "Language" },
        { "미니맵 크기", "Minimap Size" },
        { "HUD 투명도", "HUD Opacity" },
        { "색맹 모드", "Color Blind Mode" },
        { "기본값 복원", "Restore Defaults" },
        { "적용", "Apply" },
        { "적용됨", "Applied" },
        // 설정 드롭다운
        { "전체화면", "Fullscreen" },
        { "테두리 없는 창", "Borderless" },
        { "창 모드", "Windowed" },
        { "매우 낮음", "Very Low" },
        { "낮음", "Low" },
        { "중간", "Medium" },
        { "높음", "High" },
        { "매우 높음", "Very High" },
        { "최고", "Ultra" },
        { "끄기", "Off" },
        { "적색맹", "Protanopia" },
        { "녹색맹", "Deuteranopia" },
        { "청색맹", "Tritanopia" },

        // 조작 방법
        { "키보드", "Keyboard" },
        { "이동 / 시점", "Movement / View" },
        { "이동", "Move" },
        { "전력질주", "Sprint" },
        { "구르기", "Roll" },
        { "카메라 좌·우 회전", "Rotate Camera L / R" },
        { "전투", "Combat" },
        { "재장전", "Reload" },
        { "원거리 무기 장착", "Equip Ranged Weapon" },
        { "근접 무기 장착", "Equip Melee Weapon" },
        { "아이템 / 핫바", "Items / Hotbar" },
        { "핫바 슬롯 선택", "Select Hotbar Slot" },
        { "선택한 슬롯 아이템 버리기", "Drop Selected Item" },
        { "인벤토리 열기", "Open Inventory" },
        { "상호작용 / 시스템", "Interact / System" },
        { "상호작용 · 짧게 / 길게 누르기", "Interact · Tap / Hold" },
        { "플래시라이트 ON / OFF", "Flashlight ON / OFF" },
        { "재시작", "Restart" },
        { "마우스", "Mouse" },
        { "마우스 휠 스크롤", "Mouse Wheel" },
        { "카메라 줌 인·아웃", "Camera Zoom In / Out" },
        { "좌클릭", "Left Click" },
        { "공격 / 사격", "Attack / Fire" },
        { "우클릭", "Right Click" },
        { "조준", "Aim" },
        { "마우스 이동", "Mouse Move" },
        { "조준 방향 · 커서 따라감", "Aim follows the cursor" },

        // 일시정지
        { "일시 정지", "Paused" },
        { "계속하기", "Resume" },
        { "게임으로 돌아가기", "Back to game" },

        // 생존 종료
        { "생존 종료", "Game Over" },
        { "이번 생존 기록", "Survival Record" },
        { "도달 일차", "Day Reached" },
        { "생존 시간", "Time Survived" },
        { "완료한 미션", "Missions Done" },

        // 탈출 헬기 컷신 (HelicopterCutscene)
        { "구조 헬기 접근 중...", "Rescue helicopter approaching..." },
        { "생존자 구출 성공", "Survivor Rescued" },
    };
}
