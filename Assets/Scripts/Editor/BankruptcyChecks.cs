using System;
using System.Collections.Generic;
using System.IO;
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
    /// 마감 미납 파산을 검증한다 (이슈 #30, 지갑 초기화는 #158).
    ///
    /// 규칙은 **미납 = 즉시 파산**이고 유예·부분 납부·반액 정산이 없다 (#6 에서 확정).
    /// 여기서는 판정 경계(마감 당일 vs 하루 넘김)와 회차 초기화 범위를 본다.
    ///
    /// 초기화 범위의 정본은 ARCHITECTURE "저장 경계"다. **날짜·고지서·대출·퍼크·단계·코인·소수
    /// 잔여·업그레이드**까지 전부 이 카드가 닫는다 — 파산을 넘어 유지되는 것은 레거시 포인트와
    /// 반지뿐이다 (이슈 #250, GDD 파산 절 층 표). 그 둘이 남는지는 LegacyPointChecks 가 본다.
    ///
    /// 한계: Edit Mode 는 생명주기를 부르지 않아 필요한 곳은 리플렉션으로 직접 부른다.
    /// </summary>
    public static class BankruptcyChecks
    {
        /// <summary>
        /// 가짜 초기화 통로가 불린 순서를 세는 공용 카운터 (이슈 #250). ResetRound 안에서
        /// 업그레이드가 지갑보다 먼저 지워지는지를 보려면 호출 순서를 알아야 한다.
        /// </summary>
        private static int _callSequence;

        public static void RunBatch()
        {
            // 기대값을 코드에 적지 않는다. CSV 를 고치면 이 검증도 같이 따라가야 한다 (AGENTS.md).
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            AssertCondition(balance != null, "BalanceData 에셋을 찾지 못했습니다.");
            AssertCondition(balance.Stages.Count > 0, "stages.csv 에서 읽은 단계가 없습니다.");

            var checkCount = 0;
            checkCount += RunJudgementChecks(balance);
            checkCount += RunResetChecks(balance);
            checkCount += RunBootstrapWiringCheck();
            AssertCondition(!EditorUtility.IsDirty(balance), "BalanceData 가 수정됐습니다.");
            checkCount++;

            Debug.Log("[BankruptcyChecks] PASS " + checkCount + " checks.");
        }

        // ---------------------------------------------------------------- 판정 경계

        private static int RunJudgementChecks(BalanceData balance)
        {
            var checkCount = 0;
            var stage1 = balance.GetStage(1);
            AssertCondition(stage1 != null, "stages.csv 에 1단계가 없습니다.");

            var bankruptCount = 0;
            Action onBankrupt = () => bankruptCount++;

            BillManager manager = null;
            GameObject host = null;

            // 정리는 전부 finally 에 둔다. 구독이 남으면 다음 실행에서 파산이 두 번 세어진다.
            try
            {
                GameEvents.OnBankrupt += onBankrupt;

                // 마감 **전날**까지는 파산하지 않는다. 아직 낼 날이 남았다.
                manager = CreateManager(balance, out host);
                AdvanceDays(manager, stage1.DueDays - 1);
                AssertCondition(manager.DaysLeft == 2, "마감 전날인데 DaysLeft 가 2 가 아닙니다: " + manager.DaysLeft);
                manager.EndRun();
                AssertCondition(bankruptCount == 0, "아직 기한이 남았는데 파산했습니다.");
                checkCount++;

                // 마감 당일이 미납으로 끝나도 EndRun 직후에는 파산하지 않는다 (이슈 #211).
                // 정산창을 보여주고 납부 기회를 보장하기 위해 판정은 TryCloseDay 로 이동했다.
                manager.BeginRun();
                AssertCondition(manager.DaysLeft == 1, "마감 당일인데 DaysLeft 가 1 이 아닙니다: " + manager.DaysLeft);
                manager.EndRun();
                AssertCondition(bankruptCount == 0, "마감 당일 EndRun 직후에 이미 파산했습니다: " + bankruptCount);
                checkCount++;

                // 다음 날 진입 시 마감을 확정하면 파산이다 (#211).
                var isBankrupt = manager.TryCloseDay();
                AssertCondition(isBankrupt, "마감 당일 미납인데 TryCloseDay 가 true 를 반환하지 않았습니다.");
                AssertCondition(bankruptCount == 1, "TryCloseDay 호출 시 파산이 1회 발행되지 않았습니다: " + bankruptCount);
                checkCount++;

                TearDown(ref manager, ref host);

                // 마감 전에 내면 파산하지 않는다.
                bankruptCount = 0;
                manager = CreateManager(balance, out host);
                manager.SetEconomyService(new AlwaysPaysEconomyService());
                manager.BeginRun();
                AssertCondition(manager.TryPay(manager.ActiveBill), "마감 전 납부가 실패했습니다.");
                manager.EndRun();
                AssertCondition(bankruptCount == 0, "납부했는데 파산했습니다.");
                AssertCondition(!manager.TryCloseDay(), "납부 완료했는데 TryCloseDay 가 true 를 반환했습니다.");
                checkCount++;

                // 다음 날 새 고지서가 나오고, 그 고지서는 아직 기한이 남아 파산하지 않는다.
                // 여기서 여러 날을 한꺼번에 밀면 **그 새 고지서**가 연체돼 파산한다 — 그건 정상 동작이다.
                manager.BeginRun();
                AssertCondition(manager.ActiveBill != null, "납부 다음 날 고지서가 발행되지 않았습니다.");
                manager.EndRun();
                AssertCondition(bankruptCount == 0, "새로 나온 고지서가 기한 안인데 파산했습니다.");
                AssertCondition(!manager.TryCloseDay(), "기한이 남은 새 고지서인데 TryCloseDay 가 true 를 반환했습니다.");
                checkCount++;

                TearDown(ref manager, ref host);

                // 고지서가 없으면 판정할 것이 없다.
                bankruptCount = 0;
                manager = CreateManager(balance, out host);
                manager.EndRun();
                AssertCondition(bankruptCount == 0, "고지서가 없는데 파산했습니다.");
                AssertCondition(!manager.TryCloseDay(), "고지서가 없는데 TryCloseDay 가 true 를 반환했습니다.");
                checkCount++;
            }
            finally
            {
                GameEvents.OnBankrupt -= onBankrupt;
                TearDown(ref manager, ref host);
            }

            return checkCount;
        }

        // ---------------------------------------------------------------- 회차 초기화

        private static int RunResetChecks(BalanceData balance)
        {
            var checkCount = 0;
            var stage1 = balance.GetStage(1);

            BillManager manager = null;
            GameObject host = null;

            try
            {
                manager = CreateManager(balance, out host);
                manager.SetEconomyService(new AlwaysPaysEconomyService());
                var stageService = new FakeStageService();
                manager.SetStageService(stageService);
                _callSequence = 0;
                var walletPersistence = new FakeWalletPersistence();
                manager.SetWalletPersistence(walletPersistence);
                var upgradePersistence = new FakeUpgradePersistence();
                manager.SetUpgradePersistence(upgradePersistence);

                // 며칠 진행해 날짜·고지서를 쌓은 뒤 파산시킨다.
                AdvanceToDueDay(manager, stage1);
                AssertCondition(manager.CurrentDay > 1, "날짜가 진행되지 않아 초기화를 확인할 수 없습니다.");
                manager.EndRun();
                AssertCondition(manager.TryCloseDay(), "마감일 미납 후 TryCloseDay 가 false 를 반환했습니다.");

                AssertCondition(manager.CurrentDay == 1, "파산 후 날짜가 1 이 아닙니다: " + manager.CurrentDay);
                AssertCondition(manager.ActiveBill == null, "파산 후에도 고지서가 남아 있습니다.");
                AssertCondition(manager.LoanDailyCut == 0f, "파산 후에도 대출 징수가 남아 있습니다.");
                AssertCondition(manager.OfferedPerkIds.Length == 0, "파산 후에도 퍼크 후보가 남아 있습니다.");
                checkCount++;

                // 단계도 1단계로 돌아간다 (IStageService, #150). 주입이 없으면 되돌릴 대상이 없다.
                AssertCondition(stageService.RestoredIndex == 0,
                                "파산이 단계를 1단계로 되돌리지 않았습니다: " + stageService.RestoredIndex);
                checkCount++;

                // 코인·소수 잔여도 0 으로 비운다 (계약 7번 "소수 잔여는 파산 시 버린다", 이슈 #158).
                AssertCondition(walletPersistence.RestoreCallCount == 1,
                                "파산 후 지갑 초기화가 정확히 1회 호출되지 않았습니다: " + walletPersistence.RestoreCallCount);
                AssertCondition(walletPersistence.RestoredBalance == 0L,
                                "파산 후 지갑 잔액이 0 으로 초기화되지 않았습니다: " + walletPersistence.RestoredBalance);
                AssertCondition(walletPersistence.RestoredRemainderText == "0",
                                "파산 후 소수 잔여가 초기화되지 않았습니다: " + walletPersistence.RestoredRemainderText);
                checkCount++;

                // 업그레이드도 0 으로 비운다 (이슈 #250, 4.16). 업그레이드는 회차 층이라 파산에서
                // 사라진다 — null 복원이 UpgradeState 의 레벨 배열을 통째로 지운다.
                AssertCondition(upgradePersistence.RestoreCallCount == 1,
                                "파산 후 업그레이드 초기화가 정확히 1회 호출되지 않았습니다: " + upgradePersistence.RestoreCallCount);
                AssertCondition(upgradePersistence.RestoredLevels == null,
                                "파산이 업그레이드 레벨을 null 로 비우지 않았습니다.");
                checkCount++;

                // **순서**: 업그레이드 초기화가 지갑 초기화보다 먼저다. RestoreWallet 이 발행하는
                // OnBalanceChanged 를 UpgradeShopPanel 이 받아 카드를 다시 그리므로, 뒤집히면
                // 이미 지워진 레벨이 화면에 옛 값으로 남는다.
                AssertCondition(upgradePersistence.RestoreOrder < walletPersistence.RestoreOrder,
                                "업그레이드 초기화가 지갑 초기화보다 늦게 불렸습니다: " +
                                upgradePersistence.RestoreOrder + " vs " + walletPersistence.RestoreOrder);
                checkCount++;

                // 다음 런이 1일차 첫 고지서를 발행한다 (ARCHITECTURE "게임 시작과 파산 재시작에도").
                manager.BeginRun();
                AssertCondition(manager.CurrentDay == 1, "파산 재시작 첫 런이 1일차가 아닙니다: " + manager.CurrentDay);
                AssertCondition(manager.ActiveBill != null, "파산 재시작에 고지서가 발행되지 않았습니다.");
                AssertCondition(manager.ActiveBill.Amount == stage1.BillAmount,
                                "파산 재시작 고지서가 1단계 금액이 아닙니다: " + manager.ActiveBill.Amount);
                AssertCondition(manager.DaysLeft == stage1.DueDays,
                                "파산 재시작 고지서의 기한이 1단계 값과 다릅니다: " + manager.DaysLeft);
                checkCount++;
            }
            finally
            {
                TearDown(ref manager, ref host);
            }

            return checkCount;
        }

        // ---------------------------------------------------------------- 조립

        /// <summary>
        /// 조립 지점이 업그레이드 초기화 통로를 실제로 넣는지 원문으로 본다 (이슈 #250).
        ///
        /// ResetRound 는 _upgradePersistence?. 로 부르므로 주입이 빠지면 **조용히 건너뛴다** —
        /// 파산해도 업그레이드가 남는데 검증은 전부 통과하는 상태가 된다. 위 RunResetChecks 는
        /// 가짜를 직접 넣어 ManagerBootstrap 을 우회하므로 그 구멍을 못 본다.
        /// SavePersistenceChecks 가 WirePersistence 를 원문으로 확인하는 것과 같은 이유다.
        /// </summary>
        private static int RunBootstrapWiringCheck()
        {
            var bootstrap = File.ReadAllText("Assets/Scripts/Runtime/ManagerBootstrap.cs");
            AssertCondition(bootstrap.Contains("SetUpgradePersistence("),
                            "ManagerBootstrap 이 BillManager.SetUpgradePersistence 를 부르지 않습니다. " +
                            "주입이 없으면 파산이 업그레이드를 지우지 않고 조용히 넘어갑니다 (이슈 #250).");
            return 1;
        }

        // ---------------------------------------------------------------- 보조

        /// <summary>마감 당일까지 날짜를 민다 — 이 상태로 EndRun 하면 파산한다. 기한을 코드에 적지 않기 위한 보조다.</summary>
        private static void AdvanceToDueDay(BillManager manager, StageDef stage1)
        {
            AdvanceDays(manager, stage1.DueDays);
        }

        private static void AdvanceDays(BillManager manager, int days)
        {
            for (var i = 0; i < days; i++)
            {
                manager.BeginRun();
            }
        }

        private static BillManager CreateManager(BalanceData balance, out GameObject host)
        {
            host = new GameObject("BankruptcyCheckBills")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            host.SetActive(false);
            var manager = host.AddComponent<BillManager>();
            SetPrivateField(manager, "_balanceData", balance);
            host.SetActive(true);
            return manager;
        }

        private static void SetPrivateField(Component target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(field != null,
                            target.GetType().Name + " 의 " + fieldName + " 필드를 찾지 못했습니다.");
            field.SetValue(target, value);
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
        /// 단계 조회를 대신하는 가짜. 되돌리기 호출을 받았는지만 본다 —
        /// 단계 진행 자체는 StageGoalManagerChecks 의 몫이다.
        /// </summary>
        private class FakeStageService : IStageService
        {
            /// <summary>RestoreStage 가 받은 인덱스. 아직 안 불렸으면 -1 이다.</summary>
            public int RestoredIndex { get; private set; } = -1;

            public int CurrentStageIndex => 0;
            public int CurrentStageNumber => 1;
            public bool IsStageCleared => false;
            public bool IsMaxStage => false;

            public bool AdvanceStage()
            {
                return false;
            }

            public void RestoreStage(int stageIndex)
            {
                RestoredIndex = stageIndex;
            }
        }

        /// <summary>지갑 초기화 호출을 받았는지만 본다. 실제 CoinWallet 계산은 CoinWalletChecks 의 몫이다.</summary>
        private class FakeWalletPersistence : IWalletPersistence
        {
            public int RestoreCallCount { get; private set; }
            public int RestoreOrder { get; private set; }
            public long RestoredBalance { get; private set; } = -1L;
            public string RestoredRemainderText { get; private set; }

            public string CurrentRemainderText => "0";

            public void RestoreWallet(long balance, string remainderText)
            {
                RestoreCallCount++;
                RestoreOrder = ++_callSequence;
                RestoredBalance = balance;
                RestoredRemainderText = remainderText;
            }
        }

        /// <summary>
        /// 업그레이드 초기화 호출을 받았는지와 그 순서만 본다 (이슈 #250). 레벨 배열을 실제로
        /// 지우는 것은 UpgradeState.RestoreLevels 의 몫이고 UpgradeChecks 가 본다.
        /// </summary>
        private class FakeUpgradePersistence : IUpgradePersistence
        {
            public int RestoreCallCount { get; private set; }
            public int RestoreOrder { get; private set; }
            public int[] RestoredLevels { get; private set; } = Array.Empty<int>();

            public int[] CurrentUpgradeLevels => Array.Empty<int>();

            public void RestoreUpgradeLevels(int[] levelsBySortOrder)
            {
                RestoreCallCount++;
                RestoreOrder = ++_callSequence;
                RestoredLevels = levelsBySortOrder;
            }
        }

        /// <summary>납부가 항상 성공하는 가짜. 코인 계산은 CoinWalletChecks 의 몫이라 여기서 격리한다.</summary>
        private class AlwaysPaysEconomyService : IEconomyService
        {
            public long CurrentCoin => 0L;
            public long RunCoin => 0L;
            public IReadOnlyList<CoinDrop> RunCoinBreakdown => Array.Empty<CoinDrop>();
            public void AddCoin(decimal rawAmount) { }
            public void AddLoanPrincipal(long amount) { }
            public bool TrySpendCoin(long amount) { return true; }
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
