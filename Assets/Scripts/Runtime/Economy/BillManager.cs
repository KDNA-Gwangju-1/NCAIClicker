using System;
using System.Collections.Generic;
using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using UnityEngine;

namespace NCAIClicker.Economy
{
    /// <summary>
    /// 하루 진행과 청구서 발행·조기 납부를 관리한다. 한 번의 런 = 하루 하나 (ARCHITECTURE.md "하루 종료 순서").
    /// 대출(4.3)은 여기서 빌리고 갚는 것까지 맡는다. 다만 수입에서 실제로 떼는 일은 EconomyManager 가 LoanDailyCut 을 읽어 하고,
    /// 파산 판정(4.4)도 여기서 한다 — ARCHITECTURE 1절이 이 매니저에 맡겨 두었다.
    /// 납부(4.2)의 완료 기준 정본은 GitHub 이슈 #28.
    ///
    /// 퍼크 선택(OnPerkChosen)의 실제 게임플레이 효과 적용은 이 클래스의 범위 밖이다 — 각 시스템이
    /// BalanceData.GetPerk(id) 로 값을 읽어 스스로 적용한다 (TECH_NOTES 알려진 한계).
    ///
    /// Managers 프리팹(Resources/Managers)에 붙인다. 생성은 ManagerBootstrap 이 한다.
    /// </summary>
    public class BillManager : MonoBehaviour, IBillService
    {
        // SaveManager.Instance 와 같은 패턴 — 공용 인터페이스 타입으로 조회 통로만 연다 (AGENTS.md).
        // OnEnable 시점의 초기 상태를 한 번 읽는 용도(ARCHITECTURE 3절) — 이후 갱신은 OnBillIssued/OnBillDueSoon 구독으로 받는다.
        public static IBillService Instance { get; private set; }

        [SerializeField] private BalanceData _balanceData;

        private int _currentDay = 1;
        private int _billIndex = 1;
        private bool _hasBegun;
        private Bill _activeBill;
        private string[] _offeredPerkIds = Array.Empty<string>();

        /// <summary>진행 중인 대출. 없으면 null 이다 (ARCHITECTURE.md "메모리상에서는 null 로 둔다").</summary>
        private Loan _activeLoan;

        /// <summary>
        /// 마지막으로 완제한 날. 대출 객체를 지워도 재대출 쿨다운은 이 값으로 유지된다 (ARCHITECTURE.md SaveData).
        /// -1 은 아직 한 번도 빌린 적이 없다는 뜻이라 쿨다운을 적용하지 않는다.
        /// </summary>
        private int _lastLoanRepaidDay = -1;

        /// <summary>코인 차감의 출처. EconomyManager 가 초기화 때 넣어 준다 (ManagerBootstrap). 없으면 납부는 항상 실패한다.</summary>
        private IEconomyService _economyService;

        /// <summary>단계 진행 조회 통로. ManagerBootstrap 이 넣어 준다 (이슈 #150). 없으면 1단계로 폴백한다.</summary>
        private IStageService _stageService;

        public int CurrentDay => _currentDay;

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

        private void Awake()
        {
            Instance = this;
            if (_balanceData == null)
            {
                Debug.LogError("[BillManager] BalanceData 가 연결되지 않았다. " +
                               "청구서를 발행하지 못하니 Managers 프리팹의 참조를 확인하라.");
            }
        }

        /// <summary>EconomyManager 가 자기 자신을 넘겨 준다. 구현 클래스를 직접 참조하지 않기 위한 통로다.</summary>
        public void SetEconomyService(IEconomyService economyService)
        {
            _economyService = economyService;
        }

        /// <summary>
        /// 단계 진행 상태 조회 통로를 넣는다. 서비스 계약이 아니라 조립(wiring) 통로다 (이슈 #150).
        /// 청구서 금액과 기한이 단일 출처(IStageService)의 현재 단계를 따른다.
        /// </summary>
        public void SetStageService(IStageService stageService)
        {
            _stageService = stageService;
        }

        /// <summary>
        /// 하루(런)를 시작한다. 첫 호출은 1일차를 그대로 쓰고, 이후 호출마다 날짜를 하루 올린다.
        /// 활성 청구서가 없을 때만 새 청구서를 발행한다 — 게임 시작·파산 재시작에도 첫 청구서가 나간다.
        /// 청구서가 있으면 매 호출 끝에 OnBillDueSoon 을 발행한다 — HUD 가 이 이벤트로 남은 일수를 갱신한다
        /// (ARCHITECTURE.md 3절 "UI는 구독 후 공용 조회 인터페이스로 초기 상태를 한 번 읽는다").
        /// GameManager 가 런 시작 직전에 부른다 (작업 2.5, 아직 배선되지 않음).
        /// </summary>
        public void BeginRun()
        {
            if (_hasBegun)
            {
                _currentDay++;
            }
            _hasBegun = true;

            if (_activeBill == null)
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
        /// GameManager 가 런 종료 처리 중 부른다 (작업 2.5, 아직 배선되지 않음).
        /// </summary>
        public void EndRun()
        {
            GameEvents.PublishDayEnded(_currentDay);

            if (!IsBillOverdue())
            {
                return;
            }
            HandleBankruptcy();
        }

        /// <summary>
        /// 마감을 넘긴 미납 청구서가 있는가. **미납 = 즉시 파산**이며 유예·부분 납부·반액 정산은
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
        /// **경고: 지금 이 메서드는 게임에서 실행되지 않는다.** BillManager 가 IRunScoped 를
        /// 구현하지 않아 GameManager 의 GetComponentsInChildren&lt;IRunScoped&gt; 에 잡히지 않고,
        /// EndRun() 을 부르는 곳이 런타임에 없다. 판정 로직은 Edit Mode 검증으로만 확인했다.
        /// 배선은 별도 카드에서 다룬다 (docs/TECH_NOTES/billing.md "알려진 한계").
        ///
        /// 배선이 붙으면 EndRun 은 Result 전이 도중에 불리게 되므로, OnBankrupt 를 받아
        /// GameManager 가 Result 로 전이한다는 계약(ARCHITECTURE 3절)은 그때도 이 경로에서는
        /// 늦다. 파산은 런을 끝내는 원인이 아니라 끝난 런의 결과다 — 마감을 놓치는 순간이 곧
        /// 하루의 끝이라 런 도중에 파산이 날 길이 없다. 정리는 #158 에서 한다.
        /// </summary>
        private void HandleBankruptcy()
        {
            GameEvents.PublishBankrupt();
            ResetRound();
        }

        /// <summary>
        /// 새 회차 값으로 되돌린다. 영구 업그레이드와 최고 기록은 건드리지 않는다
        /// (ARCHITECTURE "저장 경계"). 다음 BeginRun 이 1일차 첫 청구서를 발행한다 —
        /// _hasBegun 을 내려 두므로 날짜가 증가하지 않는다.
        ///
        /// _billIndex 도 되돌린다. #150 이후 이 필드는 단계가 아니라 **누적 청구서 순번**이며
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

            // 단계를 1단계로 되돌린다. 인덱스는 0부터라 0 이 1단계다.
            _stageService?.RestoreStage(0);

            // **코인과 소수 잔여는 여기서 못 비운다.** 지갑을 비울 수 있는 IWalletPersistence 는
            // "SaveManager 만 쓴다"로 사용자가 묶여 있고, EconomyManager 가 스스로 비우려면
            // OnBankrupt 를 구독해야 하는데 그것도 "GameManager 만 구독"이다.
            // 어느 쪽이든 공용 계약을 넓혀야 해서 별도 이슈로 뺐다 — 그때까지 파산해도 돈은 남는다.
        }

        /// <summary>
        /// 마감 전 조기 납부. 활성 청구서와 같은 인스턴스여야 하고, EconomyManager 를 통해 코인을
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
            _activeBill = null;
            GameEvents.PublishBillPaid(bill);

            RollPerkOffer();
            return true;
        }

        /// <summary>OfferedPerkIds 중 하나를 고른다. 실제 효과 적용은 각 시스템의 몫 — 여기서는 알리기만 한다.</summary>
        public bool TryChoosePerk(string perkId)
        {
            if (_offeredPerkIds.Length == 0 || Array.IndexOf(_offeredPerkIds, perkId) < 0)
            {
                return false;
            }
            _offeredPerkIds = Array.Empty<string>();
            GameEvents.PublishPerkChosen(perkId);
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

            // _billIndex 는 다음에 발행할 순번이라, 지금 손에 든 청구서는 그 하나 앞이다.
            if (_billIndex - 1 < config.LoanUnlockBillIndex)
            {
                return false;
            }

            if (IsLoanOnCooldown(config))
            {
                return false;
            }

            // 한도는 지금 막아야 할 청구서 금액이다. 낼 청구서가 없으면 빌릴 이유도 없다
            // (BALANCE.md 4절이 "청구서 전액을 빌리면" 을 상한으로 두고 상환 가능성을 검증한다).
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
        /// 일일 징수율을 빌린 금액에 비례해 정한다 — 청구서 전액을 빌리면 상한, 조금만 빌리면 하한에 가깝다.
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
        /// stages.csv 의 단계값으로 청구서를 만든다. 마감일 = 발행일 + 기한 - 1 (Bill.DueDay 계약).
        /// 단계는 단일 출처(_stageService)의 현재 단계를 따르고, 없으면 1단계로 폴백한다 (이슈 #150).
        /// _billIndex 는 대출 해금 등에서 쓸 누적 청구서 순번으로 유지한다.
        /// </summary>
        private void IssueBill()
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
                IssuedDay = _currentDay,
                DueDay = _currentDay + stage.DueDays - 1,
                IsPaid = false,
            };
            _billIndex++;
            GameEvents.PublishBillIssued(_activeBill);
        }
    }
}
