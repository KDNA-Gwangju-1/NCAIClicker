using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using NCAIClicker.Core;
using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 공용 계약(인터페이스, 이벤트 버스, DTO) 정합성을 검증한다.
    /// </summary>
    public static class ContractsValidationChecks
    {
        public static void RunBatch()
        {
            VerifyGameEvents();
            VerifyDataStructures();
            VerifyRunScopedContracts();
            VerifyGameManagerLifecycle();
            Debug.Log("[ContractsValidationChecks] All contract checks passed successfully.");
        }

        private static void VerifyGameEvents()
        {
            var coinEarnedReceived = 0L;
            GameEvents.OnCoinEarned += val => coinEarnedReceived = val;
            GameEvents.PublishCoinEarned(500L);
            if (coinEarnedReceived != 500L)
            {
                throw new InvalidOperationException("OnCoinEarned event failed");
            }

            var targetBrokenInvoked = false;
            var testBreak = new BreakInfo("target_1", 100m, Array.Empty<CoinDrop>(), 5f, new Vector3(1f, 2f, 3f));
            GameEvents.OnTargetBroken += info =>
            {
                if (info.TargetId == "target_1" && info.RawCoin == 100m && info.StaminaRestore == 5f)
                {
                    targetBrokenInvoked = true;
                }
            };
            GameEvents.PublishTargetBroken(testBreak);
            if (!targetBrokenInvoked)
            {
                throw new InvalidOperationException("OnTargetBroken event failed");
            }

            var coinAfterReset = 0L;
            var brokenAfterReset = false;
            GameEvents.OnCoinEarned += val => coinAfterReset = val;
            GameEvents.OnTargetBroken += _ => brokenAfterReset = true;
            GameEvents.ResetAll();
            GameEvents.PublishCoinEarned(999L);
            GameEvents.PublishTargetBroken(testBreak);
            if (coinAfterReset != 0L || brokenAfterReset)
            {
                throw new InvalidOperationException("ResetAll failed to clear delegates");
            }
        }

        private static void VerifyDataStructures()
        {
            var hit = new HitInfo(HitSource.Hover, 10f, Vector3.zero);
            if (hit.Source != HitSource.Hover || hit.Damage != 10f)
            {
                throw new InvalidOperationException("HitInfo failed");
            }

            var save = new SaveData();
            // 버전 숫자를 박지 않는다. 박아 두면 저장 구조를 늘릴 때마다 이 검증이 깨지는데,
            // 정작 위험한 것은 숫자가 바뀌는 것이 아니라 **마이그레이션 분기를 빠뜨리는 것**이다
            // (바로 아래에서 본다).
            if (save.Version != SaveData.CurrentVersion || save.CurrentDay != 1 || save.LastLoanRepaidDay != -1)
            {
                throw new InvalidOperationException("SaveData default values mismatch");
            }

            VerifySaveMigrations();
        }

        /// <summary>
        /// 1 부터 현재 버전까지 **모든 저장 버전**이 마이그레이션을 통과해 현재 버전으로 올라오는지 본다.
        ///
        /// 버전만 올리고 `ApplyVersionMigrations` 에 분기를 더하지 않으면 그 버전의 저장 파일이
        /// `NotSupportedException` 으로 떨어져 **사용자의 저장이 통째로 버려진다.** 그 경로는
        /// 예전 저장 파일이 있는 사람에게만 터지므로 개발 중에는 드러나지 않는다.
        /// </summary>
        private static void VerifySaveMigrations()
        {
            var migrate = typeof(SaveManager).GetMethod("ApplyVersionMigrations",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (migrate == null)
            {
                throw new InvalidOperationException("ApplyVersionMigrations not found — renamed?");
            }

            for (var version = 1; version <= SaveData.CurrentVersion; version++)
            {
                var data = new SaveData { Version = version, TotalCoin = 777L };
                SaveData migrated;
                try
                {
                    migrated = (SaveData)migrate.Invoke(null, new object[] { data });
                }
                catch (TargetInvocationException e)
                {
                    throw new InvalidOperationException(
                        "저장 버전 " + version + " 의 마이그레이션 분기가 없다. " +
                        "SaveData.CurrentVersion 을 올렸으면 ApplyVersionMigrations 에 case 를 더한다: " +
                        e.InnerException?.Message);
                }

                if (migrated.Version != SaveData.CurrentVersion)
                {
                    throw new InvalidOperationException(
                        "저장 버전 " + version + " 이 현재 버전으로 올라오지 않았다: " + migrated.Version);
                }
                if (migrated.TotalCoin != 777L)
                {
                    throw new InvalidOperationException(
                        "저장 버전 " + version + " 마이그레이션이 기존 값을 잃었다.");
                }
            }
        }

        private static void VerifyRunScopedContracts()
        {
            var runScopedType = typeof(IRunScoped);
            var beginMethod = runScopedType.GetMethod("BeginRun");
            var endMethod = runScopedType.GetMethod("EndRun");

            if (beginMethod == null || endMethod == null)
            {
                throw new InvalidOperationException("IRunScoped must define both BeginRun and EndRun");
            }

            var staminaType = Type.GetType("NCAIClicker.Core.StaminaManager, Assembly-CSharp");
            var feverType = Type.GetType("NCAIClicker.Fever.FeverManager, Assembly-CSharp");
            var economyType = Type.GetType("NCAIClicker.Economy.EconomyManager, Assembly-CSharp");

            if (staminaType == null || !runScopedType.IsAssignableFrom(staminaType))
            {
                throw new InvalidOperationException("StaminaManager must implement IRunScoped");
            }

            if (feverType == null || !runScopedType.IsAssignableFrom(feverType))
            {
                throw new InvalidOperationException("FeverManager must implement IRunScoped");
            }

            if (economyType == null || !runScopedType.IsAssignableFrom(economyType))
            {
                throw new InvalidOperationException("EconomyManager must implement IRunScoped");
            }
        }

        private static void VerifyGameManagerLifecycle()
        {
            var host = new GameObject("GameManagerCheckHost");

            try
            {
                var gm = host.AddComponent<GameManager>();
                var mockEconomy = host.AddComponent<MockRunScopedEconomy>();
                var mockStamina = host.AddComponent<MockRunScopedStamina>();
                var mockFever = host.AddComponent<MockRunScopedFever>();

                var callOrder = new List<string>();
                mockEconomy.OnBegin = () => callOrder.Add("Economy");
                mockStamina.OnBegin = () => callOrder.Add("Stamina");
                mockFever.OnBegin = () => callOrder.Add("Fever");

                mockEconomy.OnEnd = () => callOrder.Add("EconomyEnd");
                mockStamina.OnEnd = () => callOrder.Add("StaminaEnd");
                mockFever.OnEnd = () => callOrder.Add("FeverEnd");

                var onEnableMethod = typeof(GameManager).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
                var setMethod = typeof(GameManager).GetMethod("SetState", BindingFlags.NonPublic | BindingFlags.Instance);
                if (onEnableMethod == null || setMethod == null)
                {
                    throw new InvalidOperationException("GameManager methods not found");
                }

                onEnableMethod.Invoke(gm, null);

                // 1. Running 으로 전이 시 BeginRun 호출 및 초기화 순서 검증 (Economy -> Stamina -> Fever)
                setMethod.Invoke(gm, new object[] { RunState.Running });
                if (callOrder.Count != 3)
                {
                    throw new InvalidOperationException("BeginRun count mismatch: expected 3, got " + callOrder.Count);
                }
                if (callOrder[0] != "Economy" || callOrder[1] != "Stamina" || callOrder[2] != "Fever")
                {
                    throw new InvalidOperationException("BeginRun order violation: expected Economy -> Stamina -> Fever");
                }

                // 2. Result 로 전이 시 EndRun 호출 검증
                callOrder.Clear();
                setMethod.Invoke(gm, new object[] { RunState.Result });
                if (callOrder.Count != 3)
                {
                    throw new InvalidOperationException("EndRun count mismatch: expected 3, got " + callOrder.Count);
                }

                // 3. 스태미나 소진 이벤트 시 Result 전이 및 EndRun 호출 확인
                setMethod.Invoke(gm, new object[] { RunState.Running });
                callOrder.Clear();
                GameEvents.PublishStaminaDepleted();
                if (gm.CurrentState != RunState.Result || callOrder.Count != 3)
                {
                    throw new InvalidOperationException("PublishStaminaDepleted failed to transition to Result and call EndRun");
                }

                // 4. 방어적 경로: OnBankrupt 가 Running 중 직접 발행돼도 Result 로 전이한다.
                //    실제 게임에서는 이렇게 일어나지 않는다 — BillManager.EndRun() 은 이미 Result 로
                //    전이된 뒤에 불려 그 안에서 OnBankrupt 를 발행한다 (진짜 흐름은 5번 케이스).
                //    이 케이스는 GameManager 의 구독이 방어적으로 여전히 동작하는지만 본다
                //    (docs/TECH_NOTES/billing.md "OnBankrupt 로 Result 로 전이한다는 계약은 이 경로에서 늦다").
                setMethod.Invoke(gm, new object[] { RunState.Running });
                callOrder.Clear();
                GameEvents.PublishBankrupt();
                if (gm.CurrentState != RunState.Result || callOrder.Count != 3)
                {
                    throw new InvalidOperationException("PublishBankrupt failed to transition to Result and call EndRun");
                }

                // 5. 진짜 흐름: 이미 Result 로 전이된 뒤(EndRun 도중) 발행되는 OnBankrupt 는
                //    CurrentState == Running 가드에 막혀 EndRun 을 다시 부르지 않는다.
                //    OnBankrupt 는 Result 전이의 원인이 아니라 전이 도중 확정되는 결과 통지다.
                callOrder.Clear();
                GameEvents.PublishBankrupt();
                if (gm.CurrentState != RunState.Result || callOrder.Count != 0)
                {
                    throw new InvalidOperationException("PublishBankrupt while already in Result must not re-invoke EndRun");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                GameEvents.ResetAll();
            }
        }

        private class MockRunScopedEconomy : MonoBehaviour, IRunScoped
        {
            public Action OnBegin;
            public Action OnEnd;
            public void BeginRun() => OnBegin?.Invoke();
            public void EndRun() => OnEnd?.Invoke();
        }

        private class MockRunScopedStamina : MonoBehaviour, IRunScoped
        {
            public Action OnBegin;
            public Action OnEnd;
            public void BeginRun() => OnBegin?.Invoke();
            public void EndRun() => OnEnd?.Invoke();
        }

        private class MockRunScopedFever : MonoBehaviour, IRunScoped
        {
            public Action OnBegin;
            public Action OnEnd;
            public void BeginRun() => OnBegin?.Invoke();
            public void EndRun() => OnEnd?.Invoke();
        }
    }
}