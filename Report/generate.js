// 좀비 생존 게임 주간 보고서 - 사이버펑크 HUD 스타일
const pptxgen = require("pptxgenjs");

let pres = new pptxgen();
pres.layout = "LAYOUT_WIDE"; // 13.3 x 7.5
pres.author = "트리거 팀";
pres.title  = "주간 보고서";

// ── 색상 팔레트 ───────────────────────────────
const C = {
  bgDark:    "0A1628",
  bgPanel:   "0F1F35",
  cyan:      "00D9FF",
  cyanDim:   "0088B3",
  white:     "FFFFFF",
  textMain:  "E0F4FF",
  textDim:   "8FA9C7",
  accent:    "FF3D6E",
  yellow:    "FFD700",
};

const SLIDE_W = 13.3, SLIDE_H = 7.5;

function addBackground(slide) {
  slide.background = { color: C.bgDark };
  for (let x = 0; x < SLIDE_W; x += 1.5) {
    slide.addShape(pres.shapes.LINE, {
      x: x, y: 0, w: 0, h: SLIDE_H,
      line: { color: C.cyanDim, width: 0.25, transparency: 85 }
    });
  }
  for (let y = 0; y < SLIDE_H; y += 1.5) {
    slide.addShape(pres.shapes.LINE, {
      x: 0, y: y, w: SLIDE_W, h: 0,
      line: { color: C.cyanDim, width: 0.25, transparency: 85 }
    });
  }
}

function addFooter(slide) {
  slide.addShape(pres.shapes.LINE, {
    x: 0.5, y: 7.0, w: 12.3, h: 0,
    line: { color: C.cyan, width: 1.0 }
  });
  slide.addText([
    { text: "TEAM ", options: { color: C.textDim, fontSize: 10 } },
    { text: "[트리거]", options: { color: C.cyan, fontSize: 10, bold: true } },
    { text: "   |   ", options: { color: C.textDim, fontSize: 10 } },
    { text: "ENGINE ", options: { color: C.textDim, fontSize: 10 } },
    { text: "[Unity 6]", options: { color: C.cyan, fontSize: 10, bold: true } },
    { text: "   |   ", options: { color: C.textDim, fontSize: 10 } },
    { text: "DATE ", options: { color: C.textDim, fontSize: 10 } },
    { text: "[2026.05]", options: { color: C.cyan, fontSize: 10, bold: true } },
  ], { x: 0.5, y: 7.1, w: 12.3, h: 0.35, align: "center" });
}

function addPanel(slide, x, y, w, h) {
  slide.addShape(pres.shapes.RECTANGLE, {
    x: x, y: y, w: w, h: h,
    fill: { color: C.bgPanel },
    line: { color: C.cyan, width: 1.2 }
  });
  slide.addShape(pres.shapes.RECTANGLE, {
    x: x, y: y, w: 0.06, h: h,
    fill: { color: C.cyan }, line: { color: C.cyan, width: 0 }
  });
}

function addPanelHeader(slide, x, y, w, label, num) {
  slide.addShape(pres.shapes.RECTANGLE, {
    x: x, y: y - 0.05, w: w, h: 0.45,
    fill: { color: C.cyan }, line: { color: C.cyan, width: 0 }
  });
  slide.addText(`${num ? num + " " : ""}${label}`, {
    x: x + 0.2, y: y - 0.05, w: w - 0.4, h: 0.45,
    fontSize: 14, bold: true, color: C.bgDark,
    fontFace: "Arial", align: "left", valign: "middle", margin: 0
  });
}

function addSlideTitle(slide, title, subtitle) {
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 0.4, y: 0.4, w: 0.12, h: 0.9,
    fill: { color: C.cyan }, line: { color: C.cyan, width: 0 }
  });
  slide.addText(title, {
    x: 0.7, y: 0.35, w: 8.5, h: 0.6,
    fontSize: 32, bold: true, color: C.white, fontFace: "Arial Black", margin: 0
  });
  if (subtitle) {
    slide.addText(subtitle, {
      x: 0.7, y: 0.95, w: 8.5, h: 0.4,
      fontSize: 14, color: C.cyan, fontFace: "Arial", margin: 0
    });
  }
}

function addSlideNumber(slide, n, total) {
  slide.addText([
    { text: String(n).padStart(2, "0"), options: { color: C.cyan, fontSize: 28, bold: true } },
    { text: ` / ${String(total).padStart(2, "0")}`, options: { color: C.textDim, fontSize: 16 } },
  ], { x: 11.5, y: 0.4, w: 1.4, h: 0.6, align: "right", valign: "middle", margin: 0 });
}

const TOTAL_SLIDES = 9;

// ═══════════════════════════════════════════════
// 슬라이드 1: 표지
// ═══════════════════════════════════════════════
{
  let s = pres.addSlide();
  s.background = { color: C.bgDark };
  s.addShape(pres.shapes.RECTANGLE, {
    x: 0.3, y: 0.3, w: 12.7, h: 6.9,
    fill: { color: C.bgDark }, line: { color: C.cyan, width: 1.5 }
  });
  const corner = 0.6;
  [[0.3, 0.3], [12.4, 0.3], [0.3, 6.6], [12.4, 6.6]].forEach(([cx, cy]) => {
    s.addShape(pres.shapes.RECTANGLE, {
      x: cx, y: cy, w: corner, h: 0.06,
      fill: { color: C.cyan }, line: { color: C.cyan, width: 0 }
    });
    s.addShape(pres.shapes.RECTANGLE, {
      x: cx, y: cy, w: 0.06, h: corner,
      fill: { color: C.cyan }, line: { color: C.cyan, width: 0 }
    });
  });
  s.addShape(pres.shapes.RECTANGLE, {
    x: 1.0, y: 1.3, w: 3.2, h: 1.2,
    fill: { color: C.bgPanel }, line: { color: C.cyan, width: 1 }
  });
  s.addText([
    { text: "TEAM ",   options: { color: C.textDim, fontSize: 11 } },
    { text: "[트리거]",  options: { color: C.cyan, fontSize: 11, bold: true, breakLine: true } },
    { text: "ENGINE ", options: { color: C.textDim, fontSize: 11 } },
    { text: "[Unity 6]", options: { color: C.cyan, fontSize: 11, bold: true, breakLine: true } },
    { text: "DATE ",   options: { color: C.textDim, fontSize: 11 } },
    { text: "[2026.05]", options: { color: C.cyan, fontSize: 11, bold: true } },
  ], { x: 1.2, y: 1.4, w: 3.0, h: 1.0, valign: "middle" });
  s.addText("MLAgents 강화학습 기반", {
    x: 1.0, y: 3.0, w: 11.3, h: 0.6,
    fontSize: 28, color: C.cyan, fontFace: "Arial", align: "center",
  });
  s.addText("탑뷰 좀비 생존 게임 개발", {
    x: 1.0, y: 3.6, w: 11.3, h: 1.0,
    fontSize: 52, bold: true, color: C.white, fontFace: "Arial Black", align: "center",
  });
  s.addText("주간 보고서", {
    x: 1.0, y: 4.7, w: 11.3, h: 0.8,
    fontSize: 40, bold: true, color: C.cyan, fontFace: "Arial Black", align: "center",
  });
  s.addShape(pres.shapes.LINE, {
    x: 4.5, y: 5.7, w: 4.3, h: 0,
    line: { color: C.cyan, width: 2 }
  });
  s.addText("WEEKLY DEVELOPMENT REPORT", {
    x: 1.0, y: 5.85, w: 11.3, h: 0.4,
    fontSize: 14, color: C.textDim, fontFace: "Arial", align: "center", charSpacing: 8
  });
  s.addText("●", { x: 11.8, y: 0.6, w: 0.3, h: 0.3, fontSize: 14, color: C.accent });
  s.addText("REC", { x: 12.0, y: 0.6, w: 0.7, h: 0.3, fontSize: 11, color: C.accent, bold: true });
}

// ═══════════════════════════════════════════════
// 슬라이드 2: 플레이 영상 (전체 화면)
// ═══════════════════════════════════════════════
{
  let s = pres.addSlide();
  addBackground(s);
  addSlideTitle(s, "GAMEPLAY DEMO", "이번 주 플레이 영상");
  addSlideNumber(s, 2, TOTAL_SLIDES);

  // 전체 화면 비디오 패널
  addPanel(s, 0.4, 1.7, 12.5, 5.0);
  addPanelHeader(s, 0.4, 1.7, 12.5, "WEEKLY GAMEPLAY VIDEO", "▶");
  s.addText([
    { text: "[ 플레이 영상 삽입 ]\n", options: { fontSize: 22, color: C.cyan, bold: true, breakLine: true } },
    { text: "\n", options: { fontSize: 12 } },
    { text: "PowerPoint 메뉴 → 삽입 → 비디오 → 이 파일에서", options: { fontSize: 12, color: C.textDim, italic: true } },
  ], {
    x: 0.6, y: 1.7 + 0.55, w: 12.1, h: 4.4,
    fontFace: "Arial", align: "center", valign: "middle"
  });

  addFooter(s);
}

// ═══════════════════════════════════════════════
// 슬라이드 3: 프로젝트 개요
// ═══════════════════════════════════════════════
{
  let s = pres.addSlide();
  addBackground(s);
  addSlideTitle(s, "PROJECT OVERVIEW", "프로젝트 개요");
  addSlideNumber(s, 3, TOTAL_SLIDES);

  const py = 1.7, ph = 2.5, gap = 0.2;
  const pw = (12.5 - gap) / 2;

  addPanel(s, 0.4, py, pw, ph);
  addPanelHeader(s, 0.4, py, pw, "GENRE", "01");
  s.addText("탑뷰 좀비 생존 게임", {
    x: 0.6, y: py + 0.55, w: pw - 0.4, h: 0.5,
    fontSize: 20, bold: true, color: C.cyan, fontFace: "Arial"
  });
  s.addText([
    { text: "• 자원 수집 / 무기 제작 / 좀비 격퇴", options: { breakLine: true } },
    { text: "• 낮·밤 환경에 따른 생존 전략", options: { breakLine: true } },
    { text: "• ML-Agents 기반 적응형 AI 적용 예정" },
  ], { x: 0.6, y: py + 1.1, w: pw - 0.4, h: 1.3,
       fontSize: 14, color: C.textMain, fontFace: "Arial", paraSpaceAfter: 5 });

  addPanel(s, 0.4 + pw + gap, py, pw, ph);
  addPanelHeader(s, 0.4 + pw + gap, py, pw, "TEAM & TIMELINE", "02");
  s.addText("2인 캡스톤 · 2026.03 ~ 11", {
    x: 0.6 + pw + gap, y: py + 0.55, w: pw - 0.4, h: 0.5,
    fontSize: 20, bold: true, color: C.cyan, fontFace: "Arial"
  });
  s.addText([
    { text: "• 팀명: 트리거", options: { breakLine: true } },
    { text: "• Engine: Unity 6 (URP)", options: { breakLine: true } },
    { text: "• 핵심 기술: NavMesh, ML-Agents", options: { breakLine: true } },
    { text: "• 협업: GitHub" },
  ], { x: 0.6 + pw + gap, y: py + 1.1, w: pw - 0.4, h: 1.3,
       fontSize: 14, color: C.textMain, fontFace: "Arial", paraSpaceAfter: 5 });

  const py2 = py + ph + gap;
  addPanel(s, 0.4, py2, 12.5, 2.2);
  addPanelHeader(s, 0.4, py2, 12.5, "THIS WEEK", "03");
  s.addText([
    { text: "● ", options: { color: C.accent } },
    { text: "맵 전체 교체  ", options: { color: C.cyan, bold: true } },
    { text: "Polygon City Pack 기반 도시 환경 구축\n", options: { color: C.textMain } },
    { text: "● ", options: { color: C.accent } },
    { text: "좀비 시스템 전면 개편  ", options: { color: C.cyan, bold: true } },
    { text: "AI / 스포너 / 애니메이션 / 데미지 처리 재설계\n", options: { color: C.textMain } },
    { text: "● ", options: { color: C.accent } },
    { text: "DayNightCycle 연동  ", options: { color: C.cyan, bold: true } },
    { text: "낮/밤 별 스폰 강도 차등 적용\n", options: { color: C.textMain } },
    { text: "● ", options: { color: C.accent } },
    { text: "총기·아이템·UI 통합  ", options: { color: C.cyan, bold: true } },
    { text: "AR/권총/샷건 + 핫바·드롭·픽업 한글 UI\n", options: { color: C.textMain } },
    { text: "● ", options: { color: C.accent } },
    { text: "시각 효과 도입  ", options: { color: C.cyan, bold: true } },
    { text: "건물 진입 시 자동 천장 제거 + X-Ray 표시", options: { color: C.textMain } },
  ], { x: 0.6, y: py2 + 0.55, w: 12.1, h: 1.6,
       fontSize: 14, fontFace: "Arial", paraSpaceAfter: 6 });

  addFooter(s);
}

// ═══════════════════════════════════════════════
// 슬라이드 4: 맵 - Polygon City
// ═══════════════════════════════════════════════
{
  let s = pres.addSlide();
  addBackground(s);
  addSlideTitle(s, "MAP DESIGN", "맵 - Polygon City Pack");
  addSlideNumber(s, 4, TOTAL_SLIDES);

  // 좌측 - 구성 요소
  addPanel(s, 0.4, 1.7, 6.2, 5.0);
  addPanelHeader(s, 0.4, 1.7, 6.2, "COMPONENTS", "01");
  s.addText([
    { text: "● ", options: { color: C.cyan, fontSize: 16 } },
    { text: "Building / House\n", options: { color: C.cyan, bold: true, fontSize: 17 } },
    { text: "    건물·집 (인테리어 포함)\n\n", options: { color: C.textMain, fontSize: 13 } },
    { text: "● ", options: { color: C.cyan, fontSize: 16 } },
    { text: "Tree / Stone\n", options: { color: C.cyan, bold: true, fontSize: 17 } },
    { text: "    자연 장애물 / 엄폐물\n\n", options: { color: C.textMain, fontSize: 13 } },
    { text: "● ", options: { color: C.cyan, fontSize: 16 } },
    { text: "Grass\n", options: { color: C.cyan, bold: true, fontSize: 17 } },
    { text: "    잡초 (콜라이더 제거 → 통과 가능)\n\n", options: { color: C.textMain, fontSize: 13 } },
    { text: "● ", options: { color: C.cyan, fontSize: 16 } },
    { text: "Ground\n", options: { color: C.cyan, bold: true, fontSize: 17 } },
    { text: "    NavMesh 베이크 대상 지면", options: { color: C.textMain, fontSize: 13 } },
  ], { x: 0.6, y: 1.7 + 0.6, w: 5.8, h: 4.2, fontFace: "Arial" });

  // 우측 - 특징
  addPanel(s, 6.7, 1.7, 6.2, 5.0);
  addPanelHeader(s, 6.7, 1.7, 6.2, "FEATURES", "02");
  s.addText([
    { text: "Low-Poly Style\n", options: { color: C.cyan, bold: true, fontSize: 18, breakLine: true } },
    { text: "가벼운 폴리곤 구조로 다수의 좀비를\n", options: { color: C.textMain, fontSize: 13 } },
    { text: "동시 스폰해도 경량 렌더링 유지\n\n", options: { color: C.textMain, fontSize: 13 } },
    { text: "Open Field + Indoor\n", options: { color: C.cyan, bold: true, fontSize: 18, breakLine: true } },
    { text: "야외 광장 + 건물 내부 공간 결합\n", options: { color: C.textMain, fontSize: 13 } },
    { text: "→ 실내/실외 분리 스폰 가능\n\n", options: { color: C.textMain, fontSize: 13 } },
    { text: "Day-Night Atmosphere\n", options: { color: C.cyan, bold: true, fontSize: 18, breakLine: true } },
    { text: "환경 광원 변화에 따라 다른 분위기\n", options: { color: C.textMain, fontSize: 13 } },
    { text: "낮: 평화로움 · 밤: 공포감", options: { color: C.textMain, fontSize: 13 } },
  ], { x: 6.9, y: 1.7 + 0.6, w: 5.8, h: 4.2, fontFace: "Arial" });

  addFooter(s);
}

// ═══════════════════════════════════════════════
// 슬라이드 5: 낮/밤 시스템
// ═══════════════════════════════════════════════
{
  let s = pres.addSlide();
  addBackground(s);
  addSlideTitle(s, "DAY / NIGHT CYCLE", "낮·밤 환경 시스템");
  addSlideNumber(s, 5, TOTAL_SLIDES);

  // 상단 - 4 페이즈 (가로 4분할)
  addPanel(s, 0.4, 1.7, 12.5, 2.4);
  addPanelHeader(s, 0.4, 1.7, 12.5, "4 PHASES", "01");

  const phases = [
    { icon: "☀", name: "DAY",   desc: "안전한 탐색 시간",   color: C.yellow },
    { icon: "🌇", name: "DUSK",  desc: "위협 증가 시작",     color: "FF9966" },
    { icon: "🌙", name: "NIGHT", desc: "좀비 폭주 / 생존",   color: C.cyan   },
    { icon: "🌄", name: "DAWN",  desc: "위협 감소 / 회복",   color: "FFB37D" },
  ];
  const pw = (12.5 - 0.2 * 5) / 4;
  phases.forEach((p, i) => {
    const px = 0.5 + 0.2 + i * (pw + 0.2);
    const py = 2.4;
    s.addShape(pres.shapes.RECTANGLE, {
      x: px, y: py, w: pw, h: 1.6,
      fill: { color: C.bgDark }, line: { color: p.color, width: 1.5 }
    });
    s.addText(p.icon, {
      x: px, y: py + 0.1, w: pw, h: 0.5,
      fontSize: 22, color: p.color, align: "center", margin: 0
    });
    s.addText(p.name, {
      x: px, y: py + 0.65, w: pw, h: 0.45,
      fontSize: 18, bold: true, color: p.color, fontFace: "Arial Black", align: "center", margin: 0
    });
    s.addText(p.desc, {
      x: px, y: py + 1.15, w: pw, h: 0.4,
      fontSize: 11, color: C.textMain, fontFace: "Arial", align: "center", margin: 0
    });
  });

  // 하단 - 스폰 영향
  addPanel(s, 0.4, 4.3, 12.5, 2.4);
  addPanelHeader(s, 0.4, 4.3, 12.5, "SPAWN IMPACT", "02");
  s.addText([
    { text: "DayNightCycle.Instance.CurrentPhase ", options: { color: C.cyan, italic: true, fontSize: 13 } },
    { text: "→ ", options: { color: C.textDim } },
    { text: "ZombieSpawner ", options: { color: C.cyan, bold: true } },
    { text: "가 페이즈별 스폰 간격·버스트 자동 조정\n\n", options: { color: C.textMain } },
    { text: "☀ 낮:   ", options: { color: C.yellow, bold: true, fontSize: 15 } },
    { text: "야외 6초마다 3~4마리 / 실내 거의 없음 (탐색 위주)\n\n", options: { color: C.textMain, fontSize: 14 } },
    { text: "🌙 밤:   ", options: { color: C.cyan, bold: true, fontSize: 15 } },
    { text: "실내·외 동시 폭주 (2~3초 간격) / 최대 30+ 마리 (생존 모드)", options: { color: C.textMain, fontSize: 14 } },
  ], { x: 0.6, y: 4.3 + 0.55, w: 12.1, h: 1.8, fontFace: "Arial" });

  addFooter(s);
}

// ═══════════════════════════════════════════════
// 슬라이드 6: 좀비 시스템 새로 개편 (메인)
// ═══════════════════════════════════════════════
{
  let s = pres.addSlide();
  addBackground(s);
  addSlideTitle(s, "ZOMBIE SYSTEM REWORK", "좀비 시스템 전면 개편 ★");
  addSlideNumber(s, 6, TOTAL_SLIDES);

  // 강조 배지
  s.addShape(pres.shapes.RECTANGLE, {
    x: 10.9, y: 1.4, w: 1.6, h: 0.35,
    fill: { color: C.accent }, line: { color: C.accent, width: 0 }
  });
  s.addText("MAJOR UPDATE", {
    x: 10.9, y: 1.4, w: 1.6, h: 0.35,
    fontSize: 10, bold: true, color: C.white, fontFace: "Arial",
    align: "center", valign: "middle", margin: 0
  });

  // 2x2 카드
  const cards = [
    { t: "AI BEHAVIOR", n: "01", lines: [
      ["배회", " - 플레이어 못 볼 때 8m 반경 랜덤 이동"],
      ["추적", " - 30m 내 감지 시 풀스피드"],
      ["공격", " - 1.8m 내 자동 공격"],
      ["NavMesh 자가 복구", " - 벗어나도 자동 워프"],
    ]},
    { t: "SPAWNER", n: "02", lines: [
      ["실내 / 실외 분리", " - 건물 안과 야외 별도 관리"],
      ["낮·밤 차등 강도", " - 시간대별 다른 스폰 패턴"],
      ["버스트 스폰", " - 한 번에 N마리 동시 출현"],
      ["랜덤 분산", " - 스폰 지점 주변 위치 흩어짐"],
    ]},
    { t: "DAMAGE SYSTEM", n: "03", lines: [
      ["Health 컴포넌트", " - 자동 추가/연결"],
      ["총알·근접·폭발", " - 모든 무기 통합 처리"],
      ["데미지 텍스트", " - 화면에 숫자 표시"],
      ["Rigidbody 추가", " - 트리거 이벤트 100% 작동"],
    ]},
    { t: "DEATH ANIMATION", n: "04", lines: [
      ["쓰러짐 모션", " - 코루틴 기반 0.6s 회전"],
      ["땅 속 가라앉기", " - 1초 페이드 아웃"],
      ["콜라이더 해제", " - 시신 충돌 방지"],
      ["3초 후 자동 소멸", " - 메모리 정리"],
    ]},
  ];
  const cardW = (12.5 - 0.3) / 2;
  const cardH = (5.0 - 0.3) / 2;
  cards.forEach((c, i) => {
    const cx = 0.4 + (i % 2) * (cardW + 0.3);
    const cy = 1.85 + Math.floor(i / 2) * (cardH + 0.3);
    addPanel(s, cx, cy, cardW, cardH);
    addPanelHeader(s, cx, cy, cardW, c.t, c.n);
    const txt = [];
    c.lines.forEach((line, idx) => {
      txt.push({ text: "▸ ", options: { color: C.cyan, fontSize: 13 } });
      txt.push({ text: line[0], options: { color: C.cyan, bold: true, fontSize: 13 } });
      txt.push({ text: line[1], options: { color: C.textMain, fontSize: 12, breakLine: idx < c.lines.length - 1 } });
    });
    s.addText(txt, {
      x: cx + 0.2, y: cy + 0.55, w: cardW - 0.4, h: cardH - 0.65,
      fontFace: "Arial", paraSpaceAfter: 6, valign: "top"
    });
  });

  addFooter(s);
}

// ═══════════════════════════════════════════════
// 슬라이드 7: 총기류
// ═══════════════════════════════════════════════
{
  let s = pres.addSlide();
  addBackground(s);
  addSlideTitle(s, "WEAPON SYSTEM", "총기류 - AR · 권총 · 샷건");
  addSlideNumber(s, 7, TOTAL_SLIDES);

  // 가로 3분할
  const weapons = [
    { t: "AR",        sub: "Assault Rifle", n: "01",
      desc: "연사 / 중거리\n다수 좀비 제압용",
      stats: [["DMG", "25"], ["RPM", "600"], ["MAG", "30"]] },
    { t: "PISTOL",    sub: "권총", n: "02",
      desc: "단발 / 정확도\n초반 기본 무기",
      stats: [["DMG", "20"], ["RANGE", "정밀"], ["MAG", "12"]] },
    { t: "SHOTGUN",   sub: "샷건", n: "03",
      desc: "근거리 강력\n다중 발사체",
      stats: [["DMG", "60"], ["PELLETS", "8"], ["MAG", "6"]] },
  ];
  const ww = (12.5 - 0.4) / 3;
  const wh = 5.0;
  weapons.forEach((w, i) => {
    const wx = 0.4 + i * (ww + 0.2);
    const wy = 1.7;
    addPanel(s, wx, wy, ww, wh);
    addPanelHeader(s, wx, wy, ww, w.t, w.n);

    s.addText(w.sub, {
      x: wx + 0.2, y: wy + 0.55, w: ww - 0.4, h: 0.4,
      fontSize: 13, color: C.textDim, fontFace: "Arial", italic: true, margin: 0
    });
    s.addText(w.desc, {
      x: wx + 0.2, y: wy + 1.05, w: ww - 0.4, h: 1.2,
      fontSize: 14, color: C.textMain, fontFace: "Arial"
    });
    // 구분선
    s.addShape(pres.shapes.LINE, {
      x: wx + 0.3, y: wy + 2.5, w: ww - 0.6, h: 0,
      line: { color: C.cyanDim, width: 1 }
    });
    // 스탯
    w.stats.forEach((st, si) => {
      const sy = wy + 2.75 + si * 0.55;
      s.addText(st[0], {
        x: wx + 0.3, y: sy, w: (ww - 0.6) / 2, h: 0.45,
        fontSize: 12, color: C.textDim, fontFace: "Arial", margin: 0, valign: "middle"
      });
      s.addText(st[1], {
        x: wx + 0.3 + (ww - 0.6) / 2, y: sy, w: (ww - 0.6) / 2, h: 0.45,
        fontSize: 18, bold: true, color: C.cyan, fontFace: "Arial Black",
        align: "right", margin: 0, valign: "middle"
      });
    });
  });

  addFooter(s);
}

// ═══════════════════════════════════════════════
// 슬라이드 8: 아이템 & UI
// ═══════════════════════════════════════════════
{
  let s = pres.addSlide();
  addBackground(s);
  addSlideTitle(s, "ITEM & UI", "아이템 시스템 · 한글 UI");
  addSlideNumber(s, 8, TOTAL_SLIDES);

  // 좌측 - 픽업 시스템
  addPanel(s, 0.4, 1.7, 6.2, 5.0);
  addPanelHeader(s, 0.4, 1.7, 6.2, "PICKUP SYSTEM", "01");
  s.addText([
    { text: "▸ ", options: { color: C.cyan, fontSize: 16 } },
    { text: "근접 시 안내 표시\n", options: { color: C.cyan, bold: true, fontSize: 16 } },
    { text: '    "F키로 획득" 한글 UI 자동 출현\n\n', options: { color: C.textMain, fontSize: 13 } },
    { text: "▸ ", options: { color: C.cyan, fontSize: 16 } },
    { text: "FaceCamera 자동 회전\n", options: { color: C.cyan, bold: true, fontSize: 16 } },
    { text: "    플레이어가 어디서 봐도 정면 향함\n\n", options: { color: C.textMain, fontSize: 13 } },
    { text: "▸ ", options: { color: C.cyan, fontSize: 16 } },
    { text: "핫바 가득 시 빨강 경고\n", options: { color: C.cyan, bold: true, fontSize: 16 } },
    { text: '    "핫바 가득 참" 빨강 텍스트 표시\n\n', options: { color: C.accent, bold: true, fontSize: 13 } },
    { text: "▸ ", options: { color: C.cyan, fontSize: 16 } },
    { text: "아웃라인 글로우\n", options: { color: C.cyan, bold: true, fontSize: 16 } },
    { text: "    바닥 아이템 외곽 발광 효과", options: { color: C.textMain, fontSize: 13 } },
  ], { x: 0.6, y: 1.7 + 0.6, w: 5.8, h: 4.2, fontFace: "Arial" });

  // 우측 - 핫바 UI
  addPanel(s, 6.7, 1.7, 6.2, 5.0);
  addPanelHeader(s, 6.7, 1.7, 6.2, "HOTBAR UI", "02");
  s.addText([
    { text: "▸ ", options: { color: C.cyan, fontSize: 16 } },
    { text: "5칸 무기 슬롯\n", options: { color: C.cyan, bold: true, fontSize: 16 } },
    { text: "    무기 최대 5개 동시 소지\n\n", options: { color: C.textMain, fontSize: 13 } },
    { text: "▸ ", options: { color: C.cyan, fontSize: 16 } },
    { text: "1~5 키 빠른 전환\n", options: { color: C.cyan, bold: true, fontSize: 16 } },
    { text: "    숫자키로 즉시 무기 교체\n\n", options: { color: C.textMain, fontSize: 13 } },
    { text: "▸ ", options: { color: C.cyan, fontSize: 16 } },
    { text: "드롭 슬롯 / 우클릭 버리기\n", options: { color: C.cyan, bold: true, fontSize: 16 } },
    { text: "    원하는 무기 즉시 폐기\n\n", options: { color: C.textMain, fontSize: 13 } },
    { text: "▸ ", options: { color: C.cyan, fontSize: 16 } },
    { text: "마지막 무기 시 손에서 제거\n", options: { color: C.cyan, bold: true, fontSize: 16 } },
    { text: "    빈 손 상태 자연스럽게 처리", options: { color: C.textMain, fontSize: 13 } },
  ], { x: 6.9, y: 1.7 + 0.6, w: 5.8, h: 4.2, fontFace: "Arial" });

  addFooter(s);
}

// ═══════════════════════════════════════════════
// 슬라이드 9: 카메라 / 시각 효과 + Next Steps 통합
// ═══════════════════════════════════════════════
{
  let s = pres.addSlide();
  addBackground(s);
  addSlideTitle(s, "CAMERA & VISUAL FX", "카메라 / 시각 효과");
  addSlideNumber(s, 9, TOTAL_SLIDES);

  // 좌측 - X-Ray
  addPanel(s, 0.4, 1.7, 6.2, 5.0);
  addPanelHeader(s, 0.4, 1.7, 6.2, "PLAYER X-RAY", "01");
  s.addText("벽·나무 너머 캐릭터 표시", {
    x: 0.6, y: 1.7 + 0.6, w: 5.8, h: 0.5,
    fontSize: 17, bold: true, color: C.cyan, fontFace: "Arial"
  });
  s.addText([
    { text: "▸ ", options: { color: C.cyan, fontSize: 16 } },
    { text: "Stencil 기반\n", options: { color: C.cyan, bold: true, fontSize: 15 } },
    { text: "    자기 자신은 제외 → 무기 가려짐 X\n\n", options: { color: C.textMain, fontSize: 12 } },
    { text: "▸ ", options: { color: C.cyan, fontSize: 16 } },
    { text: "무기 자동 감지\n", options: { color: C.cyan, bold: true, fontSize: 15 } },
    { text: "    0.3초마다 새 렌더러 스캔\n\n", options: { color: C.textMain, fontSize: 12 } },
    { text: "▸ ", options: { color: C.cyan, fontSize: 16 } },
    { text: "원본 텍스처 합성\n", options: { color: C.cyan, bold: true, fontSize: 15 } },
    { text: "    실루엣 X · 실제 캐릭터 모습\n\n", options: { color: C.textMain, fontSize: 12 } },
    { text: "▸ ", options: { color: C.cyan, fontSize: 16 } },
    { text: "Custom Shader (URP 호환)\n", options: { color: C.cyan, bold: true, fontSize: 15 } },
    { text: "    Polygon City 셰이더와 같이 작동", options: { color: C.textMain, fontSize: 12 } },
  ], { x: 0.6, y: 1.7 + 1.2, w: 5.8, h: 3.6, fontFace: "Arial" });

  // 우측 - 건물 진입
  addPanel(s, 6.7, 1.7, 6.2, 5.0);
  addPanelHeader(s, 6.7, 1.7, 6.2, "BUILDING ENTRY", "02");
  s.addText("Project Zomboid 스타일", {
    x: 6.9, y: 1.7 + 0.6, w: 5.8, h: 0.5,
    fontSize: 17, bold: true, color: C.cyan, fontFace: "Arial"
  });
  s.addText([
    { text: "▸ ", options: { color: C.cyan, fontSize: 16 } },
    { text: "트리거 진입 감지\n", options: { color: C.cyan, bold: true, fontSize: 15 } },
    { text: "    건물 내부 BoxCollider 트리거\n\n", options: { color: C.textMain, fontSize: 12 } },
    { text: "▸ ", options: { color: C.cyan, fontSize: 16 } },
    { text: "천장·벽 자동 제거\n", options: { color: C.cyan, bold: true, fontSize: 15 } },
    { text: "    내부 활동 시 시야 확보\n\n", options: { color: C.textMain, fontSize: 12 } },
    { text: "▸ ", options: { color: C.cyan, fontSize: 16 } },
    { text: "이탈 시 자동 복구\n", options: { color: C.cyan, bold: true, fontSize: 15 } },
    { text: "    원래 외관 복원\n\n", options: { color: C.textMain, fontSize: 12 } },
    { text: "▸ ", options: { color: C.cyan, fontSize: 16 } },
    { text: "다중 건물 독립 작동\n", options: { color: C.cyan, bold: true, fontSize: 15 } },
    { text: "    건물마다 별개 처리", options: { color: C.textMain, fontSize: 12 } },
  ], { x: 6.9, y: 1.7 + 1.2, w: 5.8, h: 3.6, fontFace: "Arial" });

  addFooter(s);
}

pres.writeFile({ fileName: "C:/Users/HY/test/Report/주간보고서_2026_05.pptx" })
  .then(f => console.log("✅ 생성 완료: " + f))
  .catch(e => console.error("❌ 에러: " + e));
