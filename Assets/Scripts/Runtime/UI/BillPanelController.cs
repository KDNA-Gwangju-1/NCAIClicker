using System;
using System.Collections;
using System.Globalization;
using NCAIClicker.Core;
using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 고지서 화면. 같은 패널이 두 가지 상태로 쓰인다 (이슈 #34).
    ///
    /// - <b>모달</b>: 정산창에서 "납부하기" 를 누르면 뜬다. 상단 탭 줄이 없다
    /// - <b>탭</b>: 다음 턴 시작 전에 남은 날짜와 목표를 확인하는 화면. 우하단 "계속하기" 가 런을 연다
    ///
    /// 두 벌로 만들지 않는 이유: 금액과 기한 표기가 조용히 어긋난다. 실제로 결과 화면이
    /// 그렇게 갈라진 적이 있다.
    /// </summary>
    public class BillPanelController : MonoBehaviour
    {
        [Header("루트")]
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private GameObject _tabBar;
        [SerializeField] private GameObject _continueRow;

        [Header("탭")]
        [SerializeField] private GameObject _billTabRoot;
        [SerializeField] private GameObject _upgradeTabRoot;

        // 반지 탭 (#183). 업그레이드 탭과 같은 방식이다 — 처음 펼칠 때 프리팹을 한 번 심고
        // 이후에는 켜고 끄기만 한다.
        [SerializeField] private GameObject _ringTabRoot;
        [SerializeField] private Button _ringTabButton;
        [SerializeField] private RectTransform _ringContent;
        [SerializeField] private GameObject _ringShopPrefab;
        [SerializeField] private Transform _upgradeContent;
        [SerializeField] private GameObject _upgradeShopPrefab;
        [SerializeField] private Button _billTabButton;
        [SerializeField] private Button _upgradeTabButton;

        // 저금통 도감 탭 (#299). 반지 탭과 같은 방식이다.
        [SerializeField] private GameObject _codexTabRoot;
        [SerializeField] private Button _codexTabButton;
        [SerializeField] private Transform _codexContent;
        [SerializeField] private GameObject _codexPrefab;

        [Header("고지서 종이")]
        [SerializeField] private TextMeshProUGUI _issuerText;
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _amountText;
        [SerializeField] private TextMeshProUGUI _dueLabelText;
        [SerializeField] private TextMeshProUGUI _dueValueText;

        [Header("보유 코인 및 레거시 포인트")]
        [SerializeField] private TextMeshProUGUI _balanceText;
        [SerializeField] private TextMeshProUGUI _legacyPointText;
        [SerializeField] private LegacyPointHint _legacyPointHint;

        [Header("버튼")]
        [SerializeField] private Button _payButton;
        [SerializeField] private TextMeshProUGUI _payCaptionText;
        [SerializeField] private Button _laterButton;
        [SerializeField] private Button _loanButton;
        [SerializeField] private TextMeshProUGUI _loanCaptionText;
        [SerializeField] private Button _repayButton;
        [SerializeField] private TextMeshProUGUI _repayCaptionText;
        [SerializeField] private Button _continueButton;
        [SerializeField] private Button _declareBankruptcyButton;
        [SerializeField] private TextMeshProUGUI _declareBankruptcyCaptionText;

        // 되돌릴 수 없는 선택이라 확인을 한 번 받는다 (#175). MainMenuController 의
        // 저장 덮어쓰기 확인창과 같은 방식이다 — 새 패턴을 만들지 않는다.
        [SerializeField] private GameObject _bankruptcyConfirmPanel;
        [SerializeField] private Button _bankruptcyConfirmYesButton;
        [SerializeField] private Button _bankruptcyConfirmNoButton;

        // 대출 금액 선택창 (이슈 #273). 파산 확인창과 같은 방식으로 패널 위에 겹쳐 띄운다.
        // 기본값은 부족분, 상한은 고지서 전액이다 — 상한은 BillManager.TryTakeLoan 이 정한 한도와 같다.
        [Header("대출 금액 선택")]
        [SerializeField] private GameObject _loanPickerPanel;
        [SerializeField] private Slider _loanAmountSlider;
        [SerializeField] private TextMeshProUGUI _loanAmountText;
        [SerializeField] private TextMeshProUGUI _loanPreviewText;
        [SerializeField] private TextMeshProUGUI _loanShortfallWarningText;
        [SerializeField] private Button _loanShortfallPresetButton;
        [SerializeField] private Button _loanFullPresetButton;
        [SerializeField] private Button _loanConfirmButton;
        [SerializeField] private Button _loanCancelButton;

        [Header("스킬 트리 안내")]
        [SerializeField] private GameObject _skillTreeNoticePanel;
        [SerializeField] private Button _skillTreeNoticeConfirmButton;

        // 다른 프리팹(Target, HammerSwingController)과 같은 방식으로 프리팹에 직렬화해 둔다.
        // 씬을 건너 주입할 통로를 새로 만들지 않기 위해서다.
        [Header("데이터")]
        [SerializeField] private BalanceData _balanceData;

        private IBillService _billService;
        private IEconomyService _economyService;
        private ILegacyService _legacyService;
        private IGameFlowService _gameFlowService;
        private Coroutine _paidFeedbackRoutine;

        /// <summary>선택창을 연 순간의 부족분. 창이 열려 있는 동안 기본값·경고 문구의 기준이다 (이슈 #273).</summary>
        private long _loanPickerShortfall;

        /// <summary>이번 회차에 마지막 단계를 냈다 — 이후 고지서는 재발행이라 "더 벌기" 안내를 붙인다 (이슈 #271).</summary>
        private bool _isFinalStageCleared;

        /// <summary>어느 상태로 열려 있는가. 탭일 때만 탭 줄과 계속하기가 보인다.</summary>
        public enum Mode
        {
            Modal,
            Tab,
            PrestigeOnly,
        }

        /// <summary>탭 화면에서 무엇을 보고 있는가.</summary>
        public enum Tab
        {
            Bill,
            Upgrade,
            Ring,
            Codex,
        }

        private Mode _mode = Mode.Modal;
        private Tab _tab = Tab.Bill;

        public Tab CurrentTab => _tab;

        /// <summary>패널이 닫힐 때 알린다. 정산창이 이걸 듣고 자기 화면을 다시 켠다.</summary>
        public event Action Closed;

        public bool IsOpen => _panelRoot != null && _panelRoot.activeSelf;
        public Mode CurrentMode => _mode;

        private void Awake()
        {
            Close();
        }

        private void OnEnable()
        {
            GameEvents.OnBillIssued += HandleBillIssued;
            GameEvents.OnBalanceChanged += HandleBalanceChanged;
            GameEvents.OnBillPaid += HandleBillPaid;
            GameEvents.OnStageGoalReached += HandleStageGoalReached;
            GameEvents.OnBankrupt += HandleBankrupt;

            if (_payButton != null)
            {
                _payButton.onClick.AddListener(HandlePayClicked);
            }
            if (_laterButton != null)
            {
                // "아직" 은 고지서 탭이 아니라 스킬 트리 탭으로 이동하며, 최초 1회 투자 안내를 표시한다 (이슈 #249).
                _laterButton.onClick.AddListener(HandleLaterClicked);
            }
            if (_loanButton != null)
            {
                _loanButton.onClick.AddListener(HandleLoanClicked);
            }
            if (_repayButton != null)
            {
                _repayButton.onClick.AddListener(HandleRepayClicked);
            }
            if (_continueButton != null)
            {
                _continueButton.onClick.AddListener(HandleContinueClicked);
            }
            if (_billTabButton != null)
            {
                _billTabButton.onClick.AddListener(ShowBillTab);
            }
            if (_upgradeTabButton != null)
            {
                _upgradeTabButton.onClick.AddListener(ShowUpgradeTab);
            }
            if (_ringTabButton != null)
            {
                _ringTabButton.onClick.AddListener(ShowRingTab);
            }
            if (_codexTabButton != null)
            {
                _codexTabButton.onClick.AddListener(ShowCodexTab);
            }
            if (_declareBankruptcyButton != null)
            {
                _declareBankruptcyButton.onClick.AddListener(ShowBankruptcyConfirm);
            }
            if (_bankruptcyConfirmYesButton != null)
            {
                _bankruptcyConfirmYesButton.onClick.AddListener(HandleBankruptcyConfirmed);
            }
            if (_bankruptcyConfirmNoButton != null)
            {
                _bankruptcyConfirmNoButton.onClick.AddListener(HideBankruptcyConfirm);
            }
            if (_skillTreeNoticeConfirmButton != null)
            {
                _skillTreeNoticeConfirmButton.onClick.AddListener(HideSkillTreeNotice);
            }
            if (_loanAmountSlider != null)
            {
                _loanAmountSlider.onValueChanged.AddListener(HandleLoanAmountChanged);
            }
            if (_loanShortfallPresetButton != null)
            {
                _loanShortfallPresetButton.onClick.AddListener(SelectShortfallLoanAmount);
            }
            if (_loanFullPresetButton != null)
            {
                _loanFullPresetButton.onClick.AddListener(SelectFullLoanAmount);
            }
            if (_loanConfirmButton != null)
            {
                _loanConfirmButton.onClick.AddListener(HandleLoanConfirmClicked);
            }
            if (_loanCancelButton != null)
            {
                _loanCancelButton.onClick.AddListener(HideLoanPicker);
            }
            HideBankruptcyConfirm();
            HideLoanPicker();
            HideSkillTreeNoticePanelOnly();
            RestorePostPaymentView();
        }

        private void OnDisable()
        {
            GameEvents.OnBillIssued -= HandleBillIssued;
            GameEvents.OnBalanceChanged -= HandleBalanceChanged;
            GameEvents.OnBillPaid -= HandleBillPaid;
            GameEvents.OnStageGoalReached -= HandleStageGoalReached;
            GameEvents.OnBankrupt -= HandleBankrupt;
            if (_paidFeedbackRoutine != null)
            {
                StopCoroutine(_paidFeedbackRoutine);
                _paidFeedbackRoutine = null;
                ResetPaidFeedbackVisuals();
            }

            if (_payButton != null)
            {
                _payButton.onClick.RemoveListener(HandlePayClicked);
            }
            if (_laterButton != null)
            {
                _laterButton.onClick.RemoveListener(HandleLaterClicked);
            }
            if (_loanButton != null)
            {
                _loanButton.onClick.RemoveListener(HandleLoanClicked);
            }
            if (_repayButton != null)
            {
                _repayButton.onClick.RemoveListener(HandleRepayClicked);
            }
            if (_continueButton != null)
            {
                _continueButton.onClick.RemoveListener(HandleContinueClicked);
            }
            if (_billTabButton != null)
            {
                _billTabButton.onClick.RemoveListener(ShowBillTab);
            }
            if (_upgradeTabButton != null)
            {
                _upgradeTabButton.onClick.RemoveListener(ShowUpgradeTab);
            }
            if (_ringTabButton != null)
            {
                _ringTabButton.onClick.RemoveListener(ShowRingTab);
            }
            if (_codexTabButton != null)
            {
                _codexTabButton.onClick.RemoveListener(ShowCodexTab);
            }
            if (_declareBankruptcyButton != null)
            {
                _declareBankruptcyButton.onClick.RemoveListener(ShowBankruptcyConfirm);
            }
            if (_bankruptcyConfirmYesButton != null)
            {
                _bankruptcyConfirmYesButton.onClick.RemoveListener(HandleBankruptcyConfirmed);
            }
            if (_bankruptcyConfirmNoButton != null)
            {
                _bankruptcyConfirmNoButton.onClick.RemoveListener(HideBankruptcyConfirm);
            }
            if (_skillTreeNoticeConfirmButton != null)
            {
                _skillTreeNoticeConfirmButton.onClick.RemoveListener(HideSkillTreeNotice);
            }
            if (_loanAmountSlider != null)
            {
                _loanAmountSlider.onValueChanged.RemoveListener(HandleLoanAmountChanged);
            }
            if (_loanShortfallPresetButton != null)
            {
                _loanShortfallPresetButton.onClick.RemoveListener(SelectShortfallLoanAmount);
            }
            if (_loanFullPresetButton != null)
            {
                _loanFullPresetButton.onClick.RemoveListener(SelectFullLoanAmount);
            }
            if (_loanConfirmButton != null)
            {
                _loanConfirmButton.onClick.RemoveListener(HandleLoanConfirmClicked);
            }
            if (_loanCancelButton != null)
            {
                _loanCancelButton.onClick.RemoveListener(HideLoanPicker);
            }
        }

        /// <summary>조립 지점이 넣어 준다. 소비처가 구현 클래스를 직접 찾지 않는다.</summary>
        public void SetServices(IBillService billService, IEconomyService economyService = null,
                                ILegacyService legacyService = null, IGameFlowService gameFlowService = null)
        {
            _billService = billService;
            _economyService = economyService;
            _legacyService = legacyService ?? (economyService as ILegacyService);
            _gameFlowService = gameFlowService;
            RestorePostPaymentView();
        }

        /// <summary>검증에서 데이터만 갈아끼울 때 쓴다.</summary>
        public void SetBalanceData(BalanceData balanceData)
        {
            _balanceData = balanceData;
        }

        /// <summary>정산창에서 납부하러 들어올 때.</summary>
        public void ShowAsModal() => Show(Mode.Modal);

        /// <summary>다음 턴 준비 화면으로 열 때. 어느 탭을 펼칠지 고른다.</summary>
        public void ShowAsTab(Tab tab = Tab.Bill)
        {
            _tab = tab;
            Show(Mode.Tab);
        }

        /// <summary>
        /// 파산 후 프레스티지(보석함) 전용 단일 화면으로 연다.
        /// 상단 탭바를 숨기고 배경을 100% 완전 불투명하게 처리하여 뒤의 게임 씬과 HUD를 완전히 가린다.
        /// </summary>
        public void ShowAsPrestige(int cycleNumber = -1)
        {
            _tab = Tab.Ring;
            EnsureServices();
            var cycle = cycleNumber > 0 ? cycleNumber : (_billService != null ? _billService.CurrentCycle : 1);
            UpdateContinueButtonLabel(cycle);
            Show(Mode.PrestigeOnly);
        }

        private void UpdateContinueButtonLabel(int cycleNumber)
        {
            if (_continueButton == null)
            {
                return;
            }
            var label = _continueButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = $"사이클 {cycleNumber} 시작";
            }
        }

        private void ResetContinueButtonLabel()
        {
            if (_continueButton == null)
            {
                return;
            }
            var label = _continueButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = "계속하기";
            }
        }

        private void ShowBillTab() => ShowAsTab(Tab.Bill);

        private void ShowUpgradeTab() => ShowAsTab(Tab.Upgrade);
        private void ShowRingTab() => ShowAsTab(Tab.Ring);
        private void ShowCodexTab() => ShowAsTab(Tab.Codex);

        private void Show(Mode mode)
        {
            _mode = mode;
            HideLoanPicker();

            if (_panelRoot != null)
            {
                _panelRoot.SetActive(true);

                // 파산 단일 화면일 때는 뒤가 전혀 비치지 않게 100% 불투명 처리한다.
                var bg = _panelRoot.GetComponent<Image>();
                if (bg != null)
                {
                    bg.color = mode == Mode.PrestigeOnly
                        ? new Color(0.06f, 0.05f, 0.04f, 1f)
                        : new Color(0f, 0f, 0f, 0.8f);
                }
            }

            // 같은 캔버스의 형제끼리는 계층 순서대로 그려진다. 먼저 생성된 쪽이 뒤로 가므로
            // 열 때마다 맨 앞으로 올린다 — 생성 순서에 기대면 조립 순서만 바뀌어도 가려진다.
            transform.SetAsLastSibling();

            if (_tabBar != null)
            {
                _tabBar.SetActive(mode == Mode.Tab);
            }
            if (_continueRow != null)
            {
                _continueRow.SetActive(mode == Mode.Tab || mode == Mode.PrestigeOnly);
            }

            if (mode != Mode.PrestigeOnly)
            {
                ResetContinueButtonLabel();
            }

            RenderTabs();
            Render();
        }

        /// <summary>
        /// 탭을 갈아 끼운다. 업그레이드 상점은 MainMenu 용으로 만들어진 패널이라 (#91)
        /// 여기서 처음 펼칠 때 한 번만 심고, 이후에는 켜고 끄기만 한다.
        /// 패널은 살아날 때마다 스스로 다시 배선하고 그린다.
        /// </summary>
        private void RenderTabs()
        {
            // 모달일 때는 탭이 없다. 고지서만 보인다.
            var showUpgrade = _mode == Mode.Tab && _tab == Tab.Upgrade;
            // 반지는 파산 후 프레스티지 화면에서만 보인다 (이슈 #291). 매일 여는 탭 모드에서는
            // 반지 탭 버튼 자체를 숨긴다 — 눌러도 살 수 없는 상점을 보여 줄 이유가 없다.
            // 구매 규칙의 정본은 EconomyManager.TryPurchaseRing 이고, 이 줄은 화면을 맞출 뿐이다.
            var showRing = _mode == Mode.PrestigeOnly && _tab == Tab.Ring;
            var showCodex = _mode == Mode.Tab && _tab == Tab.Codex;
            if (_ringTabButton != null)
            {
                _ringTabButton.gameObject.SetActive(_mode == Mode.PrestigeOnly);
            }

            if (_billTabRoot != null)
            {
                _billTabRoot.SetActive(!showUpgrade && !showRing && !showCodex);
            }
            if (_upgradeTabRoot != null)
            {
                _upgradeTabRoot.SetActive(showUpgrade);
            }
            if (_ringTabRoot != null)
            {
                _ringTabRoot.SetActive(showRing);
            }
            if (_codexTabRoot != null)
            {
                _codexTabRoot.SetActive(showCodex);
            }

            if (showUpgrade && _upgradeContent != null && _upgradeShopPrefab != null && _upgradeContent.childCount == 0)
            {
                Instantiate(_upgradeShopPrefab, _upgradeContent, false);
            }
            if (showRing && _ringContent != null && _ringShopPrefab != null && _ringContent.childCount == 0)
            {
                Instantiate(_ringShopPrefab, _ringContent, false);
            }
            if (showCodex && _codexContent != null && _codexPrefab != null && _codexContent.childCount == 0)
            {
                Instantiate(_codexPrefab, _codexContent, false);
            }

            // 어느 탭에 있는지 버튼 색으로 알린다. 업그레이드를 보고 있는데 고지서가 켜진 것처럼
            // 보이면 탭이 안 먹은 줄 안다.
            SetTabSelected(_billTabButton, !showUpgrade && !showRing && !showCodex);
            SetTabSelected(_upgradeTabButton, showUpgrade);
            SetTabSelected(_ringTabButton, showRing);
            SetTabSelected(_codexTabButton, showCodex);
        }

        private static void SetTabSelected(Button tab, bool selected)
        {
            if (tab == null)
            {
                return;
            }

            var label = tab.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.color = selected ? Color.white : new Color(0.55f, 0.48f, 0.38f);
            }

            var outline = tab.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = selected
                    ? new Color(0.91f, 0.69f, 0.29f)
                    : new Color(0.22f, 0.19f, 0.15f);
            }
        }

        public void Close(bool notifyClosed = true)
        {
            var wasOpen = IsOpen;
            HideLoanPicker();
            if (_panelRoot != null)
            {
                _panelRoot.SetActive(false);
            }
            if (wasOpen && notifyClosed)
            {
                Closed?.Invoke();
            }
        }

        /// <summary>
        /// 조립이 한 번에 성공하지 못했을 때를 대비한다. 로더가 넣어 주는 것이 정상 경로지만,
        /// 놓치면 화면이 "고지서 없음" 으로 보여 납부할 방법이 사라진다 — 실제로 그렇게 됐다.
        /// 구현 클래스가 아니라 인터페이스로만 찾는다 (AGENTS.md).
        /// </summary>
        private void EnsureServices()
        {
            if (_legacyService == null && _economyService is ILegacyService legacyFallback)
            {
                _legacyService = legacyFallback;
            }
        }

        private void Render()
        {
            EnsureServices();
            var bill = _billService?.ActiveBill;

            if (_balanceText != null)
            {
                if (_mode == Mode.PrestigeOnly)
                {
                    _balanceText.text = string.Empty;
                }
                else
                {
                    // 원작처럼 달러 기호와 숫자로 현재 보유 코인을 깔끔하게 표시한다.
                    var economy = _economyService;
                    _balanceText.text = economy != null ? $"${economy.CurrentCoin:N0}" : "$0";
                }
            }

            if (_legacyPointText != null)
            {
                // 원작처럼 레거시 포인트를 아이콘 옆에 표시한다.
                var legacy = _legacyService;
                _legacyPointText.text = legacy != null ? $"{legacy.CurrentLegacyPoints:N0}" : "0";
            }

            if (_legacyPointHint != null && _balanceData != null)
            {
                _legacyPointHint.Bind(_balanceData);
            }

            if (_issuerText != null || _titleText != null)
            {
                // 씨앗값은 고지서가 가진 값으로 만든다. 같은 고지서면 늘 같은 이름이 나오고,
                // 고지서가 바뀌면 이름도 바뀐다. 난수를 따로 굴리면 화면을 다시 열 때마다 바뀐다.
                var name = _balanceData != null ? _balanceData.GetBillName(MakeNameSeed(bill)) : null;

                if (_issuerText != null)
                {
                    _issuerText.text = name != null ? name.Issuer : string.Empty;
                }
                if (_titleText != null)
                {
                    _titleText.text = name != null ? name.Title : "고지서";
                }
            }

            if (_amountText != null)
            {
                _amountText.text = bill != null ? $"${bill.Amount:N0}" : "—";
            }

            RenderDue(bill);
            RenderButtons(bill);
        }

        /// <summary>고지서를 식별하는 씨앗값. 발행일과 금액이 다르면 다른 고지서다.</summary>
        private static int MakeNameSeed(Bill bill)
        {
            if (bill == null)
            {
                return 0;
            }
            unchecked
            {
                return bill.IssuedDay * 397 ^ (int)(bill.Amount % int.MaxValue);
            }
        }

        private void RenderDue(Bill bill)
        {
            var daysLeft = GetDaysLeftAfterToday(bill);

            // 기한 당일이면 남은 일수를 세지 않고 "지금 납부!" 로 바꾼다 (원작).
            // 숫자로 "0일" 이라고 쓰면 아직 하루가 남은 것처럼 읽힌다.
            var isDueToday = bill != null && !bill.IsPaid && daysLeft <= 0;

            if (_dueLabelText != null)
            {
                _dueLabelText.text = "납부 기한";
            }
            if (_dueValueText != null)
            {
                var isPaid = bill == null || bill.IsPaid;
                _dueValueText.text = isPaid
                    ? "납부 완료"
                    : isDueToday ? "지금 납부!" : $"{daysLeft}일";
                if (!isPaid)
                {
                    _dueValueText.color = new Color(0.141f, 0.102f, 0.071f, 1f);
                }
            }
        }

        /// <summary>
        /// 고지서 화면은 그날 런이 끝난 뒤에만 열린다. 오늘은 이미 썼으므로 남은 날에서 뺀다 — 원작도 첫 정산에
        /// 3일짜리 고지서를 "2일 남음", 새로 받은 5일짜리를 "5일" 로 보여 준다 (#247). 새 고지서는 다음 날부터
        /// 세도록 발행되므로(IssueBill(_currentDay + 1)) 이 계산으로 정확히 기한 일수가 나온다.
        /// IBillService.DaysLeft 는 런 중 HUD 용으로 오늘을 포함해 센다.
        /// </summary>
        private int GetDaysLeftAfterToday(Bill bill)
        {
            if (bill == null || _billService == null)
            {
                return 0;
            }
            return Mathf.Max(0, bill.DueDay - _billService.CurrentDay);
        }

        private void RenderButtons(Bill bill)
        {
            var hasUnpaidBill = bill != null && !bill.IsPaid;
            var daysLeft = GetDaysLeftAfterToday(bill);
            var isDueToday = hasUnpaidBill && daysLeft <= 0;

            if (_payButton != null)
            {
                _payButton.gameObject.SetActive(hasUnpaidBill);
            }
            if (_payCaptionText != null && !hasUnpaidBill)
            {
                _payCaptionText.text = string.Empty;
            }

            // "아직" 은 탭 화면으로 빠지는 버튼이다. 이미 탭 화면이면 할 일이 없으므로 감춘다.
            // 마감 당일에는 납부·대출 선택만 남기는 원작 규칙에 따라 숨긴다.
            if (_laterButton != null)
            {
                _laterButton.gameObject.SetActive(_mode == Mode.Modal && hasUnpaidBill && !isDueToday);
            }

            var hasActiveLoan = _billService != null && _billService.LoanDailyCut > 0f;

            if (_loanButton != null)
            {
                var isUnlocked = _billService != null && _billService.IsLoanUnlocked;
                var cooldownDaysLeft = _billService != null ? _billService.LoanCooldownDaysRemaining : 0;
                var shortfall = hasUnpaidBill ? CalculateShortfall(bill) : 0L;
                var canLoan = hasUnpaidBill && !hasActiveLoan && isUnlocked && cooldownDaysLeft <= 0 && shortfall > 0L;
                _loanButton.interactable = canLoan;
                if (_loanCaptionText != null)
                {
                    if (hasActiveLoan)
                    {
                        _loanCaptionText.text = "대출 완료";
                    }
                    else if (!hasUnpaidBill)
                    {
                        _loanCaptionText.text = string.Empty;
                    }
                    else if (!isUnlocked)
                    {
                        // 해금 순번은 CSV(loan_unlock_bill_index) 원본이다 (AGENTS.md 데이터 규칙, 이슈 #306).
                        // 값이 곧 "몇 번째 고지서부터" 다 — BillManager.IsLoanUnlocked 는 손에 든 고지서의 1부터 센
                        // 순번(_billIndex - 1)이 이 값 이상일 때 연다. +1 을 붙이면 한 장 늦게 안내한다 (#273 에서 고침).
                        var unlockOrdinal = _balanceData != null ? _balanceData.Bill.LoanUnlockBillIndex : 0;
                        _loanCaptionText.text = $"{unlockOrdinal}번째 고지서부터";
                    }
                    else if (cooldownDaysLeft > 0)
                    {
                        _loanCaptionText.text = $"{cooldownDaysLeft}일 후 가능";
                    }
                    else if (shortfall <= 0L)
                    {
                        // 부족분이 0 이하면 대출 불가 (이슈 #273 DoD). 잠긴 이유가 안 보이면 고장으로 읽힌다.
                        _loanCaptionText.text = "잔액으로 충분";
                    }
                    else
                    {
                        _loanCaptionText.text = string.Empty;
                    }
                }
            }

            // 활성 대출이 있을 때만 상환 버튼과 상환액을 보인다 (이슈 #272 DoD).
            if (_repayButton != null)
            {
                _repayButton.gameObject.SetActive(hasActiveLoan);
            }
            if (_repayCaptionText != null)
            {
                _repayCaptionText.text = hasActiveLoan && _billService != null
                    ? $"상환액 ${_billService.LoanOwedAmount:N0}"
                    : string.Empty;
            }

            // 자발적 파산 (#175). 고지서가 살아 있을 때만 의미가 있다 — 낼 것이 없는데
            // 파산을 선언하면 잃기만 하고 얻는 것이 없다.
            if (_declareBankruptcyButton != null)
            {
                _declareBankruptcyButton.interactable = hasUnpaidBill;
            }
            if (_declareBankruptcyCaptionText != null)
            {
                // 잠긴 이유가 안 보이는 버튼은 고장으로 읽힌다.
                _declareBankruptcyCaptionText.text = hasUnpaidBill ? "회차를 접는다" : "낼 고지서 없음";
            }
        }

        private void HandlePayClicked()
        {
            var bill = _billService?.ActiveBill;
            if (bill == null || _billService == null)
            {
                return;
            }

            if (_billService.TryPay(bill))
            {
                // 납부 성공 시 기존 고지서 종이에 "납부 완료"가 즉시 표시된다 (이슈 #249).
                // 이 피드백이 갱신됨과 동시에 OnPerkOffered 에 의해 퍽 3장 선택 창이 위에 뜬다.
                if (_payCaptionText != null)
                {
                    _payCaptionText.text = string.Empty;
                }
                Render();
                return;
            }

            // 잔액 부족으로 납부에 실패했을 때는 부족액 캡션을 표시한다 (이슈 #212).
            // 마감 당일이라도 즉시 파산시키지 않고 부족액을 보여주어 대출로 충당할 기회를 제공한다.
            // 최종 미납 파산은 GameManager.ContinueRun 에서 TryCloseDay 가 단일 확정한다 (이슈 #211).
            if (_payCaptionText != null)
            {
                var coin = _economyService != null ? _economyService.CurrentCoin : 0L;
                _payCaptionText.text = $"${bill.Amount - coin:N0} 부족";
            }
        }

        private void HandleBillIssued(Bill newBill)
        {
            // 엔딩을 본 뒤의 고지서는 마지막 단계 재발행이다. 낼 이유가 "더 벌기" 라는 걸 알린다 (이슈 #271).
            if (_isFinalStageCleared && _payCaptionText != null)
            {
                _payCaptionText.text = "고지서는 다 냈다 · 이제부터는 더 벌기";
            }

            // 퍽 선택 완료 직후 새 고지서가 발행되면 모달 상태로 금액과 납기 일수를 보여준다 (이슈 #249).
            if (IsOpen && _mode != Mode.PrestigeOnly)
            {
                ShowAsModal();
            }
        }

        /// <summary>
        /// 판정은 EndingController 와 같다 — 도달한 단계 번호로 본다. IsMaxStage 는 마지막 직전 단계를
        /// 낸 순간 이미 참이라 쓸 수 없다. 엔딩 컨트롤러의 상태를 직접 읽지 않고 같은 이벤트를 듣는다 (PATTERNS 3절).
        /// </summary>
        private void HandleStageGoalReached(int stageNumber)
        {
            if (_balanceData != null && stageNumber >= _balanceData.Stages.Count)
            {
                _isFinalStageCleared = true;
            }
        }

        private void HandleBankrupt()
        {
            _isFinalStageCleared = false;
        }

        private void HandleLaterClicked()
        {
            // 새 고지서 확인 흐름 상태인 경우 투자 메뉴 상태로 전이합니다 (이슈 #249).
            if (_billService != null && _billService.PaymentFlowState == PostPaymentFlowState.NewBillConfirmation)
            {
                _billService.TryEnterInvestmentMenu();
            }

            // 고지서 모달에서 아직 버튼을 누르면 스킬 트리 탭으로 화면을 전환합니다.
            ShowUpgradeTab();
            ShowSkillTreeNoticeIfNeeded();
        }

        private void RestorePostPaymentView()
        {
            var billService = _billService;
            if (billService == null)
            {
                return;
            }

            switch (billService.PaymentFlowState)
            {
                case PostPaymentFlowState.PaidFeedback:
                    ShowAsModal();
                    StartPaidFeedbackTransition();
                    break;
                case PostPaymentFlowState.PerkSelection:
                case PostPaymentFlowState.NewBillConfirmation:
                    ShowAsModal();
                    break;
                case PostPaymentFlowState.InvestmentMenu:
                    ShowAsTab(Tab.Upgrade);
                    ShowSkillTreeNoticeIfNeeded();
                    break;
            }
        }

        private void StartPaidFeedbackTransition()
        {
            if (_paidFeedbackRoutine == null && isActiveAndEnabled)
            {
                _paidFeedbackRoutine = StartCoroutine(ShowPerksAfterPaidFeedback());
            }
        }

        private void ResetPaidFeedbackVisuals()
        {
            if (_dueValueText != null)
            {
                _dueValueText.rectTransform.localScale = Vector3.one;
                _dueValueText.rectTransform.localRotation = Quaternion.identity;
                var paperRect = _dueValueText.transform.parent as RectTransform;
                if (paperRect != null)
                {
                    var canvasGroup = paperRect.GetComponent<CanvasGroup>();
                    if (canvasGroup != null)
                    {
                        canvasGroup.alpha = 1f;
                    }
                }
            }
        }

        private IEnumerator ShowPerksAfterPaidFeedback()
        {
            if (!Application.isPlaying)
            {
                _paidFeedbackRoutine = null;
                _billService?.TryConfirmPaidFeedback();
                yield break;
            }

            var originalScale = Vector3.one;
            var originalRotation = Quaternion.identity;
            RectTransform paperRect = null;
            CanvasGroup paperCanvasGroup = null;
            var originalPaperPos = Vector2.zero;
            var originalPaperScale = Vector3.one;

            if (_dueValueText != null)
            {
                originalScale = _dueValueText.rectTransform.localScale;
                originalRotation = _dueValueText.rectTransform.localRotation;
                _dueValueText.text = "납부 완료";
                _dueValueText.color = new Color(0.85f, 0.16f, 0.14f, 1f);

                paperRect = _dueValueText.transform.parent as RectTransform;
                if (paperRect != null)
                {
                    originalPaperPos = paperRect.anchoredPosition;
                    originalPaperScale = paperRect.localScale;
                    paperCanvasGroup = paperRect.GetComponent<CanvasGroup>();
                    if (paperCanvasGroup == null)
                    {
                        paperCanvasGroup = paperRect.gameObject.AddComponent<CanvasGroup>();
                    }
                    paperCanvasGroup.alpha = 1f;
                }
            }

            // 1단계: 도장 쾅 찍히는 펀치 스케일 연출 (0.15초)
            var stampDuration = 0.15f;
            var elapsed = 0f;
            while (elapsed < stampDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / stampDuration);
                var smoothT = Mathf.SmoothStep(0f, 1f, t);

                if (_dueValueText != null)
                {
                    _dueValueText.rectTransform.localScale = Vector3.LerpUnclamped(Vector3.one * 1.45f, Vector3.one, smoothT);
                    _dueValueText.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(8f, -3f, smoothT));
                }
                yield return null;
            }

            if (_dueValueText != null)
            {
                _dueValueText.rectTransform.localScale = Vector3.one;
                _dueValueText.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -3f);
            }

            // 2단계: 납부 완료 상태 인지 대기 (약 0.45초 유지)
            yield return new WaitForSecondsRealtime(0.45f);

            // 3단계: 고지서 종이 퇴장 및 부드러운 전환 연출 (0.25초)
            var exitDuration = 0.25f;
            elapsed = 0f;
            while (elapsed < exitDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / exitDuration);
                var smoothT = Mathf.SmoothStep(0f, 1f, t);

                if (paperRect != null)
                {
                    paperRect.anchoredPosition = originalPaperPos + new Vector2(0f, -80f * smoothT);
                    paperRect.localScale = Vector3.Lerp(originalPaperScale, originalPaperScale * 0.94f, smoothT);
                    if (paperCanvasGroup != null)
                    {
                        paperCanvasGroup.alpha = Mathf.Lerp(1f, 0f, smoothT);
                    }
                }
                yield return null;
            }

            // 원상 복구 후 퍽 선택 화면으로 전이
            if (paperRect != null)
            {
                paperRect.anchoredPosition = originalPaperPos;
                paperRect.localScale = originalPaperScale;
                if (paperCanvasGroup != null)
                {
                    paperCanvasGroup.alpha = 1f;
                }
            }
            if (_dueValueText != null)
            {
                _dueValueText.rectTransform.localScale = originalScale;
                _dueValueText.rectTransform.localRotation = originalRotation;
            }

            _paidFeedbackRoutine = null;
            _billService?.TryConfirmPaidFeedback();
        }

        private const string SkillTreeNoticeKey = "HasSeenSkillTreeNotice";

        private void ShowSkillTreeNoticeIfNeeded()
        {
            if (PlayerPrefs.GetInt(SkillTreeNoticeKey, 0) == 0)
            {
                if (_skillTreeNoticePanel != null)
                {
                    _skillTreeNoticePanel.SetActive(true);
                }
            }
        }

        private void HideSkillTreeNotice()
        {
            PlayerPrefs.SetInt(SkillTreeNoticeKey, 1);
            PlayerPrefs.Save();
            HideSkillTreeNoticePanelOnly();
        }

        private void HideSkillTreeNoticePanelOnly()
        {
            if (_skillTreeNoticePanel != null)
            {
                _skillTreeNoticePanel.SetActive(false);
            }
        }

        /// <summary>
        /// 바로 빌리지 않고 금액 선택창을 연다 (이슈 #273). 예전에는 늘 고지서 전액을 빌렸는데,
        /// 징수율이 빌린 비율에 비례하므로(LoanTerms.CalculateDailyCut) "적게 빌리면 덜 뜯긴다" 는 선택이 화면에 없었다.
        /// </summary>
        private void HandleLoanClicked()
        {
            var bill = _billService?.ActiveBill;
            if (bill == null || _billService == null || _loanPickerPanel == null || _loanAmountSlider == null)
            {
                return;
            }

            _loanPickerShortfall = CalculateShortfall(bill);
            _loanAmountSlider.wholeNumbers = true;
            _loanAmountSlider.minValue = 1f;
            _loanAmountSlider.maxValue = bill.Amount;
            _loanAmountSlider.SetValueWithoutNotify(ClampLoanAmount(_loanPickerShortfall, bill.Amount));
            _loanPickerPanel.SetActive(true);
            RenderLoanPicker();
        }

        /// <summary>부족분 = 고지서 금액 - 보유 코인. 0 이하면 지금 가진 돈으로 낼 수 있다.</summary>
        private long CalculateShortfall(Bill bill)
        {
            var coin = _economyService != null ? _economyService.CurrentCoin : 0L;
            return bill.Amount - coin;
        }

        /// <summary>하한은 1이다 — 오늘은 일부만 빌리고 나머지는 벌어서 채우는 선택도 막지 않는다.</summary>
        private static long ClampLoanAmount(long amount, long billAmount)
        {
            return Math.Max(1L, Math.Min(amount, billAmount));
        }

        private long SelectedLoanAmount => _loanAmountSlider != null ? (long)Mathf.Round(_loanAmountSlider.value) : 0L;

        private void HandleLoanAmountChanged(float value)
        {
            RenderLoanPicker();
        }

        /// <summary>슬라이더로는 정확한 값에 다시 맞추기 어렵다. 두 기준값으로 곧장 돌아가는 단추다.</summary>
        private void SelectShortfallLoanAmount()
        {
            var bill = _billService?.ActiveBill;
            if (bill != null && _loanAmountSlider != null)
            {
                _loanAmountSlider.value = ClampLoanAmount(_loanPickerShortfall, bill.Amount);
            }
        }

        private void SelectFullLoanAmount()
        {
            if (_loanAmountSlider != null)
            {
                _loanAmountSlider.value = _loanAmountSlider.maxValue;
            }
        }

        /// <summary>
        /// 상환액과 징수율은 BillManager 가 대출을 확정할 때 쓰는 바로 그 식(LoanTerms)으로 계산한다.
        /// 화면이 식을 따로 가지면 미리보기와 실제 값이 조용히 어긋난다.
        /// </summary>
        private void RenderLoanPicker()
        {
            var bill = _billService?.ActiveBill;
            if (bill == null)
            {
                return;
            }

            var amount = SelectedLoanAmount;
            if (_loanAmountText != null)
            {
                _loanAmountText.text = $"${amount:N0}";
            }
            if (_loanPreviewText != null)
            {
                if (_balanceData != null)
                {
                    var config = _balanceData.Bill;
                    var owed = LoanTerms.CalculateOwed(amount, config.LoanInterestRate);
                    var cut = LoanTerms.CalculateDailyCut(amount, bill.Amount, config);
                    _loanPreviewText.text = $"갚을 돈 ${owed:N0} (이자 {FormatPercent(config.LoanInterestRate)})\n"
                                            + $"갚을 때까지 수입의 {FormatPercent(cut)} 징수";
                }
                else
                {
                    _loanPreviewText.text = string.Empty;
                }
            }
            if (_loanShortfallWarningText != null)
            {
                _loanShortfallWarningText.text = amount < _loanPickerShortfall
                    ? $"${_loanPickerShortfall - amount:N0} 모자라 이대로는 납부할 수 없습니다"
                    : string.Empty;
            }
        }

        /// <summary>징수율은 하한과 상한 사이를 선형으로 오가 정수 퍼센트로 떨어지지 않는다. 소수 한 자리까지 보인다.</summary>
        private static string FormatPercent(float ratio)
        {
            return (ratio * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        private void HandleLoanConfirmClicked()
        {
            if (_billService == null)
            {
                return;
            }

            var amount = SelectedLoanAmount;
            HideLoanPicker();
            if (_billService.TryTakeLoan(amount))
            {
                if (_loanCaptionText != null)
                {
                    _loanCaptionText.text = "대출 완료";
                }
                Render();
            }
            else
            {
                if (_loanCaptionText != null)
                {
                    _loanCaptionText.text = "대출 실패";
                }
            }
        }

        private void HideLoanPicker()
        {
            if (_loanPickerPanel != null)
            {
                _loanPickerPanel.SetActive(false);
            }
        }

        /// <summary>
        /// 전액 상환만 있다 — 부분 상환 없음(이슈 #272 범위 밖). 성공하면 대출이 사라져
        /// 다음 Render() 에서 일일 징수 표시("대출 완료" 캡션)도 함께 사라진다.
        /// </summary>
        private void HandleRepayClicked()
        {
            if (_billService == null)
            {
                return;
            }

            if (_billService.TryRepayLoan())
            {
                if (_repayCaptionText != null)
                {
                    _repayCaptionText.text = string.Empty;
                }
                Render();
                return;
            }

            // 잔액 부족으로 상환 실패 — HandlePayClicked 의 부족액 캡션과 같은 패턴(이슈 #212).
            if (_repayCaptionText != null)
            {
                var coin = _economyService != null ? _economyService.CurrentCoin : 0L;
                var owed = _billService.LoanOwedAmount;
                _repayCaptionText.text = $"${owed - coin:N0} 부족";
            }
        }

        /// <summary>
        /// 확인창을 띄운다. **여기서 파산시키지 않는다** — 되돌릴 수 없는 선택이라 한 번 더 묻는다.
        /// </summary>
        private void ShowBankruptcyConfirm()
        {
            if (_bankruptcyConfirmPanel != null)
            {
                _bankruptcyConfirmPanel.SetActive(true);
            }
        }

        private void HideBankruptcyConfirm()
        {
            if (_bankruptcyConfirmPanel != null)
            {
                _bankruptcyConfirmPanel.SetActive(false);
            }
        }

        /// <summary>
        /// 확인을 받고 실제로 선언한다. 마감 미납 파산과 같은 처리를 탄다 (IBillService, #175).
        /// 코인·단계는 사라지고 레거시 포인트와 반지는 남는다 (#183).
        /// 파산 확정 후에는 곧바로 런으로 직행하지 않고 프레스티지(보석함) 단일 화면으로 이동한다.
        /// </summary>
        private void HandleBankruptcyConfirmed()
        {
            HideBankruptcyConfirm();
            _billService?.DeclareBankruptcy();

            // 파산 후 정비를 위해 보석함 단일 화면을 띄운다.
            // 우측 하단 [사이클 N 시작]을 누르면 1일차 새 런으로 진입한다.
            ShowAsPrestige();
        }

        private void HandleBalanceChanged(long currentBalance)
        {
            if (_balanceText != null && _mode != Mode.PrestigeOnly)
            {
                _balanceText.text = $"${currentBalance:N0}";
            }
        }

        private void HandleBillPaid(Bill bill)
        {
            Render();
            StartPaidFeedbackTransition();
        }

        private void HandleContinueClicked()
        {
            if (_billService != null && !_billService.TryCompletePostPaymentFlow())
            {
                return;
            }

            // 계속하기로 다음 런을 진행할 때는 정산창이 0.1초 동안 다시 깜빡이지 않도록 닫기 알림 없이 닫는다.
            Close(notifyClosed: false);
            if (Application.isPlaying)
            {
                _gameFlowService?.ContinueRun();
            }
        }
    }
}
