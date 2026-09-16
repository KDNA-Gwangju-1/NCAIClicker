---
name: start-work
description: 이슈에 착수할 때 가장 먼저 사용합니다. 담당자 배정 → 칸반 In Progress → Develop에서 브랜치 생성까지 하고, 코드는 건드리지 않습니다. 기능 구현·수정 요청을 받았는데 아직 작업 브랜치가 없으면 이것부터 실행합니다.
---

# 작업 착수

`AGENTS.md` 의 **"작업 순서 (필수)" 0번**이 이 스킬의 정본이다. 절차의 근거와 예외는 거기와
`docs/GIT_WORKFLOW.md` 2·3절에 있다. 여기에는 실행 방법만 적는다.

## 0. 이슈 번호를 확인한다

인자로 받았으면 그것을 쓴다 (`/start-work 52`). 없으면 **추측하지 말고 묻는다.**
대응하는 이슈가 아예 없으면 `작업` 템플릿으로 이슈부터 만들자고 제안한다 — 이슈가 작업 지시서다.

## 1. 이슈 확인과 배정

```bash
gh issue view <번호> --json number,title,assignees,state,body
```

- 이미 **다른 사람**이 배정돼 있으면 **멈추고 보고한다.** 뺏지 않는다.
- 비어 있으면 사용자를 배정한다: `gh issue edit <번호> --add-assignee <로그인>`
  (`gh issue assign` 은 없는 서브커맨드다 — help 만 출력하고 조용히 실패한다.)
- 본문에 `**선행**:` 줄이 있으면 그 선행 이슈들이 닫혔는지 확인하고, 열려 있으면 보고한다.

## 2. 칸반 Status → In Progress

보드는 [NCAIClicker 프로젝트 2번](https://github.com/orgs/KDNA-Gwangju-1/projects/2/views/2)이다.
카드가 보드에 없으면 먼저 올린다.

```bash
gh project item-add 2 --owner KDNA-Gwangju-1 --url <이슈 URL> --format json -q .id
gh project item-edit --id <아이템 ID> --project-id <프로젝트 ID> \
  --field-id <Status 필드 ID> --single-select-option-id <In Progress 옵션 ID>
```

ID 는 박아 두지 말고 그때그때 조회한다 — 보드를 다시 만들면 전부 바뀐다.

```bash
gh api graphql -f query='{organization(login:"KDNA-Gwangju-1"){projectV2(number:2){
  id fields(first:20){nodes{... on ProjectV2SingleSelectField{id name options{id name}}}}}}}'
```

바뀐 결과를 **반드시 눈으로 확인한다** (item-edit 는 실패해도 조용할 때가 있다):

```bash
gh project item-list 2 --owner KDNA-Gwangju-1 --limit 200 --format json \
  -q '.items[]|select(.content.number==<번호>)|"\(.status) \(.assignees)"'
```

## 3. 브랜치 생성

```bash
git fetch origin Develop
git checkout -b <브랜치명> origin/Develop
```

- 브랜치 이름은 `docs/GIT_WORKFLOW.md` **1절**을 그대로 따른다. 형식·구분자·예시가 거기에 있다.
- 커밋하지 않은 다른 작업이 남아 있으면 **먼저 보고하고 확인을 받는다.** 임의로 stash 하거나 커밋하지 않는다.

## 4. 보고하고 멈춘다

이슈 번호·담당자·Status·브랜치 이름 네 가지를 적어 보고한다.
**여기서 코드를 쓰지 않는다.** 구현은 `AGENTS.md` 작업 순서 1번(계획 승인)부터다.

이 파일과 `AGENTS.md` 의 내용이 다르면 `AGENTS.md` 를 따른다.
