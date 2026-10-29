# -*- coding: utf-8 -*-
"""
사운드 전처리: Assets/Audio/<카테고리>/*.mp3 원본 → Assets/Audio/_Processed/<카테고리>/<용도>_NN.wav

- 원본은 건드리지 않음 (출력만 _Processed에 새로 만듦, 다시 돌리면 _Processed를 지우고 재생성)
- 긴 원샷 파일(그르렁/비명 등)은 무음 구간 기준으로 자르거나 2~5초 단위로 분할
- 발소리 녹음은 한 발짝씩 잘라서 랜덤 재생용 클립 여러 개로
- 모든 클립 -20 LUFS로 볼륨 통일 (+ 피크 -1dBFS 리미터)
- 반복 재생용(비/환경음)은 자르지 않고 볼륨만 맞춤

실행: python Tools/Audio/preprocess_audio.py   (ffmpeg 필요: PATH 또는 pip install imageio-ffmpeg)
분류 규칙은 아래 PLAN 표에서 수정.
"""
import json, os, re, shutil, subprocess, sys, tempfile

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Audio")
OUT = os.path.join(ROOT, "_Processed")
TARGET_LUFS = -20.0


def find_ffmpeg():
    if shutil.which("ffmpeg"):
        return "ffmpeg"
    try:
        import imageio_ffmpeg
        return imageio_ffmpeg.get_ffmpeg_exe()
    except ImportError:
        sys.exit("ffmpeg 없음: ffmpeg를 설치하거나 pip install imageio-ffmpeg")


FF = find_ffmpeg()


def run(args):
    return subprocess.run([FF, "-hide_banner", "-nostats"] + args, capture_output=True,
                          text=True, encoding="utf-8", errors="ignore").stderr


def duration(path):
    m = re.search(r"Duration: (\d+):(\d+):([\d.]+)", run(["-i", path]))
    return int(m[1]) * 3600 + int(m[2]) * 60 + float(m[3])


def sound_segments(path, noise_db=-40, min_silence=0.25):
    """무음(noise_db 이하가 min_silence초 이상) 사이의 소리 구간 [(시작, 끝)]"""
    out = run(["-i", path, "-af", f"silencedetect=n={noise_db}dB:d={min_silence}", "-f", "null", "-"])
    starts = [float(x) for x in re.findall(r"silence_start: ([-\d.]+)", out)]
    ends = [float(x) for x in re.findall(r"silence_end: ([\d.]+)", out)]
    total = duration(path)
    segs, cur = [], 0.0
    for i, s in enumerate(starts):
        if s - cur > 0.05:
            segs.append((max(cur, 0.0), s))
        cur = ends[i] if i < len(ends) else total
    if total - cur > 0.05:
        segs.append((cur, total))
    return segs


def loudness(path):
    """적분 라우드니스(LUFS). 1초보다 짧은 클립(발소리/클릭)은 LUFS 측정창(0.4초)보다 짧아 값이 안 나오거나
    무음 꼬리 때문에 작게 나와 과하게 증폭됨 → 여러 번 이어 붙여(반복 재생한 소리로) 측정"""
    loop = ["-stream_loop", "8"] if duration(path) < 1.0 else []
    out = run(loop + ["-i", path, "-af", "loudnorm=print_format=json", "-f", "null", "-"])
    i = float(json.loads(out[out.rindex("{"):out.rindex("}") + 1])["input_i"])
    if i > -70:
        return i
    m = re.search(r"mean_volume: ([-\d.]+) dB", run(["-i", path, "-af", "volumedetect", "-f", "null", "-"]))
    return float(m[1]) if m else TARGET_LUFS


def cut(src, dst, start=None, end=None, pitch=1.0, fade_in=0.01, fade_out=0.05):
    """구간 잘라내기(+페이드, 피치) → 임시 wav, 그 다음 -20 LUFS로 맞춰 dst에 저장"""
    tmp = os.path.join(tempfile.gettempdir(), "sfx_tmp.wav")  # Assets 밖 (Unity가 임시 파일을 임포트하지 않게)
    args = []
    if start is not None:
        args += ["-ss", f"{start:.3f}"]
    if end is not None:
        args += ["-to", f"{end:.3f}"]
    args += ["-i", src]
    length = (end if end is not None else duration(src)) - (start or 0.0)
    filters = []
    if pitch != 1.0:  # 피치(+속도) 변경: 같은 원본으로 다른 총소리 만들기
        filters += [f"asetrate=44100*{pitch}", "aresample=44100"]
        length /= pitch
    filters += [f"afade=t=in:d={fade_in}",
                f"afade=t=out:st={max(0.0, length - fade_out):.3f}:d={fade_out}"]
    run(["-y"] + args + ["-af", ",".join(filters), "-ar", "44100", tmp])

    gain = TARGET_LUFS - loudness(tmp)
    run(["-y", "-i", tmp, "-af", f"volume={gain:.2f}dB,alimiter=limit=0.89:level=false",
         "-ar", "44100", "-c:a", "pcm_s16le", dst])
    os.remove(tmp)
    print(f"  {os.path.relpath(dst, OUT):40s} {length:5.2f}s  gain {gain:+6.1f}dB")


def chunks(start, end, piece):
    """[start, end]를 piece초 안팎의 비슷한 길이 조각으로"""
    n = max(1, round((end - start) / piece))
    step = (end - start) / n
    return [(start + k * step, start + (k + 1) * step) for k in range(n)]


# ─────────────────────────────────────────────────────────────
# 분류 표: (출력 카테고리/이름, 원본, 방식, 옵션)
#   whole   : 통째로 (trim=(시작,끝) 지정 가능)
#   split   : 무음 기준으로 자른 구간 각각 (min/max 길이, 최대 개수)
#   chunk   : 소리 있는 구간을 piece초 조각으로 분할 (긴 신음/비명)
#   loop    : 반복용 - 자르지 않고 볼륨만 (페이드 없음)
# ─────────────────────────────────────────────────────────────
PLAN = [
    # 좀비
    ("zombie/zombie_groan", "zombie/dragon-studio-zombie-sound-2-357976.mp3", "chunk", dict(piece=2.7)),
    ("zombie/zombie_groan", "zombie/dragon-studio-zombie-sound-357975.mp3", "split", dict(min=0.7, max=3.0)),
    ("zombie/zombie_groan", "zombie/freesound_community-zombie-breathing-70682.mp3", "chunk", dict(piece=3.0, count=6)),
    ("zombie/zombie_scream", "zombie/dragon-studio-zombie-scream-324752.mp3", "chunk", dict(piece=3.2)),
    ("zombie/zombie_scream", "zombie/dragon-studio-female-zombie-screams-324744.mp3", "chunk", dict(piece=2.8)),
    ("zombie/zombie_scream", "zombie/dragon-studio-zombie-sfx-450450.mp3", "chunk", dict(piece=3.5)),
    ("zombie/zombie_death", "zombie/dragon-studio-zombie-dying-sound-357974.mp3", "chunk", dict(piece=3.7)),
    # 무기 (총 종류별로 다른 소리: 원본이 권총류 위주라 일부는 피치를 낮춰 더 묵직하게)
    ("weapon/fire_pistol", "weapon/universfield-pistol-shot-278821.mp3", "whole", dict(trim=(0, 1.6))),
    ("weapon/fire_pistol", "weapon/mrfriends-pistol-shot-233473.mp3", "whole", dict(trim=(0, 1.5))),
    ("weapon/fire_smg", "weapon/freesound_community-9mm-pistol-shoot-short-reverb-7152.mp3", "whole", dict(trim=(0, 1.0), pitch=1.08)),
    ("weapon/fire_rifle", "weapon/freesound_community-single-pistol-gunshot-33-37187.mp3", "whole", dict(trim=(0, 1.8), pitch=0.85)),
    ("weapon/fire_shotgun", "weapon/flutie8211-gun-shot-and-echo-549910.mp3", "whole", dict(trim=(0, 2.2), pitch=0.75)),
    ("weapon/fire_sniper", "weapon/flutie8211-gun-shot-and-echo-549910.mp3", "whole", dict(trim=(0, 4.0))),
    ("weapon/reload_rifle", "weapon/dragon-studio-gun-reload-511309.mp3", "whole", {}),
    ("weapon/reload_rifle", "weapon/dragon-studio-gun-reload-2-511308.mp3", "whole", {}),
    ("weapon/reload_pistol", "weapon/freesound_community-pistol-cock-6014.mp3", "whole", {}),
    # 피격/타격
    ("impact/hit_flesh", "impact/u_xjrmmgxfru-hit-flesh-02-266309.mp3", "whole", {}),
    ("impact/hit_flesh", "impact/floraphonic-slime-splatter-7-220269.mp3", "whole", {}),
    ("impact/hit_flesh", "impact/freesound_community-080884_bullet-hit-39872.mp3", "whole", dict(trim=(0, 1.0))),
    ("impact/hit_flesh", "impact/dev_guy-bullet-gunshot-impact-390253.mp3", "whole", {}),
    ("impact/hit_melee", "impact/yodguard-giant-axe-impact-striking-into-dirt-1-450257.mp3", "whole", {}),
    ("impact/hit_melee", "impact/universfield-bone-crack-3-121580.mp3", "whole", {}),
    ("impact/hit_melee", "impact/universfield-punch-03-352040.mp3", "whole", {}),
    ("impact/blood_splatter", "impact/universfield-blood-splatter-122253.mp3", "whole", {}),
    ("impact/body_fall", "impact/universfield-body-fall-259680.mp3", "whole", {}),
    ("impact/glass_break", "impact/dragon-studio-glass-breaking-504033.mp3", "whole", dict(trim=(0, 1.6))),
    ("impact/wood_break", "impact/freesound_community-wood-crate-destory-2-97263.mp3", "whole", {}),
    # 플레이어
    ("player/footstep", "player/freesound_community-concrete-footsteps-1-6265.mp3", "split", dict(min=0.12, max=0.45, noise=-35, sil=0.08)),
    ("player/footstep", "player/soundreality-footsteps-walking-boots-parquet-1-420135.mp3", "split", dict(min=0.12, max=0.45, noise=-35, sil=0.08, count=8)),
    ("player/footstep", "player/freesound_community-footsteps-solid-wood-rug-male-boots-medium-pace-and-scuffs-34309.mp3", "split", dict(min=0.12, max=0.45, noise=-35, sil=0.08, count=5)),
    ("player/hurt", "player/freesound_community-male_hurt7-48124.mp3", "whole", {}),
    ("player/hurt", "player/freesound_community-voice_male_grunt-98362.mp3", "split", dict(min=0.4, max=1.0)),
    ("player/death", "player/u_r7cny11q7r-man-death-scream-186763.mp3", "whole", dict(trim=(0, 1.8))),
    ("player/death", "player/universfield-man-scream-05-252033.mp3", "whole", {}),
    ("player/roll", "player/freesound_community-fabric-flap-80054.mp3", "whole", {}),
    ("player/roll", "player/freesound_community-clothes-drop-2-40202.mp3", "whole", {}),
    ("player/out_of_breath", "player/voicebosch-out-of-breath-male-176715.mp3", "split", dict(min=1.0, max=4.0, count=4)),
    # UI
    ("ui/inventory_open", "UI/freesound_community-open-bag-sound-39216.mp3", "whole", {}),
    ("ui/inventory_close", "UI/yodguard-inventory-open_close-3-540173.mp3", "whole", {}),
    ("ui/pickup", "UI/freesound_community-item-pickup-37089.mp3", "whole", {}),
    ("ui/coin", "UI/yodguard-coin-collect-3-540190.mp3", "whole", {}),
    ("ui/door_open", "UI/freesound_community-door-2-6841.mp3", "whole", {}),
    ("ui/door_close", "UI/universfield-door-close-123784.mp3", "whole", {}),
    ("ui/crate_open", "UI/spinopel-open-a-egg-box-345735.mp3", "whole", {}),
    ("ui/click", "UI/universfield-computer-mouse-click-352734.mp3", "whole", dict(trim=(0, 0.3))),
    ("ui/hover", "UI/litupsubway-ui-hover-sfx-513360.mp3", "whole", {}),
    ("ui/notification", "UI/dragon-studio-new-notification-3-398649.mp3", "whole", {}),
    ("ui/success", "UI/updatepelgo-success-221935.mp3", "whole", {}),
    ("ui/fail", "UI/u_8g40a9z0la-fail-234710.mp3", "whole", {}),
    ("ui/fail", "UI/miraclei-sample_error_fail01_kofi_by_miraclei-360129.mp3", "whole", {}),
    ("ui/heartbeat", "UI/brvhrtz-heartbeat-02-225103.mp3", "split", dict(min=0.3, max=1.2, noise=-30, sil=0.12, count=1)),
    # 반복음 (자르지 않음)
    ("weather/rain_loop", "weather/liecio-calming-rain-257596.mp3", "loop", {}),
    ("ambient/night_loop", "ambient/freesound_community-night-ambience-17064.mp3", "loop", {}),
    ("ambient/wind_loop", "ambient/freesound_community-ambient_loop1-88046.mp3", "loop", {}),
    ("ambient/crows", "ambient/soundsforyou-flock-of-crows-ravens-cawing-129073.mp3", "chunk", dict(piece=4.0, count=4)),
]


def main():
    if os.path.isdir(OUT):
        # 이전 결과만 지움 (.meta는 남겨서 Unity GUID 유지)
        for d, _, files in os.walk(OUT):
            for f in files:
                if f.endswith(".wav"):
                    os.remove(os.path.join(d, f))
    counters = {}
    for name, rel, mode, opt in PLAN:
        src = os.path.join(ROOT, rel)
        os.makedirs(os.path.join(OUT, os.path.dirname(name)), exist_ok=True)
        print(f"[{mode}] {rel}")

        if mode == "whole":
            ranges = [opt.get("trim", (None, None))]
        elif mode == "loop":
            ranges = [(None, None)]
        elif mode == "split":
            segs = sound_segments(src, opt.get("noise", -40), opt.get("sil", 0.25))
            # 한 발짝/한 마디 = 너무 짧은 잡음은 버리고, 너무 긴 건 앞부분만
            ranges = [(s, min(e, s + opt["max"])) for s, e in segs if e - s >= opt["min"]]
            # 앞뒤에 약간 여유 (소리 시작이 잘리지 않게)
            ranges = [(max(0.0, s - 0.02), e + 0.03) for s, e in ranges]
        else:  # chunk
            segs = [(s, e) for s, e in sound_segments(src) if e - s > 0.5]
            ranges = [c for s, e in segs for c in chunks(s, e, opt["piece"])]
        if "count" in opt:
            ranges = ranges[:opt["count"]]

        for start, end in ranges:
            counters[name] = counters.get(name, 0) + 1
            dst = os.path.join(OUT, f"{name}_{counters[name]:02d}.wav")
            if mode == "loop":
                cut(src, dst, fade_in=0.001, fade_out=0.001)
            else:
                cut(src, dst, start, end, pitch=opt.get("pitch", 1.0),
                    fade_out=0.15 if mode == "chunk" else 0.05)


if __name__ == "__main__":
    main()
