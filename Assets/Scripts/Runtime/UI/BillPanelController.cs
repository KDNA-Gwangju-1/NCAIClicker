using System;
using NCAIClicker.Core;
using NCAIClicker.Data;
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

        [Header("고지서 종이")]
        [SerializeField] private TextMeshProUGUI _issuerText;
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _amountText;
        [SerializeField] private TextMeshProUGUI _dueLabelText;
        [SerializeField] private TextMeshProUGUI _dueValueText;

        [Header("보유 코인")]
        [SerializeField] private TextMeshProUGUI _balanceText;

        [Header("버튼")]
        [SerializeField] private Button _payButton;
        [SerializeField] private TextMeshProUGUI _payCaptionText;
        [SerializeField] private Button _laterButton;
        [SerializeField] private Button _loanButton;
        [SerializeField] private TextMeshProUGUI _loanCaptionText;
        [SerializeField] private Button _continueButton;
        [SerializeField] private Button _declareBankruptcyButton;
        [SerializeField] private TextMeshProUGUI _declareBankruptcyCaptionText;

        // 되돌릴 수 없는 선택이라 확인을 한 번 받는다 (#175). MainMenuController 의
        // 저장 덮어쓰기 확인창과 같은 방식이다 — 새 패턴을 만들지 않는다.
        [SerializeField] private GameObject _bankruptcyConfirmPanel;
        [SerializeField] private Button _bankruptcyConfirmYesButton;
        [SerializeField] private Button _bankruptcyConfirmNoButton;

        // 다른 프리팹(Target, HammerSwingController)과 같은 방식으로 프리팹에 직렬화해 둔다.
        // 씬을 건너 주입할 통로를 새로 만들지 않기 위해서다.
        [Header("데이터")]
        [SerializeField] private BalanceData _balanceData;

        private IBillService _billService;
        private IEconomyService _economyService;

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
            if (_payButton != null)
            {
                _payButton.onClick.AddListener(HandlePayClicked);
            }
            if (_laterButton != null)
            {
                // "아직" 은 닫는 버튼이 아니라 **미루는** 버튼이다. 닫아 버리면 바로 다음 런이
                // 시작돼 업그레이드를 살 기회가 사라진다. 탭 화면으로 나가 선택지를 남긴다.
                _laterButton.onClick.AddListener(ShowBillTab);
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
            HideBankruptcyConfirm();
        }

        private void OnDisable()
        {
            if (_payButton != null)
            {
                _payButton.onClick.RemoveListener(HandlePayClicked);
            }
            if (_laterButton != null)
            {
                _laterButton.onClick.RemoveListener(ShowBillTab);
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
        }

        /// <summary>조립 지점이 넣어 준다. 소비처가 구현 클래스를 직접 찾지 않는다.</summary>
        public void SetServices(IBillService billService, IEconomyService economyService = null)
        {
            _billService = billService;
            _economyService = economyService;
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

        private void Show(Mode mode)
        {
            _mode = mode;

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
            var showRing = (_mode == Mode.Tab || _mode == Mode.PrestigeOnly) && _tab == Tab.Ring;

            if (_billTabRoot != null)
            {
                _billTabRoot.SetActive(!showUpgrade && !showRing);
            }
            if (_upgradeTabRoot != null)
            {
                _upgradeTabRoot.SetActive(showUpgrade);
            }
            if (_ringTabRoot != null)
            {
                _ringTabRoot.SetActive(showRing);
            }

            if (showUpgrade && _upgradeContent != null && _upgradeShopPrefab != null && _upgradeContent.childCount == 0)
            {
                Instantiate(_upgradeShopPrefab, _upgradeContent, false);
            }
            if (showRing && _ringContent != null && _ringShopPrefab != null && _ringContent.childCount == 0)
            {
                Instantiate(_ringShopPrefab, _ringContent, false);
            }

            // 어느 탭에 있는지 버튼 색으로 알린다. 업그레이드를 보고 있는데 고지서가 켜진 것처럼
            // 보이면 탭이 안 먹은 줄 안다.
            SetTabSelected(_billTabButton, !showUpgrade && !showRing);
            SetTabSelected(_upgradeTabButton, showUpgrade);
            SetTabSelected(_ringTabButton, showRing);
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

        public void Close()
        {
            var wasOpen = IsOpen;
            if (_panelRoot != null)
            {
                _panelRoot.SetActive(false);
            }
            if (wasOpen)
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
            if (_billService != null)
            {
                return;
            }

            var managersGo = GameObject.Find("Managers");
            if (managersGo != null)
            {
                _billService = managersGo.GetComponentInChildren<IBillService>(true);
                _economyService = managersGo.GetComponentInChildren<IEconomyService>(true);
            }

            if (_billService != null)
            {
                return;
            }

            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < behaviours.Length; i++)
            {
                if (_economyService == null && behaviours[i] is IEconomyService economy)
                {
                    _economyService = economy;
                }
                if (behaviours[i] is IBillService bill)
                {
                    _billService = bill;
                    return;
                }
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
                    // 낼 수 있는지 판단하려면 지금 얼마를 들고 있는지가 같이 보여야 한다.
                    var economy = _economyService;
                    _balanceText.text = economy != null ? $"보유 ${economy.CurrentCoin:N0}" : string.Empty;
                }
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
            var daysLeft = _billService != null ? _billService.DaysLeft : 0;

            // 기한 당일이면 남은 일수를 세지 않고 "지금 납부!" 로 바꾼다 (원작).
            // 숫자로 "0일" 이라고 쓰면 아직 하루가 남은 것처럼 읽힌다.
            var isDueToday = bill != null && !bill.IsPaid && daysLeft <= 1;

            if (_dueLabelText != null)
            {
                _dueLabelText.text = "납부 기한";
            }
            if (_dueValueText != null)
            {
                _dueValueText.text = bill == null || bill.IsPaid
                    ? "납부 완료"
                    : isDueToday ? "지금 납부!" : $"{daysLeft}일";
            }
        }

        private void RenderButtons(Bill bill)
        {
            var hasUnpaidBill = bill != null && !bill.IsPaid;
            var daysLeft = _billService != null ? _billService.DaysLeft : 0;
            var isDueToday = hasUnpaidBill && daysLeft <= 1;

            if (_payButton != null)
            {
                _payButton.gameObject.SetActive(hasUnpaidBill);
            }
            if (_payCaptionText != null && !hasUnpaidBill)
            {
                _payCaptionText.text = string.Empty;
            }

            // "아직" 은 탭 화면으로 빠지는 버튼이다. 이미 탭 화면이면 할 일이 없으므로 감춘다.
            // 마감 당일(isDueToday)에는 미루기 선택지를 차단하기 위해 감춘다 — 무조건 납부를 시도해야 한다.
            if (_laterButton != null)
            {
                _laterButton.gameObject.SetActive(_mode == Mode.Modal && hasUnpaidBill && !isDueToday);
            }

            if (_loanButton != null)
            {
                var canLoan = hasUnpaidBill && _billService != null && _billService.LoanDailyCut <= 0f;
                _loanButton.interactable = canLoan;
                if (_loanCaptionText != null)
                {
                    _loanCaptionText.text = canLoan ? string.Empty : "대출 불가";
                }
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
                // 납부가 끝나면 탭 화면으로 나간다. 납부 완료 화면에 머물면 납부·아직 버튼이
                // 모두 사라져 빠져나갈 길이 없고, 닫아 버리면 업그레이드를 살 기회가 사라진다.
                if (_payCaptionText != null)
                {
                    _payCaptionText.text = string.Empty;
                }
                ShowBillTab();
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

        private void HandleContinueClicked()
        {
            Close();
            GameManager.Instance?.ContinueRun();
        }
    }
}
