using System;
using System.IO;
using System.Reflection;
using NCAIClicker.Core;
using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using NCAIClicker.Fever;
using NCAIClicker.Interfaces;
using NCAIClicker.Targets;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 업그레이드 실효값이 **소비처에 실제로 닿는지**를 검증한다 (이슈 #131).
    ///
    /// UpgradeChecks 는 계산(UpgradeState)이 맞는지까지만 본다. 그 값을 읽는 쪽이 하나도 없어도
    /// 그 검증은 전부 통과한다 — 실제로 #116 머지 시점에 소비처가 0곳이었는데 아무도 몰랐다.
    /// 그래서 여기서는 소비처를 직접 만들어 **레벨 0 과 레벨 N 의 결과가 달라지는지**를 본다.
    ///
    /// 한계: Edit Mode 는 Awake/OnEnable/Update 를 부르지 않으므로 리플렉션으로 직접 부른다
    /// (기존 *Checks 와 같은 제약).
    /// </summary>
    public static class UpgradeConsumerChecks
    {
        public static void RunBatch()
        {
            // 기대값을 코드에 적지 않는다. CSV 를 고치면 이 검증도 같이 따라가야 한다 (AGENTS.md).
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            AssertCondition(balance != null, "BalanceData 에셋을 찾지 못했습니다.");

            var checkCount = 0;
            checkCount += RunStaminaChecks(balance);
            checkCount += RunFeverChecks(balance);
            checkCount += RunHammerChecks(balance);
            checkCount += RunTargetChecks(balance);
            checkCount += RunEconomyChecks(balance);
            checkCount += RunAutoHammerChecks(balance);
            checkCount += RunAutoHammerProcChecks(balance);
            checkCount += RunNextRunRuleChecks(balance);
            checkCount += RunBootstrapWiringCheck();
            AssertCondition(!EditorUtility.IsDirty(balance), "BalanceData 가 수정됐습니다.");
            checkCount++;

            Debug.Log("[UpgradeConsumerChecks] PASS " + checkCount + " checks.");
        }

        // ---------------------------------------------------------------- 스태미나

        private static int RunStaminaChecks(BalanceData balance)
        {
            var checkCount = 0;
            var baseMax = balance.Stamina.Max;
            var baseDrain = balance.Stamina.IdleDrainPerSec;

            StaminaManager manager = null;
            GameObject host = null;

            try
            {
                manager = CreateManager<StaminaManager>(balance, "UpgradeConsumerStamina", out host);
                InvokeLifecycle(manager, "OnEnable");

                // 주입이 없으면 CSV 기준값 그대로다. 배선이 빠져도 게임이 돌아가야 한다.
                manager.BeginRun();
                AssertNear(manager.MaxStamina, baseMax, "주입 없이 최대 스태미나가 기준값과 다릅니다.");
                var bareDrain = DrainOverOneSecond(manager);
                AssertNear(bareDrain, baseDrain, "주입 없이 감소 속도가 기준값과 다릅니다.");
                checkCount++;

                // 최대치 업그레이드가 반영된다.
                var maxStats = CreateStub(StatId.MaxStamina, baseMax, out var expectedMax);
                manager.SetUpgradeStats(maxStats);
                manager.BeginRun();
                AssertNear(manager.MaxStamina, expectedMax, "최대 스태미나가 GetStat 을 거치지 않습니다.");
                checkCount++;

                // 감소 속도도 같은 통로를 탄다.
                var drainStats = CreateStub(StatId.IdleDrainPerSec, baseDrain, out var expectedDrain);
                manager.SetUpgradeStats(drainStats);
                manager.BeginRun();
                AssertNear(DrainOverOneSecond(manager), expectedDrain, "감소 속도가 GetStat 을 거치지 않습니다.");
                checkCount++;
            }
            finally
            {
                TearDown(manager, host);
            }

            return checkCount;
        }

        /// <summary>1초분을 흘려 실제로 줄어든 양을 잰다. 감소 속도를 밖에서 읽을 방법이 없다.</summary>
        private static float DrainOverOneSecond(StaminaManager manager)
        {
            var before = manager.CurrentStamina;
            Tick(manager, 1f);
            return before - manager.CurrentStamina;
        }

        // ---------------------------------------------------------------- 피버

        private static int RunFeverChecks(BalanceData balance)
        {
            var checkCount = 0;
            var baseDuration = balance.Fever.DurationSec;
            var basePerHit = balance.Fever.GaugePerHit;

            FeverManager manager = null;
            GameObject host = null;

            try
            {
                manager = CreateManager<FeverManager>(balance, "UpgradeConsumerFever", out host);
                InvokeLifecycle(manager, "OnEnable");

                // 주입이 없으면 적중 1회 누적량이 CSV 기준값 그대로다.
                manager.BeginRun();
                GameEvents.PublishSwingResolved(HitSource.Hover, true);
                AssertNear(manager.CurrentGauge, basePerHit, "주입 없이 누적량이 기준값과 다릅니다.");
                checkCount++;

                // 누적량이 같은 통로를 탄다.
                var hitStats = CreateStub(StatId.FeverGaugePerHit, basePerHit, out var expectedPerHit);
                manager.SetUpgradeStats(hitStats);
                manager.BeginRun();
                GameEvents.PublishSwingResolved(HitSource.Hover, true);
                AssertNear(manager.CurrentGauge, expectedPerHit, "누적량이 GetStat 을 거치지 않습니다.");
                checkCount++;

                // 지속 시간도 마찬가지 — 기준 지속 시간이 지나도 아직 피버여야 한다.
                var durationStats = CreateStub(StatId.FeverDuration, baseDuration, out var expectedDuration);
                manager.SetUpgradeStats(durationStats);
                manager.BeginRun();
                HitUntilFever(manager, balance);
                Tick(manager, baseDuration);
                AssertCondition(manager.IsFeverActive,
                                "기준 지속 시간이 지나자 피버가 끝났습니다. 지속 시간이 GetStat 을 거치지 않습니다.");
                Tick(manager, expectedDuration);
                AssertCondition(!manager.IsFeverActive, "실효 지속 시간이 지났는데 피버가 유지됩니다.");
                checkCount++;
            }
            finally
            {
                TearDown(manager, host);
            }

            return checkCount;
        }

        // ---------------------------------------------------------------- 망치

        private static int RunHammerChecks(BalanceData balance)
        {
            var checkCount = 0;
            var basePower = balance.Economy.BaseHitPower;

            HammerSwingController hammer = null;
            GameObject host = null;

            try
            {
                hammer = CreateManager<HammerSwingController>(balance, "UpgradeConsumerHammer", out host);
                InvokeLifecycle(hammer, "Awake");

                // 타격력은 밖으로 열려 있지 않아 실제 피격으로 확인한다.
                AssertNear(GetHitPower(hammer), basePower, "주입 없이 타격력이 기준값과 다릅니다.");
                checkCount++;

                var stats = CreateStub(StatId.BaseHitPower, basePower, out var expectedPower);
                hammer.SetUpgradeStats(stats);
                AssertNear(GetHitPower(hammer), expectedPower, "타격력이 GetStat 을 거치지 않습니다.");
                checkCount++;
            }
            finally
            {
                TearDown(hammer, host);
            }

            return checkCount;
        }

        /// <summary>굳혀 둔 실효 타격력. private 필드라 리플렉션으로 읽는다.</summary>
        private static float GetHitPower(HammerSwingController hammer)
        {
            var field = typeof(HammerSwingController).GetField("_runHitPower",
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(field != null, "_runHitPower 필드를 찾지 못했습니다. 이름이 바뀌었습니까?");
            return (float)field.GetValue(hammer);
        }

        // ---------------------------------------------------------------- 타격 대상

        private static int RunTargetChecks(BalanceData balance)
        {
            var checkCount = 0;
            var basePercent = balance.Economy.HitRadiusBonusPercent;

            GameObject host = null;

            try
            {
                host = new GameObject("UpgradeConsumerTarget")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                host.SetActive(false);
                var collider = host.AddComponent<SphereCollider>();
                var target = host.AddComponent<Target>();
                SetPrivateField(target, "_balanceData", balance);
                SetPrivateField(target, "_targetId", GetFirstTargetId(balance));

                var baseRadius = (float)GetPrivateField(target, "_baseHitRadius");

                // 주입이 없으면 CSV 기준 비율만 걸린다.
                target.Initialize();
                AssertNear(collider.radius, baseRadius * (1f + basePercent / 100f),
                           "주입 없이 판정 반경이 기준값과 다릅니다: " + collider.radius);
                checkCount++;

                // 업그레이드가 반경을 넓힌다. Initialize 보다 먼저 넣어야 반영된다.
                var stats = CreateStub(StatId.HitRadius, basePercent, out var expectedPercent);
                target.SetUpgradeStats(stats);
                target.Initialize();
                AssertNear(collider.radius, baseRadius * (1f + expectedPercent / 100f),
                           "판정 반경이 GetStat 을 거치지 않습니다: " + collider.radius);
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

        // ---------------------------------------------------------------- 보너스 배율

        /// <summary>
        /// EconomyManager 의 coin_bonus_multiplier 가 GetStat 을 거치는지 본다.
        ///
        /// 여기만 스텁을 꽂을 수 없다 — EconomyManager 는 주입받는 쪽이 아니라 IUpgradeStats 를
        /// **구현하는 쪽**이라 자기 자신의 업그레이드 레벨로만 움직인다. 그래서 실제 CSV 에
        /// 그 stat 을 올리는 업그레이드가 있어야만 확인할 수 있는데, 지금은 없다.
        /// 건너뛰되 조용히 넘기지 않는다 — 나중에 효과가 추가되면 이 검증이 저절로 켜진다.
        /// </summary>
        private static int RunEconomyChecks(BalanceData balance)
        {
            var owner = FindUpgradeFor(balance, StatId.CoinBonusMultiplier, out var index);
            if (owner == null || owner.MaxLevel < 1)
            {
                Debug.Log("[UpgradeConsumerChecks] coin_bonus_multiplier 를 올리는 업그레이드가 " +
                          "upgrade_effects.csv 에 없어 이 항목은 건너뛴다 (미검증).");
                return 0;
            }

            var checkCount = 0;
            EconomyManager manager = null;
            GameObject host = null;

            try
            {
                manager = CreateManager<EconomyManager>(balance, "UpgradeConsumerEconomy", out host);
                InvokeLifecycle(manager, "Awake");
                InvokeLifecycle(manager, "OnEnable");

                manager.BeginRun();
                GameEvents.PublishTargetBroken(new BreakInfo("consumer-check", 100m, Array.Empty<CoinDrop>(), 0f, Vector3.zero));
                var bare = manager.CurrentCoin;

                // 레벨 복원은 BeginRun 보다 먼저. 실효 배율을 런 시작에 굳힌다.
                var levels = new int[balance.Upgrades.Count];
                levels[index] = Math.Min(3, owner.MaxLevel);
                manager.RestoreUpgradeLevels(levels);
                manager.BeginRun();

                GameEvents.PublishTargetBroken(new BreakInfo("consumer-check", 100m, Array.Empty<CoinDrop>(), 0f, Vector3.zero));
                var raised = manager.CurrentCoin - bare;
                AssertCondition(raised > bare,
                                "보너스 배율 업그레이드가 지급액에 반영되지 않았습니다: " + raised);
                checkCount++;
            }
            finally
            {
                TearDown(manager, host);
            }

            return checkCount;
        }

        // ---------------------------------------------------------------- 자동 망치

        /// <summary>
        /// 자동 망치 보유 수가 GetStat 을 거치는지 본다 (이슈 #258).
        ///
        /// **여기만 덧셈 스텁을 쓴다.** 다른 소비처는 기준값의 StubFactor 배로 재는데,
        /// auto_hammer_count_init 은 0 이고(BALANCE.md 196행, 의도된 값) 0 은 몇 배를 해도 0 이라
        /// 배선 여부를 증명하지 못한다. upgrade_effects.csv 에서도 이 stat 만 add 다.
        /// </summary>
        private static int RunAutoHammerChecks(BalanceData balance)
        {
            var checkCount = 0;
            var baseCount = balance.Economy.AutoHammerCountInit;

            AutoHammerController manager = null;
            GameObject host = null;

            try
            {
                manager = CreateManager<AutoHammerController>(balance, "UpgradeConsumerAutoHammer", out host);

                // 주입이 없으면 CSV 기준값 그대로다. 배선이 빠져도 게임이 돌아가야 한다.
                manager.BeginRun();
                AssertCondition(manager.AutoHammerCount == baseCount,
                                "주입 없이 자동 망치 수가 기준값과 다릅니다: " + manager.AutoHammerCount);
                checkCount++;

                // 업그레이드가 실제로 반영된다.
                manager.SetUpgradeStats(new AddUpgradeStats(StatId.AutoHammerCount, StubBonusCount));
                manager.BeginRun();
                AssertCondition(manager.AutoHammerCount == baseCount + StubBonusCount,
                                "자동 망치 수가 GetStat 을 거치지 않습니다: " + manager.AutoHammerCount);
                checkCount++;

                // 런 도중에 붙어도 이번 런은 그대로다 (BALANCE 6절 "효과는 다음 런부터").
                manager.SetUpgradeStats(new AddUpgradeStats(StatId.AutoHammerCount, StubBonusCount * 2));
                AssertCondition(manager.AutoHammerCount == baseCount + StubBonusCount,
                                "런 도중에 자동 망치 수가 바뀌었습니다. 다음 런부터여야 합니다.");
                manager.BeginRun();
                AssertCondition(manager.AutoHammerCount == baseCount + StubBonusCount * 2,
                                "다음 런에서 자동 망치 수가 갱신되지 않았습니다.");
                checkCount++;
            }
            finally
            {
                TearDown(manager, host);
            }

            return checkCount;
        }

        /// <summary>
        /// 자동 망치 발동 확률이 GetStat 을 거치는지 본다 (이슈 #268, 3.13).
        ///
        /// 퍼크가 확률을 올리려면 이 통로를 타야 한다. 여기가 끊기면 퍼크를 붙여도 확률이
        /// CSV 기준값에서 움직이지 않는데, 발동이 8런에 한 번이라 플레이로는 알아채기 어렵다
        /// — 자동 망치가 #258 전까지 아무도 모르게 죽어 있던 것과 같은 종류의 사고다.
        /// </summary>
        private static int RunAutoHammerProcChecks(BalanceData balance)
        {
            var checkCount = 0;
            var baseChance = balance.Economy.AutoHammerProcChancePercent;

            AutoHammerController manager = null;
            GameObject host = null;

            try
            {
                manager = CreateManager<AutoHammerController>(balance, "UpgradeConsumerProcChance", out host);
                var resolve = typeof(AutoHammerController).GetMethod("ResolveProcChancePercent",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                AssertCondition(resolve != null, "AutoHammerController 의 ResolveProcChancePercent 를 찾지 못했습니다.");

                // 주입이 없으면 CSV 기준값 그대로다.
                AssertNear((float)resolve.Invoke(manager, null), baseChance,
                           "주입 없이 발동 확률이 기준값과 다릅니다.");
                checkCount++;

                // 퍼크가 올린 값이 반영된다.
                manager.SetUpgradeStats(new AddUpgradeStats(StatId.AutoHammerProcChance, StubBonusCount));
                AssertNear((float)resolve.Invoke(manager, null), baseChance + StubBonusCount,
                           "발동 확률이 GetStat 을 거치지 않습니다.");
                checkCount++;

                // 100 을 넘기지 않는다 — 확률이라 넘으면 의미가 없고, 퍼크가 겹치면 넘을 수 있다.
                manager.SetUpgradeStats(new AddUpgradeStats(StatId.AutoHammerProcChance, 500f));
                AssertNear((float)resolve.Invoke(manager, null), 100f,
                           "발동 확률이 100 을 넘었습니다.");
                checkCount++;
            }
            finally
            {
                TearDown(manager, host);
            }

            return checkCount;
        }

        // ---------------------------------------------------------------- 조립

        /// <summary>
        /// 조립 지점이 소비처마다 통로를 실제로 넣는지 원문으로 본다 (이슈 #258).
        ///
        /// 위 검증들은 가짜를 직접 주입해 ManagerBootstrap 을 우회하므로 **배선 누락을 못 본다.**
        /// 자동 망치가 정확히 그 구멍에 빠져 있었다 — 계약도 계산도 맞는데 WireUpgradeStats 가
        /// 이 소비처 하나만 빠뜨려서, 상점에서 살 수는 있지만 게임에는 반영되지 않았다.
        /// 주입이 없어도 기준값으로 조용히 폴백하는 설계라 아무도 알아채지 못했다.
        /// </summary>
        private static int RunBootstrapWiringCheck()
        {
            var bootstrap = File.ReadAllText("Assets/Scripts/Runtime/ManagerBootstrap.cs");
            var consumers = new[] { "StaminaManager", "FeverManager", "CreatureManager", "AutoHammerController" };

            foreach (var consumer in consumers)
            {
                // 정규식 대신 위치로 본다 — 소비처 이름 뒤 가까운 곳에서 주입이 불리면 된다.
                var at = bootstrap.IndexOf(consumer, StringComparison.Ordinal);
                var call = at < 0 ? -1 : bootstrap.IndexOf(".SetUpgradeStats(", at, StringComparison.Ordinal);
                AssertCondition(at >= 0 && call >= 0 && call - at <= 200,
                                "ManagerBootstrap 이 " + consumer + " 에 SetUpgradeStats 를 넣지 않습니다. " +
                                "주입이 없으면 그 소비처만 기준값으로 조용히 떨어집니다 (이슈 #258).");
            }

            return 1;
        }

        // ---------------------------------------------------------------- 다음 런부터 규칙

        /// <summary>
        /// BALANCE 6절 "효과는 다음 런부터". 런 도중에 레벨이 올라도 이번 런의 값은 바뀌지 않는다.
        /// 스태미나 최대치로 대표해서 본다 — 런 시작에 굳히는 stat 이 전부 같은 경로를 탄다.
        /// </summary>
        private static int RunNextRunRuleChecks(BalanceData balance)
        {
            var checkCount = 0;
            var baseMax = balance.Stamina.Max;

            StaminaManager manager = null;
            GameObject host = null;

            try
            {
                manager = CreateManager<StaminaManager>(balance, "UpgradeConsumerNextRun", out host);
                InvokeLifecycle(manager, "OnEnable");

                manager.BeginRun();
                AssertNear(manager.MaxStamina, baseMax, "런 시작 최대치가 기준값과 다릅니다.");

                // 런 도중에 업그레이드가 붙어도 이번 런은 그대로다.
                var stats = CreateStub(StatId.MaxStamina, baseMax, out var expectedMax);
                manager.SetUpgradeStats(stats);
                AssertNear(manager.MaxStamina, baseMax, "런 도중에 최대치가 바뀌었습니다. 다음 런부터여야 합니다.");
                checkCount++;

                // 다음 런에서 반영된다.
                manager.BeginRun();
                AssertNear(manager.MaxStamina, expectedMax, "다음 런에서도 업그레이드가 반영되지 않았습니다.");
                checkCount++;
            }
            finally
            {
                TearDown(manager, host);
            }

            return checkCount;
        }

        // ---------------------------------------------------------------- 보조

        /// <summary>
        /// 그 stat 하나만 기준값의 StubFactor 배로 돌려주는 조회 통로. 나머지 stat 은 기준값 그대로다.
        ///
        /// **실제 업그레이드를 쓰지 않는 이유**: upgrade_effects.csv 가 건드리는 stat 은 일부뿐이라
        /// (max_stamina·fever_gauge_per_hit·coin_bonus_multiplier 는 아직 아무 업그레이드도 올리지 않는다),
        /// 실제 레벨로 재면 그 소비처들은 영영 검증되지 않는다. 여기서 볼 것은 효과의 크기가 아니라
        /// **소비처가 GetStat 을 거치는가**이므로, CSV 내용과 무관한 스텁이 더 정확하다.
        /// 실제 CSV 값으로 도는 경로는 FeverPayoutChecks·CreatureMovementChecks 가 본다.
        /// </summary>
        private static IUpgradeStats CreateStub(StatId stat, float baseValue, out float expected)
        {
            expected = baseValue * StubFactor;
            return new StubUpgradeStats(stat);
        }

        /// <summary>해당 stat 을 올리는 업그레이드를 CSV 에서 찾는다. 없으면 null 이다.</summary>
        private static UpgradeDef FindUpgradeFor(BalanceData balance, StatId stat, out int index)
        {
            for (var i = 0; i < balance.Upgrades.Count; i++)
            {
                foreach (var effect in balance.Upgrades[i].Effects)
                {
                    if (effect.Stat == stat)
                    {
                        index = i;
                        return balance.Upgrades[i];
                    }
                }
            }
            index = -1;
            return null;
        }

        /// <summary>기준값과 확실히 구별되는 배수. 1 이면 통과해도 배선을 증명하지 못한다.</summary>
        private const float StubFactor = 2f;

        /// <summary>기준값이 0 인 stat 에 쓰는 증분. 곱셈으로는 0 과 구별되지 않는다 (#258).</summary>
        private const int StubBonusCount = 3;

        private class StubUpgradeStats : IUpgradeStats
        {
            private readonly StatId _target;

            public StubUpgradeStats(StatId target)
            {
                _target = target;
            }

            public float GetStat(StatId stat, float baseValue)
            {
                return stat == _target ? baseValue * StubFactor : baseValue;
            }
        }

        /// <summary>
        /// 그 stat 하나만 기준값에 고정 증분을 더해 돌려주는 조회 통로 (#258).
        /// 기준값이 0 인 stat 은 곱셈 스텁으로 배선을 증명할 수 없어 이쪽을 쓴다.
        /// </summary>
        private class AddUpgradeStats : IUpgradeStats
        {
            private readonly StatId _target;
            private readonly float _bonus;

            public AddUpgradeStats(StatId target, float bonus)
            {
                _target = target;
                _bonus = bonus;
            }

            public float GetStat(StatId stat, float baseValue)
            {
                return stat == _target ? baseValue + _bonus : baseValue;
            }
        }

        private static string GetFirstTargetId(BalanceData balance)
        {
            AssertCondition(balance.Targets.Count > 0, "targets.csv 에서 읽은 대상이 없습니다.");
            return balance.Targets[0].Id;
        }

        private static void HitUntilFever(FeverManager fever, BalanceData balance)
        {
            var limit = Mathf.CeilToInt(balance.Fever.GaugeMax / balance.Fever.GaugePerHit) + 10;
            for (var i = 0; i < limit && !fever.IsFeverActive; i++)
            {
                GameEvents.PublishSwingResolved(HitSource.Hover, true);
            }
            AssertCondition(fever.IsFeverActive, "적중을 반복해도 피버가 발동하지 않았습니다.");
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
