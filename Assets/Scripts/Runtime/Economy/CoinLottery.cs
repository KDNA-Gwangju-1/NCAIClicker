using System;
using System.Collections.Generic;
using NCAIClicker.Data;

namespace NCAIClicker.Economy
{
    /// <summary>
    /// coins.csv 가중치 표에서 파괴 보상 액면을 추첨한다 (이슈 #178).
    /// 순수 정적 로직이라 Edit Mode 에서 프리팹·MonoBehaviour 없이 검증할 수 있다
    /// (docs/TECH_NOTES/coin-economy.md 의 CoinWallet 과 같은 이유).
    ///
    /// 파괴마다 새로 뽑아야 "같은 저금통을 부숴도 무엇이 나올지 다르다"가 성립한다 — 그래서
    /// 이 호출은 스폰 시(Target.Initialize)가 아니라 파괴 시(Target.OnHit)에서 이뤄진다.
    /// </summary>
    public static class CoinLottery
    {
        /// <summary>
        /// minDenomId 액면의 value 이상인 액면만 후보로 놓고, weight 비례로 count 개를 뽑는다.
        /// nextDouble 은 [0,1) 구간의 난수를 반환하는 함수다 — 실제 플레이에서는 공유 System.Random,
        /// 검증에서는 시드 고정 System.Random 을 넣어 재현 가능하게 한다.
        /// 후보가 없거나(모두 min 미만) count 가 0 이하이면 빈 목록을 돌려준다 — 예외를 던지지 않는다.
        /// 같은 액면이 여러 번 뽑히면 한 CoinDrop 으로 합친다.
        /// </summary>
        public static IReadOnlyList<CoinDrop> Draw(BalanceData balanceData, string minDenomId, int count,
            Func<double> nextDouble)
        {
            var drops = new List<CoinDrop>();
            if (balanceData == null || nextDouble == null || count <= 0)
            {
                return drops;
            }

            var minDenom = balanceData.GetCoin(minDenomId);
            var pool = new List<CoinDef>();
            var totalWeight = 0;
            foreach (var coin in balanceData.Coins)
            {
                if (coin.Weight <= 0)
                {
                    continue;
                }
                if (minDenom != null && coin.Value < minDenom.Value)
                {
                    continue;
                }
                pool.Add(coin);
                totalWeight += coin.Weight;
            }

            if (pool.Count == 0 || totalWeight <= 0)
            {
                return drops;
            }

            var counts = new Dictionary<string, int>();
            for (var i = 0; i < count; i++)
            {
                var picked = PickOne(pool, totalWeight, nextDouble());
                counts.TryGetValue(picked.Id, out var existing);
                counts[picked.Id] = existing + 1;
            }

            // coins.csv 순서를 유지한다 — 결과 화면이 이 순서로 액면 칸을 채운다.
            foreach (var coin in balanceData.Coins)
            {
                if (counts.TryGetValue(coin.Id, out var n))
                {
                    drops.Add(new CoinDrop(coin.Id, n));
                }
            }
            return drops;
        }

        /// <summary>
        /// 한 번 부술 때 기대 금액 = count × (후보 액면의 가중 평균). 후보 규칙은 Draw 와 같다.
        /// 도감이 "기대 코인"으로 보여 준다 (#299). 즉시 파괴·분노 같은 역할 효과는 넣지 않는다.
        /// </summary>
        public static decimal GetExpectedValue(BalanceData balanceData, string minDenomId, int count)
        {
            if (balanceData == null || count <= 0)
            {
                return 0m;
            }

            var minDenom = balanceData.GetCoin(minDenomId);
            var weighted = 0m;
            var totalWeight = 0;
            foreach (var coin in balanceData.Coins)
            {
                if (coin.Weight <= 0 || (minDenom != null && coin.Value < minDenom.Value))
                {
                    continue;
                }
                weighted += (decimal)coin.Value * coin.Weight;
                totalWeight += coin.Weight;
            }
            return totalWeight > 0 ? count * weighted / totalWeight : 0m;
        }

        /// <summary>드롭 내역의 합계 금액. BreakInfo.RawCoin 에 그대로 들어간다.</summary>
        public static decimal SumValue(IReadOnlyList<CoinDrop> drops, BalanceData balanceData)
        {
            var sum = 0m;
            if (drops == null || balanceData == null)
            {
                return sum;
            }
            foreach (var drop in drops)
            {
                var coin = balanceData.GetCoin(drop.DenomId);
                if (coin == null)
                {
                    continue;
                }
                sum += (decimal)coin.Value * drop.Count;
            }
            return sum;
        }

        /// <summary>
        /// Draw 가 낼 수 있는 합계를 "뽑힌 것 중 가장 큰 액면" 으로 묶은 구간들 (이슈 #300 해금 카드).
        /// 후보(Draw 와 같은 필터)를 값 오름차순 v0 &lt; v1 &lt; … 로 놓고 가중치 합을 W 라 하면,
        /// 가장 큰 액면이 vi 일 확률은 (vi 이하 가중치 / W)^count − (vi 미만 가중치 / W)^count 이고,
        /// 그때 합계는 vi + (count−1)·v0 이상 count·vi 이하다. 추첨을 흉내 내지 않고 식으로 정확히 구한다.
        /// 값 오름차순으로 돌려준다. 후보가 없거나 count 가 0 이하이면 빈 목록이다.
        /// </summary>
        public static IReadOnlyList<CoinRewardBand> GetRewardBands(BalanceData balanceData, string minDenomId, int count)
        {
            var bands = new List<CoinRewardBand>();
            if (balanceData == null || count <= 0)
            {
                return bands;
            }

            var minDenom = balanceData.GetCoin(minDenomId);
            var pool = new List<CoinDef>();
            var totalWeight = 0;
            foreach (var coin in balanceData.Coins)
            {
                if (coin.Weight <= 0)
                {
                    continue;
                }
                if (minDenom != null && coin.Value < minDenom.Value)
                {
                    continue;
                }
                pool.Add(coin);
                totalWeight += coin.Weight;
            }
            if (pool.Count == 0 || totalWeight <= 0)
            {
                return bands;
            }

            pool.Sort((a, b) => a.Value.CompareTo(b.Value));
            var lowest = pool[0].Value;
            var weightBelow = 0;
            foreach (var coin in pool)
            {
                var weightUpTo = weightBelow + coin.Weight;
                var probability = Math.Pow((double)weightUpTo / totalWeight, count)
                                  - Math.Pow((double)weightBelow / totalWeight, count);
                bands.Add(new CoinRewardBand(coin.Id, coin.Value + (long)(count - 1) * lowest, (long)count * coin.Value,
                    probability));
                weightBelow = weightUpTo;
            }
            return bands;
        }

        private static CoinDef PickOne(List<CoinDef> pool, int totalWeight, double roll)
        {
            var target = roll * totalWeight;
            var acc = 0d;
            for (var i = 0; i < pool.Count; i++)
            {
                acc += pool[i].Weight;
                if (target < acc)
                {
                    return pool[i];
                }
            }
            // 부동소수 오차로 끝까지 못 넘기면 마지막 후보를 준다 — 후보 하나를 반드시 돌려준다.
            return pool[pool.Count - 1];
        }
    }
}
