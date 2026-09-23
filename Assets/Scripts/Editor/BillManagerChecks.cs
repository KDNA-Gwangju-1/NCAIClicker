using System;
using System.Collections.Generic;
using System.Reflection;
using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 하루 진행(BeginRun/EndRun), 고지서 발행(IssueBill), 조기 납부·퍼크 선택(TryPay/TryChoosePerk, #28)을 검증한다.
    /// 대출(TryTakeLoan/TryRepayLoan, #29)은 해금 순번·이자·징수율·동시 건수·쿨다운을 RunLoanChecks 에서 따로 본다.
    /// TryPay 는 실제 EconomyManager 대신 FakeEconomyService 로 코인 차감 성공/실패를 제어해 격리한다
    /// (EconomyManagerChecks 의 FakeBillService 와 같은 패턴).
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
            AssertCondition(balance.Perks.Count >= 3, "perks.csv 행이 3개 미만입니다. 퍼크 후보 3종을 뽑을 수 없습니다.");

            var checkCount = RunManagerChecks(balance);
            checkCount += RunLoanChecks(balance);
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

                // BeginRun 전에는 고지서가 없다. HUD 가 값을 읽어도 0일차·0원이어야 한다.
                AssertCondition(manager.CurrentDay == 1, "시작 전 CurrentDay 가 1 이 아닙니다: " + manager.CurrentDay);
                AssertCondition(manager.DaysLeft == 0, "시작 전 DaysLeft 가 0 이 아닙니다: " + manager.DaysLeft);
                checkCount++;

                // 첫 BeginRun: 날짜는 그대로 1일차, 1단계 금액·기한으로 고지서가 나간다 (게임 시작 시 첫 고지서).
                manager.BeginRun();
                AssertCondition(issuedCount == 1, "첫 BeginRun 에 고지서가 정확히 1번 발행되지 않았습니다: " + issuedCount);
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

                // 두 번째 BeginRun 부터 날짜가 하루씩 오른다. 고지서가 아직 안 갚혔으니 새로 나가지 않는다.
                manager.BeginRun();
                AssertCondition(manager.CurrentDay == 2, "두 번째 BeginRun 후 CurrentDay 가 2 가 아닙니다: " + manager.CurrentDay);
                AssertCondition(issuedCount == 1, "미납 상태인데 고지서가 다시 발행됐습니다: " + issuedCount);
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
                // 파산 판정은 다음 날 진입 시점으로 분리되었으므로 (이슈 #211), EndRun 직후에는 CurrentDay 가 유지된다.
                dayEndedCount = 0;
                var endedDay = manager.CurrentDay;
                manager.EndRun();
                AssertCondition(dayEndedCount == 1, "EndRun 1회에 OnDayEnded 가 1번 발행되지 않았습니다: " + dayEndedCount);
                AssertCondition(lastCompletedDay == endedDay,
                                "OnDayEnded 인자가 마감한 날과 다릅니다: " + lastCompletedDay);
                checkCount++;

                // 다음 날 진입 시점에 마감을 확정한다 (이슈 #211).
                // 위에서 기한을 넘긴 고지서를 그대로 뒀으므로 TryCloseDay 에서 파산이 처리된다.
                var closed = manager.TryCloseDay();
                AssertCondition(closed, "기한을 넘긴 고지서가 TryCloseDay 에서 파산 처리되지 않았습니다.");
                AssertCondition(manager.ActiveBill == null, "파산 후에도 고지서가 남아 있습니다.");
                manager.BeginRun();
                AssertCondition(manager.ActiveBill != null, "파산 후 첫 BeginRun 이 고지서를 발행하지 않았습니다.");
                checkCount++;

                // EconomyService 가 없으면 조기 납부는 항상 실패하고 고지서가 그대로 남는다.
                AssertCondition(manager.TryPay(lastIssued) == false, "EconomyService 없이 TryPay 가 성공했습니다.");
                AssertCondition(manager.ActiveBill == lastIssued, "실패한 TryPay 가 고지서를 지웠습니다.");
                checkCount++;

                var economy = new FakeEconomyService { NextSpendSucceeds = false };
                manager.SetEconomyService(economy);

                // 코인이 모자라면 실패하고 고지서가 남는다.
                AssertCondition(manager.TryPay(lastIssued) == false, "코인이 모자란데 TryPay 가 성공했습니다.");
                AssertCondition(manager.ActiveBill == lastIssued, "실패한 TryPay 가 고지서를 지웠습니다.");
                checkCount++;

                // 활성 고지서와 다른 인스턴스는 코인이 충분해도 납부할 수 없다.
                economy.NextSpendSucceeds = true;
                var foreignBill = new Bill
                {
                    Amount = lastIssued.Amount,
                    IssuedDay = lastIssued.IssuedDay,
                    DueDay = lastIssued.DueDay,
                    IsPaid = false,
                };
                AssertCondition(manager.TryPay(foreignBill) == false, "활성 고지서가 아닌데 TryPay 가 성공했습니다.");
                checkCount++;

                var paidCount = 0;
                Bill lastPaid = null;
                Action<Bill> onPaid = bill =>
                {
                    paidCount++;
                    lastPaid = bill;
                };
                var offeredCount = 0;
                string[] lastOffered = null;
                Action<string[]> onOffered = ids =>
                {
                    offeredCount++;
                    lastOffered = ids;
                };
                var chosenCount = 0;
                string lastChosen = null;
                Action<string> onChosen = id =>
                {
                    chosenCount++;
                    lastChosen = id;
                };

                GameEvents.OnBillPaid += onPaid;
                GameEvents.OnPerkOffered += onOffered;
                GameEvents.OnPerkChosen += onChosen;
                try
                {
                    // 코인이 충분하면 성공한다 — 고지서가 사라지고 OnBillPaid 가 뜨고 퍼크 후보 3종이 제시된다.
                    AssertCondition(manager.TryPay(lastIssued) == true, "코인이 충분한데 TryPay 가 실패했습니다.");
                    AssertCondition(economy.LastSpendAmount == lastIssued.Amount,
                                    "차감 요청 금액이 청구 금액과 다릅니다: " + economy.LastSpendAmount);
                    AssertCondition(lastIssued.IsPaid, "납부한 고지서의 IsPaid 가 true 로 바뀌지 않았습니다.");
                    AssertCondition(manager.ActiveBill != null && manager.ActiveBill.IsPaid, "납부 후 기존 고지서가 납부 완료 상태로 유지되어야 합니다 (#249).");
                    AssertCondition(paidCount == 1, "OnBillPaid 가 정확히 1번 발행되지 않았습니다: " + paidCount);
                    AssertCondition(lastPaid == lastIssued, "OnBillPaid 인자가 납부한 고지서와 다릅니다.");
                    checkCount++;

                    AssertCondition(manager.PaymentFlowState == PostPaymentFlowState.PaidFeedback,
                                    "납부 직후 납부 완료 피드백 상태가 아닙니다.");
                    AssertCondition(manager.OfferedPerkIds.Length == 0,
                                    "납부 완료가 확인되기 전에 퍼크 후보가 열렸습니다.");
                    AssertCondition(manager.TryConfirmPaidFeedback(), "납부 완료 확인 전환이 실패했습니다.");

                    AssertCondition(offeredCount == 1, "OnPerkOffered 가 정확히 1번 발행되지 않았습니다: " + offeredCount);
                    AssertCondition(manager.OfferedPerkIds.Length == 3, "퍼크 후보가 3종이 아닙니다: " + manager.OfferedPerkIds.Length);
                    AssertCondition(new HashSet<string>(manager.OfferedPerkIds).Count == 3, "퍼크 후보에 중복이 있습니다.");
                    foreach (var perkId in manager.OfferedPerkIds)
                    {
                        AssertCondition(balance.GetPerk(perkId) != null,
                                        "퍼크 후보 id 를 perks.csv 에서 찾지 못했습니다: " + perkId);
                    }
                    AssertCondition(lastOffered == manager.OfferedPerkIds, "OnPerkOffered 인자가 OfferedPerkIds 와 다릅니다.");
                    checkCount++;

                    // 이미 낸 고지서는 다시 낼 수 없다.
                    AssertCondition(manager.TryPay(lastIssued) == false, "이미 낸 고지서를 다시 TryPay 할 수 있었습니다.");
                    checkCount++;

                    // 후보에 없는 id 는 고를 수 없고, 후보 목록은 그대로 남는다.
                    var offeredBefore = manager.OfferedPerkIds;
                    AssertCondition(manager.TryChoosePerk("no_such_perk") == false, "존재하지 않는 퍼크를 고를 수 있었습니다.");
                    AssertCondition(manager.OfferedPerkIds == offeredBefore, "실패한 TryChoosePerk 가 후보 목록을 바꿨습니다.");
                    AssertCondition(chosenCount == 0, "실패한 TryChoosePerk 인데 OnPerkChosen 이 발행됐습니다.");
                    checkCount++;

                    // 후보 중 하나를 고르면 성공하고 OnPerkChosen 이 뜨고 후보 목록이 비워지며 다음 단계 고지서가 즉시 발행된다 (#249).
                    var picked = offeredBefore[0];
                    AssertCondition(manager.TryChoosePerk(picked) == true, "제시된 퍼크를 고르지 못했습니다.");
                    AssertCondition(chosenCount == 1, "OnPerkChosen 이 정확히 1번 발행되지 않았습니다: " + chosenCount);
                    AssertCondition(lastChosen == picked, "OnPerkChosen 인자가 고른 퍼크와 다릅니다.");
                    AssertCondition(manager.OfferedPerkIds.Length == 0, "고른 뒤에도 후보 목록이 남아 있습니다.");
                    AssertCondition(manager.ActiveBill != null && !manager.ActiveBill.IsPaid, "퍼크 선택 직후 새 고지서가 발행되어야 합니다 (#249).");
                    checkCount++;

                    // 퍼크 선택 시점엔 아직 계속하기 전이라 CurrentDay 가 정산 중인 날 그대로다.
                    // 새 고지서의 발행일은 다음 런의 날짜(CurrentDay + 1)여야 due_days 만큼 온전히 돈다 (#270).
                    var expectedIssuedDay = manager.CurrentDay + 1;
                    AssertCondition(manager.ActiveBill.IssuedDay == expectedIssuedDay,
                        "퍼크 선택 후 발행된 고지서의 발행일이 다음 날이 아닙니다: " + manager.ActiveBill.IssuedDay
                        + " (기대 " + expectedIssuedDay + ")");
                    AssertCondition(manager.ActiveBill.DueDay == expectedIssuedDay + stage1.DueDays - 1,
                        "퍼크 선택 후 발행된 고지서의 마감일이 due_days 만큼 돌지 않습니다: " + manager.ActiveBill.DueDay);
                    checkCount++;

                    // 이미 고른 뒤에는 같은 id 라도 다시 고를 수 없다.
                    AssertCondition(manager.TryChoosePerk(picked) == false, "이미 고른 뒤에 다시 TryChoosePerk 가 성공했습니다.");
                    AssertCondition(chosenCount == 1, "재선택이 실패했는데 OnPerkChosen 이 다시 발행됐습니다.");
                    checkCount++;
                }
                finally
                {
                    GameEvents.OnBillPaid -= onPaid;
                    GameEvents.OnPerkOffered -= onOffered;
                    GameEvents.OnPerkChosen -= onChosen;
                }


                _ = stage2; // 단계는 IStageService 가 단일 출처라(#150) 주입이 없는 이 인스턴스는 1단계로 폴백한다. 값만 참조해 미사용 경고를 막는다.
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

        /// <summary>
        /// 대출(#29)을 전용 매니저 인스턴스로 검증한다 — 해금 순번·한도·이자·징수율·동시 건수·쿨다운은
        /// 날짜와 고지서 순번에 얽혀 있어 납부 검증이 끝난 인스턴스를 재활용하면 상태가 섞인다.
        /// </summary>
        private static int RunLoanChecks(BalanceData balance)
        {
            var checkCount = 0;
            var config = balance.Bill;
            var economy = new FakeEconomyService();
            var manager = CreateManager(balance, out var host);
            manager.SetEconomyService(economy);

            try
            {
                // 첫 고지서에서는 아직 대출을 쓸 수 없다 (loan_unlock_bill_index).
                manager.BeginRun();
                var firstBill = manager.ActiveBill;
                AssertCondition(firstBill != null, "첫 고지서가 발행되지 않았습니다.");
                AssertCondition(manager.TryTakeLoan(1L) == false, "첫 고지서인데 대출이 성공했습니다.");
                AssertCondition(manager.LoanDailyCut == 0f, "대출이 없는데 LoanDailyCut 이 0 이 아닙니다: " + manager.LoanDailyCut);
                AssertCondition(manager.LoanOwedAmount == 0L, "대출이 없는데 LoanOwedAmount 가 0 이 아닙니다: " + manager.LoanOwedAmount);
                AssertCondition(manager.IsLoanUnlocked == false, "첫 고지서인데 IsLoanUnlocked 가 true 입니다 (이슈 #306).");
                AssertCondition(manager.LoanCooldownDaysRemaining == 0, "빌린 적 없는데 쿨다운이 남아 있습니다: " + manager.LoanCooldownDaysRemaining);
                checkCount++;

                // 첫 고지서를 내고 하루를 넘기면 두 번째 고지서가 나오고 대출이 열린다.
                economy.NextSpendSucceeds = true;
                AssertCondition(manager.TryPay(firstBill), "첫 고지서 납부가 실패했습니다.");
                AssertCondition(manager.TryConfirmPaidFeedback(), "첫 고지서 납부 완료 확인이 실패했습니다.");
                AssertCondition(manager.TryChoosePerk(manager.OfferedPerkIds[0]), "첫 고지서 퍽 선택이 실패했습니다.");
                AssertCondition(manager.TryEnterInvestmentMenu(), "첫 고지서 뒤 투자 메뉴 진입이 실패했습니다.");
                AssertCondition(manager.TryCompletePostPaymentFlow(), "첫 고지서 뒤 계속하기가 실패했습니다.");
                manager.BeginRun();
                var secondBill = manager.ActiveBill;
                AssertCondition(secondBill != null, "두 번째 고지서가 발행되지 않았습니다.");
                AssertCondition(manager.IsLoanUnlocked, "두 번째 고지서인데 IsLoanUnlocked 가 false 입니다 (이슈 #306).");
                checkCount++;

                // 한도는 활성 고지서 금액이다. 한 푼이라도 넘으면 거부한다.
                AssertCondition(manager.TryTakeLoan(secondBill.Amount + 1L) == false,
                    "고지서 금액을 넘는 대출이 성공했습니다.");
                checkCount++;

                // 전액을 빌리면 성공하고, 이자는 올림으로 확정되며 징수율은 상한이 된다.
                AssertCondition(manager.TryTakeLoan(secondBill.Amount), "해금 뒤에도 대출이 실패했습니다.");
                var expectedOwed = (long)Math.Ceiling(secondBill.Amount * (1m + (decimal)config.LoanInterestRate));
                AssertCondition(economy.LastLoanPrincipal == secondBill.Amount,
                    "원금이 AddLoanPrincipal 로 입금되지 않았습니다: " + economy.LastLoanPrincipal);
                AssertCondition(Mathf.Approximately(manager.LoanDailyCut, config.LoanDailyCutMax),
                    "전액 대출인데 징수율이 상한이 아닙니다: " + manager.LoanDailyCut);
                AssertCondition(manager.LoanOwedAmount == expectedOwed,
                    "LoanOwedAmount 가 이자 포함 상환액과 다릅니다: " + manager.LoanOwedAmount + " (기대 " + expectedOwed + ")");
                checkCount++;

                // 동시 1건. 갚기 전에는 다시 빌릴 수 없다.
                AssertCondition(manager.TryTakeLoan(1L) == false, "대출이 남아 있는데 또 빌릴 수 있었습니다.");
                checkCount++;

                // 잔액이 모자라면 상환은 실패하고 대출은 그대로 남는다.
                economy.NextSpendSucceeds = false;
                AssertCondition(manager.TryRepayLoan() == false, "잔액이 없는데 상환이 성공했습니다.");
                AssertCondition(Mathf.Approximately(manager.LoanDailyCut, config.LoanDailyCutMax),
                    "실패한 상환이 징수율을 바꿨습니다: " + manager.LoanDailyCut);
                AssertCondition(manager.LoanOwedAmount == expectedOwed,
                    "실패한 상환이 LoanOwedAmount 를 바꿨습니다: " + manager.LoanOwedAmount);
                checkCount++;

                // 상환은 이자 포함 전액을 떼고, 끝나면 징수가 멎는다.
                economy.NextSpendSucceeds = true;
                AssertCondition(manager.TryRepayLoan(), "상환이 실패했습니다.");
                AssertCondition(economy.LastSpendAmount == expectedOwed,
                    "상환액이 이자 포함 금액과 다릅니다: " + economy.LastSpendAmount + " (기대 " + expectedOwed + ")");
                AssertCondition(manager.LoanDailyCut == 0f, "상환 뒤에도 징수가 남아 있습니다: " + manager.LoanDailyCut);
                AssertCondition(manager.LoanOwedAmount == 0L, "상환 뒤에도 LoanOwedAmount 가 남아 있습니다: " + manager.LoanOwedAmount);
                checkCount++;

                // 완제 직후에는 재대출 쿨다운에 걸린다.
                manager.BeginRun();
                AssertCondition(manager.TryTakeLoan(1L) == false, "완제 직후인데 재대출이 성공했습니다.");
                AssertCondition(manager.LoanCooldownDaysRemaining > 0,
                    "완제 직후인데 LoanCooldownDaysRemaining 이 0 입니다 (이슈 #306).");
                checkCount++;

                // 쿨다운 일수만큼 날이 지나면 다시 빌릴 수 있고, 조금만 빌리면 징수율은 하한에 가깝다.
                for (var i = 0; i < config.LoanCooldownDays; i++)
                {
                    manager.BeginRun();
                }
                AssertCondition(manager.LoanCooldownDaysRemaining == 0,
                    "쿨다운이 지났는데 LoanCooldownDaysRemaining 이 남아 있습니다: " + manager.LoanCooldownDaysRemaining);
                AssertCondition(manager.TryTakeLoan(1L), "쿨다운이 지났는데 재대출이 실패했습니다.");
                AssertCondition(manager.LoanDailyCut < config.LoanDailyCutMax,
                    "소액 대출인데 징수율이 상한입니다: " + manager.LoanDailyCut);
                AssertCondition(manager.LoanDailyCut >= config.LoanDailyCutMin,
                    "징수율이 하한보다 낮습니다: " + manager.LoanDailyCut);
                checkCount++;
            }
            finally
            {
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

        /// <summary>코인 차감 성공/실패만 제어하는 가짜 구현. 잔액 계산은 CoinWalletChecks/EconomyManagerChecks 가 본다.</summary>
        private class FakeEconomyService : IEconomyService
        {
            public bool NextSpendSucceeds { get; set; } = true;
            public long LastSpendAmount { get; private set; } = -1L;
            public long CurrentCoin => 0L;
            public long RunCoin => 0L;
            public long EarnedTotal => 0L;
            public IReadOnlyList<CoinDrop> RunCoinBreakdown => Array.Empty<CoinDrop>();
            public void AddCoin(decimal rawAmount) { }
            public long LastLoanPrincipal { get; private set; } = -1L;
            public void AddLoanPrincipal(long amount) { LastLoanPrincipal = amount; }

            public bool TrySpendCoin(long amount)
            {
                LastSpendAmount = amount;
                return NextSpendSucceeds;
            }
        }
    }
}
