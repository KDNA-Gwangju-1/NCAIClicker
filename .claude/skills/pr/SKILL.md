---
name: pr
description: PR을 올려 달라는 요청("PR 올려줘", "풀리퀘 만들어줘", "PR 부탁")을 받았을 때 사용합니다. 브랜치를 push하고 GitHub CLI로 PR을 만듭니다.
disable-model-invocation: true
---

# PR 올리기

1. `docs/GIT_WORKFLOW.md` 의 **1·4절**을 읽는다. 규칙 정본은 거기에만 있다.
2. 현재 브랜치명이 `feature/<이니셜>-<모듈>-<이슈번호>-설명` 규칙에 맞는지 확인한다.
   맞지 않거나 `main`/`Develop` 위에 있으면 멈추고 알린다.
3. 커밋하지 않은 변경이 남아 있으면 알린다.
4. `git fetch origin Develop && git rebase origin/Develop` — PR 직전 리베이스는 필수다.
   충돌 중 공용 인터페이스·이벤트·CSV 스키마가 바뀐 것을 발견하면 **두 버전을 섞지 말고 멈춘 뒤 보고한다.**
5. `convention-checker` 에이전트로 이 브랜치의 변경분을 점검하고, 위반이 있으면 PR 전에 보고한다.
6. **기술 문서가 있는지 확인한다** — 구현 PR인데 `docs/TECH_NOTES/` 변경이 없으면
   `/tech-note` 로 먼저 쓰도록 알린다.
7. 이 브랜치에 쌓인 커밋을 확인한다 (`git log origin/Develop..HEAD`).
8. PR 제목과 본문을 써서 보여 주고 **승인을 받는다.** 본문은 `.github/PULL_REQUEST_TEMPLATE.md`
   양식을 그대로 채운다. 체크리스트 항목을 **확인하지 않고 체크하지 않는다.**
9. 승인을 받으면 push 하고 `gh pr create --base Develop` 로 PR을 만든다. **대상은 `main` 이 아니다.**
10. 만든 PR 주소를 알려 주고, **이슈 상태를 In Review 로 바꾼다.**

**머지는 하지 않는다.** 리뷰어 최소 1명의 승인이 필요하고, 머지는 사람이 한다.

이 파일과 `docs/GIT_WORKFLOW.md` 의 내용이 다르면 `GIT_WORKFLOW.md` 를 따른다.
