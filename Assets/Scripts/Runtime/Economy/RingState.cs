using System;
using NCAIClicker.Data;

namespace NCAIClicker.Economy
{
    /// <summary>
    /// 반지의 계산부 (이슈 #183). 레벨 보유, 다음 레벨 비용, 효과가 적용된 실효값만 담당한다.
    /// 포인트를 차감하지 않고 이벤트도 발행하지 않는다 — 구매는 EconomyManager 가 맡는다.
    ///
    /// <see cref="UpgradeState"/> 와 짝이다. 하는 일이 같고 **다른 것은 둘뿐이다** —
    /// 읽는 목록이 BalanceData.Rings 이고, 비용 단위가 코인이 아니라 레거시 포인트다.
    /// 계산 공식은 <see cref="GrowthFormula"/> 한 곳에서 공유하므로 두 벌이 되지 않는다.
    ///
    /// **반지 레벨은 파산해도 남는다.** 그것이 업그레이드와 갈리는 지점이고, 그 보장은 이
    /// 클래스가 아니라 회차 초기화 경로가 이 상태를 건드리지 않는 것으로 지킨다.
    /// </summary>
    public class RingState
    {
        private readonly BalanceData _balance;

        /// <summary>
        /// 반지별 현재 레벨. 순서는 BalanceData.Rings 와 같고, 임포터가 sort_order 로 정렬해
        /// 두므로 SaveData.RingLevels 와 같은 순서다.
        /// </summary>
        private readonly int[] _levels;

        public RingState(BalanceData balance)
        {
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _levels = new int[_balance.Rings.Count];
        }

        /// <summary>반지 종류 수. 저장 배열 길이와 같다.</summary>
        public int Count => _levels.Length;

        public int GetLevel(string ringId)
        {
            var index = IndexOf(ringId);
            return index < 0 ? 0 : _levels[index];
        }

        public bool IsMaxLevel(string ringId)
        {
            var index = IndexOf(ringId);
            if (index < 0)
            {
                return true;
            }
            return _levels[index] >= _balance.Rings[index].MaxLevel;
        }

        /// <summary>
        /// 다음 레벨을 사는 데 드는 **레거시 포인트**. 최대 레벨이거나 없는 id 면 false 다.
        /// </summary>
        public bool TryGetNextCost(string ringId, out long cost)
        {
            cost = 0L;
            var index = IndexOf(ringId);
            if (index < 0 || IsMaxLevel(ringId))
            {
                return false;
            }

            var def = _balance.Rings[index];

            // 업그레이드와 달리 economy.csv 로 폴백하지 않는다. upgrade_cost_growth 는 코인
            // 곡선이라 포인트 곡선에 끌어다 쓰면 두 화폐가 조용히 엮인다.
            var growth = def.CostGrowth > 0f ? def.CostGrowth : 1f;

            cost = GrowthFormula.NextCost(def.InitCost, growth, _levels[index]);
            return true;
        }

        /// <summary>레벨을 하나 올린다. 포인트 차감은 호출측(EconomyManager)이 먼저 끝낸 뒤 부른다.</summary>
        public void LevelUp(string ringId)
        {
            var index = IndexOf(ringId);
            if (index < 0)
            {
                throw new ArgumentException("rings.csv 에 없는 id 다: " + ringId, nameof(ringId));
            }
            if (IsMaxLevel(ringId))
            {
                throw new InvalidOperationException("이미 최대 레벨이다: " + ringId);
            }

            _levels[index]++;
        }

        /// <summary>
        /// 반지 효과를 얹은 실효값. 기준값은 호출측이 넘긴다 — IUpgradeStats 와 같은 규칙이다(#116).
        /// </summary>
        public float GetStat(StatId stat, float baseValue)
        {
            var addSum = 0f;
            var percentSum = 0f;

            for (var i = 0; i < _levels.Length; i++)
            {
                GrowthFormula.Accumulate(_balance.Rings[i].Effects, _levels[i], stat,
                                         ref addSum, ref percentSum);
            }

            return GrowthFormula.Compose(baseValue, addSum, percentSum);
        }

        /// <summary>
        /// 저장 데이터에서 레벨을 되살린다. **null 이 올 수 있다** — v2 이하 저장에는 이 배열이
        /// 없고 JsonUtility 가 빈 배열이 아니라 null 로 되살린다 (SaveManager 의 case 2).
        /// 길이가 다르면 겹치는 만큼만 채운다.
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
                _levels[i] = Math.Max(0, Math.Min(levels[i], _balance.Rings[i].MaxLevel));
            }
        }

        /// <summary>저장용 레벨 배열. 내부 배열을 그대로 넘기지 않는다.</summary>
        public int[] ToArray()
        {
            var copy = new int[_levels.Length];
            Array.Copy(_levels, copy, _levels.Length);
            return copy;
        }

        private int IndexOf(string ringId)
        {
            for (var i = 0; i < _balance.Rings.Count; i++)
            {
                if (_balance.Rings[i].Id == ringId)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
