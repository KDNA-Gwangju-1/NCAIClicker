using System;

namespace NCAIClicker.Core
{
    /// <summary>
    /// 스태미나의 계산부. 시간 감소와 회복, 상한 클램프, 소진 판정만 담당한다.
    /// 이벤트를 발행하지 않는다 — 구독과 발행은 StaminaManager 가 맡는다.
    ///
    /// MonoBehaviour 와 분리한 이유는 Edit Mode 가 생명주기를 부르지 않아
    /// 계산을 컴포넌트에 묶어 두면 검증이 어렵기 때문이다 (docs/TECH_NOTES/stamina.md).
    /// </summary>
    public class StaminaPool
    {
        private float _max;
        private float _current;

        /// <summary>최대치. 업그레이드가 붙기 전까지는 stamina.csv 의 max_stamina 다.</summary>
        public float Max => _max;

        public float Current => _current;

        /// <summary>남은 스태미나가 없는 상태. 런 종료 요청의 판정 기준이다.</summary>
        public bool IsDepleted => _current <= 0f;

        /// <summary>
        /// 최대치를 정하고 가득 채운다. 런을 시작할 때 부른다.
        /// </summary>
        public void Fill(float max)
        {
            if (max <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(max), max, "최대 스태미나는 0보다 커야 한다.");
            }

            _max = max;
            _current = max;
        }

        /// <summary>
        /// 경과 시간만큼 줄인다. 0 아래로는 내려가지 않는다.
        /// </summary>
        /// <param name="drainPerSec">stamina.csv 의 idle_drain_per_sec</param>
        /// <param name="deltaSeconds">경과 시간</param>
        /// <returns>실제로 줄어든 양. 이미 0이면 0이다</returns>
        public float Drain(float drainPerSec, float deltaSeconds)
        {
            if (drainPerSec < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(drainPerSec), drainPerSec,
                                                      "초당 감소량은 음수일 수 없다.");
            }
            if (deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds), deltaSeconds,
                                                      "경과 시간은 음수일 수 없다.");
            }

            var before = _current;
            _current -= drainPerSec * deltaSeconds;
            if (_current < 0f)
            {
                _current = 0f;
            }
            return before - _current;
        }

        /// <summary>
        /// 회복한다. 최대치를 넘지 않으며, 넘치는 몫은 버린다.
        /// </summary>
        /// <param name="amount">BreakInfo.StaminaRestore 로 들어온 회복량</param>
        /// <returns>실제로 회복된 양. OnStaminaRestored 의 인자가 된다 (ARCHITECTURE 3절)</returns>
        public float Restore(float amount)
        {
            if (amount <= 0f)
            {
                return 0f;
            }

            var before = _current;
            _current += amount;
            if (_current > _max)
            {
                _current = _max;
            }
            return _current - before;
        }
    }
}
