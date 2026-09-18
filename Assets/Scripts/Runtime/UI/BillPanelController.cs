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

        [Header("고지서 종이")]
        [SerializeField] private TextMeshProUGUI _issuerText;
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _amountText;
        [SerializeField] private TextMeshProUGUI _dueLabelText;
        [SerializeField] private TextMeshProUGUI _dueValueText;

        [Header("버튼")]
        [SerializeField] private Button _payButton;
        [SerializeField] private Button _laterButton;
        [SerializeField] private Button _loanButton;
        [SerializeField] private TextMeshProUGUI _loanCaptionText;
        [SerializeField] private Button _continueButton;
        [SerializeField] private Button _declareBankruptcyButton;

        // 다른 프리팹(Target, HammerSwingController)과 같은 방식으로 프리팹에 직렬화해 둔다.
        // 씬을 건너 주입할 통로를 새로 만들지 않기 위해서다.
        [Header("데이터")]
        [SerializeField] private BalanceData _balanceData;

        private IBillService _billService;

        /// <summary>어느 상태로 열려 있는가. 탭일 때만 탭 줄과 계속하기가 보인다.</summary>
        public enum Mode
        {
            Modal,
            Tab,
        }

        private Mode _mode = Mode.Modal;

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
                _laterButton.onClick.AddListener(Close);
            }
            if (_continueButton != null)
            {
                _continueButton.onClick.AddListener(HandleContinueClicked);
            }
        }

        private void OnDisable()
        {
            if (_payButton != null)
            {
                _payButton.onClick.RemoveListener(HandlePayClicked);
            }
            if (_laterButton != null)
            {
                _laterButton.onClick.RemoveListener(Close);
            }
            if (_continueButton != null)
            {
                _continueButton.onClick.RemoveListener(HandleContinueClicked);
            }
        }

        /// <summary>조립 지점이 넣어 준다. 소비처가 구현 클래스를 직접 찾지 않는다.</summary>
        public void SetServices(IBillService billService)
        {
            _billService = billService;
        }

        /// <summary>검증에서 데이터만 갈아끼울 때 쓴다.</summary>
        public void SetBalanceData(BalanceData balanceData)
        {
            _balanceData = balanceData;
        }

        /// <summary>정산창에서 납부하러 들어올 때.</summary>
        public void ShowAsModal() => Show(Mode.Modal);

        /// <summary>다음 턴 준비 화면으로 열 때.</summary>
        public void ShowAsTab() => Show(Mode.Tab);

        private void Show(Mode mode)
        {
            _mode = mode;

            if (_panelRoot != null)
            {
                _panelRoot.SetActive(true);
            }
            if (_tabBar != null)
            {
                _tabBar.SetActive(mode == Mode.Tab);
            }
            if (_continueRow != null)
            {
                _continueRow.SetActive(mode == Mode.Tab);
            }

            Render();
        }

        public void Close()
        {
            if (_panelRoot != null)
            {
                _panelRoot.SetActive(false);
            }
        }

        private void Render()
        {
            var bill = _billService?.ActiveBill;

            if (_issuerText != null || _titleText != null)
            {
                // 이름은 청구서 번호로 고른다. 같은 청구서를 두 번 열어도 이름이 바뀌지 않아야 한다.
                var billIndex = bill != null ? bill.IssuedDay : 0;
                var name = _balanceData != null ? _balanceData.GetBillName(billIndex) : null;

                if (_issuerText != null)
                {
                    _issuerText.text = name != null ? name.Issuer : string.Empty;
                }
                if (_titleText != null)
                {
                    _titleText.text = name != null ? name.Title : "청구서";
                }
            }

            if (_amountText != null)
            {
                _amountText.text = bill != null ? $"${bill.Amount:N0}" : "—";
            }

            RenderDue(bill);
            RenderButtons(bill);
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

            // 마감 당일에는 미루는 선택지를 없앤다. 버튼을 잠그는 대신 아예 감춘다 —
            // 잠긴 버튼은 "왜 안 눌리지" 를 만들지만 없는 버튼은 질문을 만들지 않는다.
            if (_laterButton != null)
            {
                _laterButton.gameObject.SetActive(hasUnpaidBill && !isDueToday);
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

            // 자발적 파산은 #175 범위다. 자리만 두고 잠근다.
            if (_declareBankruptcyButton != null)
            {
                _declareBankruptcyButton.interactable = false;
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
                Render();
            }
        }

        private void HandleContinueClicked()
        {
            Close();
            GameManager.Instance?.ContinueRun();
        }
    }
}
