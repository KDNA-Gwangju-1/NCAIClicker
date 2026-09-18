using NCAIClicker.Core;
using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 런 종료 시 결과 화면 2종(하루 정산 vs 파산)을 표시하는 UI 컨트롤러.
    /// 완료 기준: 정산 완료 / 파산 구분 표시 (이슈 #34).
    /// </summary>
    public class ResultUIController : MonoBehaviour
    {
        private const string MainMenuSceneName = "MainMenu";

        /// <summary>
        /// 조회 계약이 아직 없어 값을 채울 수 없는 칸에 넣는 글자.
        /// 0 을 넣으면 "정말 0" 인지 "배선이 빠진" 것인지 아무도 구분하지 못한다.
        /// </summary>
        public const string UnwiredPlaceholder = "—";

        [Header("루트 패널")]
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private GameObject _settlementContainer;
        [SerializeField] private GameObject _bankruptcyContainer;

        [Header("하루 정산 화면 UI")]
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _dayText;
        [SerializeField] private TextMeshProUGUI _runCoinText;
        [SerializeField] private TextMeshProUGUI _accuracyText;
        [SerializeField] private TextMeshProUGUI _billStatusText;
        [SerializeField] private TextMeshProUGUI _stageGoalText;
        [SerializeField] private Button _continueButton;

        // 아래는 배선할 조회 계약이 아직 없는 자리다 (이슈 #34 후속).
        // 값을 채우는 코드는 일부러 넣지 않는다 — UnwiredPlaceholder 로 두어
        // "0원"과 "아직 안 이어짐"이 구분되게 한다.
        [Header("정산 명세 — 데이터 미배선")]
        [SerializeField] private TextMeshProUGUI _grossText;
        [SerializeField] private TextMeshProUGUI _feeText;
        [SerializeField] private TextMeshProUGUI _netText;
        [SerializeField] private TextMeshProUGUI[] _denomCountTexts;
        [SerializeField] private TextMeshProUGUI _brokenCountText;
        [SerializeField] private TextMeshProUGUI[] _brokenChipTexts;
        [SerializeField] private TextMeshProUGUI _codexProgressText;
        [SerializeField] private Button _upgradeButton;
        [SerializeField] private Button _payButton;
        [SerializeField] private TextMeshProUGUI _payCaptionText;

        // 빅 토니 징수는 대출이 있을 때만 존재하는 항목이다. 원작도 같다 —
        // 게임 내 안내문이 "Tony takes 5 to 10% of your earnings every day until you repay
        // the loan" 이라고 못 박는다 (REFERENCE_ANALYSIS.md 대출 절, 등급 A 플레이 영상).
        // 대출이 없으면 행을 통째로 숨긴다 — "0원 징수" 를 보여 주면 없는 빚이 있는 것처럼 읽힌다.
        [SerializeField] private GameObject _loanCutRow;
        [SerializeField] private TextMeshProUGUI _loanCutLabelText;

        [Header("파산 화면 UI")]
        [SerializeField] private TextMeshProUGUI _bankruptcyTitleText;
        [SerializeField] private TextMeshProUGUI _bankruptcyDetailText;
        [SerializeField] private TextMeshProUGUI _bankruptcyCoinLossText;
        [SerializeField] private Button _restartButton;

        [Header("공통 UI")]
        [SerializeField] private Button _mainMenuButton;

        // 정산창의 납부 버튼은 고지서 모달을 연다. 조립 지점이 넣어 준다.
        private BillPanelController _billPanel;

        private IEconomyService _economyService;
        private IBillService _billService;
        private IStageService _stageService;

        private int _totalHoverSwings;
        private int _hitHoverSwings;
        private bool _isBankrupt;

        public bool IsPanelActive => _panelRoot != null && _panelRoot.activeSelf;
        public bool IsSettlementActive => _settlementContainer != null && _settlementContainer.activeSelf;
        public bool IsBankruptcyActive => _bankruptcyContainer != null && _bankruptcyContainer.activeSelf;
        public int TotalHoverSwings => _totalHoverSwings;
        public int HitHoverSwings => _hitHoverSwings;
        public float Accuracy => _totalHoverSwings > 0 ? ((float)_hitHoverSwings / _totalHoverSwings) * 100f : 0f;

        private void Awake()
        {
            HideAll();
        }

        private void OnEnable()
        {
            GameEvents.OnSwingResolved += HandleSwingResolved;
            GameEvents.OnStaminaDepleted += HandleStaminaDepleted;
            GameEvents.OnBankrupt += HandleBankrupt;

            if (_continueButton != null)
            {
                _continueButton.onClick.AddListener(HandleContinueClicked);
            }
            if (_restartButton != null)
            {
                _restartButton.onClick.AddListener(HandleRestartClicked);
            }
            if (_mainMenuButton != null)
            {
                _mainMenuButton.onClick.AddListener(HandleMainMenuClicked);
            }
            if (_upgradeButton != null)
            {
                _upgradeButton.onClick.AddListener(HandleUpgradeClicked);
            }
            if (_payButton != null)
            {
                _payButton.onClick.AddListener(HandlePayClicked);
            }
        }

        private void OnDisable()
        {
            GameEvents.OnSwingResolved -= HandleSwingResolved;
            GameEvents.OnStaminaDepleted -= HandleStaminaDepleted;
            GameEvents.OnBankrupt -= HandleBankrupt;

            if (_continueButton != null)
            {
                _continueButton.onClick.RemoveListener(HandleContinueClicked);
            }
            if (_restartButton != null)
            {
                _restartButton.onClick.RemoveListener(HandleRestartClicked);
            }
            if (_mainMenuButton != null)
            {
                _mainMenuButton.onClick.RemoveListener(HandleMainMenuClicked);
            }
            if (_upgradeButton != null)
            {
                _upgradeButton.onClick.RemoveListener(HandleUpgradeClicked);
            }
            if (_payButton != null)
            {
                _payButton.onClick.RemoveListener(HandlePayClicked);
            }
            if (_billPanel != null)
            {
                _billPanel.Closed -= HandleBillPanelClosed;
            }
        }

        /// <summary>고지서 패널을 잇는다. 없으면 납부 버튼은 잠긴 채로 둔다.</summary>
        public void SetBillPanel(BillPanelController billPanel)
        {
            if (_billPanel != null)
            {
                _billPanel.Closed -= HandleBillPanelClosed;
            }

            _billPanel = billPanel;

            if (_billPanel != null)
            {
                _billPanel.Closed += HandleBillPanelClosed;
            }
        }

        /// <summary>고지서를 닫으면 정산으로 돌아온다. 하루가 아직 안 끝났기 때문이다.</summary>
        private void HandleBillPanelClosed()
        {
            ShowSettlement();
        }

        /// <summary>테스트 또는 외부 주입용 서비스 설정 메서드.</summary>
        public void SetServices(IEconomyService economyService, IBillService billService, IStageService stageService)
        {
            _economyService = economyService;
            _billService = billService;
            _stageService = stageService;
        }

        public void ResetRunStats()
        {
            _totalHoverSwings = 0;
            _hitHoverSwings = 0;
            _isBankrupt = false;
            HideAll();
        }

        public void HandleSwingResolved(HitSource source, bool isHit)
        {
            if (source != HitSource.Hover)
            {
                return;
            }

            _totalHoverSwings++;
            if (isHit)
            {
                _hitHoverSwings++;
            }
        }

        private void HandleStaminaDepleted()
        {
            if (_isBankrupt)
            {
                return;
            }
            ShowSettlement();
        }

        private void HandleBankrupt()
        {
            _isBankrupt = true;
            ShowBankruptcy();
        }

        public void ShowSettlement()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            FindFirstObjectByType<HammerSwingVisual>(FindObjectsInactive.Include)?.SetVisible(false);

            if (_panelRoot != null)
            {
                _panelRoot.SetActive(true);
            }
            if (_settlementContainer != null)
            {
                _settlementContainer.SetActive(true);
            }
            if (_bankruptcyContainer != null)
            {
                _bankruptcyContainer.SetActive(false);
            }

            UpdateSettlementView();
        }

        public void ShowBankruptcy()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            FindFirstObjectByType<HammerSwingVisual>(FindObjectsInactive.Include)?.SetVisible(false);

            if (_panelRoot != null)
            {
                _panelRoot.SetActive(true);
            }
            if (_settlementContainer != null)
            {
                _settlementContainer.SetActive(false);
            }
            if (_bankruptcyContainer != null)
            {
                _bankruptcyContainer.SetActive(true);
            }

            UpdateBankruptcyView();
        }

        public void HideAll()
        {
            if (_panelRoot != null)
            {
                _panelRoot.SetActive(false);
            }
            if (_settlementContainer != null)
            {
                _settlementContainer.SetActive(false);
            }
            if (_bankruptcyContainer != null)
            {
                _bankruptcyContainer.SetActive(false);
            }
        }

        private void EnsureServices()
        {
            if (_economyService != null && _billService != null && _stageService != null)
            {
                return;
            }

            var managersGo = GameObject.Find("Managers");
            if (managersGo != null)
            {
                if (_economyService == null)
                {
                    _economyService = managersGo.GetComponentInChildren<IEconomyService>(true);
                }
                if (_billService == null)
                {
                    _billService = managersGo.GetComponentInChildren<IBillService>(true);
                }
                if (_stageService == null)
                {
                    _stageService = managersGo.GetComponentInChildren<IStageService>(true);
                }
            }

            if (_economyService == null || _billService == null || _stageService == null)
            {
                var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (var i = 0; i < behaviours.Length; i++)
                {
                    var b = behaviours[i];
                    if (_economyService == null && b is IEconomyService eco)
                    {
                        _economyService = eco;
                    }
                    if (_billService == null && b is IBillService bill)
                    {
                        _billService = bill;
                    }
                    if (_stageService == null && b is IStageService stage)
                    {
                        _stageService = stage;
                    }
                }
            }
        }

        private void UpdateSettlementView()
        {
            EnsureServices();

            if (_titleText != null)
            {
                _titleText.text = "지친 손!";
            }

            if (_dayText != null)
            {
                int currentDay = _billService != null ? _billService.CurrentDay : 1;
                _dayText.text = $"DAY {currentDay}";
            }

            if (_runCoinText != null)
            {
                long runCoin = _economyService != null ? _economyService.RunCoin : 0;
                _runCoinText.text = $"{runCoin:N0}";
            }

            if (_accuracyText != null)
            {
                _accuracyText.text = $"{Accuracy:F0}%";
            }

            UpdateUnwiredView();

            if (_billStatusText != null)
            {
                if (_billService != null && _billService.ActiveBill != null && !_billService.ActiveBill.IsPaid)
                {
                    _billStatusText.text = $"고지서 마감: {_billService.DaysLeft}일 남음 ({_billService.ActiveBill.Amount:N0}원)";
                }
                else
                {
                    _billStatusText.text = "고지서: 납부 완료";
                }
            }

            if (_stageGoalText != null)
            {
                bool isGoalReached = _stageService != null && _stageService.IsGoalReached;
                _stageGoalText.text = isGoalReached ? "단계 목표: 달성 완료!" : "단계 목표: 미달성";
            }
        }

        /// <summary>
        /// 조회 계약이 없어 값을 못 채우는 칸을 자리표시로 채우고, 동작이 없는 버튼을 잠근다.
        /// 배선이 붙는 이슈에서 이 메서드의 해당 줄을 실제 조회로 바꾼다.
        /// </summary>
        private void UpdateUnwiredView()
        {
            SetPlaceholder(_grossText);
            SetPlaceholder(_feeText);
            SetPlaceholder(_netText);
            SetPlaceholder(_brokenCountText);
            SetPlaceholder(_codexProgressText);
            SetPlaceholders(_denomCountTexts);
            SetPlaceholders(_brokenChipTexts);

            UpdatePayButton();
            UpdateLoanCutRow();

            // 패널이 안 이어졌으면 눌러도 아무 일이 없다. 그럴 바엔 잠근다.
            if (_upgradeButton != null)
            {
                _upgradeButton.interactable = _billPanel != null;
            }

            if (_payCaptionText != null)
            {
                // 원작은 버튼 자체가 정보다 — "$5,800 / 4일 남음".
                var bill = _billService?.ActiveBill;
                _payCaptionText.text = bill == null || bill.IsPaid
                    ? "납부 완료"
                    : $"{_billService.DaysLeft}일 남음";
            }
        }

        /// <summary>
        /// 낼 고지서가 있고 고지서 패널이 이어져 있을 때만 납부 버튼을 연다.
        /// 배선이 없는데 열어 두면 눌러도 아무 일이 없어 고장으로 읽힌다.
        /// </summary>
        private void UpdatePayButton()
        {
            var bill = _billService?.ActiveBill;
            var canPay = _billPanel != null && bill != null && !bill.IsPaid;

            if (_payButton != null)
            {
                _payButton.interactable = canPay;
            }
        }

        private void HandlePayClicked()
        {
            if (_billPanel == null)
            {
                return;
            }

            // 원작은 고지서가 뜨면 정산창이 보이지 않는다. 겹쳐 두면 글자가 서로 비쳐 읽히지 않는다.
            HideAll();
            _billPanel.ShowAsModal();
        }

        /// <summary>대출이 있을 때만 징수 행을 보인다. 비율은 조회로 채우고 금액은 아직 배선이 없다.</summary>
        private void UpdateLoanCutRow()
        {
            var dailyCut = _billService != null ? _billService.LoanDailyCut : 0f;
            var hasLoan = dailyCut > 0f;

            if (_loanCutRow != null)
            {
                _loanCutRow.SetActive(hasLoan);
            }

            if (hasLoan && _loanCutLabelText != null)
            {
                _loanCutLabelText.text = $"빅 토니 징수 ({dailyCut * 100f:F0}%)";
            }
        }

        private static void SetPlaceholder(TextMeshProUGUI label)
        {
            if (label != null)
            {
                label.text = UnwiredPlaceholder;
            }
        }

        private static void SetPlaceholders(TextMeshProUGUI[] labels)
        {
            if (labels == null)
            {
                return;
            }

            for (var i = 0; i < labels.Length; i++)
            {
                SetPlaceholder(labels[i]);
            }
        }

        private static void SetLocked(Button button)
        {
            if (button != null)
            {
                button.interactable = false;
            }
        }

        private void UpdateBankruptcyView()
        {
            EnsureServices();

            if (_bankruptcyTitleText != null)
            {
                _bankruptcyTitleText.text = "파산";
            }

            if (_bankruptcyDetailText != null)
            {
                int day = _billService != null ? _billService.CurrentDay : 1;
                long amount = (_billService != null && _billService.ActiveBill != null) ? _billService.ActiveBill.Amount : 0;
                _bankruptcyDetailText.text = $"{day}일차 고지서 {amount:N0}원 미납으로 파산하였습니다.";
            }

            if (_bankruptcyCoinLossText != null)
            {
                _bankruptcyCoinLossText.text = "보유 코인이 모두 몰수되며, 1일차부터 다시 시작합니다. (영구 업그레이드는 유지됩니다)";
            }
        }

        private void HandleContinueClicked()
        {
            HideAll();
            GameManager.Instance?.ContinueRun();
        }

        private void HandleRestartClicked()
        {
            HideAll();
            GameManager.Instance?.StartNewRun();
        }

        /// <summary>
        /// 업그레이드는 정산창 위에 덮는 것이 아니라 **탭 화면으로 전환**한다.
        /// 원작에서 업그레이드와 고지서는 같은 메뉴의 두 탭이고, 거기서 계속하기를 눌러야
        /// 다음 런이 시작된다. 오버레이로 띄우면 정산 내용과 상점이 겹쳐 둘 다 읽히지 않는다.
        /// </summary>
        private void HandleUpgradeClicked()
        {
            if (_billPanel == null)
            {
                return;
            }

            HideAll();
            _billPanel.ShowAsTab(BillPanelController.Tab.Upgrade);
        }

        private void HandleMainMenuClicked()
        {
            HideAll();
            SceneManager.LoadScene(MainMenuSceneName);
        }
    }
}
