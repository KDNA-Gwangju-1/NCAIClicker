using System;
using System.Collections.Generic;
using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using UnityEngine;

namespace NCAIClicker.Economy
{
    /// <summary>
    /// 하루 진행과 고지서 발행·조기 납부를 관리한다. 한 번의 런 = 하루 하나 (ARCHITECTURE.md "하루 종료 순서").
    /// 대출(4.3)은 여기서 빌리고 갚는 것까지 맡는다. 다만 수입에서 실제로 떼는 일은 EconomyManager 가 LoanDailyCut 을 읽어 하고,
    /// 파산 판정(4.4)도 여기서 한다 — ARCHITECTURE 1절이 이 매니저에 맡겨 두었다.
    /// 납부(4.2)의 완료 기준 정본은 GitHub 이슈 #28.
    ///
    /// 퍼크 선택(OnPerkChosen)의 실제 게임플레이 효과 적용은 이 클래스의 범위 밖이다 — 각 시스템이
    /// BalanceData.GetPerk(id) 로 값을 읽어 스스로 적용한다 (TECH_NOTES 알려진 한계).
    ///
    /// Managers 프리팹(Resources/Managers)에 붙인다. 생성은 ManagerBootstrap 이 한다.
    /// </summary>
    public class BillManager : MonoBehaviour, IBillService, IRunScoped, IBillPersistence
    {
        // SaveManager.Instance 와 같은 패턴 — 공용 인터페이스 타입으로 조회 통로만 연다 (AGENTS.md).
        // OnEnable 시점의 초기 상태를 한 번 읽는 용도(ARCHITECTURE 3절) — 이후 갱신은 OnBillIssued/OnBillDueSoon 구독으로 받는다.
        public static IBillService Instance { get; private set; }

        [SerializeField] private BalanceData _balanceData;

        private int _currentDay = 1;
        private int _billIndex = 1;
        private int _cycleIndex = 1;
        private bool _hasBegun;
        private Bill _activeBill;
        private string[] _offeredPerkIds = Array.Empty<string>();
        private PostPaymentFlowState _postPaymentFlowState;
        private IGamePersistence _persistence;

        /// <summary>진행 중인 대출. 없으면 null 이다 (ARCHITECTURE.md "메모리상에서는 null 로 둔다").</summary>
        private Loan _activeLoan;

        /// <summary>
        /// 마지막으로 완제한 날. 대출 객체를 지워도 재대출 쿨다운은 이 값으로 유지된다 (ARCHITECTURE.md SaveData).
        /// -1 은 아직 한 번도 빌린 적이 없다는 뜻이라 쿨다운을 적용하지 않는다.
        /// </summary>
        private int _lastLoanRepaidDay = -1;

        /// <summary>코인 차감의 출처. EconomyManager 가 초기화 때 넣어 준다 (ManagerBootstrap). 없으면 납부는 항상 실패한다.</summary>
        private IEconomyService _economyService;

        /// <summary>
        /// 파산 시 지갑을 비우는 통로. EconomyManager 가 초기화 때 넣어 준다 (ManagerBootstrap, 이슈 #158).
        /// IWalletPersistence 는 SaveManager·BillManager 만 쓴다 — 없으면 지갑을 비우지 않고 건너뛴다.
        /// </summary>
        private IWalletPersistence _walletPersistence;

        /// <summary>
        /// 파산 시 업그레이드 레벨을 비우는 통로. EconomyManager 가 초기화 때 넣어 준다
        /// (ManagerBootstrap, 이슈 #250). 업그레이드는 영구 층이 아니라 회차 층이라 파산에서
        /// 함께 지워진다 (GDD 파산 절 층 표, 4.16). 없으면 지우지 않고 건너뛴다.
        /// </summary>
        private IUpgradePersistence _upgradePersistence;
        private IUnlockPersistence _unlockPersistence;

        /// <summary>단계 진행 조회 통로. ManagerBootstrap 이 넣어 준다 (이슈 #150). 없으면 1단계로 폴백한다.</summary>
        private IStageService _stageService;

        public int CurrentDay => _currentDay;
        public int CurrentCycle => _cycleIndex;

        public void RestoreCycle(int cycle)
        {
            _cycleIndex = Mathf.Max(1, cycle);
        }

        public int DaysLeft => _activeBill == null
            ? 0
            : Mathf.Max(0, _activeBill.DueDay - _currentDay + 1);

        /// <summary>
        /// 활성 대출의 일일 징수율. 대출이 없으면 0 이다 (IBillService 계약).
        /// 징수 자체는 여기서 하지 않는다 — EconomyManager 가 이 값을 읽어 수입에 (1 - 징수율) 을 곱한다
        /// (AGENTS.md "코인 배율과 대출 징수는 EconomyManager 안에서만").
        /// </summary>
        public float LoanDailyCut => _activeLoan == null ? 0f : _activeLoan.DailyCut;

        public Bill ActiveBill => _activeBill;

        public string[] OfferedPerkIds => _offeredPerkIds;
        public PostPaymentFlowState PaymentFlowState => _postPaymentFlowState;

        public int CurrentBillIndex => _billIndex;

        public Loan CurrentLoan => _activeLoan;

        public int LastLoanRepaidDay => _lastLoanRepaidDay;

        /// <summary>SaveManager 가 로드 직후 한 번 호출한다 (IBillPersistence, 이슈 #221). 저장된 날짜·고지서·대출을 그대로 되돌린다.</summary>
        public void RestoreBillState(int currentDay, int billIndex, Bill activeBill,
                                      Loan activeLoan, int lastLoanRepaidDay, string[] offeredPerkIds,
                                      PostPaymentFlowState postPaymentFlowState = PostPaymentFlowState.None)
        {
            _currentDay = currentDay;
            _billIndex = billIndex;
            _activeBill = activeBill;
            _activeLoan = activeLoan;
            _lastLoanRepaidDay = lastLoanRepaidDay;
            _offeredPerkIds = offeredPerkIds ?? Array.Empty<string>();
            _postPaymentFlowState = postPaymentFlowState;
            _hasBegun = true;
        }

        private void Awake()
        {
            Instance = this;
            if (_balanceData == null)
            {
                Debug.LogError("[BillManager] BalanceData 가 연결되지 않았다. " +
                               "고지서를 발행하지 못하니 Managers 프리팹의 참조를 확인하라.");
            }
        }

        /// <summary>EconomyManager 가 자기 자신을 넘겨 준다. 구현 클래스를 직접 참조하지 않기 위한 통로다.</summary>
        public void SetEconomyService(IEconomyService economyService)
        {
            _economyService = economyService;
        }

        /// <summary>
        /// EconomyManager 가 지갑 초기화 통로를 넘겨 준다. 파산 시 회차 초기화(ResetRound)에서만
        /// 쓴다 — 코인 지급/차감은 여전히 IEconomyService 하나로만 한다 (이슈 #158).
        /// </summary>
        public void SetWalletPersistence(IWalletPersistence walletPersistence)
        {
            _walletPersistence = walletPersistence;
        }

        /// <summary>
        /// EconomyManager 가 업그레이드 초기화 통로를 넘겨 준다. 파산 시 회차 초기화(ResetRound)에서만
        /// 쓴다 — 구매·조회는 여전히 IUpgradeShop 으로만 한다 (이슈 #250).
        /// </summary>
        public void SetUpgradePersistence(IUpgradePersistence upgradePersistence)
        {
            _upgradePersistence = upgradePersistence;
        }

        /// <summary>
        /// 회차 누적 수입 초기화 통로 (#301). 파산 시 회차 초기화에서만 쓴다 — 크리처 해금은 회차 층이다.
        /// </summary>
        public void SetUnlockPersistence(IUnlockPersistence unlockPersistence)
        {
            _unlockPersistence = unlockPersistence;
        }

        /// <summary>
        /// 단계 진행 상태 조회 통로를 넣는다. 서비스 계약이 아니라 조립(wiring) 통로다 (이슈 #150).
        /// 고지서 금액과 기한이 단일 출처(IStageService)의 현재 단계를 따른다.
        /// </summary>
        public void SetStageService(IStageService stageService)
        {
            _stageService = stageService;
        }

        public void SetPersistence(IGamePersistence persistence)
        {
            _persistence = persistence;
        }

        /// <summary>
        /// 하루(런)를 시작한다. 첫 호출은 1일차를 그대로 쓰고, 이후 호출마다 날짜를 하루 올린다.
        /// 활성 고지서가 없을 때만 새 고지서를 발행한다 — 게임 시작·파산 재시작에도 첫 고지서가 나간다.
        /// 고지서가 있으면 매 호출 끝에 OnBillDueSoon 을 발행한다 — HUD 가 이 이벤트로 남은 일수를 갱신한다
        /// (ARCHITECTURE.md 3절 "UI는 구독 후 공용 조회 인터페이스로 초기 상태를 한 번 읽는다").
        /// GameManager 가 런 시작 직전에 부른다 (IRunScoped, #164).
        /// </summary>
        public void BeginRun()
        {
            if (_postPaymentFlowState != PostPaymentFlowState.None)
            {
                return;
            }

            if (_hasBegun)
            {
                _currentDay++;
            }
            _hasBegun = true;

            if (_activeBill == null || _activeBill.IsPaid)
            {
                IssueBill();
            }

            if (_activeBill != null)
            {
                GameEvents.PublishBillDueSoon(DaysLeft);
            }
        }

        /// <summary>
        /// 하루를 마감한다. 런 종료를 하루 종료로 집계하는 지점 — OnDayEnded 를 발행한다.
        /// 런당 하루이므로 날짜당 한 번만 발행된다는 계약(ARCHITECTURE.md)을 자연히 지킨다.
        /// GameManager 가 런 종료 처리 중 부른다 (IRunScoped, #164).
        ///
        /// 파산 판정은 런 종료 직후가 아니라 정산창을 확인하고 다음 날로 넘어가는 시점(TryCloseDay, #211)에
        /// 수행하여 마감 당일 납부 기회를 보장한다.
        /// </summary>
        public void EndRun()
        {
            GameEvents.PublishDayEnded(_currentDay);
        }

        /// <summary>
        /// 다음 날로 넘어가기 직전에 마감을 확정한다 (이슈 #211).
        /// 미납 고지서가 연체되었으면 파산을 처리하고 true 를 반환한다.
        /// 연체되지 않았으면 false 를 반환한다.
        /// </summary>
        public bool TryCloseDay()
        {
            if (!IsBillOverdue())
            {
                return false;
            }

            HandleBankruptcy();
            return true;
        }

        /// <summary>
        /// 마감을 넘긴 미납 고지서가 있는가. **미납 = 즉시 파산**이며 유예·부분 납부·반액 정산은
        /// 없다 (#6 에서 확정, 근거는 REFERENCE_ANALYSIS 6절). 대출로 코인을 만들어 내는 것이
        /// 유일한 회피 수단이고, 그것도 마감 전에 TryPay 로 내야 한다.
        ///
        /// 하루의 끝에서만 판정하므로 "마감 판정 전에 납부 기회를 제공한다"(ARCHITECTURE)를
        /// 자연히 지킨다 — 런 내내 TryPay 가 열려 있다.
        /// </summary>
        private bool IsBillOverdue()
        {
            return _activeBill != null && !_activeBill.IsPaid && _currentDay >= _activeBill.DueDay;
        }

        /// <summary>
        /// 파산을 알리고 회차를 되돌린다.
        ///
        /// **알리는 것이 먼저다.** 되돌린 뒤에 알리면 받는 쪽이 이미 초기화된 상태를 보게 되어
        /// 결과 화면(작업 6.2)이 무엇이 실패했는지 읽을 수 없다.
        ///
        /// EndRun 은 Result 전이 **도중에** 불린다 (IRunScoped, #164). 그래서 OnBankrupt 를 받아
        /// GameManager 가 Result 로 전이한다는 계약은 이 경로에서 늦다 — 발행 시점에는 이미
        /// Result 라 GameManager 의 Running 가드에 막힌다. 파산은 런을 끝내는 원인이 아니라
        /// 끝난 런의 결과다. 마감을 놓치는 순간이 곧 하루의 끝이라 런 도중에 파산이 날 길이 없다.
        /// 계약 문구는 #158 에서 이 뜻으로 정리했다 (ARCHITECTURE.md "하루 종료 순서").
        /// </summary>
        private void HandleBankruptcy()
        {
            _cycleIndex++;
            GameEvents.PublishBankrupt();
            ResetRound();
        }

        /// <summary>
        /// 자발적 파산 (이슈 #175). 마감 미납 경로와 **같은 처리를 부른다** — 두 갈래로 나누면
        /// 한쪽만 고쳐졌을 때 "스스로 선언한 파산"과 "미납 파산"의 결과가 달라진다.
        ///
        /// 되돌릴 수 없으므로 확인 절차는 부르는 쪽(고지서 화면)이 먼저 거친다. 여기서는
        /// 다시 묻지 않는다 — 확인을 두 곳에 두면 한쪽을 건너뛰는 경로가 생긴다.
        /// </summary>
        public void DeclareBankruptcy()
        {
            HandleBankruptcy();
        }

        /// <summary>
        /// 새 회차 값으로 되돌린다. 레거시 포인트·반지와 최고 기록은 건드리지 않는다
        /// (ARCHITECTURE "저장 경계"). 다음 BeginRun 이 1일차 첫 고지서를 발행한다 —
        /// _hasBegun 을 내려 두므로 날짜가 증가하지 않는다.
        ///
        /// _billIndex 도 되돌린다. #150 이후 이 필드는 단계가 아니라 **누적 고지서 순번**이며
        /// 대출 해금(loan_unlock_bill_index)의 기준이라, 새 회차에서 다시 1부터 세어야 한다.
        ///
        /// 단계는 _stageService 가 단일 출처다 (IStageService, #150). 그쪽 RestoreStage 주석이
        /// "저장 복원 및 파산 처리용"으로 이 자리를 가리킨다.
        /// </summary>
        private void ResetRound()
        {
            _currentDay = 1;
            _billIndex = 1;
            _hasBegun = false;
            _activeBill = null;
            _activeLoan = null;
            _lastLoanRepaidDay = -1;
            _offeredPerkIds = Array.Empty<string>();
            _postPaymentFlowState = PostPaymentFlowState.None;

            // 단계를 1단계로 되돌린다. 인덱스는 0부터라 0 이 1단계다.
            _stageService?.RestoreStage(0);

            // 업그레이드 레벨을 0 으로 비운다 (이슈 #250, 4.16). 업그레이드는 영구 층이 아니라
            // 회차 층이라 파산에서 사라진다 — 영구로 남는 것은 레거시 포인트와 반지뿐이다
            // (GDD 파산 절 층 표). ILegacyPersistence 를 여기서 건드리지 않는 것이 그 구분이다.
            //
            // **지갑 복원보다 먼저 부른다.** RestoreWallet 이 OnBalanceChanged 를 발행하고
            // UpgradeShopPanel 이 그것을 받아 카드를 다시 그리는데, 순서가 뒤집히면 이미 지워진
            // 레벨이 화면에 옛 값으로 남는다. RestoreUpgradeLevels 는 이벤트를 쏘지 않는다.
            _upgradePersistence?.RestoreUpgradeLevels(null);

            // 코인과 소수 잔여도 새 회차 값(0)으로 비운다 (ARCHITECTURE "저장 경계", 계약 7번
            // "소수 잔여는 파산 시 버린다"). IWalletPersistence 소비자에 BillManager 를 추가해
            // 열었다 (이슈 #158) — 주입이 없으면 조용히 건너뛴다.
            _walletPersistence?.RestoreWallet(0L, "0");

            // 크리처 해금도 회차 층이다 (#301, PM 결정). 누적 수입을 비우면 첫 종류만 남는다.
            _unlockPersistence?.RestoreEarnedTotal(0L);
        }

        /// <summary>
        /// 마감 전 조기 납부. 활성 고지서와 같은 인스턴스여야 하고, EconomyManager 를 통해 코인을
        /// 뗀다(경제 계약 — 코인 차감은 EconomyManager 안에서만). 성공하면 퍼크 후보 3종을 뽑아
        /// OnPerkOffered 로 알린다. 고르는 것은 TryChoosePerk 의 몫이다.
        /// </summary>
        public bool TryPay(Bill bill)
        {
            if (bill == null || bill != _activeBill || bill.IsPaid || _economyService == null)
            {
                return false;
            }
            if (!_economyService.TrySpendCoin(bill.Amount))
            {
                return false;
            }

            bill.IsPaid = true;
            _activeBill = bill;
            _postPaymentFlowState = PostPaymentFlowState.PaidFeedback;
            GameEvents.PublishBillPaid(bill);
            _persistence?.CollectAndSave();
            return true;
        }

        public bool TryConfirmPaidFeedback()
        {
            if (_postPaymentFlowState != PostPaymentFlowState.PaidFeedback)
            {
                return false;
            }

            _postPaymentFlowState = PostPaymentFlowState.PerkSelection;
            RollPerkOffer();
            _persistence?.CollectAndSave();
            return true;
        }

        /// <summary>OfferedPerkIds 중 하나를 고른다. 실제 효과 적용은 각 시스템의 몫 — 여기서는 알리기만 한다.</summary>
        public bool TryChoosePerk(string perkId)
        {
            if (_postPaymentFlowState != PostPaymentFlowState.PerkSelection ||
                _offeredPerkIds.Length == 0 || Array.IndexOf(_offeredPerkIds, perkId) < 0)
            {
                return false;
            }
            _offeredPerkIds = Array.Empty<string>();
            _postPaymentFlowState = PostPaymentFlowState.NewBillConfirmation;
            GameEvents.PublishPerkChosen(perkId);
            // 정산 중(계속하기 전)이라 _currentDay 는 아직 끝난 날이다. 다음 런의 날짜(_currentDay + 1)를
            // 발행일로 넘긴다 — 그대로 쓰면 마감까지 due_days 보다 하루 짧아진다 (#270).
            IssueBill(_currentDay + 1);
            _persistence?.CollectAndSave();
            return true;
        }

        public bool TryEnterInvestmentMenu()
        {
            if (_postPaymentFlowState != PostPaymentFlowState.NewBillConfirmation)
            {
                return false;
            }

            _postPaymentFlowState = PostPaymentFlowState.InvestmentMenu;
            _persistence?.CollectAndSave();
            return true;
        }

        public bool TryCompletePostPaymentFlow()
        {
            if (_postPaymentFlowState == PostPaymentFlowState.None)
            {
                return true;
            }
            if (_postPaymentFlowState != PostPaymentFlowState.InvestmentMenu)
            {
                return false;
            }

            _postPaymentFlowState = PostPaymentFlowState.None;
            _persistence?.CollectAndSave();
            return true;
        }

        /// <summary>
        /// 빅 토니에게 amount 만큼 빌린다. 거부 조건은 순서대로 금액, 동시 건수, 해금 순번, 재대출 쿨다운, 한도다.
        /// 성공하면 이자를 더한 상환액이 그 자리에서 확정되고 원금이 지갑에 들어간다 — 원금 입금은
        /// IEconomyService.AddLoanPrincipal 을 거치므로 배율·징수·RunCoin 집계에서 빠진다
        /// (ARCHITECTURE.md "코인 계산 순서" 8번).
        /// </summary>
        public bool TryTakeLoan(long amount)
        {
            if (amount <= 0L || _balanceData == null || _economyService == null)
            {
                return false;
            }

            var config = _balanceData.Bill;
            if (_activeLoan != null || config.LoanMaxConcurrent < 1)
            {
                return false;
            }

            // _billIndex 는 다음에 발행할 순번이라, 지금 손에 든 고지서는 그 하나 앞이다.
            if (_billIndex - 1 < config.LoanUnlockBillIndex)
            {
                return false;
            }

            if (IsLoanOnCooldown(config))
            {
                return false;
            }

            // 한도는 지금 막아야 할 고지서 금액이다. 낼 고지서가 없으면 빌릴 이유도 없다
            // (BALANCE.md 4절이 "고지서 전액을 빌리면" 을 상한으로 두고 상환 가능성을 검증한다).
            if (_activeBill == null || amount > _activeBill.Amount)
            {
                return false;
            }

            _activeLoan = new Loan
            {
                Principal = amount,
                Owed = CalculateOwed(amount, config.LoanInterestRate),
                DailyCut = CalculateDailyCut(amount, _activeBill.Amount, config),
            };
            _economyService.AddLoanPrincipal(amount);
            return true;
        }

        /// <summary>
        /// 이자를 포함한 전액을 갚는다. 부분 상환은 없다 — 잔액이 모자라면 실패하고 아무것도 바뀌지 않는다.
        /// 성공하면 그날을 완제일로 적어 재대출 쿨다운이 시작된다. 미상환 기간에 뜯긴 징수분은
        /// 이 금액을 한 푼도 줄이지 않는다 (GDD 4절 "징수분은 부채를 줄이지 않는다").
        /// </summary>
        public bool TryRepayLoan()
        {
            if (_activeLoan == null || _economyService == null)
            {
                return false;
            }
            if (!_economyService.TrySpendCoin(_activeLoan.Owed))
            {
                return false;
            }

            _activeLoan = null;
            _lastLoanRepaidDay = _currentDay;
            return true;
        }

        /// <summary>완제한 적이 없으면(-1) 쿨다운도 없다. 쿨다운 일수를 0 으로 두면 기능 자체가 꺼진다.</summary>
        private bool IsLoanOnCooldown(BillConfig config)
        {
            return _lastLoanRepaidDay >= 0
                   && _currentDay - _lastLoanRepaidDay < config.LoanCooldownDays;
        }

        /// <summary>
        /// 이자 포함 상환액. 소수 부분은 올려 정수로 확정한다 (ARCHITECTURE.md "코인 계산 순서" 8번).
        /// float 이자율을 그대로 곱하면 410 × 1.1 이 450.99… 로 떨어져 한 푼이 깎이므로 decimal 로 올려 계산한다.
        /// </summary>
        private static long CalculateOwed(long principal, float interestRate)
        {
            var owed = principal * (1m + (decimal)interestRate);
            return (long)Math.Ceiling(owed);
        }

        /// <summary>
        /// 일일 징수율을 빌린 금액에 비례해 정한다 — 고지서 전액을 빌리면 상한, 조금만 빌리면 하한에 가깝다.
        /// 대출할 때 한 번만 정하고 미상환 기간 내내 고정한다 (ARCHITECTURE.md Loan.DailyCut).
        /// 범위 안에서 무작위로 뽑지 않는 이유: 같은 선택이 늘 같은 결과를 내야 7.2 밸런싱 실측과
        /// Edit Mode 검증이 성립하고, "많이 빌릴수록 비싸다" 는 저울질도 이쪽이 분명하다.
        /// </summary>
        private static float CalculateDailyCut(long amount, long billAmount, BillConfig config)
        {
            var ratio = billAmount <= 0L ? 1f : Mathf.Clamp01((float)amount / billAmount);
            return Mathf.Lerp(config.LoanDailyCutMin, config.LoanDailyCutMax, ratio);
        }

        /// <summary>
        /// perks.csv 전체에서 중복 없이 3종을 뽑는다. ponytail: 후보가 정확히 4종이라 남는 조합이
        /// 많지 않지만, 복원추출 없는 무작위 뽑기 자체는 풀 크기가 늘어도 그대로 맞는다.
        /// </summary>
        private void RollPerkOffer()
        {
            var perks = _balanceData == null ? null : _balanceData.Perks;
            if (perks == null || perks.Count == 0)
            {
                _offeredPerkIds = Array.Empty<string>();
                return;
            }

            var pool = new List<string>(perks.Count);
            foreach (var perk in perks)
            {
                pool.Add(perk.Id);
            }

            var pickCount = Mathf.Min(3, pool.Count);
            var offered = new string[pickCount];
            for (var i = 0; i < pickCount; i++)
            {
                var index = UnityEngine.Random.Range(0, pool.Count);
                offered[i] = pool[index];
                pool.RemoveAt(index);
            }

            _offeredPerkIds = offered;
            GameEvents.PublishPerkOffered(_offeredPerkIds);
        }

        /// <summary>
        /// stages.csv 의 단계값으로 고지서를 만든다. 마감일 = 발행일 + 기한 - 1 (Bill.DueDay 계약).
        /// 단계는 단일 출처(_stageService)의 현재 단계를 따르고, 없으면 1단계로 폴백한다 (이슈 #150).
        /// _billIndex 는 대출 해금 등에서 쓸 누적 고지서 순번으로 유지한다.
        /// BeginRun 은 이미 다음 날로 넘어간 뒤 부르므로 _currentDay 를 그대로 쓴다.
        /// </summary>
        private void IssueBill()
        {
            IssueBill(_currentDay);
        }

        /// <summary>정산 중(TryChoosePerk)처럼 아직 날짜가 넘어가지 않은 시점에서 발행일을 명시해 부른다 (#270).</summary>
        private void IssueBill(int issuedDay)
        {
            if (_balanceData == null || _balanceData.Stages.Count == 0)
            {
                return;
            }

            var stageNumber = _stageService != null
                ? _stageService.CurrentStageNumber
                : 1;
            stageNumber = Mathf.Clamp(stageNumber, 1, _balanceData.Stages.Count);

            var stage = _balanceData.GetStage(stageNumber);
            if (stage == null)
            {
                Debug.LogError("[BillManager] stages.csv 에 " + stageNumber + " 단계가 없다.");
                return;
            }

            _activeBill = new Bill
            {
                Amount = stage.BillAmount,
                IssuedDay = issuedDay,
                DueDay = issuedDay + stage.DueDays - 1,
                IsPaid = false,
            };
            _billIndex++;
            GameEvents.PublishBillIssued(_activeBill);
        }
    }
}
