using System;
using System.Reflection;
using NCAIClicker.Core;
using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// StageGoalManager 의 이벤트 배선, 목표 판정, 단계 진행, 소비처 연동을 검증한다.
    /// 완료 기준의 정본은 GitHub 이슈 #26 및 #150 (3.7).
    /// </summary>
    public static class StageGoalManagerChecks
    {
        public static void RunBatch()
        {
            var checkCount = 0;
            checkCount += RunGoalChecks();
            checkCount += RunProgressionChecks();
            checkCount += RunConsumerWiringChecks();
            Debug.Log("[StageGoalManagerChecks] PASS " + checkCount + " checks.");
        }

        /// <summary>
        /// 단계 클리어 판정 — 기준은 고지서 납부다 (GDD 5절). 예전에는 런 순수입이
        /// goal_coin 에 도달했는지를 봤으나, 그 열은 bill_amount 와 늘 같은 값이었다.
        /// </summary>
        private static int RunGoalChecks()
        {
            var checkCount = 0;
            var balanceData = ScriptableObject.CreateInstance<BalanceData>();
            balanceData.Stages.Add(new StageDef { Stage = 1, BillAmount = 100L });

            var reachedCount = 0;
            var lastReachedStage = 0;
            Action<int> onReached = stageNumber =>
            {
                reachedCount++;
                lastReachedStage = stageNumber;
            };

            StageGoalManager manager = null;
            GameObject host = null;

            try
            {
                GameEvents.OnStageGoalReached += onReached;
                manager = CreateManager(balanceData, out host);
                manager.BeginRun();

                // 납부 전에는 클리어가 아니다. 코인을 아무리 벌어도 마찬가지다.
                AssertCondition(!manager.IsStageCleared, "납부 전인데 클리어로 판정했습니다.");
                AssertCondition(reachedCount == 0, "납부 전인데 이벤트가 발행됐습니다.");
                checkCount++;

                // 납부하면 정확히 한 번 발행한다.
                GameEvents.PublishBillPaid(new Bill { Amount = 100L, IsPaid = true });
                AssertCondition(manager.IsStageCleared, "납부했는데 클리어로 판정하지 않았습니다.");
                AssertCondition(reachedCount == 1, "달성 이벤트 발행 횟수가 다릅니다: " + reachedCount);
                AssertCondition(lastReachedStage == 1, "발행된 단계 번호가 다릅니다: " + lastReachedStage);
                checkCount++;

                // 같은 런에서 다시 납부 이벤트가 와도 중복 발행하지 않는다.
                GameEvents.PublishBillPaid(new Bill { Amount = 100L, IsPaid = true });
                AssertCondition(reachedCount == 1, "같은 런에서 중복 발행됐습니다: " + reachedCount);
                checkCount++;

                // 다음 런을 시작하면 플래그가 되돌아가고 다시 판정한다.
                manager.BeginRun();
                AssertCondition(!manager.IsStageCleared, "런 시작 후에도 클리어 상태가 남아 있습니다.");
                GameEvents.PublishBillPaid(new Bill { Amount = 100L, IsPaid = true });
                AssertCondition(reachedCount == 2, "다음 런에서 재판정되지 않았습니다: " + reachedCount);
                checkCount++;

                // 설정 범위를 넘는 단계는 조회하지 않고 조용히 무시한다.
                manager.BeginRun();
                SetStageIndex(manager, 5);
                GameEvents.PublishBillPaid(new Bill { Amount = 100L, IsPaid = true });
                AssertCondition(!manager.IsStageCleared, "범위를 넘는 단계인데 클리어로 판정했습니다.");
                AssertCondition(reachedCount == 2, "범위를 넘는 단계인데 이벤트가 발행됐습니다.");
                checkCount++;
                SetStageIndex(manager, 0);

                // 해제하면 더 이상 판정하지 않는다.
                InvokeLifecycle(manager, "OnDisable");
                manager.BeginRun();
                GameEvents.PublishBillPaid(new Bill { Amount = 100L, IsPaid = true });
                AssertCondition(reachedCount == 2, "해제 후에도 판정됐습니다. 구독 해제가 빠졌습니다.");
                checkCount++;

                return checkCount;
            }
            finally
            {
                GameEvents.OnStageGoalReached -= onReached;
                TearDown(ref manager, ref host);
                UnityEngine.Object.DestroyImmediate(balanceData);
            }
        }

        /// <summary>
        /// 이슈 #150: 목표 달성에 따른 EndRun 단계 진행 및 최대 단계 가드 검증.
        /// </summary>
        private static int RunProgressionChecks()
        {
            var checkCount = 0;
            var balanceData = ScriptableObject.CreateInstance<BalanceData>();
            balanceData.Stages.Add(new StageDef { Stage = 1, BillAmount = 100L });
            balanceData.Stages.Add(new StageDef { Stage = 2, BillAmount = 200L });
            balanceData.Stages.Add(new StageDef { Stage = 3, BillAmount = 300L });

            StageGoalManager manager = null;
            GameObject host = null;

            try
            {
                manager = CreateManager(balanceData, out host);
                manager.BeginRun();

                // 1단계 시작 상태 확인
                AssertCondition(manager.CurrentStageIndex == 0, "초기 단계 인덱스가 0이 아닙니다.");
                AssertCondition(manager.CurrentStageNumber == 1, "초기 단계 번호가 1이 아닙니다.");
                AssertCondition(!manager.IsMaxStage, "초기 상태인데 최고 단계로 판정되었습니다.");
                checkCount++;

                // 납부 전에는 런이 끝나도 단계가 그대로다.
                manager.EndRun();
                AssertCondition(manager.CurrentStageIndex == 0, "납부 전인데 다음 단계로 진행했습니다.");
                checkCount++;

                // 납부하면 그 자리에서 2단계로 오른다.
                GameEvents.PublishBillPaid(new Bill { Amount = 100L, IsPaid = true });
                AssertCondition(manager.IsStageCleared, "납부했으나 클리어로 판정되지 않았습니다.");
                AssertCondition(manager.CurrentStageIndex == 1, "납부 후 2단계(인덱스 1)로 진행하지 않았습니다.");
                AssertCondition(manager.CurrentStageNumber == 2, "단계 번호가 2가 아닙니다.");
                AssertCondition(!manager.IsMaxStage, "2단계인데 최고 단계로 판정되었습니다.");
                checkCount++;

                // 2단계 시작 -> 판정 플래그 리셋 확인
                manager.BeginRun();
                AssertCondition(!manager.IsStageCleared, "새 런 시작 후 목표 달성 플래그가 리셋되지 않았습니다.");
                checkCount++;

                // 2단계 목표 달성 후 3단계 진행
                GameEvents.PublishBillPaid(new Bill { Amount = 100L, IsPaid = true });
                AssertCondition(manager.CurrentStageIndex == 2, "3단계(인덱스 2)로 진행하지 않았습니다.");
                AssertCondition(manager.CurrentStageNumber == 3, "단계 번호가 3이 아닙니다.");
                AssertCondition(manager.IsMaxStage, "3단계(마지막 단계)인데 IsMaxStage 가 false 입니다.");
                checkCount++;

                // 3단계(최고 단계) 목표 달성 후에도 3단계 초과 없이 유지
                manager.BeginRun();
                GameEvents.PublishBillPaid(new Bill { Amount = 100L, IsPaid = true });
                AssertCondition(manager.CurrentStageIndex == 2, "최고 단계를 초과하여 진행되었습니다.");
                AssertCondition(manager.CurrentStageNumber == 3, "최고 단계 초과 번호가 되었습니다.");
                checkCount++;

                // RestoreStage 로 1단계(인덱스 0) 복원
                manager.RestoreStage(0);
                AssertCondition(manager.CurrentStageIndex == 0, "RestoreStage 로 1단계 복원이 실패했습니다.");
                AssertCondition(!manager.IsStageCleared, "RestoreStage 후 IsStageCleared 가 false 가 아닙니다.");
                checkCount++;

                return checkCount;
            }
            finally
            {
                TearDown(ref manager, ref host);
                UnityEngine.Object.DestroyImmediate(balanceData);
            }
        }

        /// <summary>
        /// 이슈 #150: CreatureManager 및 BillManager 에 IStageService 가 올바르게 연동되는지 검증.
        /// </summary>
        private static int RunConsumerWiringChecks()
        {
            var checkCount = 0;
            var balanceData = ScriptableObject.CreateInstance<BalanceData>();
            balanceData.Stages.Add(new StageDef
            {
                Stage = 1,
                BillAmount = 450L,
                DueDays = 5,
                SpawnCount = 6,
                NormalRatio = 0.6f,
                AnchorRatio = 0.15f,
                RunnerRatio = 0.1f,
                TouristRatio = 0.15f
            });
            balanceData.Stages.Add(new StageDef
            {
                Stage = 2,
                BillAmount = 1125L,
                DueDays = 4,
                SpawnCount = 7,
                NormalRatio = 0.45f,
                AnchorRatio = 0.2f,
                RunnerRatio = 0.2f,
                TouristRatio = 0.15f
            });

            StageGoalManager stageManager = null;
            GameObject stageHost = null;
            CreatureManager creatureManager = null;
            GameObject creatureHost = null;
            BillManager billManager = null;
            GameObject billHost = null;

            try
            {
                stageManager = CreateManager(balanceData, out stageHost);

                // CreatureManager 조립 및 1단계 스폰 검증
                creatureHost = new GameObject("CheckCreature") { hideFlags = HideFlags.HideAndDontSave };
                creatureManager = creatureHost.AddComponent<CreatureManager>();
                SetField(creatureManager, "_balanceData", balanceData);
                creatureManager.SetStageService(stageManager);

                creatureManager.BeginRun();
                AssertCondition(creatureManager.CurrentStageNumber == 1, "CreatureManager 1단계 반영 실패");
                AssertCondition(creatureManager.GetRequiredSpawnCount() == 6, "CreatureManager 1단계 스폰 수 6 불일치: " + creatureManager.GetRequiredSpawnCount());
                creatureManager.EndRun();
                checkCount++;

                // BillManager 조립 및 1단계 고지서 검증
                billHost = new GameObject("CheckBill") { hideFlags = HideFlags.HideAndDontSave };
                billManager = billHost.AddComponent<BillManager>();
                SetField(billManager, "_balanceData", balanceData);
                billManager.SetStageService(stageManager);

                billManager.BeginRun();
                AssertCondition(billManager.ActiveBill != null, "1단계 고지서 발행 실패");
                AssertCondition(billManager.ActiveBill.Amount == 450L, "1단계 고지서 금액 450 불일치: " + billManager.ActiveBill.Amount);
                AssertCondition(billManager.ActiveBill.DueDay == billManager.CurrentDay + 5 - 1, "1단계 고지서 기한 5일 불일치");
                checkCount++;

                // 단계 진행: 1단계 고지서 납부 -> 2단계 진행
                stageManager.BeginRun();
                GameEvents.PublishBillPaid(new Bill { Amount = 450L, IsPaid = true });
                AssertCondition(stageManager.CurrentStageNumber == 2, "납부 후 2단계 진행 실패");
                checkCount++;

                // 2단계에서 CreatureManager 스폰 수 7 반영 확인
                creatureManager.BeginRun();
                AssertCondition(creatureManager.CurrentStageNumber == 2, "CreatureManager 2단계 반영 실패");
                AssertCondition(creatureManager.GetRequiredSpawnCount() == 7, "CreatureManager 2단계 스폰 수 7 불일치: " + creatureManager.GetRequiredSpawnCount());
                creatureManager.EndRun();
                checkCount++;

                return checkCount;
            }
            finally
            {
                TearDown(ref stageManager, ref stageHost);
                if (creatureHost != null) UnityEngine.Object.DestroyImmediate(creatureHost);
                if (billHost != null) UnityEngine.Object.DestroyImmediate(billHost);
                UnityEngine.Object.DestroyImmediate(balanceData);
            }
        }

        private static void TearDown(ref StageGoalManager manager, ref GameObject host)
        {
            if (manager != null)
            {
                InvokeLifecycle(manager, "OnDisable");
                manager = null;
            }
            if (host != null)
            {
                UnityEngine.Object.DestroyImmediate(host);
                host = null;
            }
        }

        private static StageGoalManager CreateManager(BalanceData balanceData, out GameObject host)
        {
            host = new GameObject("StageGoalManagerCheck")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            host.SetActive(false);
            var manager = host.AddComponent<StageGoalManager>();
            SetField(manager, "_balanceData", balanceData);
            host.SetActive(true);
            InvokeLifecycle(manager, "OnEnable");
            return manager;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(field != null, fieldName + " 필드를 찾지 못했습니다. 이름이 바뀌었습니까?");
            field.SetValue(target, value);
        }

        private static void SetStageIndex(StageGoalManager manager, int stageIndex)
        {
            SetField(manager, "_stageIndex", stageIndex);
        }

        private static void InvokeLifecycle(StageGoalManager manager, string methodName)
        {
            var method = typeof(StageGoalManager).GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(method != null, methodName + " 을 찾지 못했습니다. 이름이 바뀌었습니까?");
            method.Invoke(manager, null);
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
