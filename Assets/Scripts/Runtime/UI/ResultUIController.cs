using System;
using System.Collections.Generic;
using NCAIClicker.Core;
using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
        [SerializeField] private TextMeshProUGUI _codexCaptionText;
        [SerializeField] private CreaturePreview _codexPreview;
        [SerializeField] private Button _upgradeButton;
        [SerializeField] private Button _payButton;
        [SerializeField] private TextMeshProUGUI _payCaptionText;
        [SerializeField] private TextMeshProUGUI _payButtonLabel;
        [SerializeField] private TextMeshProUGUI _payDaysLeftText;

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

        [Header("데이터")]
        [SerializeField] private BalanceData _balanceData;

        [Header("공통 UI")]
        [SerializeField] private Button _mainMenuButton;
        [SerializeField] private TextMeshProUGUI _balanceText;

        // 정산창의 납부 버튼은 고지서 모달을 연다. 조립 지점이 넣어 준다.
        private BillPanelController _billPanel;

        private IEconomyService _economyService;
        private IBillService _billService;
        private IStageService _stageService;

        private int _totalHoverSwings;
        private int _hitHoverSwings;

        // 박살낸 저금통은 파괴 이벤트를 세면 된다. 따로 조회 통로를 만들 필요가 없다.
        private int _brokenTotal;
        private readonly Dictionary<string, int> _brokenByType = new Dictionary<string, int>();
        private bool _isBankrupt;

        // 결과 화면 뒤 3D 장면을 흐리는 데 쓴다. Game 씬의 Global Volume(SampleSceneProfile)에
        // 이미 있는 DepthOfField 오버라이드를 찾아 active 만 토글한다 — 씬은 코어 플레이 소유라
        // 새 Volume 을 만들어 넣지 않는다.
        private DepthOfField _backgroundBlur;

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
            GameEvents.OnTargetBroken += HandleTargetBroken;
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
            GameEvents.OnTargetBroken -= HandleTargetBroken;
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
            if (_isBankrupt)
            {
                // 이미 파산한 상태에서는 정산창을 다시 열지 않는다.
                return;
            }
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
            _brokenTotal = 0;
            _brokenByType.Clear();
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

        public void HandleTargetBroken(BreakInfo info)
        {
            _brokenTotal++;
            _brokenByType.TryGetValue(info.TargetId, out var count);
            _brokenByType[info.TargetId] = count + 1;
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
            if (_billPanel != null && _billPanel.IsOpen)
            {
                // 고지서 패널이 이미 열려 있는 상태에서는 반지 탭이 열리므로 결과창으로 덮지 않는다.
                return;
            }
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

            SetBackgroundBlur(true);
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

            SetBackgroundBlur(true);
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

            SetBackgroundBlur(false);
        }

        /// <summary>
        /// Game 씬 Global Volume 의 DepthOfField 오버라이드를 켜고 끈다.
        /// 컴포넌트를 못 찾아도(씬에 Volume 이 없는 테스트 환경 등) 조용히 넘어간다 —
        /// 블러는 연출일 뿐 결과 화면 동작을 막아서는 안 된다.
        /// </summary>
        private void SetBackgroundBlur(bool enabled)
        {
            if (_backgroundBlur == null)
            {
                var volume = FindFirstObjectByType<Volume>(FindObjectsInactive.Include);
                if (volume != null && volume.profile != null)
                {
                    volume.profile.TryGet(out _backgroundBlur);
                }
            }

            if (_backgroundBlur != null)
            {
                _backgroundBlur.active = enabled;
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
                // "코인:" 은 개수다. 금액은 _grossText("합계:") 쪽이다 — 액면이 갈리기 전에는
                // 둘이 같은 숫자였다 (이슈 #178 전 버그). RunCoinBreakdown 의 개수를 모두 더한다.
                _runCoinText.text = $"{GetTotalCoinCount():N0}";
            }

            if (_accuracyText != null)
            {
                _accuracyText.text = $"{Accuracy:F0}%";
            }

            if (_balanceText != null)
            {
                // 이번 런 수입까지 더해진 현재 보유액. 다음 판단(살까 낼까)의 기준이 된다.
                var coin = _economyService != null ? _economyService.CurrentCoin : 0L;
                _balanceText.text = $"${coin:N0}";
            }

            UpdateUnwiredView();

            // 고지서 정보는 한 줄에 모은다 (#184) — 원작은 DAY 옆 메타 한 줄이 전부이고, 금액·남은
            // 일수는 납부 버튼이 다시 말한다. 같은 사실을 세 곳에 흩어 놓지 않는다.
            // _billStatusText 가 그 한 줄이고 _stageGoalText 는 비운다 (프리팹 호환을 위해 필드는 남긴다).
            var stageNumber = _stageService != null ? _stageService.CurrentStageNumber : 1;
            var bill = _billService?.ActiveBill;
            var billLine = bill == null || bill.IsPaid
                ? $"{stageNumber}단계 고지서 납부 완료"
                : $"{stageNumber}단계 고지서 ${bill.Amount:N0} · 마감 {_billService.DaysLeft}일 남음";

            if (_billStatusText != null)
            {
                _billStatusText.text = billLine;
            }

            if (_stageGoalText != null)
            {
                _stageGoalText.text = string.Empty;
            }
        }

        /// <summary>
        /// 조회 계약이 없어 값을 못 채우는 칸을 자리표시로 채우고, 동작이 없는 버튼을 잠근다.
        /// 배선이 붙는 이슈에서 이 메서드의 해당 줄을 실제 조회로 바꾼다.
        /// </summary>
        private void UpdateUnwiredView()
        {
            // 합계는 이번 런 수입(금액) 그대로다. 코인 개수는 _runCoinText("코인:") 쪽이 따로
            // RunCoinBreakdown 으로 센다 — 액면이 갈리면서 둘이 실제로 다른 숫자가 된다 (이슈 #178).
            var gross = _economyService != null ? _economyService.RunCoin : 0L;
            if (_grossText != null)
            {
                _grossText.text = $"${gross:N0}";
            }

            // RunCoin 은 이미 징수를 뺀 순수입이다 (EconomyManager.AddCoin 이 LoanDailyCut 을 적용한
            // 뒤의 값, ARCHITECTURE 계약). 여기서 다시 빼면 이중 차감이다 (#261). 징수 전 금액은
            // IEconomyService 에 조회 통로가 없어 징수 줄은 자리표시로 둔다.
            if (_feeText != null)
            {
                _feeText.text = UnwiredPlaceholder;
            }
            if (_netText != null)
            {
                _netText.text = $"${gross:N0}";
            }

            if (_brokenCountText != null)
            {
                _brokenCountText.text = _brokenTotal.ToString();
            }
            UpdateBrokenChips();

            UpdateNextUnlock();
            UpdateDenomCounts();

            UpdatePayButton();
            UpdateLoanCutRow();

            // 패널이 안 이어졌으면 눌러도 아무 일이 없다. 그럴 바엔 잠근다.
            if (_upgradeButton != null)
            {
                _upgradeButton.interactable = _billPanel != null;
            }

            // 원작은 버튼 자체가 정보다 — 금액이 크게, 남은 일수가 그 아래 작게, 둘 다 버튼 안에.
            // 버튼 밖 캡션은 "지금 낼 수 있는가" 만 답한다.
            var activeBill = _billService?.ActiveBill;
            var unpaid = activeBill != null && !activeBill.IsPaid;

            if (_payButtonLabel != null)
            {
                _payButtonLabel.text = unpaid ? $"${activeBill.Amount:N0}" : "납부 완료";
            }
            if (_payDaysLeftText != null)
            {
                _payDaysLeftText.text = unpaid ? $"{_billService.DaysLeft}일 남음" : string.Empty;
            }
            if (_payCaptionText != null)
            {
                var coin = _economyService != null ? _economyService.CurrentCoin : 0L;
                _payCaptionText.text = !unpaid
                    ? string.Empty
                    : coin >= activeBill.Amount ? "납부 가능" : $"${activeBill.Amount - coin:N0} 부족";
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

        /// <summary>
        /// 종류별 파괴 수. 현재 단계까지 해금된 종류만 해금 순서로 채운다 (#247).
        /// 칸보다 해금 종류가 많으면 가장 최근에 해금된 것들을 보여 준다. 남는 칸은 비운다.
        /// </summary>
        private void UpdateBrokenChips()
        {
            if (_brokenChipTexts == null)
            {
                return;
            }

            var unlocked = _balanceData != null ? _balanceData.GetUnlockedTargets(GetStageNumber()) : null;
            var skip = unlocked != null ? Mathf.Max(0, unlocked.Count - _brokenChipTexts.Length) : 0;
            for (var i = 0; i < _brokenChipTexts.Length; i++)
            {
                var label = _brokenChipTexts[i];
                if (label == null)
                {
                    continue;
                }

                if (unlocked == null)
                {
                    label.text = UnwiredPlaceholder;
                    continue;
                }

                var index = skip + i;
                if (index >= unlocked.Count)
                {
                    label.text = string.Empty;
                    continue;
                }

                var target = unlocked[index];
                _brokenByType.TryGetValue(target.Id, out var count);
                label.text = $"{target.DisplayName} {count}";
            }
        }

        /// <summary>
        /// "다음 저금통 해금까지" 패널. 고지서를 내면 단계가 오르고 다음 종류가 나온다 (#247).
        /// 다음 종류는 stage_spawns.csv 에서 계산한다.
        /// </summary>
        private void UpdateNextUnlock()
        {
            if (_codexProgressText == null)
            {
                return;
            }

            if (_balanceData == null)
            {
                _codexProgressText.text = UnwiredPlaceholder;
                SetCodexCaption(string.Empty);
                return;
            }

            // 납부하면 그 자리에서 단계가 오른다 (StageGoalManager). 납부 직후에는 "다음" 이 아니라
            // 방금 해금된 종류를 보여 줘야 한다 — 다음 날 책상에 처음 나올 종류다.
            var bill = _billService != null ? _billService.ActiveBill : null;
            var isJustUnlocked = bill != null && bill.IsPaid;
            var next = isJustUnlocked
                ? FindUnlockedAt(GetStageNumber())
                : _balanceData.GetNextUnlockTarget(GetStageNumber());
            if (_codexPreview != null)
            {
                _codexPreview.Show(next != null ? next.Id : null);
            }

            if (next == null)
            {
                _codexProgressText.text = "모든 크리처 해금";
                SetCodexCaption(string.Empty);
                return;
            }

            _codexProgressText.text = next.DisplayName;
            SetCodexCaption(isJustUnlocked ? "해금 완료 · 다음 날 등장" : GetUnlockProgressCaption(bill));
        }

        private TargetDef FindUnlockedAt(int stageNumber)
        {
            foreach (var target in _balanceData.Targets)
            {
                if (_balanceData.GetUnlockStage(target.Id) == stageNumber)
                {
                    return target;
                }
            }
            return null;
        }

        /// <summary>
        /// 해금 조건은 이번 단계 고지서 납부다 (StageGoalManager). 진행률은 납부 버튼과 같은 기준인
        /// 보유 코인 ÷ 고지서 금액으로, 100% 에서 멈춘다.
        /// </summary>
        private string GetUnlockProgressCaption(Bill bill)
        {
            if (bill == null || bill.Amount <= 0)
            {
                return "고지서 납부 시 해금";
            }

            var coin = _economyService != null ? _economyService.CurrentCoin : 0L;
            var percent = Mathf.Min(100, Mathf.FloorToInt(100f * Mathf.Max(0L, coin) / bill.Amount));
            return $"고지서 ${bill.Amount:N0} 납부 시 해금 · {percent}%";
        }

        private void SetCodexCaption(string text)
        {
            if (_codexCaptionText != null)
            {
                _codexCaptionText.text = text;
            }
        }

        private int GetStageNumber()
        {
            return _stageService != null ? _stageService.CurrentStageNumber : 1;
        }

        /// <summary>이번 런에 실제로 뽑힌 코인 총 개수. RunCoinBreakdown 각 항목의 Count 합이다.</summary>
        private int GetTotalCoinCount()
        {
            if (_economyService == null)
            {
                return 0;
            }

            var total = 0;
            var breakdown = _economyService.RunCoinBreakdown;
            for (var i = 0; i < breakdown.Count; i++)
            {
                total += breakdown[i].Count;
            }
            return total;
        }

        /// <summary>
        /// 액면별 개수 칸. 프리팹이 만드는 칸 수는 coins.csv 앞 네 행(c1·c5·c25·c100)과 맞춰 뒀다
        /// (ResultUIPrefabCreator.CreateDenomRow) — c1000 은 후반 전용이라 칸이 없다.
        /// _balanceData.Coins 순서(=coins.csv 파일 순서)대로 칸을 채운다 — WorthText 라벨이
        /// 이미 그 순서로 "$1"·"$5"·"$25"·"$100" 를 박아 뒀기 때문에 순서가 어긋나면 안 맞는
        /// 액면 밑에 숫자가 붙는다.
        /// </summary>
        private void UpdateDenomCounts()
        {
            if (_denomCountTexts == null)
            {
                return;
            }

            IReadOnlyList<CoinDrop> breakdown = _economyService != null
                ? _economyService.RunCoinBreakdown
                : Array.Empty<CoinDrop>();

            for (var i = 0; i < _denomCountTexts.Length; i++)
            {
                var label = _denomCountTexts[i];
                if (label == null)
                {
                    continue;
                }

                if (_balanceData == null || i >= _balanceData.Coins.Count)
                {
                    label.text = UnwiredPlaceholder;
                    continue;
                }

                var denomId = _balanceData.Coins[i].Id;
                var count = 0;
                for (var j = 0; j < breakdown.Count; j++)
                {
                    if (breakdown[j].DenomId == denomId)
                    {
                        count = breakdown[j].Count;
                        break;
                    }
                }
                label.text = count.ToString();
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
                _bankruptcyCoinLossText.text = "보유 코인과 업그레이드가 모두 사라지며, 프레스티지(반지 상점)로 이동합니다. (레거시 포인트와 반지는 유지됩니다)";
            }
        }

        private void HandleContinueClicked()
        {
            // 납부 후 흐름(퍽 선택·새 고지서 확인·투자 메뉴)이 끝나기 전에는 이 버튼으로 다음 런을
            // 시작하지 않는다 (이슈 #249 DoD). 고지서 화면의 하단 계속하기만이 흐름을 닫는다 —
            // 조용히 막으면 버튼이 고장 난 것처럼 보이니 고지서 화면을 다시 띄운다.
            if (_billService != null && _billPanel != null &&
                _billService.PaymentFlowState != PostPaymentFlowState.None)
            {
                HideAll();
                _billPanel.ShowAsModal();
                return;
            }

            HideAll();
            GameManager.Instance?.ContinueRun();
        }

        private void HandleRestartClicked()
        {
            HideAll();
            if (_billPanel != null)
            {
                _billPanel.ShowAsPrestige();
            }
            else
            {
                GameManager.Instance?.ContinueRun();
            }
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
