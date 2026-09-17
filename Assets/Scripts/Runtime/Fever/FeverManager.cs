using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using UnityEngine;

namespace NCAIClicker.Fever
{
    /// <summary>
    /// 적중으로 피버 게이지를 채우고, 타격이 끊기면 감쇠시키며, 가득 차면 피버를 발동한다.
    /// 계산은 FeverGauge 에 맡기고 여기서는 시간을 먹이고 이벤트를 발행하는 일만 한다.
    /// 완료 기준의 정본은 GitHub 이슈 #31.
    ///
    /// **코인 배율은 여기서 적용하지 않는다.** OnFeverStart/OnFeverEnd 만 알리고
    /// 배율은 EconomyManager 안에서만 곱한다 (AGENTS.md, ARCHITECTURE 코인 계약 4번).
    /// 그래서 fever.csv 의 coin_multiplier 는 이 클래스가 읽지 않는다.
    ///
    /// Managers 프리팹(Resources/Managers)에 붙인다. 생성은 ManagerBootstrap 이 한다.
    /// </summary>
    public class FeverManager : MonoBehaviour, IRunScoped
    {
        [SerializeField] private BalanceData _balanceData;

        /// <summary>
        /// OnFeverGaugeChanged 를 묶어 발행하는 간격. 지속 감쇠를 매 프레임 발행하지 않는다
        /// (ARCHITECTURE 3절). 밸런스 수치가 아니라 UI 갱신 주기라 CSV 가 아닌 인스펙터에 둔다.
        /// </summary>
        [SerializeField] private float _publishIntervalSec = 0.1f;

        private readonly FeverGauge _gauge = new FeverGauge();

        private bool _isRunning;
        private bool _isFeverActive;

        /// <summary>피버 잔여 시간. 피버 중이 아니면 0 이다.</summary>
        private float _feverRemainingSec;

        /// <summary>마지막 적중 이후 흐른 시간. 감쇠 유예(decay_grace_sec) 판정에 쓴다.</summary>
        private float _sinceLastHitSec;

        private float _publishTimer;

        public float CurrentGauge => _gauge.Current;

        public float MaxGauge => _gauge.Max;

        public bool IsFeverActive => _isFeverActive;

        /// <summary>런이 진행 중이라 게이지가 움직이는 상태.</summary>
        public bool IsRunning => _isRunning;

        private void Awake()
        {
            if (_balanceData == null)
            {
                Debug.LogError("[FeverManager] BalanceData 가 연결되지 않았다. " +
                               "피버가 발동하지 않으니 Managers 프리팹의 참조를 확인하라.");
            }
        }

        // 정적 이벤트는 구독과 해제를 쌍으로 맞춘다. 빠뜨리면 적중 하나가 두 번 누적된다 (AGENTS.md).
        private void OnEnable()
        {
            GameEvents.OnSwingResolved += HandleSwingResolved;
        }

        private void OnDisable()
        {
            GameEvents.OnSwingResolved -= HandleSwingResolved;
        }

        /// <summary>
        /// 게이지를 비우고 누적을 시작한다. GameManager 가 런 시작 때 부른다 (IRunScoped, 이슈 #71).
        /// 이걸 부르기 전에는 적중해도 차지 않는다 — MainMenu 에서 게이지가 쌓이는 것을 막는다.
        /// </summary>
        public void BeginRun()
        {
            if (_balanceData == null)
            {
                return;
            }

            _gauge.Reset(_balanceData.Fever.GaugeMax);
            _isRunning = true;
            _sinceLastHitSec = 0f;
            _publishTimer = 0f;
            EndFeverIfActive();
            PublishChanged();
        }

        /// <summary>
        /// 누적과 감쇠를 멈춘다. 피버 중이었다면 종료를 알린다 —
        /// 알리지 않으면 EconomyManager 의 배율이 켜진 채로 남는다.
        /// GameManager 가 Result 전이 시 부른다 (IRunScoped, 이슈 #111).
        /// </summary>
        public void EndRun()
        {
            _isRunning = false;
            EndFeverIfActive();
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// 한 프레임분을 진행한다. Update 에서 분리한 이유는 Edit Mode 검증에서
        /// 경과 시간을 직접 먹여야 하기 때문이다 — Time.deltaTime 은 에디터 프레임에 좌우된다.
        /// </summary>
        private void Tick(float deltaSeconds)
        {
            if (!_isRunning || _balanceData == null)
            {
                return;
            }

            if (_isFeverActive)
            {
                TickFever(deltaSeconds);
                return;
            }

            _sinceLastHitSec += deltaSeconds;
            if (_sinceLastHitSec < _balanceData.Fever.DecayGraceSec)
            {
                return;
            }

            var decayed = _gauge.Decay(_balanceData.Fever.GaugeDecayPerSec, deltaSeconds);
            if (decayed <= 0f)
            {
                return;
            }

            // 지속 감쇠는 묶어서 발행한다. 프레임마다 쏘면 HUD 가 매 프레임 갱신된다.
            _publishTimer += deltaSeconds;
            if (_publishTimer < _publishIntervalSec)
            {
                return;
            }
            _publishTimer = 0f;
            PublishChanged();
        }

        /// <summary>
        /// 피버 지속 시간을 흘린다. 피버 중에는 게이지가 누적되지도 감쇠하지도 않는다 —
        /// 게이지는 발동 순간에 이미 0 이 되었고, 밸런스 모델도 그렇게 세었다.
        /// </summary>
        private void TickFever(float deltaSeconds)
        {
            _feverRemainingSec -= deltaSeconds;
            if (_feverRemainingSec > 0f)
            {
                return;
            }
            EndFeverIfActive();
        }

        /// <summary>
        /// 스윙 판정을 받는다. **적중만 누적한다** — 망치가 상시 스윙하므로 헛스윙까지 세면
        /// 빈 곳에 커서를 둬도 게이지가 찬다 (GDD 4절).
        /// 호버와 자동 망치를 모두 센다. 소스를 가리는 것은 정확도 집계뿐이다 (BALANCE 5절).
        /// </summary>
        private void HandleSwingResolved(HitSource source, bool isHit)
        {
            if (!_isRunning || !isHit)
            {
                return;
            }

            _sinceLastHitSec = 0f;

            // 피버 중의 적중은 게이지에 쌓지 않는다. 쌓으면 다음 피버가 앞당겨져
            // fever.csv 에 적힌 런당 발동 빈도가 어긋난다.
            if (_isFeverActive)
            {
                return;
            }

            _gauge.AddHit(_balanceData.Fever.GaugePerHit);

            // 가득 찬 게이지는 발동에 쓰고 즉시 비운다. 0 을 먼저 알린 뒤 발동을 알려야
            // HUD 가 게이지를 비우고 나서 연출로 넘어간다.
            var becameFull = _gauge.IsFull;
            if (becameFull)
            {
                _gauge.Consume();
            }

            _publishTimer = 0f;
            PublishChanged();

            if (becameFull)
            {
                StartFever();
            }
        }

        private void StartFever()
        {
            _isFeverActive = true;
            _feverRemainingSec = _balanceData.Fever.DurationSec;
            GameEvents.PublishFeverStart();
        }

        /// <summary>피버 중일 때만 종료를 알린다. 두 번 알리면 배율 상태가 꼬인다.</summary>
        private void EndFeverIfActive()
        {
            if (!_isFeverActive)
            {
                return;
            }

            _isFeverActive = false;
            _feverRemainingSec = 0f;
            GameEvents.PublishFeverEnd();
        }

        private void PublishChanged()
        {
            GameEvents.PublishFeverGaugeChanged(_gauge.Current, _gauge.Max);
        }
    }
}
