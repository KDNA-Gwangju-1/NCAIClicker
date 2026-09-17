using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using UnityEngine;

namespace NCAIClicker.Core
{
    /// <summary>
    /// 스태미나가 시간에 따라 줄어들게 하고, 회복형 대상의 파괴로 되돌려 준다.
    /// 계산은 StaminaPool 에 맡기고 여기서는 시간을 먹이고 이벤트를 발행하는 일만 한다.
    /// 완료 기준의 정본은 GitHub 이슈 #19.
    ///
    /// 런을 실제로 끝내는 것은 이 클래스가 아니다 — OnStaminaDepleted 를 발행할 뿐이고,
    /// 입력 차단과 결과 화면 전이는 GameManager 가 조정한다 (ARCHITECTURE "하루 종료 순서", 작업 2.5).
    ///
    /// Managers 프리팹(Resources/Managers)에 붙인다. 생성은 ManagerBootstrap 이 한다.
    /// </summary>
    public class StaminaManager : MonoBehaviour, IRunScoped
    {
        [SerializeField] private BalanceData _balanceData;

        /// <summary>
        /// OnStaminaChanged 를 묶어 발행하는 간격. 지속 감소를 매 프레임 발행하지 않는다 (ARCHITECTURE 3절).
        /// 밸런스 수치가 아니라 UI 갱신 주기라 CSV 가 아닌 인스펙터에 둔다.
        /// </summary>
        [SerializeField] private float _publishIntervalSec = 0.1f;

        private readonly StaminaPool _pool = new StaminaPool();

        /// <summary>
        /// 업그레이드 실효값 조회 통로 (#116). ManagerBootstrap 이 넣어 준다.
        /// 없으면 CSV 기준값을 그대로 쓴다 — 주입이 빠져도 죽지 않는다.
        /// </summary>
        private IUpgradeStats _upgradeStats;

        /// <summary>
        /// 런 시작에 굳힌 실효값 (#131). 업그레이드 효과는 **다음 런부터** 반영한다 (BALANCE 6절).
        /// Tick 마다 조회하면 런 도중에 감소 속도가 바뀔 수 있는 구조가 남는다.
        /// </summary>
        private float _runMaxStamina;
        private float _runDrainPerSec;

        /// <summary>
        /// 아직 쓰지 않은 회복 퍼크의 총량 (#126). 만충일 때 주면 그대로 버려지므로
        /// **회복량만큼 빈자리가 생기는 순간** 한 번에 쓴다 (GDD 6절).
        /// 런 도중에 고른 퍼크도 만충이면 여기 쌓였다가 나중에 들어간다.
        /// </summary>
        private float _pendingPerkRestore;

        private bool _isRunning;
        private bool _hasPublishedDepleted;
        private float _publishTimer;

        public float CurrentStamina => _pool.Current;

        public float MaxStamina => _pool.Max;

        /// <summary>런이 진행 중이라 스태미나가 줄고 있는 상태.</summary>
        public bool IsRunning => _isRunning;

        private void Awake()
        {
            if (_balanceData == null)
            {
                Debug.LogError("[StaminaManager] BalanceData 가 연결되지 않았다. " +
                               "스태미나가 줄지 않으니 Managers 프리팹의 참조를 확인하라.");
            }
        }

        /// <summary>
        /// 업그레이드 실효값 조회 통로를 넣는다. 서비스 계약이 아니라 조립(wiring) 통로다 —
        /// EconomyManager.SetBillService 와 같은 성격이다 (ARCHITECTURE "SetBillService" 문단).
        /// </summary>
        public void SetUpgradeStats(IUpgradeStats upgradeStats)
        {
            _upgradeStats = upgradeStats;
        }

        // 정적 이벤트는 구독과 해제를 쌍으로 맞춘다. 빠뜨리면 회복이 두 배로 들어온다 (AGENTS.md).
        private void OnEnable()
        {
            GameEvents.OnTargetBroken += HandleTargetBroken;
            GameEvents.OnPerkChosen += HandlePerkChosen;
        }

        private void OnDisable()
        {
            GameEvents.OnTargetBroken -= HandleTargetBroken;
            GameEvents.OnPerkChosen -= HandlePerkChosen;
        }

        /// <summary>
        /// 스태미나를 가득 채우고 감소를 시작한다. GameManager 가 런 시작 때 부른다.
        /// 이걸 부르기 전에는 줄지 않는다 — MainMenu 에서 스태미나가 새는 것을 막기 위해서다.
        /// </summary>
        public void BeginRun()
        {
            if (_balanceData == null)
            {
                return;
            }

            _runMaxStamina = GetStat(StatId.MaxStamina, _balanceData.Stamina.Max);
            _runDrainPerSec = GetStat(StatId.IdleDrainPerSec, _balanceData.Stamina.IdleDrainPerSec);

            _pool.Fill(_runMaxStamina);
            _isRunning = true;
            _hasPublishedDepleted = false;
            _publishTimer = 0f;
            PublishChanged();
            TryConsumePerkRestore();
        }

        /// <summary>
        /// 감소를 멈춘다. 남은 값은 그대로 두므로 결과 화면이 읽을 수 있다.
        /// 소진이 아닌 사유(파산·퍼크 선택 등)로 런이 끊길 때 GameManager 가 부른다.
        /// </summary>
        public void EndRun()
        {
            _isRunning = false;
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

            _pool.Drain(_runDrainPerSec, deltaSeconds);

            if (_pool.IsDepleted)
            {
                HandleDepleted();
                return;
            }

            // 예약된 회복 퍼크는 여기서 쓴다. 소진 판정 **뒤**라 0 에 닿은 런을 되살리지는 않는다 —
            // 남은 예약은 다음 런으로 넘어간다 (#126).
            TryConsumePerkRestore();

            // 지속 감소는 묶어서 발행한다. 프레임마다 쏘면 HUD 가 매 프레임 갱신된다.
            _publishTimer += deltaSeconds;
            if (_publishTimer < _publishIntervalSec)
            {
                return;
            }
            _publishTimer = 0f;
            PublishChanged();
        }

        /// <summary>
        /// 회복 퍼크를 받는다. 값의 출처는 perks.csv 하나다.
        /// 즉시 쓰지 않고 예약해 두는 이유는 만충에서 쓰면 그대로 버려지기 때문이다 (GDD 6절).
        /// 런 밖에서 고른 퍼크도 같은 자리에 쌓여 다음 런에서 쓰인다.
        /// </summary>
        private void HandlePerkChosen(string perkId)
        {
            if (_balanceData == null)
            {
                return;
            }

            var perk = _balanceData.GetPerk(perkId);
            if (perk == null || perk.Type != PerkType.StaminaRestore || perk.Value <= 0f)
            {
                return;
            }

            _pendingPerkRestore += perk.Value;
            TryConsumePerkRestore();
        }

        /// <summary>
        /// 예약된 회복량만큼 빈자리가 생겼으면 쓴다. 낭비 없이 전부 들어갈 때만 터뜨린다.
        /// 런 중이 아니면 쓰지 않는다 — 다음 런의 빈자리를 기다린다.
        /// </summary>
        private void TryConsumePerkRestore()
        {
            if (!_isRunning || _pendingPerkRestore <= 0f)
            {
                return;
            }
            if (_pool.Max - _pool.Current < _pendingPerkRestore)
            {
                return;
            }

            var restored = _pool.Restore(_pendingPerkRestore);
            _pendingPerkRestore = 0f;
            if (restored <= 0f)
            {
                return;
            }

            GameEvents.PublishStaminaRestored(restored);
            _publishTimer = 0f;
            PublishChanged();
        }

        /// <summary>
        /// 소진 처리. 0 을 먼저 알리고 종료를 요청한다 — 순서가 뒤집히면 HUD 에 0 이 찍히지 않는다.
        /// 종료 요청은 런당 한 번만 나간다.
        /// </summary>
        private void HandleDepleted()
        {
            if (_hasPublishedDepleted)
            {
                return;
            }

            _hasPublishedDepleted = true;
            _isRunning = false;
            PublishChanged();
            GameEvents.PublishStaminaDepleted();
        }

        /// <summary>
        /// 파괴 보상 중 스태미나 몫을 받는다. 회복량의 출처는 BreakInfo 하나뿐이며
        /// targets.csv 를 다시 읽거나 대상 구현을 참조하지 않는다 (#3 에서 동결).
        /// </summary>
        private void HandleTargetBroken(BreakInfo info)
        {
            if (!_isRunning)
            {
                return;
            }

            var restored = _pool.Restore(info.StaminaRestore);
            if (restored <= 0f)
            {
                // 회복형이 아닌 대상(회복량 0)과 이미 만충인 경우다. 알릴 것이 없다.
                return;
            }

            // 실제로 회복된 양만 알린다. 명령이 아니므로 받는 쪽이 다시 회복하지 않는다 (ARCHITECTURE 3절).
            GameEvents.PublishStaminaRestored(restored);
            _publishTimer = 0f;
            PublishChanged();
        }

        /// <summary>주입이 없으면 기준값 그대로다. 배선이 빠져도 게임이 돌아가야 한다.</summary>
        private float GetStat(StatId stat, float baseValue)
        {
            return _upgradeStats == null ? baseValue : _upgradeStats.GetStat(stat, baseValue);
        }

        private void PublishChanged()
        {
            GameEvents.PublishStaminaChanged(_pool.Current, _pool.Max);
        }
    }
}
