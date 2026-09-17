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
    /// 대출·파산 판정(4.3~4.4)은 이 클래스의 범위가 아니다 — TryTakeLoan/TryRepayLoan 은 계약을 지키는 스텁이다.
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

        /// <summary>코인 차감의 출처. EconomyManager 가 초기화 때 넣어 준다 (ManagerBootstrap). 없으면 납부는 항상 실패한다.</summary>
        private IEconomyService _economyService;

        public int CurrentDay => _currentDay;

        public int DaysLeft => _activeBill == null
            ? 0
            : Mathf.Max(0, _activeBill.DueDay - _currentDay + 1);

        // ponytail: 대출(4.3) 미구현. 대출이 없으면 0을 돌려주는 계약(IBillService 주석)을 그대로 만족한다.
        public float LoanDailyCut => 0f;

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

        // ponytail: 대출(4.3) 미구현.
        public bool TryTakeLoan(long amount) => false;

        // ponytail: 대출(4.3) 미구현.
        public bool TryRepayLoan() => false;

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
        /// 단계 진행을 관리하는 매니저가 아직 없어 자체 순번(_billIndex)으로 stages.csv 를 순서대로 읽는다
        /// — 마지막 단계를 넘기면 그 값을 그대로 유지한다.
        /// </summary>
        private void IssueBill()
        {
            if (_balanceData == null || _balanceData.Stages.Count == 0)
            {
                return;
            }

            var stageNumber = Mathf.Min(_billIndex, _balanceData.Stages.Count);
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
