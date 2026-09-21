using System;
using NCAIClicker.Data;

namespace NCAIClicker.Economy
{
    /// <summary>
    /// 업그레이드의 계산부. 레벨 보유, 다음 레벨 비용, 효과가 적용된 실효값만 담당한다.
    /// 코인을 차감하지 않고 이벤트도 발행하지 않는다 — 구매는 EconomyManager 가 맡는다.
    ///
    /// 계산 규칙의 정본은 docs/BALANCE.md 6절이고, 식 자체는 <see cref="GrowthFormula"/> 가
    /// 한 벌만 들고 있다 — 반지(<see cref="RingState"/>)가 같은 식을 쓰기 때문이다.
    ///
    /// 생성된 BalanceData 는 읽기만 한다. CSV 산출물이라 런타임에 고치지 않는다 (AGENTS.md).
    /// </summary>
    public class UpgradeState
    {
        private readonly BalanceData _balance;

        /// <summary>
        /// 업그레이드별 현재 레벨. 순서는 BalanceData.Upgrades 와 같고,
        /// 임포터가 그 목록을 sort_order 로 정렬해 두므로 SaveData.UpgradeLevels 와 같은 순서다
        /// (ARCHITECTURE "SaveData" 주석).
        /// </summary>
        private readonly int[] _levels;

        public UpgradeState(BalanceData balance)
        {
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _levels = new int[_balance.Upgrades.Count];
        }

        /// <summary>업그레이드 종류 수. 저장 배열 길이와 같다.</summary>
        public int Count => _levels.Length;

        public int GetLevel(string upgradeId)
        {
            var index = IndexOf(upgradeId);
            return index < 0 ? 0 : _levels[index];
        }

        public bool IsMaxLevel(string upgradeId)
        {
            var index = IndexOf(upgradeId);
            if (index < 0)
            {
                return true;
            }
            return _levels[index] >= _balance.Upgrades[index].MaxLevel;
        }

        /// <summary>
        /// 다음 레벨을 사는 데 드는 코인. 최대 레벨이거나 없는 id 면 false 를 돌려준다.
        /// </summary>
        public bool TryGetNextCost(string upgradeId, out long cost)
        {
            cost = 0L;
            var index = IndexOf(upgradeId);
            if (index < 0 || IsMaxLevel(upgradeId))
            {
                return false;
            }

            var def = _balance.Upgrades[index];

            // cost_growth 가 0 이면 종류별로 다르게 둘 이유가 없다는 뜻이므로 공통값을 쓴다
            // (BALANCE 3절 "비용 성장률은 4종 공통").
            var growth = def.CostGrowth > 0f ? def.CostGrowth : _balance.Economy.UpgradeCostGrowth;

            cost = GrowthFormula.GetNextCost(def.InitCost, growth, _levels[index]);
            return true;
        }

        /// <summary>
        /// 레벨을 하나 올린다. 코인 차감은 호출측(EconomyManager)이 먼저 끝낸 뒤 부른다.
        /// </summary>
        public void LevelUp(string upgradeId)
        {
            var index = IndexOf(upgradeId);
            if (index < 0)
            {
                throw new ArgumentException("upgrades.csv 에 없는 id 다: " + upgradeId, nameof(upgradeId));
            }
            if (IsMaxLevel(upgradeId))
            {
                throw new InvalidOperationException("이미 최대 레벨이다: " + upgradeId);
            }

            _levels[index]++;
        }

        /// <summary>
        /// 업그레이드 효과를 얹은 실효값. 소비처는 기준값 대신 이 값을 쓴다.
        ///
        /// 기준값을 인자로 받는 이유는 <c>spawn_count</c> 때문이다. 그 기준값은 stages.csv 의
        /// 단계별 값이라 BalanceData 한 곳에서 꺼낼 수 없다. 전부 호출측이 넘기게 해서
        /// 같은 일을 두 갈래로 하지 않는다.
        /// </summary>
        /// <param name="stat">upgrade_effects.csv 의 stat</param>
        /// <param name="baseValue">업그레이드가 없을 때의 값. 출처는 BALANCE 6절 표</param>
        public float GetStat(StatId stat, float baseValue)
        {
            var addSum = 0f;
            var percentSum = 0f;

            for (var i = 0; i < _levels.Length; i++)
            {
                GrowthFormula.Accumulate(_balance.Upgrades[i].Effects, _levels[i], stat,
                                         ref addSum, ref percentSum);
            }

            return GrowthFormula.Compose(baseValue, addSum, percentSum);
        }

        /// <summary>
        /// 저장 데이터에서 레벨을 되살린다. 길이가 다르면 겹치는 만큼만 채우고 나머지는 0 이다.
        /// 길이가 맞는지 판단하는 것은 Count 를 아는 호출측의 몫이다 — 경고를 남기는 쪽과 같은 곳에 둔다.
        /// </summary>
        public void RestoreLevels(int[] levels)
        {
            Array.Clear(_levels, 0, _levels.Length);
            if (levels == null)
            {
                return;
            }

            var count = Math.Min(levels.Length, _levels.Length);
            for (var i = 0; i < count; i++)
            {
                // 저장 파일이 손상되거나 CSV 의 max_level 이 낮아졌을 수 있다.
                _levels[i] = Math.Max(0, Math.Min(levels[i], _balance.Upgrades[i].MaxLevel));
            }
        }

        /// <summary>저장용 레벨 배열. 내부 배열을 그대로 넘기지 않는다.</summary>
        public int[] ToArray()
        {
            var copy = new int[_levels.Length];
            Array.Copy(_levels, copy, _levels.Length);
            return copy;
        }

        private int IndexOf(string upgradeId)
        {
            for (var i = 0; i < _balance.Upgrades.Count; i++)
            {
                if (_balance.Upgrades[i].Id == upgradeId)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
