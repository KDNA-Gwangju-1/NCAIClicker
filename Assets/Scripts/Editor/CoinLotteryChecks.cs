using System;
using System.Collections.Generic;
using NCAIClicker.Data;
using NCAIClicker.Economy;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 코인 액면 추첨(CoinLottery) 검증 (이슈 #178).
    /// 순수 정적 로직이라 실제 coins.csv 를 로드하지 않고, 제어하기 쉬운 작은 표를 직접 만들어
    /// 검증한다 — 밸런스 수치(coins.csv 값)가 나중에 바뀌어도 이 검증은 깨지지 않는다.
    /// </summary>
    public static class CoinLotteryChecks
    {
        public static void RunBatch()
        {
            var checkCount = 0;
            checkCount += RunEdgeCaseChecks();
            checkCount += RunFilterChecks();
            checkCount += RunDistributionChecks();
            checkCount += RunAggregationChecks();
            checkCount += RunRewardBandChecks();

            Debug.Log("[CoinLotteryChecks] PASS " + checkCount + " checks.");
        }

        private static BalanceData MakeBalance(params (string id, int value, int weight)[] coins)
        {
            var data = ScriptableObject.CreateInstance<BalanceData>();
            data.hideFlags = HideFlags.HideAndDontSave;
            data.Coins = new List<CoinDef>();
            foreach (var c in coins)
            {
                data.Coins.Add(new CoinDef { Id = c.id, Value = c.value, Weight = c.weight, DisplayColor = "#FFFFFF" });
            }
            return data;
        }

        private static int RunEdgeCaseChecks()
        {
            var checkCount = 0;
            var balance = MakeBalance(("c1", 1, 60), ("c5", 5, 25));

            try
            {
                AssertCondition(CoinLottery.Draw(null, "c1", 3, () => 0.5).Count == 0,
                    "balanceData 가 null 이면 빈 목록이어야 합니다.");
                checkCount++;

                AssertCondition(CoinLottery.Draw(balance, "c1", 0, () => 0.5).Count == 0,
                    "count 가 0 이면 빈 목록이어야 합니다.");
                AssertCondition(CoinLottery.Draw(balance, "c1", -1, () => 0.5).Count == 0,
                    "count 가 음수면 빈 목록이어야 합니다.");
                checkCount++;

                // min_denom_id 가 coins.csv 에 없는 id 면 GetCoin 이 null 을 돌려주고, 필터가
                // 걸리지 않아 전체 풀에서 뽑힌다 (임포터가 이 경우를 미리 막지만, 함수 자체는
                // 방어적으로 죽지 않고 전체 풀로 진행해야 한다).
                AssertCondition(CoinLottery.Draw(balance, "없는id", 3, () => 0.5).Count > 0,
                    "min_denom_id 를 못 찾아도 예외 없이 전체 풀에서 뽑혀야 합니다.");
                checkCount++;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(balance);
            }

            return checkCount;
        }

        private static int RunFilterChecks()
        {
            var checkCount = 0;
            // value 오름차순이 아니게 일부러 섞어 둔다 — min_denom 필터가 파일 순서가 아니라
            // value 크기로 비교하는지 확인하기 위해서다.
            var balance = MakeBalance(("c25", 25, 1), ("c1", 1, 1), ("c100", 100, 1), ("c5", 5, 1));

            try
            {
                // min_denom_id 를 c25 로 두면 c1·c5 는 후보에서 빠져야 한다.
                var drops = CoinLottery.Draw(balance, "c25", 100, () => 0.999999);
                var seenBelow = false;
                foreach (var d in drops)
                {
                    var coin = balance.GetCoin(d.DenomId);
                    if (coin.Value < 25)
                    {
                        seenBelow = true;
                    }
                }
                AssertCondition(!seenBelow, "min_denom_id(c25) 미만 액면이 뽑혔습니다.");
                checkCount++;

                // weight 0 인 액면은 절대 뽑히지 않아야 한다.
                var withDead = MakeBalance(("c1", 1, 0), ("c5", 5, 1));
                try
                {
                    for (var roll = 0.0; roll < 1.0; roll += 0.05)
                    {
                        var r = roll;
                        var d = CoinLottery.Draw(withDead, "c1", 1, () => r);
                        AssertCondition(d.Count == 1 && d[0].DenomId == "c5",
                            "weight 0 인 c1 이 뽑혔습니다 (roll=" + r + ").");
                    }
                    checkCount++;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(withDead);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(balance);
            }

            return checkCount;
        }

        private static int RunDistributionChecks()
        {
            var checkCount = 0;
            // weight 1:1 인 두 액면 — roll 이 [0, 0.5) 면 앞, [0.5, 1) 면 뒤가 나와야 한다.
            var balance = MakeBalance(("a", 10, 1), ("b", 20, 1));

            try
            {
                var low = CoinLottery.Draw(balance, "a", 1, () => 0.0);
                AssertCondition(low.Count == 1 && low[0].DenomId == "a", "roll 0.0 은 첫 후보(a) 를 뽑아야 합니다.");
                checkCount++;

                var justBelowHalf = CoinLottery.Draw(balance, "a", 1, () => 0.49999);
                AssertCondition(justBelowHalf.Count == 1 && justBelowHalf[0].DenomId == "a",
                    "roll 0.49999 는 여전히 a 여야 합니다.");
                checkCount++;

                var high = CoinLottery.Draw(balance, "a", 1, () => 0.5);
                AssertCondition(high.Count == 1 && high[0].DenomId == "b", "roll 0.5 는 두번째 후보(b) 를 뽑아야 합니다.");
                checkCount++;

                var top = CoinLottery.Draw(balance, "a", 1, () => 0.999999);
                AssertCondition(top.Count == 1 && top[0].DenomId == "b", "roll 이 1에 가까워도 b 여야 합니다.");
                checkCount++;

                // 기댓값 보존 확인 — 시드 고정 System.Random 으로 대량 추첨해 이론값과 비교한다
                // (재현 가능하므로 flaky 하지 않다). E[value] = (10*1 + 20*1) / 2 = 15.
                var rng = new System.Random(12345);
                const int trials = 20000;
                double sum = 0;
                for (var i = 0; i < trials; i++)
                {
                    var drop = CoinLottery.Draw(balance, "a", 1, rng.NextDouble);
                    sum += (double)CoinLottery.SumValue(drop, balance);
                }
                var mean = sum / trials;
                AssertCondition(Math.Abs(mean - 15.0) < 0.3,
                    "대량 추첨 평균이 " + mean.ToString("0.###") + " 입니다. 이론값 15 근처(±0.3)여야 합니다.");
                checkCount++;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(balance);
            }

            return checkCount;
        }

        private static int RunAggregationChecks()
        {
            var checkCount = 0;
            var balance = MakeBalance(("c1", 1, 1), ("c5", 5, 1));

            try
            {
                // 항상 첫 후보(c1)만 뽑히게 고정하고 5개를 뽑으면, 같은 액면은 CoinDrop 하나로 합쳐져야 한다.
                var drops = CoinLottery.Draw(balance, "c1", 5, () => 0.0);
                AssertCondition(drops.Count == 1, "같은 액면만 뽑혔는데 CoinDrop 이 " + drops.Count + "개로 나뉘었습니다.");
                AssertCondition(drops[0].DenomId == "c1" && drops[0].Count == 5,
                    "합산된 CoinDrop 이 c1×5 가 아닙니다.");
                checkCount++;

                AssertCondition(CoinLottery.SumValue(drops, balance) == 5m,
                    "SumValue 가 c1×5 의 합(5) 과 다릅니다.");
                checkCount++;

                // coins.csv 파일 순서(c1, c5)대로 목록이 나와야 한다 — 결과 화면이 이 순서로 칸을 채운다.
                var mixed = MakeBalance(("c1", 1, 1), ("c5", 5, 1));
                try
                {
                    // 절반은 두번째(c5), 절반은 첫번째(c1) 가 나오도록 번갈아 굴린다.
                    var toggle = false;
                    IReadOnlyList<CoinDrop> mixedDrops = CoinLottery.Draw(mixed, "c1", 4, () =>
                    {
                        toggle = !toggle;
                        return toggle ? 0.9 : 0.1;
                    });
                    AssertCondition(mixedDrops.Count == 2, "두 액면이 섞였는데 CoinDrop 이 2개가 아닙니다.");
                    AssertCondition(mixedDrops[0].DenomId == "c1" && mixedDrops[1].DenomId == "c5",
                        "CoinDrop 순서가 coins.csv 순서(c1, c5)와 다릅니다.");
                    checkCount++;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(mixed);
                }

                AssertCondition(CoinLottery.SumValue(null, balance) == 0m, "drops 가 null 이면 합계는 0이어야 합니다.");
                AssertCondition(CoinLottery.SumValue(drops, null) == 0m, "balanceData 가 null 이면 합계는 0이어야 합니다.");
                checkCount++;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(balance);
            }

            return checkCount;
        }

        /// <summary>
        /// 해금 카드의 코인 구간 (이슈 #300). 식으로 구한 구간이 손으로 푼 값과 같은지, 그리고 실제 Draw 를
        /// 대량으로 돌린 결과(가장 큰 액면별 빈도·합계 범위)와 맞는지 본다 — 카드가 보여 주는 확률이 거짓이면 안 된다.
        /// </summary>
        private static int RunRewardBandChecks()
        {
            var checkCount = 0;

            // 손으로 푸는 표: a(1원, 가중치 3), b(10원, 가중치 1), 2개 뽑기.
            // 가장 큰 게 a → 둘 다 a: (3/4)² = 0.5625, 합계 2. 가장 큰 게 b → 1 − 0.5625, 합계 11(b+a)~20(b+b).
            // 일부러 값 내림차순으로 넣는다 — 구간은 값 오름차순이어야 한다.
            var small = MakeBalance(("b", 10, 1), ("a", 1, 3));
            // 다섯 단 액면 + 가중치 0 한 줄. 실제 coins.csv 를 읽지 않는다 — CSV 가 바뀌어도 이 표로만 검사한다.
            var fiveDenoms = MakeBalance(("c1", 1, 60), ("c5", 5, 25), ("c25", 25, 10), ("c100", 100, 4), ("c1000", 1000, 1), ("c0", 7, 0));
            try
            {
                var bands = CoinLottery.GetRewardBands(small, "a", 2);
                AssertCondition(bands.Count == 2, "구간이 2개여야 합니다: " + bands.Count);
                AssertCondition(bands[0].TopDenomId == "a" && bands[0].Min == 2 && bands[0].Max == 2
                                && Math.Abs(bands[0].Probability - 0.5625) < 1e-9,
                    "첫 구간은 a · $2 · 56.25% 여야 합니다: " + Describe(bands[0]));
                AssertCondition(bands[1].TopDenomId == "b" && bands[1].Min == 11 && bands[1].Max == 20
                                && Math.Abs(bands[1].Probability - 0.4375) < 1e-9,
                    "둘째 구간은 b · $11–$20 · 43.75% 여야 합니다: " + Describe(bands[1]));
                checkCount++;

                // 최소 액면 필터는 Draw 와 같다 — b 이상이면 b 하나뿐이고 확률 1, 합계는 3×10.
                var filtered = CoinLottery.GetRewardBands(small, "b", 3);
                AssertCondition(filtered.Count == 1 && filtered[0].Min == 30 && filtered[0].Max == 30
                                && Math.Abs(filtered[0].Probability - 1.0) < 1e-9,
                    "최소 액면 b 로 3개면 $30 한 구간·100% 여야 합니다.");
                checkCount++;

                AssertCondition(CoinLottery.GetRewardBands(null, "a", 2).Count == 0
                                && CoinLottery.GetRewardBands(small, "a", 0).Count == 0,
                    "balanceData 가 없거나 count 가 0 이면 빈 목록이어야 합니다.");
                AssertCondition(!ContainsDenom(CoinLottery.GetRewardBands(fiveDenoms, "c1", 3), "c0"),
                    "가중치 0 인 액면은 구간에 나오면 안 됩니다 (Draw 도 뽑지 않습니다).");
                checkCount++;

                // 실제 추첨과 대조 — 시드 고정이라 재현된다. 확률 합은 1, 빈도는 ±1.5%, 합계는 그 구간 안.
                var rng = new System.Random(300);
                const int trials = 20000;
                var realBands = CoinLottery.GetRewardBands(fiveDenoms, "c5", 3);
                var probabilitySum = 0.0;
                foreach (var band in realBands)
                {
                    probabilitySum += band.Probability;
                }
                AssertCondition(Math.Abs(probabilitySum - 1.0) < 1e-9, "구간 확률의 합이 1 이 아닙니다: " + probabilitySum);

                var hits = new Dictionary<string, int>();
                for (var i = 0; i < trials; i++)
                {
                    var drops = CoinLottery.Draw(fiveDenoms, "c5", 3, rng.NextDouble);
                    var top = FindTopDenom(drops, fiveDenoms);
                    var sum = (long)CoinLottery.SumValue(drops, fiveDenoms);
                    var band = FindBand(realBands, top);
                    AssertCondition(band.HasValue, "추첨에서 나온 가장 큰 액면 " + top + " 의 구간이 없습니다.");
                    AssertCondition(sum >= band.Value.Min && sum <= band.Value.Max,
                        "합계 " + sum + " 가 구간 " + Describe(band.Value) + " 밖입니다.");
                    hits.TryGetValue(top, out var n);
                    hits[top] = n + 1;
                }
                foreach (var band in realBands)
                {
                    hits.TryGetValue(band.TopDenomId, out var n);
                    var observed = (double)n / trials;
                    AssertCondition(Math.Abs(observed - band.Probability) < 0.015,
                        band.TopDenomId + " 구간 확률이 식 " + band.Probability.ToString("0.###") + ", 추첨 "
                        + observed.ToString("0.###") + " 로 1.5% 넘게 어긋납니다.");
                }
                checkCount++;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(small);
                UnityEngine.Object.DestroyImmediate(fiveDenoms);
            }

            return checkCount;
        }

        private static string FindTopDenom(IReadOnlyList<CoinDrop> drops, BalanceData balance)
        {
            string top = null;
            var topValue = int.MinValue;
            foreach (var drop in drops)
            {
                var coin = balance.GetCoin(drop.DenomId);
                if (coin != null && coin.Value > topValue)
                {
                    topValue = coin.Value;
                    top = coin.Id;
                }
            }
            return top;
        }

        private static CoinRewardBand? FindBand(IReadOnlyList<CoinRewardBand> bands, string denomId)
        {
            foreach (var band in bands)
            {
                if (band.TopDenomId == denomId)
                {
                    return band;
                }
            }
            return null;
        }

        private static bool ContainsDenom(IReadOnlyList<CoinRewardBand> bands, string denomId)
        {
            return FindBand(bands, denomId).HasValue;
        }

        private static string Describe(CoinRewardBand band)
        {
            return band.TopDenomId + " $" + band.Min + "–$" + band.Max + " " + band.Probability.ToString("0.####");
        }

        private static void AssertCondition(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("[CoinLotteryChecks] " + message);
            }
        }
    }
}
