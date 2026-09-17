using System;
using System.Reflection;
using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 하루 진행(BeginRun/EndRun)과 청구서 발행(IssueBill)을 검증한다.
    /// 납부·대출(TryPay/TryTakeLoan/TryRepayLoan)은 항상 실패하는 스텁이므로 반환값만 본다 (#27 범위 밖).
    ///
    /// 한계: BillManager 는 GameEvents 를 구독하지 않으므로 OnEnable/OnDisable 짝 검증은 없다
    /// (StaminaChecks/EconomyManagerChecks 와 다른 점). BeginRun/EndRun 은 public 메서드라 리플렉션 없이 직접 부른다.
    /// </summary>
    public static class BillManagerChecks
    {
        public static void RunBatch()
        {
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            AssertCondition(balance != null, "BalanceData 에셋을 찾지 못했습니다.");
            AssertCondition(balance.Stages.Count >= 3, "stages.csv 행이 3개 미만입니다. 기대값을 다시 맞춰야 합니다.");

            var checkCount = RunManagerChecks(balance);
            Debug.Log("[BillManagerChecks] PASS " + checkCount + " checks.");
        }

        private static int RunManagerChecks(BalanceData balance)
        {
            var checkCount = 0;
            var stage1 = balance.GetStage(1);
            var stage2 = balance.GetStage(2);

            var issuedCount = 0;
            Bill lastIssued = null;
            var dayEndedCount = 0;
            var lastCompletedDay = 0;
            var dueSoonCount = 0;
            var lastDaysLeft = -1;

            Action<Bill> onIssued = bill =>
            {
                issuedCount++;
                lastIssued = bill;
            };
            Action<int> onDayEnded = completedDay =>
            {
                dayEndedCount++;
                lastCompletedDay = completedDay;
            };
            Action<int> onDueSoon = daysLeft =>
            {
                dueSoonCount++;
                lastDaysLeft = daysLeft;
            };

            BillManager manager = null;
            GameObject host = null;

            // 정리는 전부 finally 에 둔다 (AGENTS.md — 정적 이벤트 구독을 남기면 다음 실행이 오염된다).
            try
            {
                GameEvents.OnBillIssued += onIssued;
                GameEvents.OnDayEnded += onDayEnded;
                GameEvents.OnBillDueSoon += onDueSoon;

                manager = CreateManager(balance, out host);

                // BeginRun 전에는 청구서가 없다. HUD 가 값을 읽어도 0일차·0원이어야 한다.
                AssertCondition(manager.CurrentDay == 1, "시작 전 CurrentDay 가 1 이 아닙니다: " + manager.CurrentDay);
                AssertCondition(manager.DaysLeft == 0, "시작 전 DaysLeft 가 0 이 아닙니다: " + manager.DaysLeft);
                checkCount++;

                // 첫 BeginRun: 날짜는 그대로 1일차, 1단계 금액·기한으로 청구서가 나간다 (게임 시작 시 첫 청구서).
                manager.BeginRun();
                AssertCondition(issuedCount == 1, "첫 BeginRun 에 청구서가 정확히 1번 발행되지 않았습니다: " + issuedCount);
                AssertCondition(manager.CurrentDay == 1, "첫 BeginRun 후 CurrentDay 가 1 이 아닙니다: " + manager.CurrentDay);
                AssertCondition(lastIssued.Amount == stage1.BillAmount,
                                "1단계 청구 금액이 다릅니다: " + lastIssued.Amount);
                AssertCondition(lastIssued.DueDay == 1 + stage1.DueDays - 1,
                                "1단계 마감일이 다릅니다: " + lastIssued.DueDay);
                AssertCondition(manager.DaysLeft == stage1.DueDays,
                                "발행 직후 DaysLeft 가 기한과 다릅니다: " + manager.DaysLeft);
                AssertCondition(dueSoonCount == 1, "첫 BeginRun 에 OnBillDueSoon 이 정확히 1번 발행되지 않았습니다: " + dueSoonCount);
                AssertCondition(lastDaysLeft == stage1.DueDays,
                                "OnBillDueSoon 인자가 DaysLeft 와 다릅니다: " + lastDaysLeft);
                checkCount++;

                // 두 번째 BeginRun 부터 날짜가 하루씩 오른다. 청구서가 아직 안 갚혔으니 새로 나가지 않는다.
                manager.BeginRun();
                AssertCondition(manager.CurrentDay == 2, "두 번째 BeginRun 후 CurrentDay 가 2 가 아닙니다: " + manager.CurrentDay);
                AssertCondition(issuedCount == 1, "미납 상태인데 청구서가 다시 발행됐습니다: " + issuedCount);
                AssertCondition(manager.DaysLeft == stage1.DueDays - 1,
                                "하루 지난 뒤 DaysLeft 가 줄지 않았습니다: " + manager.DaysLeft);
                AssertCondition(dueSoonCount == 2, "두 번째 BeginRun 에 OnBillDueSoon 이 추가로 발행되지 않았습니다: " + dueSoonCount);
                AssertCondition(lastDaysLeft == stage1.DueDays - 1,
                                "두 번째 OnBillDueSoon 인자가 줄어든 DaysLeft 와 다릅니다: " + lastDaysLeft);
                checkCount++;

                // 기한을 넘겨도 DaysLeft 는 음수가 아니라 0 에서 멈춘다 (HUD 가 음수를 찍지 않도록).
                for (var i = 0; i < 10; i++)
                {
                    manager.BeginRun();
                }
                AssertCondition(manager.DaysLeft == 0, "기한을 넘긴 뒤 DaysLeft 가 0 이 아닙니다: " + manager.DaysLeft);
                checkCount++;

                // EndRun 은 런 종료를 하루 종료로 집계한다 — 호출당 정확히 한 번만 발행한다.
                dayEndedCount = 0;
                manager.EndRun();
                AssertCondition(dayEndedCount == 1, "EndRun 1회에 OnDayEnded 가 1번 발행되지 않았습니다: " + dayEndedCount);
                AssertCondition(lastCompletedDay == manager.CurrentDay,
                                "OnDayEnded 인자가 CurrentDay 와 다릅니다: " + lastCompletedDay);
                checkCount++;

                // 4.2/4.3 범위(납부·대출)는 항상 실패하는 스텁이다. 청구서가 그대로 남아 있어야 한다.
                AssertCondition(manager.TryPay(lastIssued) == false, "TryPay 가 false 를 돌려주지 않았습니다.");
                AssertCondition(manager.TryTakeLoan(100L) == false, "TryTakeLoan 이 false 를 돌려주지 않았습니다.");
                AssertCondition(manager.TryRepayLoan() == false, "TryRepayLoan 이 false 를 돌려주지 않았습니다.");
                AssertCondition(manager.LoanDailyCut == 0f, "LoanDailyCut 이 0 이 아닙니다: " + manager.LoanDailyCut);
                checkCount++;

                _ = stage2; // 2단계는 자체 _billIndex 순번 설계상 이 매니저 인스턴스에서는 미납 때문에 도달하지 않는다. 값만 참조해 미사용 경고를 막는다.
            }
            finally
            {
                GameEvents.OnBillIssued -= onIssued;
                GameEvents.OnDayEnded -= onDayEnded;
                GameEvents.OnBillDueSoon -= onDueSoon;
                TearDown(ref manager, ref host);
            }

            return checkCount;
        }

        private static void TearDown(ref BillManager manager, ref GameObject host)
        {
            manager = null;
            if (host != null)
            {
                UnityEngine.Object.DestroyImmediate(host);
                host = null;
            }
        }

        /// <summary>
        /// 비활성 상태로 만들어 컴포넌트를 붙이고 BalanceData 를 넣은 뒤 켠다 — Awake 가 null 로 먼저
        /// 불려 가짜 경고를 찍지 않도록 순서를 지킨다 (StaminaChecks/EconomyManagerChecks 와 동일 패턴).
        /// 씬을 더럽히지 않도록 HideAndDontSave 로 둔다.
        /// </summary>
        private static BillManager CreateManager(BalanceData balanceData, out GameObject host)
        {
            host = new GameObject("BillManagerCheck")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            host.SetActive(false);
            var manager = host.AddComponent<BillManager>();
            var field = typeof(BillManager).GetField("_balanceData",
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(field != null, "_balanceData 필드를 찾지 못했습니다. 이름이 바뀌었습니까?");
            field.SetValue(manager, balanceData);
            host.SetActive(true);
            return manager;
        }

        private static void AssertCondition(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
