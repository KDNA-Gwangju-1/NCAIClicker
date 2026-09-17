using System;
using System.Reflection;
using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// StageGoalManager 의 이벤트 배선과 판정 로직을 검증한다.
    ///
    /// 한계: Edit Mode 에서는 Unity 가 OnEnable/OnDisable 을 부르지 않는다.
    /// 그래서 여기서는 두 메서드를 직접 불러 구독과 해제가 짝을 이루는지를 본다.
    /// Unity 가 실제로 그 시점에 불러 주는지는 Play Mode 확인이 필요하나,
    /// 현재 테스트 asmdef 가 런타임 코드(Assembly-CSharp)를 참조하지 못해 미검증으로 남는다.
    /// </summary>
    public static class StageGoalManagerChecks
    {
        public static void RunBatch()
        {
            var checkCount = 0;
            var balanceData = ScriptableObject.CreateInstance<BalanceData>();
            balanceData.Stages.Add(new StageDef { Stage = 1, GoalCoin = 100L });

            var reachedCount = 0;
            var lastReachedStage = 0;
            Action<int> onReached = stageNumber =>
            {
                reachedCount++;
                lastReachedStage = stageNumber;
            };

            StageGoalManager manager = null;
            GameObject host = null;

            // 정리는 전부 finally 에 둔다. 구독이 남으면 다음 실행에서 판정이 중복된다.
            try
            {
                GameEvents.OnStageGoalReached += onReached;
                manager = CreateManager(balanceData, out host);
                manager.BeginRun();

                // 목표 미달이면 판정하지 않는다.
                GameEvents.PublishRunCoinChanged(50L);
                AssertCondition(!manager.IsGoalReached, "미달인데 달성으로 판정했습니다.");
                AssertCondition(reachedCount == 0, "미달인데 이벤트가 발행됐습니다.");
                checkCount++;

                // 목표에 도달하면 정확히 한 번 발행한다.
                GameEvents.PublishRunCoinChanged(100L);
                AssertCondition(manager.IsGoalReached, "도달했는데 달성으로 판정하지 않았습니다.");
                AssertCondition(reachedCount == 1, "달성 이벤트 발행 횟수가 다릅니다: " + reachedCount);
                AssertCondition(lastReachedStage == 1, "발행된 단계 번호가 다릅니다: " + lastReachedStage);
                checkCount++;

                // 같은 런에서 다시 넘어도 중복 발행하지 않는다.
                GameEvents.PublishRunCoinChanged(150L);
                AssertCondition(reachedCount == 1, "같은 런에서 중복 발행됐습니다: " + reachedCount);
                checkCount++;

                // 다음 런을 시작하면 플래그가 되돌아가고 다시 판정한다.
                manager.BeginRun();
                AssertCondition(!manager.IsGoalReached, "런 시작 후에도 달성 상태가 남아 있습니다.");
                GameEvents.PublishRunCoinChanged(100L);
                AssertCondition(reachedCount == 2, "다음 런에서 재판정되지 않았습니다: " + reachedCount);
                checkCount++;

                // 설정 범위를 넘는 단계는 조회하지 않고 조용히 무시한다.
                manager.BeginRun();
                SetStageIndex(manager, 5);
                GameEvents.PublishRunCoinChanged(1000L);
                AssertCondition(!manager.IsGoalReached, "범위를 넘는 단계인데 달성으로 판정했습니다.");
                AssertCondition(reachedCount == 2, "범위를 넘는 단계인데 이벤트가 발행됐습니다.");
                checkCount++;
                SetStageIndex(manager, 0);

                // 해제하면 더 이상 판정하지 않는다.
                InvokeLifecycle(manager, "OnDisable");
                manager.BeginRun();
                GameEvents.PublishRunCoinChanged(100L);
                AssertCondition(reachedCount == 2, "해제 후에도 판정됐습니다. 구독 해제가 빠졌습니다.");
                checkCount++;

                Debug.Log("[StageGoalManagerChecks] PASS " + checkCount + " checks.");
            }
            finally
            {
                GameEvents.OnStageGoalReached -= onReached;
                TearDown(ref manager, ref host);
                UnityEngine.Object.DestroyImmediate(balanceData);
            }
        }

        /// <summary>구독을 먼저 풀고 오브젝트를 지운다. 순서를 바꾸면 해제 대상이 이미 파괴돼 있다.</summary>
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

        /// <summary>
        /// 비활성 상태로 만들어 컴포넌트를 붙이고 BalanceData 를 넣은 뒤 켠다.
        /// 씬을 더럽히지 않도록 HideAndDontSave 로 둔다.
        /// </summary>
        private static StageGoalManager CreateManager(BalanceData balanceData, out GameObject host)
        {
            host = new GameObject("StageGoalManagerCheck")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            host.SetActive(false);
            var manager = host.AddComponent<StageGoalManager>();
            var field = typeof(StageGoalManager).GetField("_balanceData",
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(field != null, "_balanceData 필드를 찾지 못했습니다. 이름이 바뀌었습니까?");
            field.SetValue(manager, balanceData);
            host.SetActive(true);
            InvokeLifecycle(manager, "OnEnable");
            return manager;
        }

        private static void SetStageIndex(StageGoalManager manager, int stageIndex)
        {
            var field = typeof(StageGoalManager).GetField("_stageIndex",
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(field != null, "_stageIndex 필드를 찾지 못했습니다. 이름이 바뀌었습니까?");
            field.SetValue(manager, stageIndex);
        }

        /// <summary>Edit Mode 에서는 Unity 가 부르지 않으므로 직접 부른다. 위 클래스 주석의 한계 참고.</summary>
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
