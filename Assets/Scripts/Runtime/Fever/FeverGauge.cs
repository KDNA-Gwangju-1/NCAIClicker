using System;

namespace NCAIClicker.Fever
{
    /// <summary>
    /// 피버 게이지의 계산부. 누적·감쇠·상한 판정만 담당한다.
    /// 이벤트를 발행하지 않는다 — 구독과 발행은 FeverManager 가 맡는다.
    ///
    /// MonoBehaviour 와 분리한 이유는 Edit Mode 가 생명주기를 부르지 않아
    /// 계산을 컴포넌트에 묶어 두면 검증이 어렵기 때문이다 (docs/TECH_NOTES/fever-gauge.md).
    /// </summary>
    public class FeverGauge
    {
        private float _max;
        private float _current;

        /// <summary>가득 차는 기준값. fever.csv 의 gauge_max 다.</summary>
        public float Max => _max;

        public float Current => _current;

        /// <summary>
        /// 최대치를 정하고 0 에서 시작한다. 런을 시작할 때 부른다.
        /// 스태미나와 달리 게이지는 비어 있는 상태로 출발한다.
        /// </summary>
        public void Reset(float max)
        {
            if (max <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(max), max, "게이지 최대치는 0보다 커야 한다.");
            }

            _max = max;
            _current = 0f;
        }

        /// <summary>가득 차서 피버를 발동할 수 있는 상태.</summary>
        public bool IsFull => _max > 0f && _current >= _max;

        /// <summary>
        /// 적중 한 번을 누적한다. 상한을 넘어도 잘라내지 않는다 —
        /// 가득 찼는지는 IsFull 로 묻고, 비우는 것은 Consume 이 한다.
        /// </summary>
        /// <param name="amount">fever.csv 의 gauge_per_hit</param>
        public void AddHit(float amount)
        {
            if (amount < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "누적량은 음수일 수 없다.");
            }

            _current += amount;
        }

        /// <summary>
        /// 발동에 게이지를 쓴다. 비우는 시점을 호출측에 드러내려고 누적과 나눠 두었다.
        ///
        /// **비우는 때는 발동 순간이지 종료 시점이 아니다** — 밸런스 모델
        /// (.github/scripts/simulate_balance.py)이 그렇게 세어 fever.csv 의 발동 빈도가 나왔다.
        /// </summary>
        public void Consume()
        {
            _current = 0f;
        }

        /// <summary>
        /// 경과 시간만큼 감쇠시킨다. 0 아래로는 내려가지 않는다.
        /// 유예 시간(decay_grace_sec) 판단은 시각을 아는 FeverManager 가 한다.
        /// </summary>
        /// <param name="decayPerSec">fever.csv 의 gauge_decay_per_sec</param>
        /// <param name="deltaSeconds">경과 시간</param>
        /// <returns>실제로 줄어든 양. 이미 0이면 0이다</returns>
        public float Decay(float decayPerSec, float deltaSeconds)
        {
            if (decayPerSec < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(decayPerSec), decayPerSec,
                                                      "초당 감쇠량은 음수일 수 없다.");
            }
            if (deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds), deltaSeconds,
                                                      "경과 시간은 음수일 수 없다.");
            }

            var before = _current;
            _current -= decayPerSec * deltaSeconds;
            if (_current < 0f)
            {
                _current = 0f;
            }
            return before - _current;
        }
    }
}
