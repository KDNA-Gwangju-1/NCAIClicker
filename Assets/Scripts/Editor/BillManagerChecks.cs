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
    /// 하루 진행(BeginRun/EndRun), 청구서 발행(IssueBill), 조기 납부·퍼크 선택(TryPay/TryChoosePerk, #28)을 검증한다.
    /// 대출(TryTakeLoan/TryRepayLoan)은 아직 항상 실패하는 스텁이므로 반환값만 본다 (4.3 범위 밖).
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

                // EconomyService 가 없으면 조기 납부는 항상 실패하고 청구서가 그대로 남는다.
                AssertCondition(manager.TryPay(lastIssued) == false, "EconomyService 없이 TryPay 가 성공했습니다.");
                AssertCondition(manager.ActiveBill == lastIssued, "실패한 TryPay 가 청구서를 지웠습니다.");
                checkCount++;

                var economy = new FakeEconomyService { NextSpendSucceeds = false };
                manager.SetEconomyService(economy);

                // 코인이 모자라면 실패하고 청구서가 남는다.
                AssertCondition(manager.TryPay(lastIssued) == false, "코인이 모자란데 TryPay 가 성공했습니다.");
                AssertCondition(manager.ActiveBill == lastIssued, "실패한 TryPay 가 청구서를 지웠습니다.");
                checkCount++;

                // 활성 청구서와 다른 인스턴스는 코인이 충분해도 납부할 수 없다.
                economy.NextSpendSucceeds = true;
                var foreignBill = new Bill
                {
                    Amount = lastIssued.Amount,
                    IssuedDay = lastIssued.IssuedDay,
                    DueDay = lastIssued.DueDay,
                    IsPaid = false,
                };
                AssertCondition(manager.TryPay(foreignBill) == false, "활성 청구서가 아닌데 TryPay 가 성공했습니다.");
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
                    // 코인이 충분하면 성공한다 — 청구서가 사라지고 OnBillPaid 가 뜨고 퍼크 후보 3종이 제시된다.
                    AssertCondition(manager.TryPay(lastIssued) == true, "코인이 충분한데 TryPay 가 실패했습니다.");
                    AssertCondition(economy.LastSpendAmount == lastIssued.Amount,
                                    "차감 요청 금액이 청구 금액과 다릅니다: " + economy.LastSpendAmount);
                    AssertCondition(lastIssued.IsPaid, "납부한 청구서의 IsPaid 가 true 로 바뀌지 않았습니다.");
                    AssertCondition(manager.ActiveBill == null, "납부 후 ActiveBill 이 비지 않았습니다.");
                    AssertCondition(manager.DaysLeft == 0, "납부 후 DaysLeft 가 0 이 아닙니다: " + manager.DaysLeft);
                    AssertCondition(paidCount == 1, "OnBillPaid 가 정확히 1번 발행되지 않았습니다: " + paidCount);
                    AssertCondition(lastPaid == lastIssued, "OnBillPaid 인자가 납부한 청구서와 다릅니다.");
                    checkCount++;

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

                    // 이미 낸 청구서는 다시 낼 수 없다.
                    AssertCondition(manager.TryPay(lastIssued) == false, "이미 낸 청구서를 다시 TryPay 할 수 있었습니다.");
                    checkCount++;

                    // 후보에 없는 id 는 고를 수 없고, 후보 목록은 그대로 남는다.
                    var offeredBefore = manager.OfferedPerkIds;
                    AssertCondition(manager.TryChoosePerk("no_such_perk") == false, "존재하지 않는 퍼크를 고를 수 있었습니다.");
                    AssertCondition(manager.OfferedPerkIds == offeredBefore, "실패한 TryChoosePerk 가 후보 목록을 바꿨습니다.");
                    AssertCondition(chosenCount == 0, "실패한 TryChoosePerk 인데 OnPerkChosen 이 발행됐습니다.");
                    checkCount++;

                    // 후보 중 하나를 고르면 성공하고 OnPerkChosen 이 뜨고 후보 목록이 비워진다.
                    var picked = offeredBefore[0];
                    AssertCondition(manager.TryChoosePerk(picked) == true, "제시된 퍼크를 고르지 못했습니다.");
                    AssertCondition(chosenCount == 1, "OnPerkChosen 이 정확히 1번 발행되지 않았습니다: " + chosenCount);
                    AssertCondition(lastChosen == picked, "OnPerkChosen 인자가 고른 퍼크와 다릅니다.");
                    AssertCondition(manager.OfferedPerkIds.Length == 0, "고른 뒤에도 후보 목록이 남아 있습니다.");
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

                // 대출(4.3)은 항상 실패하는 스텁이다.
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

        /// <summary>코인 차감 성공/실패만 제어하는 가짜 구현. 잔액 계산은 CoinWalletChecks/EconomyManagerChecks 가 본다.</summary>
        private class FakeEconomyService : IEconomyService
        {
            public bool NextSpendSucceeds { get; set; } = true;
            public long LastSpendAmount { get; private set; } = -1L;
            public long CurrentCoin => 0L;
            public long RunCoin => 0L;
            public void AddCoin(decimal rawAmount) { }
            public void AddLoanPrincipal(long amount) { }

            public bool TrySpendCoin(long amount)
            {
                LastSpendAmount = amount;
                return NextSpendSucceeds;
            }
        }
    }
}
