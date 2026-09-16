---
name: commit
description: 변경 사항을 커밋해 달라는 요청("커밋해줘", "이거 커밋", "커밋 부탁")을 받았을 때 사용합니다. AGENTS.md의 커밋 규칙에 맞춰 커밋합니다.
disable-model-invocation: true
---

# 커밋

1. `docs/GIT_WORKFLOW.md` 의 **2절 커밋**을 읽는다. 규칙 정본은 거기에만 있다.
2. `git status` 와 `git diff` 로 **실제로 무엇이 바뀌었는지 확인한다.**
3. `git add -A` 를 쓰지 않는다. **파일 단위로** 지정해 스테이징하고, `git diff --cached --stat` 으로 확인한다.
4. 브랜치가 `feature/<이니셜>-<모듈>-<이슈번호>-<계획번호>-설명` 규칙에 맞는지 확인한다.
   **`main` 이나 `Develop` 에 직접 커밋하려는 상황이면 멈추고 알린다.**
5. 여러 작업이 섞여 있으면 나눠서 커밋하도록 제안한다.
6. 커밋 메시지를 보여 주고 **승인을 받은 뒤** 커밋한다.
7. `push` 는 따로 요청받았을 때만 한다.

생성된 `Assets/GameData/Generated/BalanceData.asset` 이 스테이징에 들어 있으면,
CSV 변경과 **같은 커밋**인지 확인한다. CSV 없이 이 파일만 바뀌었으면 손으로 고친 것이므로 멈추고 알린다.

이 파일과 `docs/GIT_WORKFLOW.md` 의 내용이 다르면 `GIT_WORKFLOW.md` 를 따른다.
