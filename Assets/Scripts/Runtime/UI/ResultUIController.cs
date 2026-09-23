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

        // 해금 카드 (#300). 해금이 일어난 정산에서 카운트업·강조가 끝난 뒤 띄운다.
        [SerializeField] private UnlockCardView _unlockCard;

        [Tooltip("정산창이 열릴 때 금액·진행률이 0 에서 올라가는 시간(초). 0 이면 연출 없이 바로 표시")]
        [SerializeField] private float _countUpDurationSec = 0.8f;

        [Tooltip("금액 카운트업 뒤 해금 진행률이 올라가는 시간(초). 금액과 따로 보여 줘야 차오르는 게 보인다")]
        [SerializeField] private float _progressDurationSec = 1.2f;

        private Coroutine _countUpRoutine;
        private Coroutine _unlockPunchRoutine;

        [Tooltip("해금 강조 색 (캡션·이름)")]
        [SerializeField] private Color _unlockHighlightColor = new Color(0.941f, 0.776f, 0.447f, 1f);

        /// <summary>
        /// 해금 카드를 마지막으로 띄운 정산의 누적 수입. 고지서를 닫고 돌아오면 ShowSettlement 가 카운트업을
        /// 다시 돌리는데, 같은 정산이면 카드를 또 띄우지 않는다 (#300). 누적은 정산마다 바뀐다.
        /// </summary>
        private long _unlockCardShownEarned = -1L;

        private bool _hasCodexDefaultColors;
        private Color _codexNameDefaultColor;
        private Color _codexCaptionDefaultColor;
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
            StartCountUp();
        }

        /// <summary>
        /// 정산창이 열릴 때 이번 런 금액이 올라가는 연출 (#301). 최종값은 UpdateSettlementView 가 이미 썼고,
        /// 여기서는 잠깐 덮어쓰며 올려 보일 뿐이다 — 연출이 끊겨도 값이 틀리지 않는다.
        /// </summary>
        private void StartCountUp()
        {
            StopCountUp();
            if (_countUpDurationSec <= 0f || _economyService == null || !isActiveAndEnabled)
            {
                // 연출이 없으면 기다릴 것도 없다 — 곧바로 해금 카드를 판단한다 (#300).
                ShowUnlockCards();
                return;
            }
            _countUpRoutine = StartCoroutine(CountUp());
        }

        private void StopCountUp()
        {
            if (_countUpRoutine != null)
            {
                StopCoroutine(_countUpRoutine);
                _countUpRoutine = null;
            }
        }

        private System.Collections.IEnumerator CountUp()
        {
            var runCoin = _economyService.RunCoin;
            var balance = _economyService.CurrentCoin;
            var earned = _economyService.EarnedTotal;
            var before = earned - runCoin;

            // 이번 런에 해금됐더라도 곧바로 "해금!" 을 띄우지 않는다 — 지난 정산의 진행률(첫 런이면 0%)에서
            // 100% 까지 올라가는 것을 먼저 보여 주고, 끝난 뒤 강조한다 (#247 PM).
            var firstUnlocked = _balanceData != null ? FindFirstJustUnlocked(earned, runCoin) : null;
            var progressTarget = firstUnlocked ??
                                 (_balanceData != null ? _balanceData.GetNextUnlockTarget(earned) : null);
            if (firstUnlocked != null)
            {
                if (_codexProgressText != null)
                {
                    _codexProgressText.text = firstUnlocked.DisplayName;
                }
                if (_codexPreview != null)
                {
                    _codexPreview.Show(firstUnlocked.Id);
                }
                ApplyCodexHighlight(false);
            }

            var elapsed = 0f;
            while (elapsed < _countUpDurationSec)
            {
                // 정산창은 시간이 멈춘 상태일 수 있어 실제 시간으로 센다.
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / _countUpDurationSec);
                t = 1f - (1f - t) * (1f - t);
                var shown = (long)Mathf.Round(runCoin * t);
                SetMoney(_grossText, shown);
                SetMoney(_netText, shown);
                SetMoney(_balanceText, balance - runCoin + shown);
                if (progressTarget != null && before >= 0L)
                {
                    // 금액이 오르는 동안 진행률은 지난 수치에 머문다
                    SetCodexCaption(GetProgressCaptionToward(progressTarget, before));
                }
                yield return null;
            }

            // 금액이 다 오른 뒤 진행률을 따로 천천히 올린다 (#247 PM: 1초 내외로 보여 준다).
            if (progressTarget != null && before >= 0L && _progressDurationSec > 0f)
            {
                elapsed = 0f;
                while (elapsed < _progressDurationSec)
                {
                    elapsed += Time.unscaledDeltaTime;
                    var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _progressDurationSec));
                    SetCodexCaption(GetProgressCaptionToward(progressTarget, before + (long)Mathf.Round(runCoin * t)));
                    yield return null;
                }
            }

            _countUpRoutine = null;
            UpdateSettlementView();
            if (firstUnlocked != null && isActiveAndEnabled)
            {
                _unlockPunchRoutine = StartCoroutine(PunchUnlock());
            }
        }

        /// <summary>해금 강조: 이름·캡션이 잠깐 커졌다 돌아온다. 색은 UpdateNextUnlock 이 금색으로 바꿔 둔다.</summary>
        private System.Collections.IEnumerator PunchUnlock()
        {
            const float duration = 0.45f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var scale = 1f + 0.3f * Mathf.Sin(t * Mathf.PI);
                SetCodexScale(scale);
                yield return null;
            }
            SetCodexScale(1f);
            _unlockPunchRoutine = null;

            // 진행률이 100% 까지 차고 이름이 튀는 것까지 본 뒤에 카드를 덮는다 (#300).
            ShowUnlockCards();
        }

        /// <summary>
        /// 이번 정산에서 해금된 종류마다 카드를 한 장씩 띄운다 (#300, 원작처럼 정산창 위). 같은 정산에서는 한 번만.
        /// "본 적 있음" 은 저장하지 않는다 — 기준액을 넘었다는 조건이 그 정산에서만 참이라 다음 정산에는 다시 뜨지 않는다.
        /// </summary>
        private void ShowUnlockCards()
        {
            if (_unlockCard == null || _economyService == null || _balanceData == null)
            {
                return;
            }

            var earned = _economyService.EarnedTotal;
            if (earned == _unlockCardShownEarned)
            {
                return;
            }

            var unlocked = FindAllJustUnlocked(earned, _economyService.RunCoin);
            if (unlocked.Count == 0)
            {
                return;
            }

            _unlockCardShownEarned = earned;
            _unlockCard.Show(unlocked, _balanceData);
        }

        private void SetCodexScale(float scale)
        {
            if (_codexProgressText != null)
            {
                _codexProgressText.rectTransform.localScale = Vector3.one * scale;
            }
            if (_codexCaptionText != null)
            {
                _codexCaptionText.rectTransform.localScale = Vector3.one * scale;
            }
        }

        private void ApplyCodexHighlight(bool isHighlighted)
        {
            if (_codexProgressText == null || _codexCaptionText == null)
            {
                return;
            }
            if (!_hasCodexDefaultColors)
            {
                _codexNameDefaultColor = _codexProgressText.color;
                _codexCaptionDefaultColor = _codexCaptionText.color;
                _hasCodexDefaultColors = true;
            }
            _codexProgressText.color = isHighlighted ? _unlockHighlightColor : _codexNameDefaultColor;
            _codexCaptionText.color = isHighlighted ? _unlockHighlightColor : _codexCaptionDefaultColor;
        }

        private static void SetMoney(TextMeshProUGUI label, long amount)
        {
            if (label != null)
            {
                label.text = $"${amount:N0}";
            }
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
            if (_unlockCard != null)
            {
                _unlockCard.HideImmediate();
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
            if (_unlockCard != null)
            {
                _unlockCard.HideImmediate();
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
            // 다른 경로(납부 등)로 다시 그리면 연출을 멈추고 최종값을 쓴다.
            if (_countUpRoutine != null)
            {
                StopCountUp();
            }
            if (_unlockPunchRoutine != null)
            {
                StopCoroutine(_unlockPunchRoutine);
                _unlockPunchRoutine = null;
                SetCodexScale(1f);
            }

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
                : $"{stageNumber}단계 고지서 ${bill.Amount:N0} · 마감 {GetDaysLeftAfterToday(bill)}일 남음";

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
                _payDaysLeftText.text = unpaid ? $"{GetDaysLeftAfterToday(activeBill)}일 남음" : string.Empty;
            }
            // 마감일에는 업그레이드·계속을 숨겨 납부 창으로만 가게 한다 (원작과 같다, 7.1.1 #320).
            // 계속을 열어 두면 돈이 있어도 눌러서 바로 파산으로 넘어간다.
            var isDueToday = unpaid && GetDaysLeftAfterToday(activeBill) <= 0;
            if (_upgradeButton != null)
            {
                _upgradeButton.gameObject.SetActive(!isDueToday);
            }
            if (_continueButton != null)
            {
                _continueButton.gameObject.SetActive(!isDueToday);
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
        /// 종류별 파괴 수. 회차 누적 수입으로 해금된 종류만 해금 순서로 채운다 (#247, #301).
        /// 칸보다 해금 종류가 많으면 가장 최근에 해금된 것들을 보여 준다. 남는 칸은 비운다.
        /// </summary>
        private void UpdateBrokenChips()
        {
            if (_brokenChipTexts == null)
            {
                return;
            }

            var unlocked = _balanceData != null ? _balanceData.GetUnlockedTargets(GetEarnedTotal()) : null;
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
        /// "다음 크리처 해금" 패널 (#301). 해금은 이번 회차 누적 수입이 targets.csv 의 unlock_earned 를
        /// 넘는 정산에서 일어난다. 이번 정산에서 해금됐으면 그 종류를, 아니면 다음 종류와 진행률을 보여 준다.
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

            var earned = GetEarnedTotal();
            var runCoin = _economyService != null ? _economyService.RunCoin : 0L;
            var justUnlocked = FindJustUnlocked(earned, runCoin);
            var shown = justUnlocked ?? _balanceData.GetNextUnlockTarget(earned);
            if (_codexPreview != null)
            {
                _codexPreview.Show(shown != null ? shown.Id : null);
            }

            ApplyCodexHighlight(justUnlocked != null);
            if (shown == null)
            {
                _codexProgressText.text = "모든 크리처 해금";
                SetCodexCaption(string.Empty);
                return;
            }

            _codexProgressText.text = shown.DisplayName;
            SetCodexCaption(justUnlocked != null ? "해금! 다음 날 등장" : GetUnlockProgressCaption(earned));
        }

        /// <summary>
        /// 이번 런 수입으로 기준을 넘은 종류들, 해금 순서대로. 없으면 빈 목록.
        /// 파산으로 누적이 0 이 됐는데 RunCoin 이 남아 있으면 earned - runCoin 이 음수가 된다 — 그때 기준 0 인
        /// 첫 종류가 "해금!" 으로 잡히지 않도록, 처음부터 있는 종류(기준 0)와 음수 구간은 제외한다.
        /// 정산창 칸(가장 나중 것)·카운트업(가장 먼저 것)·해금 카드(전부, #300)가 모두 이 판정을 쓴다.
        /// </summary>
        private List<TargetDef> FindAllJustUnlocked(long earned, long runCoin)
        {
            var found = new List<TargetDef>();
            var before = earned - runCoin;
            if (before < 0L)
            {
                return found;
            }

            foreach (var target in _balanceData.GetUnlockOrder())
            {
                if (target.UnlockEarned > 0L && target.UnlockEarned > before && target.UnlockEarned <= earned)
                {
                    found.Add(target);
                }
            }
            return found;
        }

        /// <summary>이번 런 수입으로 기준을 넘은 종류 중 가장 나중 것. 없으면 null.</summary>
        private TargetDef FindJustUnlocked(long earned, long runCoin)
        {
            var found = FindAllJustUnlocked(earned, runCoin);
            return found.Count > 0 ? found[found.Count - 1] : null;
        }

        /// <summary>이번 런 수입으로 기준을 넘은 종류 중 가장 먼저 것 (카운트업은 여기까지 올린다). 없으면 null.</summary>
        private TargetDef FindFirstJustUnlocked(long earned, long runCoin)
        {
            var found = FindAllJustUnlocked(earned, runCoin);
            return found.Count > 0 ? found[0] : null;
        }

        /// <summary>
        /// target 까지의 진행률 캡션. 직전 해금 기준액부터 target 기준액까지 구간이고 100% 에서 멈춘다 —
        /// 카운트업 중 해금 순간까지 "100%" 로 올라가는 것을 보여 주는 데 쓴다.
        /// </summary>
        private string GetProgressCaptionToward(TargetDef target, long earned)
        {
            var previous = 0L;
            foreach (var other in _balanceData.GetUnlockOrder())
            {
                if (other.UnlockEarned < target.UnlockEarned)
                {
                    previous = System.Math.Max(previous, other.UnlockEarned);
                }
            }
            var span = System.Math.Max(1L, target.UnlockEarned - previous);
            var clamped = System.Math.Min(earned, target.UnlockEarned);
            var percent = Mathf.Clamp(Mathf.FloorToInt(100f * (clamped - previous) / span), 0, 100);
            return $"누적 ${clamped:N0} / ${target.UnlockEarned:N0} · {percent}%";
        }

        /// <summary>
        /// 진행률 = 직전 해금 기준액부터 다음 기준액까지 구간에서 누적 수입이 온 비율. 원작 해금 칸의 %.
        /// </summary>
        private string GetUnlockProgressCaption(long earned)
        {
            var next = _balanceData != null ? _balanceData.GetNextUnlockTarget(earned) : null;
            if (next == null)
            {
                return string.Empty;
            }

            var previous = 0L;
            foreach (var target in _balanceData.GetUnlockedTargets(earned))
            {
                previous = System.Math.Max(previous, target.UnlockEarned);
            }
            var span = System.Math.Max(1L, next.UnlockEarned - previous);
            var percent = Mathf.Clamp(Mathf.FloorToInt(100f * (earned - previous) / span), 0, 99);
            return $"누적 ${earned:N0} / ${next.UnlockEarned:N0} · {percent}%";
        }

        /// <summary>
        /// 정산창은 그날 런이 끝난 뒤라 오늘을 남은 날에서 뺀다 (#247, 원작: 3일짜리 첫 고지서가 첫 정산에 "2일 남음").
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

        private long GetEarnedTotal()
        {
            return _economyService != null ? _economyService.EarnedTotal : 0L;
        }

        private void SetCodexCaption(string text)
        {
            if (_codexCaptionText != null)
            {
                _codexCaptionText.text = text;
            }
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
        /// 액면별 개수 칸. 프리팹은 coins.csv 행마다 한 칸씩, 그 값으로 라벨을 만든다
        /// (ResultUIPrefabCreator.CreateDenomRow, #326) — 칸이 모자라면 그 액면이 코인 개수에는 잡히고
        /// 칸에는 안 보여 합계가 내역으로 설명되지 않는다 (예전에 $1,000 칸이 없던 버그).
        /// _balanceData.Coins 순서(=coins.csv 파일 순서)대로 칸을 채운다 — WorthText 라벨이
        /// 같은 순서로 만들어져 있어 순서가 어긋나면 안 맞는 액면 밑에 숫자가 붙는다.
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
                _billPanel.ShowAsPrestigeWithFade();
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
