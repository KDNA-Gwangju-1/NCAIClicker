"""CSV 기반 첫 런 추정. 실제 이동·조준·게임 구현의 검증을 대신하지 않는다."""
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


def simulate(root, runs, seed, uptime, policy):
    stamina = read_config(root, "stamina.csv")
    economy = read_config(root, "economy.csv")
    fever = read_config(root, "fever.csv")
    stage = next(row for row in read_rows(root, "stages.csv") if row["stage"] == "1")
    if economy["auto_hammer_count_init"] != 0:
        raise ValueError("This baseline model only supports an initial auto-hammer count of zero")
    targets = read_rows(root, "targets.csv")
    coins = read_rows(root, "coins.csv")
    for target in targets:
        value = expected_coin_value(coins, target["min_denom_id"], int(float(target["coin_count"])))
        target["expected_value"] = value
        target["expected_value_per_hp"] = value / float(target["hp"])
    # 종류별 가중치는 stage_spawns.csv (#293). 행이 없는 종류는 그 단계에 나오지 않는다.
    ratios = {row["target_id"]: float(row["ratio"])
              for row in read_rows(root, "stage_spawns.csv") if row["stage"] == stage["stage"]}
    targets = [target for target in targets if ratios.get(target["id"], 0.0) > 0.0]
    weights = [ratios[target["id"]] for target in targets]
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
            selected["hp"] -= economy["base_hit_power"]
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
        "runs": runs, "seed": seed, "hover_uptime": uptime, "policy": policy,
        "mean_coin": round(statistics.mean(coins_sorted), 2),
        "p10_coin": round(coins_sorted[int((runs - 1) * 0.1)], 2),
        "median_coin": round(statistics.median(coins_sorted), 2),
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


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runs", type=int, default=1000)
    parser.add_argument("--seed", type=int, default=46)
    parser.add_argument("--uptime", type=float, default=0.6)
    parser.add_argument("--policy", choices=["random", "value"], default="random")
    args = parser.parse_args()
    if args.runs <= 0 or not 0 <= args.uptime <= 1:
        parser.error("runs must be positive and uptime must be between 0 and 1")
    root = Path(__file__).resolve().parents[2] / "Assets/GameData/Balance"
    print(json.dumps(simulate(root, args.runs, args.seed, args.uptime, args.policy), indent=2))
