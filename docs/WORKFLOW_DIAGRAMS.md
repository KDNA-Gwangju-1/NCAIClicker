# 작업 흐름 도식

말로 적으면 긴 것들을 그림으로 모아 둔 페이지다. **규칙의 정본이 아니다** —
브랜치·커밋·PR 규칙은 [GIT_WORKFLOW.md](GIT_WORKFLOW.md), 작업 지침은 [AGENTS.md](../AGENTS.md)에 있다.

## 1. 전체 구조 — 읽는다 / 돈다 / 쌓인다

```mermaid
flowchart LR
    RULES["📖 규칙<br/>읽는다<br/><br/>AGENTS · GIT_WORKFLOW<br/>ARCHITECTURE · GDD"]
    CYCLE["🔁 이슈 한 바퀴<br/>돈다<br/><br/>카드 → 브랜치 → 구현<br/>→ 검수 → 문서 → PR"]
    LOG["📝 기록<br/>쌓인다<br/><br/>docs/TECH_NOTES/<br/>기능 단위 로그"]

    RULES == "전부 읽지 않는다<br/>필요한 절만" ==> CYCLE
    CYCLE == "구현이 끝나면" ==> LOG
    LOG -. "결과가 쎄할 때<br/>대조 기준이 된다" .-> CYCLE

    style RULES fill:#1e3a2f,stroke:#22c55e,color:#fff
    style CYCLE fill:#3b2f0b,stroke:#eab308,color:#fff
    style LOG fill:#1e3a5f,stroke:#3b82f6,color:#fff
```

규칙은 **바뀌면 한 곳만 고친다.** 기록은 **팀이 같이 갱신한다.**

## 2. 이슈 한 바퀴

```mermaid
flowchart LR
    subgraph P1["준비"]
        direction TB
        C1["① 칸반 카드<br/>In Progress"] --> C2["② Develop 에서<br/>브랜치"]
    end
    subgraph P2["구현"]
        direction TB
        C3["③ 계획 승인<br/>/new-script"] --> C4["④ 구현"]
    end
    subgraph P3["검증"]
        direction TB
        C5["⑤ 사람이 검수"] --> C6["⑥ 규칙 점검<br/>convention-checker"] --> C7["⑦ 기술 문서<br/>/tech-note"]
    end
    subgraph P4["통합"]
        direction TB
        C8["⑧ PR → Develop<br/>/pr"] --> C9["⑨ 리뷰 1명<br/>Squash merge"]
    end

    C2 --> C3
    C4 --> C5
    C7 --> C8
    C5 -. "쎄하면 /verify-docs" .-> C4

    style C5 fill:#7f1d1d,stroke:#ef4444,stroke-width:3px,color:#fff
```

**⑤ 검수만은 사람이 한다.** 한두 번 건너뛰면 컨벤션은 맞는데 어떤 논리로 만들었는지
아무도 모르는 코드가 쌓이고, 그때부터 수정·버그픽스의 **작업 일정을 말할 수 없게 된다.**

## 3. 브랜치

```mermaid
gitGraph
    commit id: "초기 세팅"
    branch Develop
    checkout Develop
    commit id: "기능 통합"
    branch "feature/KSH-economy-12"
    checkout "feature/KSH-economy-12"
    commit id: "구현"
    commit id: "기술 문서"
    checkout Develop
    merge "feature/KSH-economy-12" tag: "PR · Squash"
    checkout main
    merge Develop tag: "v1.0"
```

`main` 은 배포 가능한 상태만 담는다. **브랜치는 `Develop` 에서 따고 PR 대상도 `Develop`.**
배포 후 긴급 수정만 `main` 에서 따며, 이때는 `main` 과 `Develop` 양쪽에 반영한다.

## 4. 문서 지도

```mermaid
flowchart LR
    C["CLAUDE.md"] -. "포인터 한 줄" .-> A
    R["README<br/>사람 진입점"]
    A["AGENTS.md<br/>AI 진입점"]

    subgraph WHAT["무엇을 만드나"]
        direction TB
        GDD["GDD"]
        BAL["BALANCE"]
        REF["REFERENCE_ANALYSIS"]
    end
    subgraph HOW["어떻게 짜나"]
        direction TB
        ARCH["ARCHITECTURE<br/>공용 계약"]
        PAT["PATTERNS"]
    end
    subgraph WORK["어떻게 일하나"]
        direction TB
        PLAN["EXECUTION_PLAN"]
        GIT["GIT_WORKFLOW<br/>규칙 정본"]
        ASSET["ASSET_PIPELINE"]
        REL["RELEASE_CHECKLIST"]
    end
    subgraph REC["무엇을 만들었나"]
        direction TB
        TN["TECH_NOTES/<br/>기능 단위 로그"]
        RETRO["RETROSPECTIVE"]
    end

    R --> WHAT
    R --> HOW
    R --> WORK
    A --> WHAT
    A --> HOW
    A --> WORK
    HOW -.-> REC
    WORK -.-> REC

    style REC fill:#1e3a5f,stroke:#3b82f6,color:#fff
```

읽는 순서가 아니라 **답하는 질문**으로 나뉜다. 필요한 질문의 문서만 연다.

## 5. 사람과 AI의 경계

| 사람만 | AI에게 맡김 | AI가 하면 안 되는 것 |
|---|---|---|
| 계획 승인 | 계획 수립 | 머지 |
| **검수** (Unity에서 직접) | 구현 | 남의 씬·프리팹 저장 |
| PR 리뷰 | 규칙 점검 | 공용 계약 임의 변경 |
| 머지 | 문서 작성 · PR 생성 | 진행률 추측 |
