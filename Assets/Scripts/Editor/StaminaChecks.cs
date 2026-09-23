using System;
using System.Reflection;
using NCAIClicker.Core;
using NCAIClicker.Data;
using NCAIClicker.Events;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 스태미나의 계산(StaminaPool)과 이벤트 배선(StaminaManager)을 검증한다.
    ///
    /// 한계: Edit Mode 에서는 Unity 가 OnEnable/OnDisable/Update 를 부르지 않는다
    /// ([ExecuteAlways] 를 붙이지 않았다 — 붙이면 에디터에서도 스태미나가 줄어든다).
    /// 그래서 생명주기와 Tick 을 직접 불러 **구독과 해제가 짝을 이루는지**와
    /// **경과 시간에 대한 반응**을 본다. Unity 가 실제로 그 시점에 불러 주는지는
    /// Play Mode 확인이 필요하나, 테스트 asmdef 가 런타임 코드(Assembly-CSharp)를
    /// 참조하지 못해 미검증으로 남는다 (EconomyManagerChecks 와 같은 제약).
    /// </summary>
    public static class StaminaChecks
    {
        /// <summary>회복형 대상의 id. targets.csv 의 값을 여기서 다시 적지 않기 위한 조회 키다.</summary>
        private const string TouristId = "tourist";

        public static void RunBatch()
        {
            // 기대값을 코드에 적지 않는다. CSV 를 고치면 이 검증도 같이 따라가야 한다 (AGENTS.md).
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            AssertCondition(balance != null, "BalanceData 에셋을 찾지 못했습니다.");

            var checkCount = 0;
            checkCount += RunPoolChecks(balance);
            checkCount += RunManagerChecks(balance);
            Debug.Log("[StaminaChecks] PASS " + checkCount + " checks.");
        }

        // ---------------------------------------------------------------- 계산부

        private static int RunPoolChecks(BalanceData balance)
        {
            var checkCount = 0;
            var maxStamina = balance.Stamina.Max;
            var drainPerSec = balance.Stamina.IdleDrainPerSec;
            var touristRestore = balance.GetTarget(TouristId).StaminaRestore;
            var pool = new StaminaPool();

            // 가득 채우면 최대치와 같다.
            pool.Fill(maxStamina);
            AssertNear(pool.Current, maxStamina, "채운 뒤 현재값이 최대치와 다릅니다.");
            AssertCondition(!pool.IsDepleted, "가득 찬 상태가 소진으로 판정됐습니다.");
            checkCount++;

            // 기본 런 길이는 max / drain 이다 (docs/BALANCE.md 2절).
            var expectedSeconds = maxStamina / drainPerSec;
            var elapsed = 0f;
            while (!pool.IsDepleted && elapsed < expectedSeconds * 3f)
            {
                pool.Drain(drainPerSec, 0.02f);
                elapsed += 0.02f;
            }
            AssertNear(elapsed, expectedSeconds,
                       "기본 런 길이가 " + expectedSeconds + "초가 아닙니다: " + elapsed, 0.1f);
            AssertCondition(pool.IsDepleted, "기본 런 길이가 지나도 소진되지 않았습니다.");
            checkCount++;

            // 0 아래로 내려가지 않는다. 음수가 되면 HUD 막대가 뒤집힌다.
            pool.Drain(drainPerSec, 10f);
            AssertCondition(pool.Current == 0f, "0 아래로 내려갔습니다: " + pool.Current);
            checkCount++;

            // 실제로 줄어든 양만 돌려준다. 이미 0이면 0이다.
            pool.Fill(maxStamina);
            var drained = pool.Drain(drainPerSec, 1f);
            AssertNear(drained, drainPerSec, "1초 감소량이 다릅니다: " + drained);
            checkCount++;

            // 빈 자리가 넉넉하면 요청한 만큼 그대로 회복한다. 회복량이 커도(#247) 넘치지 않게 비운 뒤 채운다.
            pool.Drain(drainPerSec, maxStamina / drainPerSec + 1f);
            var before = pool.Current;
            var restored = pool.Restore(touristRestore);
            AssertNear(restored, touristRestore, "회복량이 다릅니다: " + restored);
            AssertNear(pool.Current, before + touristRestore, "회복 후 현재값이 다릅니다: " + pool.Current);
            checkCount++;

            // 빈 자리보다 많이 회복하려 하면 빈 자리만큼만 돌려준다.
            // 여기가 OnStaminaRestored 의 인자가 되므로 요청량을 그대로 돌려주면 UI 가 거짓말을 한다.
            var room = maxStamina - pool.Current;
            var clamped = pool.Restore(room + 50f);
            AssertNear(clamped, room, "넘치는 회복량이 빈 자리로 잘리지 않았습니다: " + clamped);
            AssertNear(pool.Current, maxStamina, "최대치를 넘겼습니다: " + pool.Current);
            checkCount++;

            // 만충에서는 회복할 자리가 없다.
            var overflow = pool.Restore(50f);
            AssertCondition(overflow == 0f, "만충 상태에서 회복량이 0 이 아닙니다: " + overflow);
            AssertNear(pool.Current, maxStamina, "만충 상태에서 값이 변했습니다: " + pool.Current);
            checkCount++;

            // 회복량 0(일반·거치·고속형)은 아무 일도 하지 않는다.
            AssertCondition(pool.Restore(0f) == 0f, "회복량 0 이 0 을 돌려주지 않았습니다.");
            checkCount++;

            // 잘못된 인자는 조용히 넘기지 않는다.
            AssertThrows(() => pool.Fill(0f), "최대치 0 이 거부되지 않았습니다.");
            AssertThrows(() => pool.Drain(-1f, 1f), "음수 감소량이 거부되지 않았습니다.");
            AssertThrows(() => pool.Drain(drainPerSec, -1f), "음수 경과 시간이 거부되지 않았습니다.");
            checkCount++;

            return checkCount;
        }

        // ---------------------------------------------------------------- 매니저

        private static int RunManagerChecks(BalanceData balance)
        {
            var checkCount = 0;
            var maxStamina = balance.Stamina.Max;
            var drainPerSec = balance.Stamina.IdleDrainPerSec;
            var touristRestore = balance.GetTarget(TouristId).StaminaRestore;

            var changedCount = 0;
            var lastCurrent = 0f;
            var lastMax = 0f;
            var restoredTotal = 0f;
            var restoredCount = 0;
            var depletedCount = 0;

            Action<float, float> onChanged = (current, max) =>
            {
                changedCount++;
                lastCurrent = current;
                lastMax = max;
            };
            Action<float> onRestored = amount =>
            {
                restoredCount++;
                restoredTotal += amount;
            };
            Action onDepleted = () => depletedCount++;

            StaminaManager manager = null;
            GameObject host = null;

            // 정리는 전부 finally 에 둔다. 중간에 검증이 실패해도 정적 이벤트에 구독이 남으면
            // 다음 실행에서 매니저가 둘이 되어 같은 파괴에 회복이 두 배로 들어간다.
            try
            {
                GameEvents.OnStaminaChanged += onChanged;
                GameEvents.OnStaminaRestored += onRestored;
                GameEvents.OnStaminaDepleted += onDepleted;

                manager = CreateManager(balance, out host);

                // BeginRun 전에는 시간이 흘러도 줄지 않는다. MainMenu 에서 새는 것을 막는다.
                Tick(manager, 5f);
                AssertCondition(changedCount == 0, "런 시작 전에 이벤트가 발행됐습니다.");
                AssertCondition(manager.CurrentStamina == 0f, "런 시작 전에 값이 채워졌습니다.");
                checkCount++;

                // 런을 시작하면 가득 찬 상태를 즉시 알린다. UI 가 초기값을 받을 유일한 통로다.
                manager.BeginRun();
                AssertCondition(changedCount == 1, "런 시작 시 OnStaminaChanged 가 한 번 발행되지 않았습니다.");
                AssertNear(lastCurrent, maxStamina, "런 시작 값이 최대치가 아닙니다.");
                AssertNear(lastMax, maxStamina, "최대치 인자가 다릅니다.");
                checkCount++;

                // 시간이 흐르면 CSV 의 초당 감소량만큼 줄어든다.
                Tick(manager, 1f);
                AssertNear(manager.CurrentStamina, maxStamina - drainPerSec,
                          "1초 후 잔량이 다릅니다: " + manager.CurrentStamina);
                checkCount++;

                // 발행을 묶는다. 0.1초 간격이므로 1초를 100프레임으로 쪼개도 10회쯤이다.
                changedCount = 0;
                for (var i = 0; i < 100; i++)
                {
                    Tick(manager, 0.01f);
                }
                AssertCondition(changedCount < 100, "지속 감소가 프레임마다 발행됐습니다: " + changedCount);
                AssertCondition(changedCount > 0, "지속 감소가 전혀 발행되지 않았습니다.");
                checkCount++;

                // 회복형 파괴는 실제 회복량을 알린다. 회복량이 커도(#247) 최대치에 잘리지 않게 먼저 줄인다.
                Tick(manager, touristRestore / drainPerSec + 1f);
                restoredCount = 0;
                restoredTotal = 0f;
                GameEvents.PublishTargetBroken(CreateBreak(touristRestore));
                AssertCondition(restoredCount == 1, "회복 이벤트가 한 번 발행되지 않았습니다: " + restoredCount);
                AssertNear(restoredTotal, touristRestore, "회복량이 다릅니다: " + restoredTotal);
                checkCount++;

                // 회복량 0 인 대상(일반·거치·고속형)은 이벤트를 만들지 않는다.
                restoredCount = 0;
                GameEvents.PublishTargetBroken(CreateBreak(0f));
                AssertCondition(restoredCount == 0, "회복량 0 인 대상이 이벤트를 발행했습니다.");
                checkCount++;

                // 만충을 넘기지 않으며, 넘칠 때는 알리지도 않는다.
                manager.BeginRun();
                restoredCount = 0;
                GameEvents.PublishTargetBroken(CreateBreak(touristRestore));
                AssertNear(manager.CurrentStamina, maxStamina,
                          "만충에서 최대치를 넘겼습니다: " + manager.CurrentStamina);
                AssertCondition(restoredCount == 0, "만충인데 회복 이벤트가 발행됐습니다.");
                checkCount++;

                // 구독을 해제하면 반응하지 않는다.
                InvokeLifecycle(manager, "OnDisable");
                var beforeStamina = manager.CurrentStamina;
                GameEvents.PublishTargetBroken(CreateBreak(touristRestore));
                AssertNear(manager.CurrentStamina, beforeStamina,
                          "해제 후에도 회복됐습니다. 구독 해제가 빠졌습니다.");
                checkCount++;

                // 다시 구독해도 하나뿐이다. 두 번 들어오면 회복이 두 배가 된다.
                InvokeLifecycle(manager, "OnEnable");
                Tick(manager, 10f);
                beforeStamina = manager.CurrentStamina;
                GameEvents.PublishTargetBroken(CreateBreak(touristRestore));
                AssertNear(manager.CurrentStamina, beforeStamina + touristRestore,
                          "재구독 후 회복이 중복되거나 빠졌습니다: " + manager.CurrentStamina);
                checkCount++;

                // 소진되면 0 을 먼저 알리고 종료를 요청한다. 순서가 뒤집히면 HUD 에 0 이 찍히지 않는다.
                manager.BeginRun();
                depletedCount = 0;
                Tick(manager, 100f);
                AssertCondition(manager.CurrentStamina == 0f, "소진 후 잔량이 0 이 아닙니다.");
                AssertCondition(lastCurrent == 0f, "소진 시 0 이 발행되지 않았습니다: " + lastCurrent);
                AssertCondition(depletedCount == 1, "종료 요청이 한 번 나가지 않았습니다: " + depletedCount);
                checkCount++;

                // 소진 뒤에도 계속 Tick 되지만 종료 요청은 다시 나가지 않는다.
                Tick(manager, 100f);
                AssertCondition(depletedCount == 1, "종료 요청이 반복 발행됐습니다: " + depletedCount);
                AssertCondition(!manager.IsRunning, "소진 후에도 런이 진행 중입니다.");
                checkCount++;

                // 소진 뒤 들어온 파괴는 스태미나를 되살리지 않는다. 런은 이미 끝났다.
                restoredCount = 0;
                GameEvents.PublishTargetBroken(CreateBreak(touristRestore));
                AssertCondition(manager.CurrentStamina == 0f, "소진 후에 회복됐습니다: " + manager.CurrentStamina);
                AssertCondition(restoredCount == 0, "소진 후에 회복 이벤트가 발행됐습니다.");
                checkCount++;

                // EndRun 은 감소만 멈추고 남은 값은 결과 화면이 읽도록 남긴다.
                manager.BeginRun();
                Tick(manager, 10f);
                var stopped = manager.CurrentStamina;
                manager.EndRun();
                Tick(manager, 10f);
                AssertNear(manager.CurrentStamina, stopped,
                          "EndRun 후에도 줄었습니다: " + manager.CurrentStamina);
                checkCount++;
            }
            finally
            {
                GameEvents.OnStaminaChanged -= onChanged;
                GameEvents.OnStaminaRestored -= onRestored;
                GameEvents.OnStaminaDepleted -= onDepleted;
                TearDown(ref manager, ref host);
            }

            return checkCount;
        }

        /// <summary>구독을 먼저 풀고 오브젝트를 지운다. 순서를 바꾸면 해제 대상이 이미 파괴돼 있다.</summary>
        private static void TearDown(ref StaminaManager manager, ref GameObject host)
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
        private static StaminaManager CreateManager(BalanceData balanceData, out GameObject host)
        {
            host = new GameObject("StaminaManagerCheck")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            host.SetActive(false);
            var manager = host.AddComponent<StaminaManager>();
            SetPrivateField(manager, "_balanceData", balanceData);
            host.SetActive(true);
            InvokeLifecycle(manager, "OnEnable");
            return manager;
        }

        private static void SetPrivateField(StaminaManager manager, string fieldName, object value)
        {
            var field = typeof(StaminaManager).GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(field != null, fieldName + " 필드를 찾지 못했습니다. 이름이 바뀌었습니까?");
            field.SetValue(manager, value);
        }

        /// <summary>Edit Mode 에서는 Unity 가 부르지 않으므로 직접 부른다. 위 클래스 주석의 한계 참고.</summary>
        private static void InvokeLifecycle(StaminaManager manager, string methodName)
        {
            var method = typeof(StaminaManager).GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(method != null, methodName + " 을 찾지 못했습니다. 이름이 바뀌었습니까?");
            method.Invoke(manager, null);
        }

        /// <summary>경과 시간을 직접 먹인다. Time.deltaTime 은 에디터 프레임에 좌우돼 쓸 수 없다.</summary>
        private static void Tick(StaminaManager manager, float deltaSeconds)
        {
            var method = typeof(StaminaManager).GetMethod("Tick",
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(method != null, "Tick 을 찾지 못했습니다. 이름이 바뀌었습니까?");
            method.Invoke(manager, new object[] { deltaSeconds });
        }

        private static BreakInfo CreateBreak(float staminaRestore)
        {
            return new BreakInfo("tourist", 0m, Array.Empty<CoinDrop>(), staminaRestore, Vector3.zero);
        }

        private static void AssertThrows(Action action, string message)
        {
            try
            {
                action();
            }
            catch (ArgumentOutOfRangeException)
            {
                return;
            }
            throw new InvalidOperationException(message);
        }

        /// <summary>
        /// 부동소수 비교. Mathf.Approximately 는 허용 오차가 값 크기에 비례해 너무 좁아,
        /// 같은 값을 다른 순서로 계산하면 실패한다 (뺄셈으로 구한 차가 원래 값과 미세하게 어긋난다).
        /// </summary>
        private static void AssertNear(float actual, float expected, string message, float tolerance = 0.001f)
        {
            if (Mathf.Abs(actual - expected) > tolerance)
            {
                throw new InvalidOperationException(message);
            }
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
