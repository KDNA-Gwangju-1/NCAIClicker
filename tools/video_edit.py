"""제출 영상(#45) 편집 도구 — 자막 입히기, 텍스트 슬라이드, 이어 붙이기 (#344).

Unity 메뉴 `NCAI > 시연 > 녹화하며 시작` 은 `Recordings/<시나리오>_<시각>.mp4` 와
같은 이름의 `.ass` 자막 파일을 남긴다. 이 스크립트는 그 둘을 합치고, 팀 소개·개발 과정 같은
텍스트 슬라이드를 만들고, 조각들을 한 편으로 이어 붙인다. 자막 문구만 고칠 때는 `.ass` 를
고치고 `burn` 만 다시 돌린다 — 다시 녹화할 필요가 없다.

게임 사운드는 넣지 않는다. 배경음이 필요하면 `concat --bgm` 으로 저작권 없는 음원을 깐다.
글꼴은 저장소의 나눔고딕(Assets/ThirdParty/Fonts)을 쓰므로 Windows·macOS 결과가 같다.
ffmpeg(libass 포함 빌드)가 PATH 에 있어야 한다.

쓰는 법:
    python tools/video_edit.py burn Recordings/DefaultDemoScenario_20260925_192034.mp4
    python tools/video_edit.py slide "2조" "김성훈 · 강찬양 · 김훈일 · 김창준 · 정야후" --seconds 4 -o Recordings/team.mp4
    python tools/video_edit.py trim Recordings/DemoFeatures_..._sub.mp4 --start 170 -o Recordings/cut.mp4
    python tools/video_edit.py concat Recordings/team.mp4 Recordings/DefaultDemoScenario_..._sub.mp4 -o Recordings/final.mp4 --bgm music.mp3
"""

import argparse
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FONT_DIR = ROOT / "Assets" / "ThirdParty" / "Fonts"
WIDTH, HEIGHT, FPS = 1920, 1080, 30

SLIDE_HEADER = """[Script Info]
ScriptType: v4.00+
PlayResX: 1920
PlayResY: 1080

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Slide,NanumGothic,80,&H00FFFFFF,&H00FFFFFF,&H00000000,&H00000000,1,0,0,0,100,100,0,0,1,0,0,5,120,120,0,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
"""


def run_ffmpeg(args, cwd):
    if shutil.which("ffmpeg") is None:
        sys.exit("ffmpeg 를 찾을 수 없습니다. libass 가 포함된 빌드를 설치하고 PATH 에 넣으세요.")
    command = ["ffmpeg", "-v", "error", "-y", *args]
    result = subprocess.run(command, cwd=cwd)
    if result.returncode != 0:
        sys.exit(f"ffmpeg 실패 (코드 {result.returncode}): {' '.join(command)}")


def ass_filter(ass_path, work_dir):
    # ass 필터는 드라이브 문자(C:)와 역슬래시를 잘 못 읽는다. 작업 폴더 기준 상대 경로로 넘긴다.
    ass_rel = Path(ass_path).name
    font_rel = Path(shutil.copytree(FONT_DIR, Path(work_dir) / "fonts", dirs_exist_ok=True)).name
    return f"ass={ass_rel}:fontsdir={font_rel}"


def encode_args():
    return ["-c:v", "libx264", "-crf", "18", "-preset", "medium", "-pix_fmt", "yuv420p", "-r", str(FPS)]


def burn(video, ass, output):
    video = Path(video).resolve()
    ass = Path(ass).resolve() if ass else video.with_suffix(".ass")
    output = Path(output).resolve() if output else video.with_name(video.stem + "_sub.mp4")
    if not ass.exists():
        sys.exit(f"자막 파일이 없습니다: {ass}")
    with tempfile.TemporaryDirectory() as work:
        shutil.copy(ass, Path(work) / ass.name)
        vf = f"scale={WIDTH}:{HEIGHT},{ass_filter(ass, work)}"
        run_ffmpeg(["-i", str(video), "-vf", vf, "-an", *encode_args(), str(output)], cwd=work)
    print(output)


def slide(lines, seconds, output):
    output = Path(output).resolve()
    # 첫 줄은 제목, 나머지는 작게. 앞뒤로 0.3초 페이드.
    title, rest = lines[0], lines[1:]
    # 제목과 본문 사이에 작은 빈 줄을 둬 위계를 벌린다.
    body = "\\N".join(["{\\fs54}" + line for line in rest])
    text = "{\\fad(300,300)}{\\fs96}" + title + ("\\N{\\fs36} \\N" + body if body else "")
    end = f"0:{int(seconds) // 60:02d}:{seconds % 60:05.2f}"
    with tempfile.TemporaryDirectory() as work:
        ass = Path(work) / "slide.ass"
        ass.write_text(SLIDE_HEADER + f"Dialogue: 0,0:00:00.00,{end},Slide,,0,0,0,,{text}\n", encoding="utf-8")
        run_ffmpeg([
            "-f", "lavfi", "-i", f"color=c=black:s={WIDTH}x{HEIGHT}:d={seconds}:r={FPS}",
            "-vf", ass_filter(ass, work), *encode_args(), str(output),
        ], cwd=work)
    print(output)


def trim(video, start, end, output):
    """반복 구간을 빼려고 앞뒤를 자른다. 자막을 입힌 뒤에 자르면 자막도 함께 잘린다."""
    args = ["-ss", str(start), "-i", str(Path(video).resolve())]
    if end is not None:
        args += ["-t", str(end - start)]
    output = Path(output).resolve()
    run_ffmpeg([*args, "-an", *encode_args(), str(output)], cwd=ROOT)
    print(output)


def concat(inputs, output, bgm, volume):
    output = Path(output).resolve()
    inputs = [Path(p).resolve() for p in inputs]
    with tempfile.TemporaryDirectory() as work:
        # 조각마다 해상도·프레임레이트를 맞춰 다시 인코딩한 뒤 잇는다 (copy 로 이으면 조각 규격이 다를 때 깨진다).
        parts = []
        for index, path in enumerate(inputs):
            part = Path(work) / f"part{index:02d}.mp4"
            run_ffmpeg(["-i", str(path), "-vf", f"scale={WIDTH}:{HEIGHT},fps={FPS}", "-an", *encode_args(), str(part)], cwd=work)
            parts.append(part)
        listing = Path(work) / "list.txt"
        listing.write_text("".join(f"file '{p.name}'\n" for p in parts), encoding="utf-8")
        joined = Path(work) / "joined.mp4"
        run_ffmpeg(["-f", "concat", "-safe", "0", "-i", listing.name, "-c", "copy", joined.name], cwd=work)

        if bgm:
            duration = float(subprocess.run(
                ["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(joined)],
                capture_output=True, text=True, check=True).stdout.strip())
            fade_start = max(0.0, duration - 3.0)
            run_ffmpeg([
                "-i", joined.name, "-stream_loop", "-1", "-i", str(Path(bgm).resolve()),
                "-filter_complex", f"[1:a]volume={volume},afade=t=in:d=2,afade=t=out:st={fade_start}:d=3[a]",
                "-map", "0:v", "-map", "[a]", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-shortest", str(output),
            ], cwd=work)
        else:
            shutil.copy(joined, output)
    print(output)


def main():
    parser = argparse.ArgumentParser(description="제출 영상 편집 도구 (#344)")
    sub = parser.add_subparsers(dest="command", required=True)

    p_burn = sub.add_parser("burn", help="녹화본에 같은 이름의 .ass 자막을 입힌다")
    p_burn.add_argument("video")
    p_burn.add_argument("--ass", help="자막 파일 (기본: 영상과 같은 이름의 .ass)")
    p_burn.add_argument("-o", "--output", help="출력 (기본: <영상>_sub.mp4)")

    p_slide = sub.add_parser("slide", help="검은 배경 텍스트 슬라이드를 만든다. 첫 줄이 제목")
    p_slide.add_argument("lines", nargs="+")
    p_slide.add_argument("--seconds", type=float, default=4.0)
    p_slide.add_argument("-o", "--output", required=True)

    p_trim = sub.add_parser("trim", help="영상의 일부 구간만 남긴다 (초 단위)")
    p_trim.add_argument("video")
    p_trim.add_argument("--start", type=float, default=0.0)
    p_trim.add_argument("--end", type=float, help="끝 (기본: 영상 끝까지)")
    p_trim.add_argument("-o", "--output", required=True)

    p_concat = sub.add_parser("concat", help="조각들을 순서대로 이어 붙인다")
    p_concat.add_argument("inputs", nargs="+")
    p_concat.add_argument("-o", "--output", required=True)
    p_concat.add_argument("--bgm", help="배경음 파일 (반복·페이드)")
    p_concat.add_argument("--volume", type=float, default=0.35)

    args = parser.parse_args()
    if args.command == "burn":
        burn(args.video, args.ass, args.output)
    elif args.command == "trim":
        trim(args.video, args.start, args.end, args.output)
    elif args.command == "slide":
        slide(args.lines, args.seconds, args.output)
    else:
        concat(args.inputs, args.output, args.bgm, args.volume)


if __name__ == "__main__":
    main()
