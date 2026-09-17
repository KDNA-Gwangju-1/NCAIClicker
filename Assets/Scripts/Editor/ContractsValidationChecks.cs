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
            var testBreak = new BreakInfo("target_1", 100m, 5f, new Vector3(1f, 2f, 3f));
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
            if (save.Version != 2 || save.CurrentDay != 1 || save.LastLoanRepaidDay != -1)
            {
                throw new InvalidOperationException("SaveData default values mismatch");
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

                // 4. 파산 이벤트 시 Result 전이 및 EndRun 호출 확인
                setMethod.Invoke(gm, new object[] { RunState.Running });
                callOrder.Clear();
                GameEvents.PublishBankrupt();
                if (gm.CurrentState != RunState.Result || callOrder.Count != 3)
                {
                    throw new InvalidOperationException("PublishBankrupt failed to transition to Result and call EndRun");
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