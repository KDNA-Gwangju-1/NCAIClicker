using System;
using System.Reflection;
using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Fever;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 피버 게이지의 계산(FeverGauge)과 이벤트 배선(FeverManager)을 검증한다.
    ///
    /// 한계: Edit Mode 에서는 Unity 가 OnEnable/OnDisable/Update 를 부르지 않는다
    /// ([ExecuteAlways] 를 붙이지 않았다 — 붙이면 에디터에서도 피버가 돌아간다).
    /// 그래서 생명주기와 Tick 을 직접 불러 **구독과 해제가 짝을 이루는지**와
    /// **경과 시간에 대한 반응**을 본다. Unity 가 실제로 그 시점에 불러 주는지는
    /// Play Mode 확인이 필요하나, 테스트 asmdef 가 런타임 코드(Assembly-CSharp)를
    /// 참조하지 못해 미검증으로 남는다 (StaminaChecks·EconomyManagerChecks 와 같은 제약).
    ///
    /// 런당 발동 횟수가 완료 기준인데 그것은 이 검증이 아니라 밸런스 모델
    /// (.github/scripts/simulate_balance.py)이 본다. 여기서는 한 주기의 동작만 본다.
    /// </summary>
    public static class FeverChecks
    {
        public static void RunBatch()
        {
            // 기대값을 코드에 적지 않는다. CSV 를 고치면 이 검증도 같이 따라가야 한다 (AGENTS.md).
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            AssertCondition(balance != null, "BalanceData 에셋을 찾지 못했습니다.");

            var checkCount = 0;
            checkCount += RunGaugeChecks(balance);
            checkCount += RunManagerChecks(balance);
            Debug.Log("[FeverChecks] PASS " + checkCount + " checks.");
        }

        // ---------------------------------------------------------------- 계산부

        private static int RunGaugeChecks(BalanceData balance)
        {
            var checkCount = 0;
            var gaugeMax = balance.Fever.GaugeMax;
            var perHit = balance.Fever.GaugePerHit;
            var decayPerSec = balance.Fever.GaugeDecayPerSec;
            var gauge = new FeverGauge();

            // 게이지는 스태미나와 달리 **비어서** 출발한다.
            gauge.Reset(gaugeMax);
            AssertCondition(gauge.Current == 0f, "런 시작 게이지가 0 이 아닙니다: " + gauge.Current);
            AssertNear(gauge.Max, gaugeMax, "최대치가 다릅니다.");
            checkCount++;

            // 적중 한 번에 gauge_per_hit 만큼 쌓이고, 그것만으로 가득 차지는 않는다.
            gauge.AddHit(perHit);
            AssertCondition(!gauge.IsFull, "적중 한 번에 가득 찼습니다. gauge_per_hit 를 확인하세요.");
            AssertNear(gauge.Current, perHit, "1회 누적량이 다릅니다: " + gauge.Current);
            checkCount++;

            // 가득 차기까지 필요한 적중 수와, 비웠을 때 0 이 되는 것을 본다.
            gauge.Reset(gaugeMax);
            var hits = 0;
            var maxHits = Mathf.CeilToInt(gaugeMax / perHit) + 10;
            while (!gauge.IsFull && hits < maxHits)
            {
                gauge.AddHit(perHit);
                hits++;
            }
            var expectedHits = gaugeMax / perHit;
            AssertCondition(gauge.IsFull, "적중을 반복해도 가득 차지 않았습니다.");
            AssertCondition(Mathf.Abs(hits - expectedHits) <= 1f,
                            "발동까지 필요한 적중 수가 다릅니다: " + hits + " (기대 " + expectedHits + ")");
            gauge.Consume();
            AssertCondition(gauge.Current == 0f, "발동에 쓴 뒤 게이지가 비워지지 않았습니다: " + gauge.Current);
            AssertCondition(!gauge.IsFull, "비운 뒤에도 가득 참으로 판정됩니다.");
            checkCount++;

            // 감쇠는 실제로 줄어든 양을 돌려준다.
            gauge.Reset(gaugeMax);
            gauge.AddHit(gaugeMax * 0.5f);
            var before = gauge.Current;
            var decayed = gauge.Decay(decayPerSec, 1f);
            AssertNear(decayed, decayPerSec, "1초 감쇠량이 다릅니다: " + decayed);
            AssertNear(gauge.Current, before - decayPerSec, "감쇠 후 값이 다릅니다: " + gauge.Current);
            checkCount++;

            // 0 아래로 내려가지 않는다. 음수가 되면 HUD 막대가 뒤집힌다.
            gauge.Decay(decayPerSec, 1000f);
            AssertCondition(gauge.Current == 0f, "0 아래로 내려갔습니다: " + gauge.Current);
            AssertCondition(gauge.Decay(decayPerSec, 1f) == 0f, "빈 게이지에서 감쇠량이 0 이 아닙니다.");
            checkCount++;

            // 잘못된 인자는 조용히 넘기지 않는다.
            AssertThrows(() => gauge.Reset(0f), "최대치 0 이 거부되지 않았습니다.");
            AssertThrows(() => gauge.AddHit(-1f), "음수 누적량이 거부되지 않았습니다.");
            AssertThrows(() => gauge.Decay(-1f, 1f), "음수 감쇠량이 거부되지 않았습니다.");
            AssertThrows(() => gauge.Decay(decayPerSec, -1f), "음수 경과 시간이 거부되지 않았습니다.");
            checkCount++;

            return checkCount;
        }

        // ---------------------------------------------------------------- 매니저

        private static int RunManagerChecks(BalanceData balance)
        {
            var checkCount = 0;
            var gaugeMax = balance.Fever.GaugeMax;
            var perHit = balance.Fever.GaugePerHit;
            var decayPerSec = balance.Fever.GaugeDecayPerSec;
            var graceSec = balance.Fever.DecayGraceSec;
            var durationSec = balance.Fever.DurationSec;

            var changedCount = 0;
            var lastGauge = 0f;
            var lastMax = 0f;
            var startCount = 0;
            var endCount = 0;

            Action<float, float> onChanged = (current, max) =>
            {
                changedCount++;
                lastGauge = current;
                lastMax = max;
            };
            Action onStart = () => startCount++;
            Action onEnd = () => endCount++;

            FeverManager manager = null;
            GameObject host = null;

            // 정리는 전부 finally 에 둔다. 중간에 검증이 실패해도 정적 이벤트에 구독이 남으면
            // 다음 실행에서 매니저가 둘이 되어 적중 하나가 두 번 누적된다.
            try
            {
                GameEvents.OnFeverGaugeChanged += onChanged;
                GameEvents.OnFeverStart += onStart;
                GameEvents.OnFeverEnd += onEnd;

                manager = CreateManager(balance, out host);

                // BeginRun 전에는 적중해도 차지 않는다. MainMenu 에서 쌓이는 것을 막는다.
                Hit();
                Tick(manager, graceSec * 2f);
                AssertCondition(changedCount == 0, "런 시작 전에 이벤트가 발행됐습니다.");
                AssertCondition(manager.CurrentGauge == 0f, "런 시작 전에 게이지가 찼습니다.");
                checkCount++;

                // 런을 시작하면 빈 게이지를 즉시 알린다.
                manager.BeginRun();
                AssertCondition(changedCount == 1, "런 시작 시 발행이 한 번이 아닙니다: " + changedCount);
                AssertCondition(lastGauge == 0f, "런 시작 게이지가 0 이 아닙니다: " + lastGauge);
                AssertNear(lastMax, gaugeMax, "최대치 인자가 다릅니다.");
                checkCount++;

                // 적중만 누적한다.
                Hit();
                AssertNear(manager.CurrentGauge, perHit, "적중 1회 누적량이 다릅니다: " + manager.CurrentGauge);
                checkCount++;

                // 헛스윙은 누적하지 않는다. 망치가 상시 스윙하므로 이게 무너지면 가만히 있어도 찬다.
                var beforeMiss = manager.CurrentGauge;
                Miss();
                Miss();
                AssertNear(manager.CurrentGauge, beforeMiss, "헛스윙이 누적됐습니다: " + manager.CurrentGauge);
                checkCount++;

                // 유예 시간 안에는 감쇠하지 않는다.
                var beforeGrace = manager.CurrentGauge;
                Tick(manager, graceSec * 0.5f);
                AssertNear(manager.CurrentGauge, beforeGrace, "유예 중에 감쇠했습니다: " + manager.CurrentGauge);
                checkCount++;

                // 유예를 넘기면 감쇠한다.
                Tick(manager, graceSec);
                AssertCondition(manager.CurrentGauge < beforeGrace,
                                "유예 후에도 감쇠하지 않았습니다: " + manager.CurrentGauge);
                checkCount++;

                // 적중하면 유예가 다시 시작된다.
                Hit();
                var afterHit = manager.CurrentGauge;
                Tick(manager, graceSec * 0.5f);
                AssertNear(manager.CurrentGauge, afterHit, "적중 후 유예가 초기화되지 않았습니다.");
                checkCount++;

                // 감쇠는 0 에서 멈춘다.
                Tick(manager, graceSec + gaugeMax / decayPerSec * 2f);
                AssertCondition(manager.CurrentGauge == 0f, "감쇠가 0 아래로 갔습니다: " + manager.CurrentGauge);
                checkCount++;

                // 가득 차면 발동하고 게이지가 비워진다.
                manager.BeginRun();
                startCount = 0;
                HitUntilFever(manager, gaugeMax, perHit);
                AssertCondition(startCount == 1, "발동이 한 번이 아닙니다: " + startCount);
                AssertCondition(manager.IsFeverActive, "발동 후 피버 상태가 아닙니다.");
                AssertCondition(manager.CurrentGauge == 0f, "발동 후 게이지가 비워지지 않았습니다: " + manager.CurrentGauge);
                checkCount++;

                // 피버 중의 적중은 쌓이지 않는다. 쌓으면 다음 피버가 앞당겨진다.
                Hit();
                Hit();
                AssertCondition(manager.CurrentGauge == 0f, "피버 중에 게이지가 쌓였습니다: " + manager.CurrentGauge);
                checkCount++;

                // 피버 중에는 감쇠도 하지 않는다 (이미 0 이므로 발동 횟수로 확인한다).
                Tick(manager, durationSec * 0.5f);
                AssertCondition(manager.IsFeverActive, "지속 시간 중에 피버가 꺼졌습니다.");
                AssertCondition(endCount == 0, "지속 시간 중에 종료가 발행됐습니다: " + endCount);
                checkCount++;

                // 지속 시간이 지나면 한 번만 종료한다.
                endCount = 0;
                Tick(manager, durationSec);
                AssertCondition(!manager.IsFeverActive, "지속 시간이 지나도 피버가 켜져 있습니다.");
                AssertCondition(endCount == 1, "종료가 한 번이 아닙니다: " + endCount);
                Tick(manager, durationSec * 2f);
                AssertCondition(endCount == 1, "종료가 반복 발행됐습니다: " + endCount);
                checkCount++;

                // 종료 뒤에는 다시 채울 수 있다 — 런당 여러 번 발동하려면 이게 돌아야 한다.
                startCount = 0;
                HitUntilFever(manager, gaugeMax, perHit);
                AssertCondition(startCount == 1, "두 번째 발동이 되지 않았습니다: " + startCount);
                checkCount++;

                // EndRun 이 피버 중이면 종료를 알린다. 안 알리면 코인 배율이 켜진 채로 남는다.
                endCount = 0;
                manager.EndRun();
                AssertCondition(endCount == 1, "런 종료 시 피버 종료가 발행되지 않았습니다: " + endCount);
                AssertCondition(!manager.IsFeverActive, "런 종료 후에도 피버가 켜져 있습니다.");
                checkCount++;

                // 구독을 해제하면 반응하지 않는다.
                manager.BeginRun();
                InvokeLifecycle(manager, "OnDisable");
                var beforeDetach = manager.CurrentGauge;
                Hit();
                AssertNear(manager.CurrentGauge, beforeDetach,
                           "해제 후에도 누적됐습니다. 구독 해제가 빠졌습니다.");
                checkCount++;

                // 다시 구독해도 하나뿐이다. 두 번 들어오면 적중 하나가 두 번 쌓인다.
                InvokeLifecycle(manager, "OnEnable");
                beforeDetach = manager.CurrentGauge;
                Hit();
                AssertNear(manager.CurrentGauge, beforeDetach + perHit,
                           "재구독 후 누적이 중복되거나 빠졌습니다: " + manager.CurrentGauge);
                checkCount++;

                // 지속 감쇠 발행을 묶는다.
                manager.BeginRun();
                Hit();
                Tick(manager, graceSec);
                changedCount = 0;
                for (var i = 0; i < 100; i++)
                {
                    Tick(manager, 0.01f);
                }
                AssertCondition(changedCount < 100, "지속 감쇠가 프레임마다 발행됐습니다: " + changedCount);
                checkCount++;
            }
            finally
            {
                GameEvents.OnFeverGaugeChanged -= onChanged;
                GameEvents.OnFeverStart -= onStart;
                GameEvents.OnFeverEnd -= onEnd;
                TearDown(ref manager, ref host);
            }

            return checkCount;
        }

        /// <summary>발동할 때까지 적중시킨다. 필요한 횟수를 코드에 적지 않기 위한 보조다.</summary>
        private static void HitUntilFever(FeverManager manager, float gaugeMax, float perHit)
        {
            var limit = Mathf.CeilToInt(gaugeMax / perHit) + 10;
            for (var i = 0; i < limit && !manager.IsFeverActive; i++)
            {
                Hit();
            }
            AssertCondition(manager.IsFeverActive, "적중을 반복해도 피버가 발동하지 않았습니다.");
        }

        private static void Hit()
        {
            GameEvents.PublishSwingResolved(HitSource.Hover, true);
        }

        private static void Miss()
        {
            GameEvents.PublishSwingResolved(HitSource.Hover, false);
        }

        /// <summary>구독을 먼저 풀고 오브젝트를 지운다. 순서를 바꾸면 해제 대상이 이미 파괴돼 있다.</summary>
        private static void TearDown(ref FeverManager manager, ref GameObject host)
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
        private static FeverManager CreateManager(BalanceData balance, out GameObject host)
        {
            host = new GameObject("FeverManagerCheck")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            host.SetActive(false);
            var manager = host.AddComponent<FeverManager>();
            var field = typeof(FeverManager).GetField("_balanceData",
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(field != null, "_balanceData 필드를 찾지 못했습니다. 이름이 바뀌었습니까?");
            field.SetValue(manager, balance);
            host.SetActive(true);
            InvokeLifecycle(manager, "OnEnable");
            return manager;
        }

        /// <summary>Edit Mode 에서는 Unity 가 부르지 않으므로 직접 부른다. 위 클래스 주석의 한계 참고.</summary>
        private static void InvokeLifecycle(FeverManager manager, string methodName)
        {
            var method = typeof(FeverManager).GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(method != null, methodName + " 을 찾지 못했습니다. 이름이 바뀌었습니까?");
            method.Invoke(manager, null);
        }

        /// <summary>경과 시간을 직접 먹인다. Time.deltaTime 은 에디터 프레임에 좌우돼 쓸 수 없다.</summary>
        private static void Tick(FeverManager manager, float deltaSeconds)
        {
            var method = typeof(FeverManager).GetMethod("Tick",
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(method != null, "Tick 을 찾지 못했습니다. 이름이 바뀌었습니까?");
            method.Invoke(manager, new object[] { deltaSeconds });
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
