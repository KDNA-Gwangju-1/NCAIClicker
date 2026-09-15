# 밸런스 설계표

모든 수치는 `Assets/GameData/Balance/*.csv`가 원본이다. 이 문서는 **왜 그 숫자가 됐는지**를 적는다. 숫자를 바꿀 때는 CSV를 고치고, 근거가 달라졌으면 이 문서도 같이 고친다.

## 1. CSV를 원본으로 삼는 이유

Unity에서 밸런스 데이터를 다루는 방법은 보통 ScriptableObject(이하 SO)다. 그런데 SO 에셋(`.asset`)은 YAML이라 **여러 명이 같은 파일을 고치면 Git 충돌이 나고, 충돌 해결이 어렵다.** [EXECUTION_PLAN.md](EXECUTION_PLAN.md)에서 공유 SO의 동시 수정을 위험으로 지목한다.

CSV를 원본으로 두면 이 문제가 사라진다.

| | CSV 원본 | SO 직접 편집 |
|---|---|---|
| Git 충돌 | 행 단위 자동 병합 — 다른 행을 고치면 충돌 없음 | 파일 전체 충돌, 수동 해결 |
| 편집 도구 | Excel, Google Sheets, 메모장 | Unity 에디터를 켜야 함 |
| 리뷰 | PR diff에 `40 → 25`로 보임 | YAML 덩어리라 판독 어려움 |
| 타입 안전성 | 없음 (임포트 시 검증 필요) | 있음 |

**구조**: CSV는 원본, SO는 빌드 산출물이다.

```
Assets/GameData/Balance/*.csv   ← 사람이 고치는 원본 (PM이 Unity 없이도 수정 가능)
        ↓ 에디터 임포터 (Unity 에디터에서만 실행)
Assets/GameData/Generated/*.asset  ← 자동 생성 SO
        ↓ 런타임은 이것만 읽는다
```

런타임에서 CSV를 파싱하지 않는 것이 핵심이다. 빌드에 파서를 넣으면 문자열 파싱 실패가 그대로 런타임 크래시가 된다. 임포트는 에디터에서 끝내고, 실패하면 그 자리에서 콘솔 에러로 잡는다.

**규칙**: 생성된 SO는 손으로 고치지 않는다. 고쳐도 다음 임포트에 덮어써진다.

### 생성된 SO도 저장소에 커밋한다

산출물인데 왜 커밋하느냐 하면, **프리팹과 씬이 이 에셋을 GUID로 참조하기 때문**이다. 각자 로컬에서 생성하게 두면 사람마다 GUID가 달라져서, 내 PC에서 연결한 참조가 남의 PC에서 끊긴다.

임포터는 기존 에셋이 있으면 그것을 갱신하고 새로 만들지 않으므로 GUID는 한번 정해진 뒤 바뀌지 않는다 (현재 `148d52a3...`).

**병합 충돌이 나면 해결하지 말고 재임포트한다.** 생성물이므로 CSV만 맞으면 결과는 같다.

## 2. 스태미나 — 기본 길이와 회복 연장을 구분한다

시연 목표는 한 런 60~90초다. 기본 길이는 `max_stamina / idle_drain_per_sec`로 정하고,
회복형과 퍼크가 이를 늘린다. 커서 이동·타격 추가 소모는 0, 피버 감소 계수는 1로 고정한다.
이 호환 필드에 다른 값을 넣으면 임포터가 거부한다.

호버 스윙 간격은 `economy.csv`의 `hover_swing_interval_sec`가 원본이다. 런 전체 스윙과 적중 수를
혼동하지 않는다. 예상 적중 수는 `기본 런 길이 / 스윙 간격 × 조준 성공률`이다.

## 3. 경제 — 파괴 모델로 다시 계산한다

**이전 타격당 코인 모델의 총수입·첫 런 돌파 결론은 폐기했다.** 파괴 보상, 회복형, 동시 출현 수,
재등장 대기, 피버를 함께 모델링해야 한다. 새 도구는 `.github/scripts/simulate_balance.py`다.

```bash
python -B .github/scripts/simulate_balance.py --runs 1000 --seed 46 --uptime 0.6 --policy random
python -B .github/scripts/simulate_balance.py --runs 1000 --seed 46 --uptime 0.6 --policy value
```

### 가정과 재현 범위

- 첫 단계, 업그레이드 없음, 대출 없음. 초기 자동 망치가 0이 아니면 도구는 지원하지 않는 조건으로 중단한다.
- CSV 출현 비율로 대상을 뽑고 슬롯별 파괴 후 재등장 대기를 적용한다. 선택한 대상은 파괴까지 유지한다.
- 스윙마다 독립적인 조준 성공 확률을 적용한다. 실제 이동속도·화면 배치·커서 이동 거리는 모델링하지 않는다.
- `random`은 다음 목표를 무작위로, `value`는 보상/내구도가 높은 목표부터 선택한다.
- 적중 시 피버를 채우고 파괴 시점의 피버 배율을 적용한다. 회복은 최대 스태미나까지 제한한다.
- 이 계산은 **몬테카를로 추정이며 Play Mode 실측이 아니다.** 이동 구현과 피버 발동 처리 순서가 정해지면 실제 로그와 비교한다.

### 초기 세팅 조정 결과 (2026-09-16, #46)

기존 CSV를 모델에 넣으면 무작위 선택 평균 런이 약 137초였다. 회복형 연장이 목표보다 커서
`targets.csv`의 회복량을 낮추고 첫 단계 목표를 모델 중앙값 부근으로 조정했다.
후속 단계는 같은 성장률로 산출한 초기값이며 **업그레이드 이후의 도달 가능성까지 검증한 값은 아니다.**

| 모델 | 평균 런(초) | 평균 순수입 | 중앙값 | 수입 10~90 백분위 | 목표 도달 비율 |
|---|---:|---:|---:|---:|---:|
| random | 84.83 | 446.35 | 442.70 | 349.20~552.20 | 46.4% |
| value | 84.82 | 442.53 | 434.30 | 347.80~550.00 | 42.2% |

각 모델은 같은 시드로 1,000회 실행했다. 이 표는 계산 결과 스냅샷이고 **설정값의 정본은 CSV**다.
회복형·피버·실제 조준에 따라 결과가 달라지므로 수치 변경 후 도구를 다시 실행한다.
첫 단계는 실수하면 미달하고 업그레이드로 재도전하도록 출발점을 잡았다.

## 4. 청구서·대출 — 구조 검사와 플레이 검증을 분리한다

`단계 목표 × 마감일수`는 수입 상한도 예상 수입도 아니다. 이전 임포터의 이 조건은 제거했다.
임포터는 납부 금액/기한의 유효 범위, 중복 ID, 출현 비율, 유한한 수치, 필수 열을 검사한다.
청구서를 실제로 낼 수 있는지와 대출 후 갚을 수 있는지는 작업 7.2에서 측정한다.

- 현재 청구서 금액은 규칙 확인용 낮은 초기값이다. 핵심 판단이 생기는 부담 수준은
  실제 마감일까지 번 **순수입**과 업그레이드 지출 여력을 함께 보며 조정한다.
- 지표: 첫 마감 납부율, 마감 직전 가용 코인, 대출 후 완제까지 걸린 날짜, 파산 후 재성장 속도.
- 미납은 마감 선택 완료 후 파산이다. 데모의 추심 누적 삭감은 도입하지 않는다.
- 대출 수입 징수는 부채를 줄이지 않는다. 원금 입금은 런 수입에 포함하지 않는다.

## 5. 피버

게이지는 **적중**으로만 채운다. 호버·자동 망치의 적중 여부와 `HitSource`를 함께 전달하고,
정확도는 호버만 집계한다. 회복·스폰 대기로 적중 수가 달라지므로 런당 발동 횟수를 상수로 단정하지 않는다.
게이지·지속 시간·배율은 `fever.csv`를 따른다. 퍼크 선택 중에는 피버 시간도 멈춘다.

## 6. 업그레이드 효과 — 어떤 수치를 건드리는지 표에서 지정한다

업그레이드는 두 파일로 나뉜다.

- `upgrades.csv` — 이름, 설명, 초기 비용, 최대 레벨, 표시 순서
- `upgrade_effects.csv` — **그 업그레이드가 어떤 수치를 얼마나 바꾸는지**

나눈 이유는, 업그레이드 하나가 여러 수치를 동시에 건드리기 때문이다. 예를 들어 강한 망치는 타격 파워와 판정 반경을 같이 올린다. 한 파일에 우겨넣으면 `effect1_stat`, `effect2_stat` 같은 열이 계속 늘어난다.

```csv
upgrade_id,stat,effect_type,value_per_level,note
strong_hammer,base_hit_power,add,0.35,레벨당 타격 피해량 +0.35
strong_hammer,hit_radius,percent,2,레벨당 피격 판정 반경 +2%
```

- `stat` — **무엇을 바꿀지.** 아래 표의 이름만 쓸 수 있다
- `effect_type` — `add`(그대로 더함) 또는 `percent`(기준값의 %만큼 더함)
- `value_per_level` — 레벨 1당 변화량

### stat 이름이 가리키는 기준값

`stat` 에 적는 이름은 임의 문자열이 아니라 **정해진 목록**이며, 각각 어느 CSV의 어느 값을 기준으로 삼는지가 아래처럼 고정되어 있다. 오타를 적으면 임포트가 실패하면서 쓸 수 있는 이름 목록을 콘솔에 출력한다.

| stat 이름 | 기준값이 있는 곳 | 의미 |
|---|---|---|
| `base_hit_power` | `economy.csv` → `base_hit_power` | 호버 타격 1회 피해량 |
| `hit_radius` | `economy.csv` → `hit_radius_bonus` | 피격 판정 확대 비율 |
| `auto_hammer_count` | `economy.csv` → `auto_hammer_count_init` | 자동 망치 보유 수 |
| `auto_hammer_power` | `economy.csv` → `auto_hammer_power` | 자동 망치 타격 1회 피해량 |
| `auto_hammer_hits_per_sec` | `economy.csv` → `auto_hammer_hits_per_sec` | 자동 망치 초당 타격 |
| `coin_bonus_multiplier` | `economy.csv` → `coin_bonus_multiplier` | 보너스 배율 |
| `spawn_count` | `stages.csv` → `spawn_count` | 동시 출현 저금통 수 |
| `spawn_interval_sec` | `economy.csv` → `spawn_interval_sec` | 재등장 대기 시간 |
| `fever_duration` | `fever.csv` → `duration_sec` | 피버 지속 시간 |
| `fever_multiplier` | `fever.csv` → `coin_multiplier` | 피버 코인 배율 |
| `fever_gauge_per_hit` | `fever.csv` → `gauge_per_hit` | 피버 게이지 누적량 |
| `max_stamina` | `stamina.csv` → `max_stamina` | 최대 스태미나 |
| `idle_drain_per_sec` | `stamina.csv` → `idle_drain_per_sec` | 초당 기본 소모 |
| `move_drain_per_unit` | `stamina.csv` → `move_drain_per_unit` | 원작형 규칙에서는 0으로 고정한 미사용 호환 필드 |
| `hit_drain_per_swing` | `stamina.csv` → `hit_drain_per_swing` | 원작형 규칙에서는 0으로 고정한 미사용 호환 필드 |

목록을 늘리려면 `BalanceData.cs` 의 `StatId` enum 에 항목을 추가하고 이 표에 한 줄 적는다. **enum 에 없는 이름은 CSV에 적어도 임포트가 거부한다** — 오타가 런타임까지 가지 않게 하려는 의도다.

> 효과값은 `base × (1 + percent 합 / 100) + add 합`으로 계산하며 해당 레벨까지 누적한다.
> 비용은 `ceil(InitCost × CostGrowth^현재레벨)`이고 CostGrowth가 0이면 EconomyConfig 값을 쓴다.
> 구매는 메뉴/결과에서만 허용하고 다음 런에 반영한다. 직렬화된 BalanceData 원본을 변경하지 않는다.
> 단계 목표는 StageDef.GoalCoin이 우선이며 StageGoalGrowth는 새 단계값 산출 근거로만 쓴다.
> HitRadius 효과의 기준은 콜라이더 원래 반경이 아니라 CSV의 기본 확대 비율이다.

### 이름만 바꾸고 싶을 때

`upgrades.csv` 의 `display_name` 과 `description` 만 고치면 된다. 수치와 무관하므로 밸런스에 영향이 없다. 게임 톤을 바꾸고 싶으면 여기만 손대면 된다.

## 7. 파일별 담당

| CSV | 담당 | 비고 |
|---|---|---|
| `stamina.csv` | 코어 플레이 | 런 길이 = 하루 길이. 변경하면 청구서 수입 계산에 연쇄 영향 |
| `upgrade_effects.csv` | 성장·저장 | stat 이름은 6절 표에 있는 것만 사용 |
| `economy.csv`, `upgrades.csv` | 성장·저장 | |
| `bills.csv` | 성장·저장 | 마감일·대출 계수. 유효 범위만 임포터가 검사, 납부 가능성은 실측 |
| `fever.csv` | 피버·보너스 | |
| `targets.csv`, `stages.csv` | PM·기획 | 대상 구성이 평균 코인 배율을 바꿔 3절 계산에 영향 |

### 코인 지급 모델 (변경됨)

**코인은 타격마다가 아니라 대상이 파괴될 때 한 번에 지급한다.**

```
파괴 시 지급 = (hp × coin_mult + break_bonus) × 피버 배율 × 보너스 배율
```

`coin_mult` 는 이제 **타격당 배율이 아니라 내구도 1당 배율**이다. 총량은 타격마다 주던 때와 같지만,
**끝까지 못 부순 대상은 0원**이라는 점이 다르다. 내구도가 곧 투자 시간이 된다.

### `due_days` 가 두 파일에 있는 이유

`bills.csv` 와 `stages.csv` 양쪽에 `due_days` 가 있다. **단계값이 우선이고 `bills.csv` 는 기본값이다**
(StageDef.DueDays가 0일 때만 BillConfig.DueDays를 쓴다). 음수는 임포트 오류다.

**모든 수치는 작업 7.2(밸런싱)에서 실측으로 검증한다.** 위 모델은 독립적인 조준 확률을 가정한다. 실제 플레이 로그와 다르면 모델의 가정을 먼저 고친다.
