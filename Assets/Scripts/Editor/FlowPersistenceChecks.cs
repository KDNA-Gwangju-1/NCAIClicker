using System;
using System.IO;
using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Interfaces;
using NCAIClicker.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 납부 후 퍽 선택, 새 고지서, 스킬 트리 안내 흐름의 4단계 저장 및 복원을 검증한다 (이슈 #249).
    /// 1. 납부 완료 상태 복원
    /// 2. 퍽 선택 대기 상태 복원
    /// 3. 새 고지서 확인 상태 복원 (날짜/납기 유지)
    /// 4. 메뉴 투자 대기 상태 복원 (하단 계속하기로만 진행)
    /// </summary>
    public static class FlowPersistenceChecks
    {
        private const string SaveFileName = "save.json";

        [MenuItem("NCAI/검증/납부 후 흐름 및 저장 복원 검증")]
        public static void RunBatch()
        {
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            Assert(balance != null, "BalanceData 에셋이 없습니다.");

            var checkCount = 0;
            var backupPath = BackupSaveFile();

            try
            {
                checkCount += RunPaidStatePersistence(balance);
                checkCount += RunPerkSelectionPersistence(balance);
                checkCount += RunNewBillPersistence(balance);
                checkCount += RunMenuInvestmentFlow(balance);
                Debug.Log("[FlowPersistenceChecks] PASS " + checkCount + " checks.");
            }
            finally
            {
                RestoreSaveFile(backupPath);
            }
        }

        private static int RunPaidStatePersistence(BalanceData balance)
        {
            var checkCount = 0;
            var saveHost = CreateRig(balance, out var save, out var billManager, out var economy);

            try
            {
                economy.AddCoin(1000000L);
                billManager.BeginRun();
                var bill = billManager.ActiveBill;
                Assert(bill != null, "고지서가 발행되지 않았습니다.");

                billManager.TryPay(bill);
                Assert(bill.IsPaid, "고지서 납부가 완료되어야 합니다.");
                Assert(billManager.ActiveBill != null && billManager.ActiveBill.IsPaid, "납부 후에도 납부 완료된 고지서가 유지되어야 합니다.");
                Assert(billManager.PaymentFlowState == PostPaymentFlowState.PaidFeedback,
                       "납부 직후 흐름 상태가 저장 가능한 PaidFeedback이어야 합니다.");

                save.CollectAndSave();

                var loaded = save.Load();
                Assert(loaded.HasActiveBill && loaded.ActiveBill != null && loaded.ActiveBill.IsPaid,
                       "저장 파일에 납부 완료 상태의 고지서가 보존되어야 합니다 (#249).");
                Assert(loaded.PostPaymentFlowState == PostPaymentFlowState.PaidFeedback,
                       "납부 완료 피드백 재개 지점이 저장되어야 합니다.");
                checkCount++;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(saveHost);
            }

            return checkCount;
        }

        private static int RunPerkSelectionPersistence(BalanceData balance)
        {
            var checkCount = 0;
            var saveHost = CreateRig(balance, out var save, out var billManager, out var economy);

            try
            {
                economy.AddCoin(1000000L);
                billManager.BeginRun();
                var bill = billManager.ActiveBill;
                billManager.TryPay(bill);
                billManager.TryConfirmPaidFeedback();

                var offered = billManager.OfferedPerkIds;
                Assert(offered != null && offered.Length == 3, "납부 후 3개의 퍽 후보가 생성되어야 합니다.");

                save.CollectAndSave();

                var loaded = save.Load();
                Assert(loaded.OfferedPerkIds != null && loaded.OfferedPerkIds.Length == 3,
                       "저장 파일에 퍽 후보 3장이 보존되어야 합니다 (#249).");
                Assert(loaded.PostPaymentFlowState == PostPaymentFlowState.PerkSelection,
                       "퍽 선택 대기 재개 지점이 저장되어야 합니다.");
                checkCount++;

                // 복원 시뮬레이션
                var newBillHost = new GameObject("BillHost", typeof(BillManager));
                var newBillManager = newBillHost.GetComponent<BillManager>();
                newBillManager.RestoreBillState(loaded.CurrentDay, loaded.BillIndex, loaded.ActiveBill,
                                              loaded.ActiveLoan, loaded.LastLoanRepaidDay, loaded.OfferedPerkIds,
                                              loaded.PostPaymentFlowState);

                Assert(newBillManager.OfferedPerkIds.Length == 3, "복원 후 퍽 후보가 3장이어야 합니다.");
                var dayBeforeBlockedBegin = newBillManager.CurrentDay;
                newBillManager.BeginRun();
                Assert(newBillManager.CurrentDay == dayBeforeBlockedBegin,
                       "퍽 선택 대기 중에는 BeginRun으로 날짜가 진행되면 안 됩니다.");
                checkCount++;

                UnityEngine.Object.DestroyImmediate(newBillHost);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(saveHost);
            }

            return checkCount;
        }

        private static int RunNewBillPersistence(BalanceData balance)
        {
            var checkCount = 0;
            var saveHost = CreateRig(balance, out var save, out var billManager, out var economy);

            try
            {
                economy.AddCoin(1000000L);
                billManager.BeginRun();
                var dayBefore = billManager.CurrentDay;
                var bill = billManager.ActiveBill;
                billManager.TryPay(bill);
                billManager.TryConfirmPaidFeedback();

                var perkId = billManager.OfferedPerkIds[0];
                var chooseSuccess = billManager.TryChoosePerk(perkId);
                Assert(chooseSuccess, "퍽 선택이 성공해야 합니다.");

                var newBill = billManager.ActiveBill;
                Assert(newBill != null && !newBill.IsPaid, "퍽 선택 직후 미납 새 고지서가 발행되어야 합니다 (#249).");
                Assert(billManager.CurrentDay == dayBefore, "새 고지서 확인 중에는 날짜가 증가하지 않아야 합니다 (#249).");
                var daysLeftBefore = billManager.DaysLeft;
                checkCount++;

                save.CollectAndSave();

                var loaded = save.Load();
                Assert(loaded.HasActiveBill && loaded.ActiveBill != null && !loaded.ActiveBill.IsPaid,
                       "새 고지서가 저장되어야 합니다.");
                Assert(loaded.CurrentDay == dayBefore, "저장된 날짜가 그대로 유지되어야 합니다.");
                Assert(loaded.PostPaymentFlowState == PostPaymentFlowState.NewBillConfirmation,
                       "새 고지서 확인 재개 지점이 저장되어야 합니다.");
                checkCount++;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(saveHost);
            }

            return checkCount;
        }

        private static int RunMenuInvestmentFlow(BalanceData balance)
        {
            var checkCount = 0;
            var saveHost = CreateRig(balance, out var save, out var billManager, out var economy);

            try
            {
                economy.AddCoin(1000000L);
                billManager.BeginRun();
                var bill = billManager.ActiveBill;
                billManager.TryPay(bill);
                billManager.TryConfirmPaidFeedback();
                billManager.TryChoosePerk(billManager.OfferedPerkIds[0]);

                var dayDuringMenu = billManager.CurrentDay;
                var daysLeftDuringMenu = billManager.DaysLeft;

                // 새 고지서 발행 후 다음 런 시작 전까지는 날짜와 납기가 그대로 유지됨
                Assert(billManager.CurrentDay == dayDuringMenu, "메뉴 체류 중 날짜가 증가하면 안 됩니다.");
                Assert(billManager.DaysLeft == daysLeftDuringMenu, "메뉴 체류 중 납기 일수가 줄어들면 안 됩니다.");
                checkCount++;

                Assert(billManager.TryEnterInvestmentMenu(), "새 고지서 확인 뒤 투자 메뉴로 전환되어야 합니다.");
                Assert(billManager.PaymentFlowState == PostPaymentFlowState.InvestmentMenu,
                       "투자 메뉴 대기 상태가 기록되어야 합니다.");
                save.CollectAndSave();
                var loaded = save.Load();
                Assert(loaded.PostPaymentFlowState == PostPaymentFlowState.InvestmentMenu,
                       "투자 메뉴 대기 재개 지점이 저장되어야 합니다.");
                Assert(billManager.TryCompletePostPaymentFlow(), "투자 메뉴에서만 계속하기가 허용되어야 합니다.");

                // 하단 계속하기를 눌러 다음 런을 시작할 때 비로소 날짜가 진행됨
                billManager.BeginRun();
                Assert(billManager.CurrentDay == dayDuringMenu + 1, "계속하기로 다음 런을 시작해야 날짜가 하루 증가합니다 (#249).");
                checkCount++;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(saveHost);
            }

            return checkCount;
        }

        private static GameObject CreateRig(BalanceData balance, out SaveManager save, out BillManager bill, out EconomyManager economy)
        {
            var host = new GameObject("PersistenceRig");
            economy = host.AddComponent<EconomyManager>();
            var stage = host.AddComponent<StageGoalManager>();
            bill = host.AddComponent<BillManager>();
            save = host.AddComponent<SaveManager>();

            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            typeof(BillManager).GetField("_balanceData", flags).SetValue(bill, balance);
            typeof(EconomyManager).GetField("_balanceData", flags).SetValue(economy, balance);
            typeof(StageGoalManager).GetField("_balanceData", flags).SetValue(stage, balance);

            bill.SetEconomyService(economy);
            bill.SetStageService(stage);
            bill.SetWalletPersistence(economy);

            save.SetPersistenceTargets(economy, economy, economy, economy, economy, stage, bill);
            return host;
        }

        private static string BackupSaveFile()
        {
            var path = Path.Combine(Application.persistentDataPath, SaveFileName);
            if (!File.Exists(path))
            {
                return null;
            }

            var backup = path + ".flow_bak";
            File.Copy(path, backup, true);
            return backup;
        }

        private static void RestoreSaveFile(string backupPath)
        {
            var path = Path.Combine(Application.persistentDataPath, SaveFileName);
            if (backupPath != null && File.Exists(backupPath))
            {
                File.Copy(backupPath, path, true);
                File.Delete(backupPath);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("[FlowPersistenceChecks] " + message);
            }
        }
    }
}
