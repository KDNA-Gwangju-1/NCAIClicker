using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using UnityEngine;
using UnityEngine.EventSystems;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 마지막 단계 고지서를 내면 퍽 선택 뒤에 엔딩(통계) 패널을 한 번 띄운다 (이슈 #271, GDD 5절).
    ///
    /// Managers 프리팹에 붙는다 — 이유는 PerkChoiceController 와 같다 (남의 씬·HUD 를 고치지 않는다).
    ///
    /// **판정은 OnStageGoalReached 로 한다.** DoD 의 `OnBillPaid && IsMaxStage` 를 그대로 쓰면
    /// 틀린다: StageGoalManager 가 납부 직후 AdvanceStage 를 부르므로, 2단계를 낸 순간 이미
    /// IsMaxStage 가 참이 된다. 도달한 단계 번호를 받는 쪽은 구독 순서와 무관하게 맞다.
    ///
    /// 플래그는 **메모리에만** 둔다 (DoD). 마지막 납부 직후 앱을 끄고 이어하면 엔딩이 안 뜬다 —
    /// 퍽 선택 전이면 도달 이벤트가 다시 오지 않기 때문이다. SaveData 를 건드리지 않는 대가다.
    /// </summary>
    public class EndingController : MonoBehaviour
    {
        [SerializeField] private BalanceData _balanceData;
        [SerializeField] private EndingPanelView _panelPrefab;

        /// <summary>
        /// 이번 회차에 엔딩을 이미 봤다. 조회는 EndingChecks 용이다 — 다른 런타임 스크립트는 이 값을
        /// 읽지 말고 같은 이벤트(OnStageGoalReached·OnBankrupt)를 듣는다 (PATTERNS 3절).
        /// </summary>
        public static bool IsRunCleared { get; private set; }

        private EndingPanelView _panelInstance;
        private GameObject _fallbackEventSystem;
        private bool _isFinalStageReached;
        private bool _isShowPending;
        private float _timeScaleBeforePause;
        private bool _isPaused;

        private void OnEnable()
        {
            GameEvents.OnStageGoalReached += HandleStageGoalReached;
            GameEvents.OnPerkChosen += HandlePerkChosen;
            GameEvents.OnBankrupt += HandleBankrupt;
        }

        private void OnDisable()
        {
            GameEvents.OnStageGoalReached -= HandleStageGoalReached;
            GameEvents.OnPerkChosen -= HandlePerkChosen;
            GameEvents.OnBankrupt -= HandleBankrupt;

            ResumeTime();
            HidePanel();
        }

        private void OnDestroy()
        {
            ResumeTime();
        }

        /// <summary>
        /// 퍽을 고른 **다음 프레임**에 띄운다. OnPerkChosen 은 TryChoosePerk 안에서 발행되고,
        /// PerkChoiceController 는 그 호출이 끝난 뒤 ResumeTime 을 부른다. 여기서 바로 멈추면
        /// 퍽 쪽이 곧장 시간을 되돌리고, 이쪽은 0 을 원래 값으로 기억해 닫을 때 게임이 굳는다.
        /// Update 는 timeScale 0 에서도 돈다.
        /// </summary>
        private void Update()
        {
            if (!_isShowPending)
            {
                return;
            }
            _isShowPending = false;
            ShowPanel();
        }

        private void HandleStageGoalReached(int stageNumber)
        {
            if (_balanceData != null && stageNumber >= _balanceData.Stages.Count && !IsRunCleared)
            {
                _isFinalStageReached = true;
            }
        }

        private void HandlePerkChosen(string perkId)
        {
            if (!_isFinalStageReached)
            {
                return;
            }
            _isFinalStageReached = false;
            IsRunCleared = true;
            _isShowPending = true;
        }

        /// <summary>파산하면 새 회차다. 다음 회차에서 다시 끝까지 가면 엔딩이 또 뜬다.</summary>
        private void HandleBankrupt()
        {
            _isFinalStageReached = false;
            _isShowPending = false;
            IsRunCleared = false;
            HidePanel();
            ResumeTime();
        }

        private void ShowPanel()
        {
            var billService = BillManager.Instance;
            if (billService == null || !TryPreparePanel())
            {
                return;
            }

            _panelInstance.Bind(billService.CurrentDay, billService.CurrentCycle, SumBillAmounts(), HandleContinueClicked);
            _panelInstance.gameObject.SetActive(true);
            if (_fallbackEventSystem != null && EventSystem.current == null)
            {
                _fallbackEventSystem.SetActive(true);
            }
            PauseTime();
        }

        /// <summary>
        /// 총 납부액. 단계마다 고지서 한 장을 내야 다음 단계로 가므로 끝까지 온 회차의 납부액은
        /// 늘 Σ BillAmount 다 — 따로 누적할 상태가 필요 없다.
        /// </summary>
        private long SumBillAmounts()
        {
            long total = 0;
            foreach (var stage in _balanceData.Stages)
            {
                total += stage.BillAmount;
            }
            return total;
        }

        private void HandleContinueClicked()
        {
            HidePanel();
            ResumeTime();
        }

        private bool TryPreparePanel()
        {
            if (_panelInstance != null)
            {
                return true;
            }
            if (_panelPrefab == null)
            {
                Debug.LogError("[EndingController] 패널 프리팹이 비어 있어 엔딩을 띄우지 못했다.");
                return false;
            }

            // Managers 의 자식으로 둔다 — DontDestroyOnLoad 를 물려받는다.
            _panelInstance = Instantiate(_panelPrefab, transform);
            _panelInstance.name = _panelPrefab.name;
            var eventSystem = _panelInstance.GetComponentInChildren<EventSystem>(true);
            _fallbackEventSystem = eventSystem == null ? null : eventSystem.gameObject;
            _panelInstance.gameObject.SetActive(false);
            return true;
        }

        private void HidePanel()
        {
            if (_fallbackEventSystem != null)
            {
                _fallbackEventSystem.SetActive(false);
            }
            if (_panelInstance != null)
            {
                _panelInstance.gameObject.SetActive(false);
            }
        }

        private void PauseTime()
        {
            if (_isPaused)
            {
                return;
            }
            _timeScaleBeforePause = Time.timeScale;
            _isPaused = true;
            Time.timeScale = 0f;
        }

        private void ResumeTime()
        {
            if (!_isPaused)
            {
                return;
            }
            _isPaused = false;
            Time.timeScale = _timeScaleBeforePause;
        }
    }
}
