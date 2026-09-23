"""CSV 기반 런 추정 (업그레이드 없음, 단계는 --stage). 실제 이동·조준·게임 구현의 검증을 대신하지 않는다."""
import argparse
import csv
import json
import random
import statistics
from pathlib import Path


def read_rows(root, name):
    with (root / name).open(encoding="utf-8-sig", newline="") as source:
        return list(csv.DictReader(source))


def read_config(root, name):
    return {row["key"]: float(row["value"]) for row in read_rows(root, name)}


def draw_coin_lottery(coins, min_denom_id, count, rng):
    """`coins.csv` 가중치 표에서 `min_denom_id` 액면의 value 이상인 액면만 후보로 놓고,
    weight 비례로 count 개를 뽑아 합계를 돌려준다. `CoinLottery.Draw` + `SumValue`
    (Assets/Scripts/Runtime/Economy/CoinLottery.cs, 이슈 #178)를 그대로 옮긴 것이다 —
    두 구현이 갈리면 이 도구의 추정이 실제 게임과 어긋난다."""
    if count <= 0:
        return 0.0
    min_value = None
    for coin in coins:
        if coin["id"] == min_denom_id:
            min_value = float(coin["value"])
            break
    pool = []
    total_weight = 0.0
    for coin in coins:
        weight = float(coin["weight"])
        if weight <= 0:
            continue
        if min_value is not None and float(coin["value"]) < min_value:
            continue
        pool.append((float(coin["value"]), weight))
        total_weight += weight
    if not pool or total_weight <= 0:
        return 0.0
    total = 0.0
    for _ in range(count):
        roll = rng.random() * total_weight
        acc = 0.0
        picked_value = pool[-1][0]
        for value, weight in pool:
            acc += weight
            if roll < acc:
                picked_value = value
                break
        total += picked_value
    return total


def expected_coin_value(coins, min_denom_id, count):
    """타겟의 기대 지급액. `value` 정책이 이제 죽은 열(coin_mult·break_bonus) 대신
    실제 지급을 결정하는 coin_count·min_denom_id 로 목표를 고르게 한다."""
    min_value = None
    for coin in coins:
        if coin["id"] == min_denom_id:
            min_value = float(coin["value"])
            break
    total_weight, weighted_value = 0.0, 0.0
    for coin in coins:
        weight = float(coin["weight"])
        if weight <= 0:
            continue
        if min_value is not None and float(coin["value"]) < min_value:
            continue
        total_weight += weight
        weighted_value += weight * float(coin["value"])
    if total_weight <= 0:
        return 0.0
    return count * weighted_value / total_weight


def simulate(root, runs, seed, uptime, policy, stage_number=1, hit_power=None, earned=0):
    stamina = read_config(root, "stamina.csv")
    economy = read_config(root, "economy.csv")
    fever = read_config(root, "fever.csv")
    stage = next(row for row in read_rows(root, "stages.csv") if row["stage"] == str(stage_number))
    if economy["auto_hammer_count_init"] != 0:
        raise ValueError("This baseline model only supports an initial auto-hammer count of zero")
    # 후반 단계는 업그레이드를 산 상태라 첫 런 파워로는 고HP 종류를 못 부순다. 가정 파워를 따로 받는다 (#247).
    power = economy["base_hit_power"] if hit_power is None else hit_power
    targets = read_rows(root, "targets.csv")
    coins = read_rows(root, "coins.csv")
    for target in targets:
        value = expected_coin_value(coins, target["min_denom_id"], int(float(target["coin_count"])))
        target["expected_value"] = value
        # value 정책은 "타격당 기대값"으로 고른다. 파워로 몇 번 쳐야 부서지는지 반영해야 고HP 종류만
        # 쫓다가 런이 끝나는 왜곡이 없다. 즉시 파괴 확률도 기대 타격 수를 줄인다.
        hits = max(1.0, -(-float(target["hp"]) // power))
        chance = float(target.get("instant_break_chance") or 0.0)
        if chance > 0.0:
            hits = (1 - (1 - chance) ** hits) / chance
        target["expected_value_per_hp"] = value / hits
    # 해금은 회차 누적 수입 기준이다 (#301). spawn_weight 가 0 이면 해금 목록에도 없다.
    targets = [target for target in targets
               if float(target["spawn_weight"]) > 0.0 and earned >= int(float(target["unlock_earned"]))]
    weights = [float(target["spawn_weight"]) for target in targets]
    extra_spawn_chance = economy["extra_spawn_chance_on_destroy"]
    rng = random.Random(seed)
    results = []
    for _ in range(runs):
        spawn_id = 0

        def spawn():
            nonlocal spawn_id
            spawn_id += 1
            target = rng.choices(targets, weights=weights)[0]
            return {"id": spawn_id, "target": target, "hp": float(target["hp"])}

        # 슬롯 타이머 모델은 폐기했다 (#156 B안). 파괴된 자리는 자동으로 채워지지 않고,
        # ① 파괴할 때마다 extra_spawn_chance_on_destroy 확률로 즉시 1개가 추가되거나,
        # ② 책상 위가 완전히 비면(0마리) 즉시 1개만 채워진다. 원작 재관찰 근거는
        # REFERENCE_ANALYSIS.md 9절.
        active = [spawn() for _ in range(int(stage["spawn_count"]))]
        energy, elapsed, coin, gauge = stamina["max_stamina"], 0.0, 0.0, 0.0
        last_hit, fever_end = -float("inf"), 0.0
        selected_id, breaks, restorations, fevers = None, 0, 0, 0
        interval = economy["hover_swing_interval_sec"]
        # 모델의 무한 반복 방지용이다. 게임에 시간 상한을 추가하지 않는다.
        cap = stamina["max_stamina"] / stamina["idle_drain_per_sec"] * 10
        while energy > 0 and elapsed < cap:
            elapsed += interval
            energy -= stamina["idle_drain_per_sec"] * interval
            if energy <= 0:
                break
            if elapsed > last_hit + fever["decay_grace_sec"] and elapsed >= fever_end:
                gauge = max(0.0, gauge - fever["gauge_decay_per_sec"] * interval)
            if selected_id is None or not any(s["id"] == selected_id for s in active):
                if not active:
                    selected_id = None
                    continue
                if policy == "random":
                    chosen = rng.choice(active)
                else:
                    # 기대 지급액을 내구도로 나눈 "타격당 기대값"이 가장 큰 목표 종류를 고른다.
                    # coin_mult·break_bonus 는 지급액에 관여하지 않는 죽은 열이라 더는 쓰지 않는다
                    # (이슈 #217, Target.OnHit 확인 완료).
                    chosen = max(active, key=lambda s: s["target"]["expected_value_per_hp"])
                selected_id = chosen["id"]
            if rng.random() >= uptime:
                continue
            last_hit = elapsed
            if elapsed >= fever_end:
                gauge += fever["gauge_per_hit"]
                if gauge >= fever["gauge_max"]:
                    fever_end, gauge = elapsed + fever["duration_sec"], 0.0
                    fevers += 1
            selected = next(s for s in active if s["id"] == selected_id)
            selected["hp"] -= power
            # 즉시 파괴 (#293). 확률 0 인 종류는 난수를 뽑지 않아 기존 시드 결과가 유지된다.
            chance = float(selected["target"].get("instant_break_chance") or 0.0)
            if selected["hp"] > 0 and chance > 0.0 and rng.random() < chance:
                selected["hp"] = 0.0
            if selected["hp"] > 0:
                continue
            target = selected["target"]
            multiplier = fever["coin_multiplier"] if elapsed < fever_end else 1.0
            # 파괴 보상은 coin_mult·break_bonus 가 아니라 coins.csv 액면 추첨의 합이다
            # (이슈 #178). Target.OnHit 은 이 두 열을 더는 읽지 않는다 — CoinLottery.Draw
            # 만 본다. 이 두 열은 현재 지급액에 영향이 없는 죽은 필드다.
            raw_coin = draw_coin_lottery(coins, target["min_denom_id"], int(float(target["coin_count"])), rng)
            coin += raw_coin * multiplier * economy["coin_bonus_multiplier"]
            restore = float(target["stamina_restore"])
            energy = min(stamina["max_stamina"], energy + restore)
            restorations += int(restore > 0)
            breaks += 1
            active.remove(selected)
            if rng.random() * 100 < extra_spawn_chance:
                active.append(spawn())
            if not active:
                active.append(spawn())
            selected_id = None
        results.append((coin, elapsed, breaks, restorations, elapsed >= cap, fevers))
    coins_sorted = sorted(row[0] for row in results)
    return {
        "stage": stage_number, "earned": earned, "hit_power": power, "runs": runs, "seed": seed, "hover_uptime": uptime, "policy": policy,
        "mean_coin": round(statistics.mean(coins_sorted), 2),
        "p10_coin": round(coins_sorted[int((runs - 1) * 0.1)], 2),
        "median_coin": round(statistics.median(coins_sorted), 2),
        "p60_coin": round(coins_sorted[int((runs - 1) * 0.6)], 2),
        "p90_coin": round(coins_sorted[int((runs - 1) * 0.9)], 2),
        "mean_seconds": round(statistics.mean(row[1] for row in results), 2),
        "mean_breaks": round(statistics.mean(row[2] for row in results), 2),
        "mean_restorations": round(statistics.mean(row[3] for row in results), 2),
        "mean_fevers": round(statistics.mean(row[5] for row in results), 2),
        "min_fevers": min(row[5] for row in results),
        "max_fevers": max(row[5] for row in results),
        "goal_reached_percent": round(100 * sum(c >= int(stage["bill_amount"]) for c in coins_sorted) / runs, 1),
        "capped_runs": sum(row[4] for row in results),
    }


def simulate_days(root, players, days, seed, uptime, policy, power_start, power_per_day):
    """새 회차를 날짜 순으로 이어 돌린다 (#301). 매 정산에 순수입을 누적하고, 기준액을 넘은 종류를
    다음 날부터 섞는다. 종류별 해금일(그 정산이 있었던 날) 분포를 돌려준다.
    파워는 날마다 power_per_day 씩 오른다고 가정한다 — 업그레이드 구매 속도의 대용이다."""
    targets = [row for row in read_rows(root, "targets.csv") if float(row["spawn_weight"]) > 0.0]
    thresholds = {row["id"]: int(float(row["unlock_earned"])) for row in targets}
    unlock_days = {target_id: [] for target_id, value in thresholds.items() if value > 0}
    earned_by_day = [[] for _ in range(days)]
    for player in range(players):
        earned = 0
        for day in range(1, days + 1):
            power = power_start + power_per_day * (day - 1)
            result = simulate(root, 1, seed * 100003 + player * 997 + day, uptime, policy,
                              1, power, earned)
            before = earned
            earned += int(result["mean_coin"])
            earned_by_day[day - 1].append(earned)
            for target_id, value in thresholds.items():
                if value > 0 and before < value <= earned:
                    unlock_days[target_id].append(day)
    summary = {}
    for target_id, found in unlock_days.items():
        found.sort()
        reached = len(found)
        summary[target_id] = {
            "unlock_earned": thresholds[target_id],
            "reached_percent": round(100 * reached / players, 1),
            "p25_day": found[int((reached - 1) * 0.25)] if reached else None,
            "median_day": found[int((reached - 1) * 0.5)] if reached else None,
            "p75_day": found[int((reached - 1) * 0.75)] if reached else None,
        }
    median_earned = [sorted(values)[len(values) // 2] for values in earned_by_day]
    return {"players": players, "days": days, "power_start": power_start, "power_per_day": power_per_day,
            "policy": policy, "unlock": summary, "median_earned_by_day": median_earned}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runs", type=int, default=1000)
    parser.add_argument("--seed", type=int, default=46)
    parser.add_argument("--uptime", type=float, default=0.6)
    parser.add_argument("--policy", choices=["random", "value"], default="random")
    parser.add_argument("--stage", type=int, default=1, help="stages.csv 의 단계 번호 (#247)")
    parser.add_argument("--hit-power", type=float, default=None,
                        help="가정 타격 파워. 생략하면 economy.csv base_hit_power (업그레이드 없음)")
    parser.add_argument("--earned", type=int, default=0,
                        help="회차 누적 수입 (#301). 이 값으로 해금된 종류만 나온다")
    parser.add_argument("--days", type=int, default=0,
                        help="0 보다 크면 새 회차를 이 날수만큼 이어 돌려 종류별 해금일을 낸다 (#301). --runs 는 플레이어 수")
    parser.add_argument("--power-per-day", type=float, default=0.35,
                        help="--days 모드에서 하루마다 오르는 가정 파워 (완력 단련 1레벨 = 0.35)")
    args = parser.parse_args()
    if args.runs <= 0 or not 0 <= args.uptime <= 1:
        parser.error("runs must be positive and uptime must be between 0 and 1")
    root = Path(__file__).resolve().parents[2] / "Assets/GameData/Balance"
    if args.days > 0:
        start = 1.0 if args.hit_power is None else args.hit_power
        print(json.dumps(simulate_days(root, args.runs, args.days, args.seed, args.uptime, args.policy,
                                       start, args.power_per_day), indent=2, ensure_ascii=False))
    else:
        print(json.dumps(simulate(root, args.runs, args.seed, args.uptime, args.policy, args.stage,
                                  args.hit_power, args.earned), indent=2))
