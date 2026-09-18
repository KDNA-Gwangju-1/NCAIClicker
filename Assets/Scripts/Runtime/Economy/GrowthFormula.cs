using System;
using NCAIClicker.Data;

namespace NCAIClicker.Economy
{
    /// <summary>
    /// 성장 계산의 **공식만** 모아 둔다. 레벨을 들고 있지 않고 상태도 없다.
    ///
    /// 업그레이드(회차 성장)와 반지(영구 성장)는 화폐도 저장 수명도 다르지만 **계산 규칙은 같다.**
    /// 규칙을 양쪽에 따로 적어 두면 한쪽만 고쳐졌을 때 서로 다른 답을 내놓고, 어느 쪽이 맞는지
    /// 알 수 없게 된다. 그래서 규칙은 여기 한 벌만 둔다.
    ///
    /// 정본은 docs/BALANCE.md 6절이다.
    ///   실효값 = 기준값 × (1 + percent 합 / 100) + add 합
    ///   비용   = ceil(InitCost × CostGrowth^현재레벨)
    /// </summary>
    public static class GrowthFormula
    {
        /// <summary>
        /// 누적한 add·percent 를 기준값에 얹는다.
        /// **percent 를 먼저 곱하고 add 를 더한다** — 순서를 바꾸면 더한 값에도 비율이 걸린다.
        /// </summary>
        public static float Compose(float baseValue, float addSum, float percentSum)
        {
            return baseValue * (1f + percentSum / 100f) + addSum;
        }

        /// <summary>
        /// 다음 레벨 비용. 화폐 단위는 부르는 쪽이 정한다 — 업그레이드는 코인, 반지는 레거시 포인트다.
        /// </summary>
        public static long NextCost(long initCost, float growth, int currentLevel)
        {
            var raw = initCost * Math.Pow(growth, currentLevel);
            return (long)Math.Ceiling(raw);
        }

        /// <summary>
        /// 효과 목록에서 <paramref name="stat"/> 에 해당하는 것만 골라 레벨만큼 누적한다.
        /// 레벨당 값이므로 보유 레벨을 곱한다.
        /// </summary>
        public static void Accumulate(System.Collections.Generic.List<UpgradeEffect> effects,
                                      int level, StatId stat, ref float addSum, ref float percentSum)
        {
            if (effects == null || level <= 0)
            {
                return;
            }

            foreach (var effect in effects)
            {
                if (effect.Stat != stat)
                {
                    continue;
                }

                if (effect.Type == EffectType.Add)
                {
                    addSum += effect.ValuePerLevel * level;
                }
                else
                {
                    percentSum += effect.ValuePerLevel * level;
                }
            }
        }
    }
}
