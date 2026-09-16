using System;
using System.Reflection;
using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// EconomyManager 의 이벤트 배선과 계수 수집을 검증한다.
    /// 계산식 자체는 CoinWalletChecks 가 본다.
    ///
    /// 한계: Edit Mode 에서는 Unity 가 OnEnable/OnDisable 을 부르지 않는다
    /// ([ExecuteAlways] 를 붙이지 않았다 — 붙이면 에디터에서도 매니저가 돌아 버린다).
    /// 그래서 여기서는 두 메서드를 직접 불러 **구독과 해제가 짝을 이루는지**를 본다.
    /// Unity 가 실제로 그 시점에 불러 주는지는 Play Mode 확인이 필요하나,
    /// 현재 테스트 asmdef 가 런타임 코드(Assembly-CSharp)를 참조하지 못해 미검증으로 남는다.
    /// </summary>
    public static class EconomyManagerChecks
    {
        public static void RunBatch()
        {
            var checkCount = 0;
            var balanceData = ScriptableObject.CreateInstance<BalanceData>();
            balanceData.Fever.CoinMultiplier = 3f;
            balanceData.Economy.CoinBonusMultiplier = 1f;

            var earned = 0L;
            Action<long> onEarned = value => earned += value;
            EconomyManager manager = null;
            GameObject host = null;

            // 정리는 전부 finally 에 둔다. 중간에 검증이 실패해도 정적 이벤트에 구독이 남으면
            // 다음 실행에서 매니저가 둘이 되어 같은 파괴에 코인이 두 배로 들어간다.
            try
            {
                GameEvents.OnCoinEarned += onEarned;
                manager = CreateManager(balanceData, out host);

                // 파괴 이벤트 한 번에 정확히 한 번 지급한다.
                GameEvents.PublishTargetBroken(CreateBreak(10m));
                AssertCondition(earned == 10L, "파괴 1회 지급액이 다릅니다: " + earned);
                AssertCondition(manager.CurrentCoin == 10L, "잔액이 다릅니다.");
                AssertCondition(manager.RunCoin == 10L, "런 순수입이 다릅니다.");
                checkCount++;

                // 해제하면 더 이상 지급되지 않는다.
                InvokeLifecycle(manager, "OnDisable");
                GameEvents.PublishTargetBroken(CreateBreak(10m));
                AssertCondition(earned == 10L, "해제 후에도 지급됐습니다. 구독 해제가 빠졌습니다.");
                checkCount++;

                // 다시 구독해도 하나뿐이다. 두 번 들어오면 코인이 두 배가 된다.
                InvokeLifecycle(manager, "OnEnable");
                GameEvents.PublishTargetBroken(CreateBreak(10m));
                AssertCondition(earned == 20L, "재구독 후 지급이 중복되거나 빠졌습니다: " + earned);
                checkCount++;

                // 피버 중에는 CSV 의 배율이 걸리고, 끝나면 풀린다.
                GameEvents.PublishFeverStart();
                GameEvents.PublishTargetBroken(CreateBreak(10m));
                AssertCondition(earned == 50L, "피버 배율이 적용되지 않았습니다: " + earned);
                GameEvents.PublishFeverEnd();
                GameEvents.PublishTargetBroken(CreateBreak(10m));
                AssertCondition(earned == 60L, "피버 종료 후에도 배율이 남았습니다: " + earned);
                checkCount++;

                TearDown(ref manager, ref host);

                // 대출 징수는 BillService 에서 받아 적용한다. 없으면 0 이다.
                earned = 0L;
                manager = CreateManager(balanceData, out host);
                var billService = new FakeBillService
                {
                    LoanDailyCut = 0.1f
                };
                manager.SetBillService(billService);
                GameEvents.PublishTargetBroken(CreateBreak(10m));
                AssertCondition(earned == 9L, "대출 징수가 적용되지 않았습니다: " + earned);
                checkCount++;

                // 대출 원금은 잔액만 늘리고 런 순수입과 OnCoinEarned 에 섞이지 않는다.
                var beforeRunCoin = manager.RunCoin;
                var beforeEarned = earned;
                manager.AddLoanPrincipal(100L);
                AssertCondition(manager.CurrentCoin == 109L, "대출 원금이 잔액에 반영되지 않았습니다.");
                AssertCondition(manager.RunCoin == beforeRunCoin, "대출 원금이 런 순수입에 섞였습니다.");
                AssertCondition(earned == beforeEarned, "대출 원금이 OnCoinEarned 로 발행됐습니다.");
                checkCount++;

                // 런을 시작하면 순수입만 0 이 되고 잔액은 남는다.
                manager.BeginRun();
                AssertCondition(manager.RunCoin == 0L, "런 시작 후 순수입이 0 이 아닙니다.");
                AssertCondition(manager.CurrentCoin == 109L, "런 시작이 잔액을 건드렸습니다.");
                checkCount++;

                // 저장 왕복. 복원 후 잔여가 이어진다.
                manager.RestoreWallet(50L, "0.5");
                AssertCondition(manager.CurrentCoin == 50L, "복원된 잔액이 다릅니다.");
                AssertCondition(manager.CurrentRemainderText == "0.5", "복원된 잔여가 다릅니다.");
                checkCount++;

                Debug.Log("[EconomyManagerChecks] PASS " + checkCount + " checks.");
            }
            finally
            {
                GameEvents.OnCoinEarned -= onEarned;
                TearDown(ref manager, ref host);
                UnityEngine.Object.DestroyImmediate(balanceData);
            }
        }

        /// <summary>구독을 먼저 풀고 오브젝트를 지운다. 순서를 바꾸면 해제 대상이 이미 파괴돼 있다.</summary>
        private static void TearDown(ref EconomyManager manager, ref GameObject host)
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
        private static EconomyManager CreateManager(BalanceData balanceData, out GameObject host)
        {
            host = new GameObject("EconomyManagerCheck")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            host.SetActive(false);
            var manager = host.AddComponent<EconomyManager>();
            var field = typeof(EconomyManager).GetField("_balanceData",
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(field != null, "_balanceData 필드를 찾지 못했습니다. 이름이 바뀌었습니까?");
            field.SetValue(manager, balanceData);
            host.SetActive(true);
            InvokeLifecycle(manager, "OnEnable");
            return manager;
        }

        /// <summary>Edit Mode 에서는 Unity 가 부르지 않으므로 직접 부른다. 위 클래스 주석의 한계 참고.</summary>
        private static void InvokeLifecycle(EconomyManager manager, string methodName)
        {
            var method = typeof(EconomyManager).GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(method != null, methodName + " 을 찾지 못했습니다. 이름이 바뀌었습니까?");
            method.Invoke(manager, null);
        }

        private static BreakInfo CreateBreak(decimal rawCoin)
        {
            return new BreakInfo("normal", rawCoin, 0f, Vector3.zero);
        }

        /// <summary>대출 징수율만 돌려주는 가짜 구현. 나머지는 이 검증에서 쓰지 않는다.</summary>
        private class FakeBillService : IBillService
        {
            public int CurrentDay => 1;
            public int DaysLeft => 1;
            public float LoanDailyCut { get; set; }
            public bool TryPay(Bill bill) => false;
            public bool TryTakeLoan(long amount) => false;
            public bool TryRepayLoan() => false;
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
