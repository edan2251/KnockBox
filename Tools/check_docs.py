"""문서 링크·경로 검사 (Unity 없이 실행).

사용: 저장소 루트에서 `python Tools/check_docs.py`. 문제가 있으면 목록을 출력하고 종료 코드 1을 돌려줍니다.

검사 범위
- docs/**/*.md, 루트 AGENTS.md·CLAUDE.md·README.md 의 Markdown 상대 링크 `[글](경로#앵커)`:
  대상 파일이 있는지, 앵커가 있으면 그 문서에 `<a id="...">` 또는 같은 제목이 있는지 확인합니다.
- 백틱 안의 저장소 경로 중 `Assets/_Project/`, `docs/`, `Tools/` 로 시작하는 것:
  파일·폴더가 있는지 확인합니다.

검사하지 않는 것 (한계)
- http(s) 링크, `D:/...` 같은 저장소 밖 보관 경로, Git에서 제외된 `Assets/_Project/LocalOnly/`.
- `Art/...`처럼 접두어 없이 줄여 쓴 경로, `*`·`<`가 들어간 예시 경로, 코드 기호·메서드 이름.
- 문서에서 앞으로 만들 위치로 안내한 폴더(PLANNED 목록).
- 문서 내용이 코드 동작과 맞는지. 이 판단은 코드 검토로 합니다.
"""
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
LINK = re.compile(r"(?<!!)\[[^\]]*\]\(([^)\s]+)\)")
TICK = re.compile(r"`([^`\n]+)`")
ANCHOR_ID = re.compile(r"<a\s+id=\"([^\"]+)\"")
HEADING = re.compile(r"^#{1,6}\s+(.+?)\s*#*\s*$")
CHECKED_PREFIXES = ("Assets/_Project/", "docs/", "Tools/")
# ART_WORKFLOW가 새 아트를 넣을 때 만들 위치로 안내하는 폴더. 아직 없어도 정상입니다.
PLANNED = {"Assets/_Project/Art/Characters/", "Assets/_Project/Art/UI/", "Assets/_Project/Art/Environment/"}


def slug(title):
    title = re.sub(r"[`*_]", "", title).strip().lower()
    title = re.sub(r"[^\w\- ]", "", title)
    return title.replace(" ", "-")


def anchors(path, cache={}):
    if path not in cache:
        text = open(path, encoding="utf-8-sig").read()
        found = set(ANCHOR_ID.findall(text))
        for line in text.splitlines():
            m = HEADING.match(line)
            if m:
                found.add(slug(m.group(1)))
        cache[path] = found
    return cache[path]


def markdown_files():
    for name in ("AGENTS.md", "CLAUDE.md", "README.md"):
        p = os.path.join(ROOT, name)
        if os.path.exists(p):
            yield p
    for folder, _, files in os.walk(os.path.join(ROOT, "docs")):
        for f in sorted(files):
            if f.endswith(".md"):
                yield os.path.join(folder, f)


def check(path):
    problems = []
    rel = os.path.relpath(path, ROOT).replace("\\", "/")
    in_code = False
    for no, line in enumerate(open(path, encoding="utf-8-sig").read().splitlines(), 1):
        if line.lstrip().startswith("```"):
            in_code = not in_code
            continue
        if in_code:
            continue
        for target in LINK.findall(line):
            if re.match(r"^[a-z]+:", target):
                continue
            file_part, _, anchor = target.partition("#")
            dest = os.path.normpath(os.path.join(os.path.dirname(path), file_part)) if file_part else path
            if not os.path.exists(dest):
                problems.append(f"{rel}:{no} 링크 대상 없음: {target}")
            elif anchor and dest.endswith(".md") and anchor not in anchors(dest):
                problems.append(f"{rel}:{no} 앵커 없음: {target}")
        for item in TICK.findall(line):
            item = item.strip()
            if not item.startswith(CHECKED_PREFIXES) or item.startswith("Assets/_Project/LocalOnly"):
                continue
            if re.search(r"[*<>\s]", item) or item in PLANNED:
                continue
            if not os.path.exists(os.path.join(ROOT, item)):
                problems.append(f"{rel}:{no} 경로 없음: {item}")
    return problems


def main():
    if not sys.stdout.isatty():
        sys.stdout.reconfigure(encoding="utf-8")
    files = list(markdown_files())
    problems = [p for f in files for p in check(f)]
    for p in problems:
        print(p)
    print(f"검사한 문서 {len(files)}개, 문제 {len(problems)}개")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
