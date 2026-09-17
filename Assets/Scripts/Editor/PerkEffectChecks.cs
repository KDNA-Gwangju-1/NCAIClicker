using System;
using System.Reflection;
using NCAIClicker.Core;
using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using NCAIClicker.Targets;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 퍼크 4종이 실제로 효과를 내는지 검증한다 (이슈 #126).
    ///
    /// 4.2(#28)가 OnPerkChosen 까지 붙였지만 **구독하는 시스템이 한 곳도 없어** 퍼크를 골라도
    /// 아무 일도 일어나지 않았다. 여기서는 퍼크를 발행한 뒤 각 시스템의 값이 실제로 달라지는지,
    /// 그리고 GDD 6절의 적용 시점 규칙(런 도중이면 즉시 / 밖이면 다음 런부터)을 지키는지 본다.
    ///
    /// 한계: Edit Mode 는 Awake/OnEnable/Update 를 부르지 않으므로 리플렉션으로 직접 부른다
    /// (기존 *Checks 와 같은 제약).
    /// </summary>
    public static class PerkEffectChecks
    {
        public static void RunBatch()
        {
            // 기대값을 코드에 적지 않는다. CSV 를 고치면 이 검증도 같이 따라가야 한다 (AGENTS.md).
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            AssertCondition(balance != null, "BalanceData 에셋을 찾지 못했습니다.");
            AssertCondition(balance.Perks.Count > 0, "perks.csv 에서 읽은 퍼크가 없습니다.");

            var checkCount = 0;
            checkCount += RunStaminaPerkChecks(balance);
            checkCount += RunCoinPerkChecks(balance);
            checkCount += RunHitPowerPerkChecks(balance);
            checkCount += RunHitRadiusPerkChecks(balance);
            AssertCondition(!EditorUtility.IsDirty(balance), "BalanceData 가 수정됐습니다.");
            checkCount++;

            Debug.Log("[PerkEffectChecks] PASS " + checkCount + " checks.");
        }

        // ---------------------------------------------------------------- 스태미나 회복

        private static int RunStaminaPerkChecks(BalanceData balance)
        {
            var perk = FindPerk(balance, PerkType.StaminaRestore);
            var checkCount = 0;
            var restoredCount = 0;
            var lastRestored = 0f;
            Action<float> onRestored = amount =>
            {
                restoredCount++;
                lastRestored = amount;
            };

            StaminaManager manager = null;
            GameObject host = null;

            // 정리는 전부 finally 에 둔다. 구독이 남으면 다음 실행에서 회복이 두 배가 된다.
            try
            {
                GameEvents.OnStaminaRestored += onRestored;
                manager = CreateManager<StaminaManager>(balance, "PerkCheckStamina", out host);
                InvokeLifecycle(manager, "OnEnable");

                // 만충에서 고르면 **쓰지 않고 예약한다.** 바로 쓰면 그대로 버려진다 (GDD 6절).
                manager.BeginRun();
                var full = manager.CurrentStamina;
                GameEvents.PublishPerkChosen(perk.Id);
                AssertNear(manager.CurrentStamina, full, "만충인데 회복 퍼크가 즉시 쓰였습니다.");
                AssertCondition(restoredCount == 0, "만충인데 회복이 발행됐습니다.");
                checkCount++;

                // 회복량만큼 빈자리가 생기면 그때 한 번에 들어간다.
                Tick(manager, GetSecondsToDrain(balance, perk.Value));
                var drained = manager.CurrentStamina;
                AssertCondition(restoredCount == 1, "빈자리가 생겼는데 회복이 한 번 나가지 않았습니다: " + restoredCount);
                AssertNear(lastRestored, perk.Value, "회복량이 perks.csv 값과 다릅니다.");
                AssertCondition(drained > 0f, "회복 후 값이 0 입니다.");
                checkCount++;

                // 한 번 쓰면 사라진다. 계속 흘려도 다시 회복되지 않는다.
                Tick(manager, GetSecondsToDrain(balance, perk.Value));
                AssertCondition(restoredCount == 1, "회복 퍼크가 두 번 쓰였습니다: " + restoredCount);
                checkCount++;

                TearDown(manager, host);

                // 런 밖에서 고르면 다음 런까지 기다린다.
                restoredCount = 0;
                manager = CreateManager<StaminaManager>(balance, "PerkCheckStaminaPending", out host);
                InvokeLifecycle(manager, "OnEnable");

                GameEvents.PublishPerkChosen(perk.Id);
                AssertCondition(restoredCount == 0, "런 밖인데 회복이 발행됐습니다.");

                manager.BeginRun();
                AssertCondition(restoredCount == 0, "런 시작 직후 만충인데 회복이 쓰였습니다.");
                Tick(manager, GetSecondsToDrain(balance, perk.Value));
                AssertCondition(restoredCount == 1, "다음 런에서 회복 퍼크가 쓰이지 않았습니다: " + restoredCount);
                checkCount++;

                // 구독을 해제하면 반응하지 않는다.
                InvokeLifecycle(manager, "OnDisable");
                manager.BeginRun();
                restoredCount = 0;
                GameEvents.PublishPerkChosen(perk.Id);
                Tick(manager, GetSecondsToDrain(balance, perk.Value));
                AssertCondition(restoredCount == 0, "해제 후에도 퍼크에 반응했습니다.");
                InvokeLifecycle(manager, "OnEnable");
                checkCount++;
            }
            finally
            {
                GameEvents.OnStaminaRestored -= onRestored;
                TearDown(manager, host);
            }

            return checkCount;
        }

        /// <summary>주어진 양만큼 줄어드는 데 걸리는 시간. 감소 속도를 코드에 적지 않기 위한 보조다.</summary>
        private static float GetSecondsToDrain(BalanceData balance, float amount)
        {
            var perSec = balance.Stamina.IdleDrainPerSec;
            AssertCondition(perSec > 0f, "stamina.csv 의 idle_drain_per_sec 가 0 이하입니다.");
            return amount / perSec + 1f;
        }

        // ---------------------------------------------------------------- 코인 획득 강화

        private static int RunCoinPerkChecks(BalanceData balance)
        {
            var perk = FindPerk(balance, PerkType.CoinGainBoost);
            AssertCondition(perk.DurationSec > 0f, "코인 퍼크의 duration_sec 가 0 입니다.");

            var checkCount = 0;
            EconomyManager manager = null;
            GameObject host = null;

            try
            {
                manager = CreateManager<EconomyManager>(balance, "PerkCheckEconomy", out host);
                InvokeLifecycle(manager, "Awake");
                InvokeLifecycle(manager, "OnEnable");
                manager.BeginRun();

                var expectedNet = 0m;
                Break(manager, balance, ref expectedNet, 1f, "퍼크 전인데 배율이 걸렸습니다.");
                checkCount++;

                // 런 도중에 고르면 즉시 걸린다.
                GameEvents.PublishPerkChosen(perk.Id);
                Break(manager, balance, ref expectedNet, perk.Value, "퍼크 배율이 걸리지 않았습니다.");
                checkCount++;

                // 지속 시간이 끝나기 전에는 유지된다.
                Tick(manager, perk.DurationSec * 0.9f);
                Break(manager, balance, ref expectedNet, perk.Value, "지속 시간 안인데 배율이 풀렸습니다.");
                checkCount++;

                // 지나면 풀린다.
                Tick(manager, perk.DurationSec);
                Break(manager, balance, ref expectedNet, 1f, "지속 시간이 지났는데 배율이 남았습니다.");
                checkCount++;

                // 런 도중에 끊기면 남은 시간은 버린다.
                GameEvents.PublishPerkChosen(perk.Id);
                manager.EndRun();
                manager.BeginRun();
                // 누계를 이어서 쓴다. BeginRun 은 런 순수입만 0 으로 돌리고 지갑 잔액은 유지한다.
                Break(manager, balance, ref expectedNet, 1f, "EndRun 후에도 퍼크 배율이 남았습니다.");
                checkCount++;

                TearDown(manager, host);

                // 런 밖에서 고르면 다음 런 시작부터 시간을 센다 (GDD 6절).
                manager = CreateManager<EconomyManager>(balance, "PerkCheckEconomyPending", out host);
                InvokeLifecycle(manager, "Awake");
                InvokeLifecycle(manager, "OnEnable");

                GameEvents.PublishPerkChosen(perk.Id);
                manager.BeginRun();
                expectedNet = 0m;
                Break(manager, balance, ref expectedNet, perk.Value, "예약한 퍼크가 다음 런에 걸리지 않았습니다.");
                Tick(manager, perk.DurationSec + 1f);
                Break(manager, balance, ref expectedNet, 1f, "예약한 퍼크의 지속 시간이 지나도 풀리지 않았습니다.");
                checkCount++;
            }
            finally
            {
                TearDown(manager, host);
            }

            return checkCount;
        }

        /// <summary>
        /// 파괴 한 번을 알리고 잔액이 기대한 배율을 따랐는지 본다.
        /// 소수 잔여가 이월되므로 누계로 비교한다 — CoinWallet 이 잔액을 만드는 방식과 같다.
        /// 피버·대출은 걸지 않는다. 그 조합은 CoinWalletChecks·FeverPayoutChecks 가 본다.
        /// </summary>
        private static void Break(EconomyManager economy, BalanceData balance, ref decimal expectedNet,
                                  float perkMultiplier, string message)
        {
            expectedNet += RawCoin * (decimal)perkMultiplier * (decimal)balance.Economy.CoinBonusMultiplier;
            GameEvents.PublishTargetBroken(new BreakInfo("perk-check", RawCoin, 0f, Vector3.zero));

            var expected = (long)decimal.Floor(expectedNet);
            AssertCondition(economy.CurrentCoin == expected,
                            message + " 기대 잔액 " + expected + ", 실제 " + economy.CurrentCoin);
        }

        /// <summary>파괴 1회의 원시 보상. 밸런스 수치가 아니라 검증용 입력이다.</summary>
        private const decimal RawCoin = 100m;

        // ---------------------------------------------------------------- 타격력 강화

        private static int RunHitPowerPerkChecks(BalanceData balance)
        {
            var perk = FindPerk(balance, PerkType.HitPowerBoost);
            var checkCount = 0;
            var basePower = balance.Economy.BaseHitPower;
            var expected = basePower * (1f + perk.Value / 100f);
            AssertCondition(expected > basePower, "타격력 퍼크가 값을 올리지 않습니다.");

            HammerSwingController hammer = null;
            GameObject host = null;

            try
            {
                hammer = CreateManager<HammerSwingController>(balance, "PerkCheckHammer", out host);
                InvokeLifecycle(hammer, "Awake");
                InvokeLifecycle(hammer, "OnEnable");

                hammer.BeginRun();
                AssertNear(GetHitPower(hammer), basePower, "퍼크 전 타격력이 기준값과 다릅니다.");

                GameEvents.PublishPerkChosen(perk.Id);
                AssertNear(GetHitPower(hammer), expected, "타격력 퍼크가 반영되지 않았습니다.");
                checkCount++;

                // "이번 런" 퍼크라 런이 끝나면 사라진다.
                hammer.EndRun();
                AssertNear(GetHitPower(hammer), basePower, "런이 끝났는데 타격력 퍼크가 남았습니다.");
                checkCount++;

                // 런 밖에서 고르면 다음 런부터 걸린다.
                GameEvents.PublishPerkChosen(perk.Id);
                AssertNear(GetHitPower(hammer), basePower, "런 밖인데 타격력 퍼크가 즉시 걸렸습니다.");
                hammer.BeginRun();
                AssertNear(GetHitPower(hammer), expected, "예약한 타격력 퍼크가 다음 런에 걸리지 않았습니다.");
                checkCount++;
            }
            finally
            {
                TearDown(hammer, host);
            }

            return checkCount;
        }

        private static float GetHitPower(HammerSwingController hammer)
        {
            return (float)GetPrivateField(hammer, "_runHitPower");
        }

        // ---------------------------------------------------------------- 피격 판정 확대

        private static int RunHitRadiusPerkChecks(BalanceData balance)
        {
            var perk = FindPerk(balance, PerkType.HitRadiusBoost);
            var checkCount = 0;

            checkCount += RunTargetRadiusChecks(balance, perk);
            checkCount += RunCreatureManagerPerkChecks(balance, perk);
            return checkCount;
        }

        /// <summary>Target 쪽 산수. 업그레이드 비율과 퍼크 비율이 더해지는지 본다.</summary>
        private static int RunTargetRadiusChecks(BalanceData balance, PerkDef perk)
        {
            var checkCount = 0;
            GameObject host = null;

            try
            {
                host = new GameObject("PerkCheckTarget")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                host.SetActive(false);
                var collider = host.AddComponent<SphereCollider>();
                var target = host.AddComponent<Target>();
                SetPrivateField(target, "_balanceData", balance);
                SetPrivateField(target, "_targetId", GetFirstTargetId(balance));

                var baseRadius = (float)GetPrivateField(target, "_baseHitRadius");
                var basePercent = balance.Economy.HitRadiusBonusPercent;

                target.Initialize();
                AssertNear(collider.radius, baseRadius * (1f + basePercent / 100f),
                           "퍼크 전 반경이 기준값과 다릅니다.");
                checkCount++;

                target.SetPerkHitRadiusPercent(perk.Value);
                AssertNear(collider.radius, baseRadius * (1f + (basePercent + perk.Value) / 100f),
                           "퍼크 반경이 기준 비율에 더해지지 않았습니다: " + collider.radius);
                checkCount++;

                // 다시 Initialize 해도 퍼크가 유지된다 — 풀에서 꺼내 쓰는 경로를 위해서다.
                target.Initialize();
                AssertNear(collider.radius, baseRadius * (1f + (basePercent + perk.Value) / 100f),
                           "재초기화에서 퍼크 반경이 사라졌습니다.");
                checkCount++;
            }
            finally
            {
                if (host != null)
                {
                    UnityEngine.Object.DestroyImmediate(host);
                }
            }

            return checkCount;
        }

        /// <summary>CreatureManager 가 퍼크를 받아 들고 있는지. 스폰 전달은 프리팹이 필요해 보지 않는다.</summary>
        private static int RunCreatureManagerPerkChecks(BalanceData balance, PerkDef perk)
        {
            var checkCount = 0;
            CreatureManager manager = null;
            GameObject host = null;

            try
            {
                manager = CreateManager<CreatureManager>(balance, "PerkCheckCreatures", out host);
                InvokeLifecycle(manager, "Awake");
                InvokeLifecycle(manager, "OnEnable");

                manager.BeginRun();
                AssertNear(GetPerkRadius(manager), 0f, "런 시작인데 퍼크 비율이 남아 있습니다.");

                GameEvents.PublishPerkChosen(perk.Id);
                AssertNear(GetPerkRadius(manager), perk.Value, "퍼크 비율을 받지 못했습니다.");
                checkCount++;

                manager.EndRun();
                AssertNear(GetPerkRadius(manager), 0f, "런이 끝났는데 퍼크 비율이 남았습니다.");
                checkCount++;

                // 런 밖에서 고르면 다음 런부터다.
                GameEvents.PublishPerkChosen(perk.Id);
                AssertNear(GetPerkRadius(manager), 0f, "런 밖인데 퍼크 비율이 즉시 걸렸습니다.");
                manager.BeginRun();
                AssertNear(GetPerkRadius(manager), perk.Value, "예약한 퍼크가 다음 런에 걸리지 않았습니다.");
                checkCount++;
            }
            finally
            {
                TearDown(manager, host);
            }

            return checkCount;
        }

        private static float GetPerkRadius(CreatureManager manager)
        {
            return (float)GetPrivateField(manager, "_perkHitRadiusPercent");
        }

        // ---------------------------------------------------------------- 보조

        /// <summary>그 종류의 퍼크를 perks.csv 에서 찾는다. 없으면 검증할 대상이 사라진 것이다.</summary>
        private static PerkDef FindPerk(BalanceData balance, PerkType type)
        {
            foreach (var perk in balance.Perks)
            {
                if (perk.Type == type)
                {
                    return perk;
                }
            }
            throw new InvalidOperationException("perks.csv 에 " + type + " 퍼크가 없습니다.");
        }

        private static string GetFirstTargetId(BalanceData balance)
        {
            AssertCondition(balance.Targets.Count > 0, "targets.csv 에서 읽은 대상이 없습니다.");
            return balance.Targets[0].Id;
        }

        /// <summary>
        /// 비활성 상태로 만들어 컴포넌트를 붙이고 BalanceData 를 넣은 뒤 켠다.
        /// 씬을 더럽히지 않도록 HideAndDontSave 로 둔다.
        /// </summary>
        private static T CreateManager<T>(BalanceData balance, string hostName, out GameObject host)
            where T : Component
        {
            host = new GameObject(hostName)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            host.SetActive(false);
            var manager = host.AddComponent<T>();
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

        private static object GetPrivateField(Component target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(field != null,
                            target.GetType().Name + " 의 " + fieldName + " 필드를 찾지 못했습니다.");
            return field.GetValue(target);
        }

        /// <summary>구독을 먼저 풀고 오브젝트를 지운다. 순서를 바꾸면 해제 대상이 이미 파괴돼 있다.</summary>
        private static void TearDown(Component manager, GameObject host)
        {
            if (manager != null && manager.GetType().GetMethod("OnDisable",
                    BindingFlags.NonPublic | BindingFlags.Instance) != null)
            {
                InvokeLifecycle(manager, "OnDisable");
            }
            if (host != null)
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>Edit Mode 에서는 Unity 가 부르지 않으므로 직접 부른다. 클래스 주석의 한계 참고.</summary>
        private static void InvokeLifecycle(Component manager, string methodName)
        {
            var method = manager.GetType().GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(method != null,
                            manager.GetType().Name + " 의 " + methodName + " 을 찾지 못했습니다.");
            method.Invoke(manager, null);
        }

        /// <summary>경과 시간을 직접 먹인다. Time.deltaTime 은 에디터 프레임에 좌우돼 쓸 수 없다.</summary>
        private static void Tick(Component manager, float deltaSeconds)
        {
            var method = manager.GetType().GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(method != null, manager.GetType().Name + " 의 Tick 을 찾지 못했습니다.");
            method.Invoke(manager, new object[] { deltaSeconds });
        }

        /// <summary>
        /// 부동소수 비교. Mathf.Approximately 는 허용 오차가 값 크기에 비례해 너무 좁아,
        /// 같은 값을 다른 순서로 계산하면 실패한다.
        /// </summary>
        private static void AssertNear(float actual, float expected, string message, float tolerance = 0.001f)
        {
            if (Mathf.Abs(actual - expected) > tolerance)
            {
                throw new InvalidOperationException(message + " (기대 " + expected + ", 실제 " + actual + ")");
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
