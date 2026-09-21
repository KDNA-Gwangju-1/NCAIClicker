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


def simulate(root, runs, seed, uptime, policy):
    stamina = read_config(root, "stamina.csv")
    economy = read_config(root, "economy.csv")
    fever = read_config(root, "fever.csv")
    stage = next(row for row in read_rows(root, "stages.csv") if row["stage"] == "1")
    if economy["auto_hammer_count_init"] != 0:
        raise ValueError("This baseline model only supports an initial auto-hammer count of zero")
    targets = read_rows(root, "targets.csv")
    weights = [float(stage[target["id"] + "_ratio"]) for target in targets]
    rng = random.Random(seed)
    results = []
    for _ in range(runs):
        def spawn():
            target = rng.choices(targets, weights=weights)[0]
            return {"target": target, "hp": float(target["hp"]), "ready": 0.0}

        slots = [spawn() for _ in range(int(stage["spawn_count"]))]
        energy, elapsed, coin, gauge = stamina["max_stamina"], 0.0, 0.0, 0.0
        last_hit, fever_end = -float("inf"), 0.0
        selected, breaks, restorations, fevers = None, 0, 0, 0
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
            for index, slot in enumerate(slots):
                if slot["hp"] <= 0 and elapsed >= slot["ready"]:
                    slots[index] = spawn()
            if selected is None or slots[selected]["hp"] <= 0:
                available = [i for i, slot in enumerate(slots) if slot["hp"] > 0]
                if not available:
                    selected = None
                    continue
                if policy == "random":
                    selected = rng.choice(available)
                else:
                    selected = max(available, key=lambda i: float(slots[i]["target"]["coin_mult"]) +
                                   float(slots[i]["target"]["break_bonus"]) / float(slots[i]["target"]["hp"]))
            if rng.random() >= uptime:
                continue
            last_hit = elapsed
            if elapsed >= fever_end:
                gauge += fever["gauge_per_hit"]
                if gauge >= fever["gauge_max"]:
                    fever_end, gauge = elapsed + fever["duration_sec"], 0.0
                    fevers += 1
            slot = slots[selected]
            slot["hp"] -= economy["base_hit_power"]
            if slot["hp"] > 0:
                continue
            target = slot["target"]
            multiplier = fever["coin_multiplier"] if elapsed < fever_end else 1.0
            coin += (float(target["hp"]) * float(target["coin_mult"]) + float(target["break_bonus"])) * multiplier * economy["coin_bonus_multiplier"]
            restore = float(target["stamina_restore"])
            energy = min(stamina["max_stamina"], energy + restore)
            restorations += int(restore > 0)
            breaks += 1
            slot["ready"] = elapsed + economy["spawn_interval_sec"]
            selected = None
        results.append((coin, elapsed, breaks, restorations, elapsed >= cap, fevers))
    coins = sorted(row[0] for row in results)
    return {
        "runs": runs, "seed": seed, "hover_uptime": uptime, "policy": policy,
        "mean_coin": round(statistics.mean(coins), 2),
        "p10_coin": round(coins[int((runs - 1) * 0.1)], 2),
        "median_coin": round(statistics.median(coins), 2),
        "p90_coin": round(coins[int((runs - 1) * 0.9)], 2),
        "mean_seconds": round(statistics.mean(row[1] for row in results), 2),
        "mean_breaks": round(statistics.mean(row[2] for row in results), 2),
        "mean_restorations": round(statistics.mean(row[3] for row in results), 2),
        "mean_fevers": round(statistics.mean(row[5] for row in results), 2),
        "min_fevers": min(row[5] for row in results),
        "max_fevers": max(row[5] for row in results),
        "goal_reached_percent": round(100 * sum(c >= int(stage["bill_amount"]) for c in coins) / runs, 1),
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
