"""원작 아트 레퍼런스를 개발사 공식 프레스킷에서 내려받는다.

받은 파일은 `research/reference/press-kit/` 에 두며 **저장소에 커밋하지 않는다**
(.gitignore 로 막아 둠). 프레스킷은 보도용으로 제공된 자산이라 다른 게임에 넣으라고
준 것이 아니다 — 보면서 우리 에셋을 만드는 데까지만 쓴다.
사용 경계는 docs/ASSET_PIPELINE.md 1-1 절에 있다.

쓰는 법:
    python tools/fetch_reference.py            # 없는 것만 받는다
    python tools/fetch_reference.py --force     # 이미 있어도 다시 받는다

로고와 Steam 캡슐은 일부러 받지 않는다. 브랜딩 자산이라 우리가 쓸 일이 없고,
받아 두면 누군가 쓰게 된다.
"""

import argparse
import sys
import urllib.error
import urllib.request
from pathlib import Path

PRESS_KIT_PAGE = "https://rikegames.com/press/bills-must-be-paid/"
BASE_URL = "https://rikegames.com/press/bills-must-be-paid/images"
OUT_DIR = Path(__file__).resolve().parent.parent / "research" / "reference" / "press-kit"

# 아트 레퍼런스로 쓰는 것만 적는다. 로고·캡슐·트레일러는 제외한다.
FILES = [
    *[f"screenshots/bills-must-be-paid-screenshot-{i:02d}.jpg" for i in range(1, 10)],
    "key-art/bills-must-be-paid-piggy.png",
    "key-art/bills-must-be-paid-thumbnail-clean.png",
]


def fetch(relative_path: str, force: bool) -> str:
    target = OUT_DIR / Path(relative_path).name
    if target.exists() and not force:
        return "건너뜀"

    url = f"{BASE_URL}/{relative_path}"
    try:
        with urllib.request.urlopen(url, timeout=30) as response:
            data = response.read()
    except (urllib.error.URLError, TimeoutError) as error:
        return f"실패 ({error})"

    target.write_bytes(data)
    return f"받음 ({len(data) // 1024}kB)"


def main() -> int:
    parser = argparse.ArgumentParser(description="원작 아트 레퍼런스 내려받기")
    parser.add_argument("--force", action="store_true", help="이미 있어도 다시 받는다")
    args = parser.parse_args()

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    print(f"출처: {PRESS_KIT_PAGE}")
    print(f"저장 위치: {OUT_DIR}\n")

    failed = 0
    for relative_path in FILES:
        result = fetch(relative_path, args.force)
        print(f"  {Path(relative_path).name:<44} {result}")
        if result.startswith("실패"):
            failed += 1

    print()
    if failed:
        print(f"{failed}개를 받지 못했다. 프레스킷 구성이 바뀌었을 수 있으니 페이지를 직접 확인한다.")
        return 1

    print("완료. 이 폴더는 gitignore 로 막혀 있으니 커밋하지 않는다.")
    print("없는 자료(UI 없는 스크린샷, 인쇄 해상도 키아트 등)는 info@rikegames.com 에 요청할 수 있다.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
