using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using UnityEngine;

namespace NCAIClicker.Economy
{
    /// <summary>
    /// 하루 진행과 청구서 발행을 관리한다. 한 번의 런 = 하루 하나 (ARCHITECTURE.md "하루 종료 순서").
    /// 납부·대출·파산 판정(4.2~4.4)은 이 클래스의 범위가 아니다 — TryPay/TryTakeLoan/TryRepayLoan 은
    /// 계약을 지키는 스텁이다. 완료 기준의 정본은 GitHub 이슈 #27.
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

        public int CurrentDay => _currentDay;

        public int DaysLeft => _activeBill == null
            ? 0
            : Mathf.Max(0, _activeBill.DueDay - _currentDay + 1);

        // ponytail: 대출(4.3) 미구현. 대출이 없으면 0을 돌려주는 계약(IBillService 주석)을 그대로 만족한다.
        public float LoanDailyCut => 0f;

        private void Awake()
        {
            Instance = this;
            if (_balanceData == null)
            {
                Debug.LogError("[BillManager] BalanceData 가 연결되지 않았다. " +
                               "청구서를 발행하지 못하니 Managers 프리팹의 참조를 확인하라.");
            }
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

        // ponytail: 납부(4.2) 미구현. 항상 실패로 두어 청구서가 그대로 남게 한다.
        public bool TryPay(Bill bill) => false;

        // ponytail: 대출(4.3) 미구현.
        public bool TryTakeLoan(long amount) => false;

        // ponytail: 대출(4.3) 미구현.
        public bool TryRepayLoan() => false;

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
