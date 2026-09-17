using System;
using System.Reflection;
using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 업그레이드의 계산(UpgradeState)과 구매(EconomyManager)를 검증한다.
    /// 계산 규칙의 정본은 docs/BALANCE.md 6절이다.
    ///
    /// 한계: Edit Mode 에서는 Unity 가 Awake/OnEnable 을 부르지 않으므로 직접 부른다
    /// (EconomyManagerChecks·StaminaChecks 와 같은 제약).
    ///
    /// **효과가 실제 게임에 반영되는지는 이 검증이 보지 않는다.** 소비처가 실효값을 읽을
    /// 통로가 아직 없기 때문이다 (이슈 #116). 여기서는 계산과 구매까지만 본다.
    /// </summary>
    public static class UpgradeChecks
    {
        public static void RunBatch()
        {
            // 기대값을 코드에 적지 않는다. CSV 를 고치면 이 검증도 같이 따라가야 한다 (AGENTS.md).
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            AssertCondition(balance != null, "BalanceData 에셋을 찾지 못했습니다.");
            AssertCondition(balance.Upgrades.Count > 0, "upgrades.csv 에서 읽은 업그레이드가 없습니다.");

            var checkCount = 0;
            checkCount += RunStateChecks(balance);
            checkCount += RunPurchaseChecks(balance);
            Debug.Log("[UpgradeChecks] PASS " + checkCount + " checks.");
        }

        // ---------------------------------------------------------------- 계산부

        private static int RunStateChecks(BalanceData balance)
        {
            var checkCount = 0;
            var state = new UpgradeState(balance);

            // 저장 배열과 길이가 맞아야 SaveData.UpgradeLevels 와 자리가 어긋나지 않는다.
            AssertCondition(state.Count == balance.Upgrades.Count, "레벨 배열 길이가 업그레이드 수와 다릅니다.");
            checkCount++;

            // 시작은 전부 0 레벨이고, 그때 비용은 초기 비용 그대로다 (growth^0 = 1).
            foreach (var def in balance.Upgrades)
            {
                AssertCondition(state.GetLevel(def.Id) == 0, def.Id + " 의 시작 레벨이 0 이 아닙니다.");
                AssertCondition(state.TryGetNextCost(def.Id, out var cost), def.Id + " 의 비용을 구하지 못했습니다.");
                AssertCondition(cost == def.InitCost,
                                def.Id + " 의 초기 비용이 CSV 와 다릅니다: " + cost + " != " + def.InitCost);
            }
            checkCount++;

            // 레벨이 오르면 ceil(InitCost x growth^level) 을 따른다.
            // cost_growth 가 0 인 종류는 economy.csv 의 공통 성장률을 쓴다.
            var sample = balance.Upgrades[0];
            var growth = sample.CostGrowth > 0f ? sample.CostGrowth : balance.Economy.UpgradeCostGrowth;
            state.LevelUp(sample.Id);
            AssertCondition(state.GetLevel(sample.Id) == 1, "레벨업이 반영되지 않았습니다.");
            AssertCondition(state.TryGetNextCost(sample.Id, out var nextCost), "다음 비용을 구하지 못했습니다.");
            var expected = (long)Math.Ceiling(sample.InitCost * Math.Pow(growth, 1));
            AssertCondition(nextCost == expected,
                            "레벨 1 비용이 공식과 다릅니다: " + nextCost + " != " + expected);
            AssertCondition(nextCost > sample.InitCost, "비용이 레벨에 따라 오르지 않습니다.");
            checkCount++;

            // 최대 레벨에서는 더 살 수 없고, 억지로 올리면 막는다.
            var capped = new UpgradeState(balance);
            for (var i = 0; i < sample.MaxLevel; i++)
            {
                capped.LevelUp(sample.Id);
            }
            AssertCondition(capped.IsMaxLevel(sample.Id), "최대 레벨 판정이 되지 않습니다.");
            AssertCondition(!capped.TryGetNextCost(sample.Id, out _), "최대 레벨인데 비용이 나옵니다.");
            AssertThrows<InvalidOperationException>(() => capped.LevelUp(sample.Id),
                                                    "최대 레벨에서 레벨업이 막히지 않았습니다.");
            checkCount++;

            // 없는 id 는 조용히 통과시키지 않는다.
            AssertCondition(new UpgradeState(balance).GetLevel("없는_업그레이드") == 0, "없는 id 의 레벨이 0 이 아닙니다.");
            AssertCondition(!state.TryGetNextCost("없는_업그레이드", out _), "없는 id 에서 비용이 나옵니다.");
            AssertThrows<ArgumentException>(() => state.LevelUp("없는_업그레이드"),
                                            "없는 id 의 레벨업이 막히지 않았습니다.");
            checkCount++;

            // 레벨 0 이면 어떤 stat 도 기준값 그대로다.
            var zero = new UpgradeState(balance);
            foreach (StatId stat in Enum.GetValues(typeof(StatId)))
            {
                AssertNear(zero.GetStat(stat, 10f), 10f, "레벨 0 인데 " + stat + " 가 기준값과 다릅니다.");
            }
            checkCount++;

            checkCount += RunEffectChecks(balance);
            return checkCount;
        }

        /// <summary>
        /// CSV 에 실제로 적힌 효과로 계산식을 확인한다.
        /// 어떤 업그레이드가 어떤 stat 을 건드리는지는 CSV 에서 찾아 쓰고 코드에 적지 않는다.
        /// </summary>
        private static int RunEffectChecks(BalanceData balance)
        {
            var checkCount = 0;

            // add 효과: 기준값에 (레벨당 값 x 레벨) 을 더한다.
            var add = FindEffect(balance, EffectType.Add, out var addOwner);
            AssertCondition(add != null, "upgrade_effects.csv 에 add 효과가 하나도 없습니다.");
            var addState = new UpgradeState(balance);
            // max_level 이 낮아져도 따라가도록 상한을 CSV 에서 받는다.
            var addLevel = Math.Min(3, addOwner.MaxLevel);
            for (var i = 0; i < addLevel; i++)
            {
                addState.LevelUp(addOwner.Id);
            }
            var addBase = 10f;
            AssertNear(addState.GetStat(add.Stat, addBase), addBase + add.ValuePerLevel * addLevel,
                       "add 효과 누적이 다릅니다: " + addState.GetStat(add.Stat, addBase));
            checkCount++;

            // percent 효과: 기준값에 비율을 곱한다. 음수면 줄어든다.
            var percent = FindEffect(balance, EffectType.Percent, out var percentOwner);
            AssertCondition(percent != null, "upgrade_effects.csv 에 percent 효과가 하나도 없습니다.");
            var percentState = new UpgradeState(balance);
            var percentLevel = Math.Min(2, percentOwner.MaxLevel);
            for (var i = 0; i < percentLevel; i++)
            {
                percentState.LevelUp(percentOwner.Id);
            }
            var percentBase = 10f;
            var percentExpected = percentBase * (1f + percent.ValuePerLevel * percentLevel / 100f);
            AssertNear(percentState.GetStat(percent.Stat, percentBase), percentExpected,
                       "percent 효과 누적이 다릅니다: " + percentState.GetStat(percent.Stat, percentBase));
            checkCount++;

            // 음수 percent 가 값을 줄인다 (재등장 대기 단축처럼).
            var negative = FindNegativePercent(balance, out var negativeOwner);
            if (negative != null)
            {
                var negativeState = new UpgradeState(balance);
                negativeState.LevelUp(negativeOwner.Id);
                AssertCondition(negativeState.GetStat(negative.Stat, 10f) < 10f,
                                "음수 percent 인데 값이 줄지 않았습니다.");
                checkCount++;
            }

            // 한 업그레이드가 stat 둘을 건드려도 서로 섞이지 않는다.
            var multi = FindMultiEffectUpgrade(balance);
            if (multi != null)
            {
                var multiState = new UpgradeState(balance);
                multiState.LevelUp(multi.Id);
                foreach (var effect in multi.Effects)
                {
                    var actual = multiState.GetStat(effect.Stat, 10f);
                    var want = effect.Type == EffectType.Add
                        ? 10f + effect.ValuePerLevel
                        : 10f * (1f + effect.ValuePerLevel / 100f);
                    AssertNear(actual, want, multi.Id + " 의 " + effect.Stat + " 계산이 다릅니다: " + actual);
                }
                checkCount++;
            }

            // 저장 왕복.
            var save = new UpgradeState(balance);
            save.LevelUp(balance.Upgrades[0].Id);
            var saved = save.ToArray();
            var loaded = new UpgradeState(balance);
            loaded.RestoreLevels(saved);
            AssertCondition(loaded.GetLevel(balance.Upgrades[0].Id) == 1, "복원된 레벨이 다릅니다.");
            checkCount++;

            // 길이가 짧거나 null 이어도 던지지 않고, 겹치는 만큼만 채운다.
            // 길이 판정은 Count 를 아는 호출측이 한다.
            loaded.RestoreLevels(new int[] { 1 });
            AssertCondition(loaded.GetLevel(balance.Upgrades[0].Id) == 1, "짧은 배열의 앞부분이 복원되지 않았습니다.");
            if (balance.Upgrades.Count > 1)
            {
                AssertCondition(loaded.GetLevel(balance.Upgrades[1].Id) == 0, "짧은 배열의 뒷부분이 0 이 아닙니다.");
            }
            loaded.RestoreLevels(null);
            foreach (var def in balance.Upgrades)
            {
                AssertCondition(loaded.GetLevel(def.Id) == 0, "null 복원 후 레벨이 0 이 아닙니다: " + def.Id);
            }
            AssertCondition(loaded.Count == balance.Upgrades.Count, "Count 가 업그레이드 수와 다릅니다.");
            checkCount++;

            // 손상된 저장값을 그대로 믿지 않는다.
            var clamp = new UpgradeState(balance);
            var broken = new int[balance.Upgrades.Count];
            broken[0] = balance.Upgrades[0].MaxLevel + 999;
            if (broken.Length > 1)
            {
                broken[1] = -5;
            }
            clamp.RestoreLevels(broken);
            AssertCondition(clamp.GetLevel(balance.Upgrades[0].Id) == balance.Upgrades[0].MaxLevel,
                            "최대 레벨을 넘는 저장값이 잘리지 않았습니다.");
            if (broken.Length > 1)
            {
                AssertCondition(clamp.GetLevel(balance.Upgrades[1].Id) == 0, "음수 저장값이 0 으로 잘리지 않았습니다.");
            }
            checkCount++;

            // 내부 배열을 그대로 넘기면 밖에서 레벨을 바꿀 수 있다.
            var copy = save.ToArray();
            copy[0] = 99;
            AssertCondition(save.GetLevel(balance.Upgrades[0].Id) == 1, "ToArray 가 내부 배열을 그대로 넘겼습니다.");
            checkCount++;

            // 생성된 에셋은 읽기만 한다. 업그레이드가 원본을 고치면 다음 임포트까지 값이 어긋난다.
            AssertCondition(!EditorUtility.IsDirty(balance), "BalanceData 가 수정됐습니다. 원본을 고치면 안 됩니다.");
            checkCount++;

            return checkCount;
        }

        // ---------------------------------------------------------------- 구매

        private static int RunPurchaseChecks(BalanceData balance)
        {
            var checkCount = 0;
            var balanceChangedCount = 0;
            Action<long> onBalanceChanged = _ => balanceChangedCount++;

            EconomyManager manager = null;
            GameObject host = null;

            // 정리는 전부 finally 에 둔다. 구독이 남으면 다음 실행에서 코인이 두 배로 들어간다.
            try
            {
                GameEvents.OnBalanceChanged += onBalanceChanged;
                manager = CreateManager(balance, out host);

                var def = balance.Upgrades[0];
                AssertCondition(manager.TryGetUpgradeCost(def.Id, out var cost), "비용을 구하지 못했습니다.");

                // 코인이 모자라면 사지 못하고 **레벨도 오르지 않는다**.
                manager.AddLoanPrincipal(cost - 1);
                balanceChangedCount = 0;
                AssertCondition(!manager.TryPurchaseUpgrade(def.Id), "코인이 부족한데 구매가 됐습니다.");
                AssertCondition(manager.GetUpgradeLevel(def.Id) == 0, "실패했는데 레벨이 올랐습니다.");
                AssertCondition(manager.CurrentCoin == cost - 1, "실패했는데 잔액이 줄었습니다.");
                AssertCondition(balanceChangedCount == 0, "실패했는데 잔액 변경이 발행됐습니다.");
                checkCount++;

                // 딱 맞게 채우면 산다. 잔액은 정확히 비용만큼만 줄어든다.
                manager.AddLoanPrincipal(1);
                balanceChangedCount = 0;
                AssertCondition(manager.TryPurchaseUpgrade(def.Id), "코인이 충분한데 구매가 실패했습니다.");
                AssertCondition(manager.GetUpgradeLevel(def.Id) == 1, "구매 후 레벨이 1 이 아닙니다.");
                AssertCondition(manager.CurrentCoin == 0L, "잔액이 비용과 다르게 줄었습니다: " + manager.CurrentCoin);
                AssertCondition(balanceChangedCount == 1, "잔액 변경 발행이 한 번이 아닙니다: " + balanceChangedCount);
                checkCount++;

                // 레벨이 오르면 다음 비용도 오른다.
                AssertCondition(manager.TryGetUpgradeCost(def.Id, out var nextCost), "다음 비용을 구하지 못했습니다.");
                AssertCondition(nextCost > cost, "레벨이 올랐는데 비용이 그대로입니다.");
                checkCount++;

                // 구매가 실효값에 반영된다.
                var effect = def.Effects[0];
                var raised = manager.GetUpgradedStat(effect.Stat, 10f);
                AssertCondition(Mathf.Abs(raised - 10f) > 0.0001f,
                                "구매했는데 실효값이 기준값 그대로입니다: " + raised);
                checkCount++;

                // 최대 레벨까지 사면 더 살 수 없다.
                for (var i = manager.GetUpgradeLevel(def.Id); i < def.MaxLevel; i++)
                {
                    AssertCondition(manager.TryGetUpgradeCost(def.Id, out var stepCost), "비용을 구하지 못했습니다.");
                    manager.AddLoanPrincipal(stepCost);
                    AssertCondition(manager.TryPurchaseUpgrade(def.Id), "최대 레벨 전인데 구매가 실패했습니다.");
                }
                AssertCondition(manager.IsUpgradeMaxLevel(def.Id), "최대 레벨 판정이 되지 않습니다.");
                manager.AddLoanPrincipal(1000000L);
                AssertCondition(!manager.TryPurchaseUpgrade(def.Id), "최대 레벨인데 구매가 됐습니다.");
                checkCount++;

                // 저장 왕복.
                var levels = manager.CurrentUpgradeLevels;
                AssertCondition(levels.Length == balance.Upgrades.Count, "저장 배열 길이가 다릅니다.");
                AssertCondition(levels[0] == def.MaxLevel, "저장 배열에 레벨이 반영되지 않았습니다.");
                manager.RestoreUpgradeLevels(new int[balance.Upgrades.Count]);
                AssertCondition(manager.GetUpgradeLevel(def.Id) == 0, "복원 후 레벨이 0 이 아닙니다.");
                checkCount++;

                // 없는 id 로 사려 하면 코인이 빠지지 않는다.
                var before = manager.CurrentCoin;
                AssertCondition(!manager.TryPurchaseUpgrade("없는_업그레이드"), "없는 id 로 구매가 됐습니다.");
                AssertCondition(manager.CurrentCoin == before, "없는 id 구매에서 코인이 빠졌습니다.");
                checkCount++;
            }
            finally
            {
                GameEvents.OnBalanceChanged -= onBalanceChanged;
                if (manager != null)
                {
                    InvokeLifecycle(manager, "OnDisable");
                }
                if (host != null)
                {
                    UnityEngine.Object.DestroyImmediate(host);
                }
            }

            return checkCount;
        }

        /// <summary>
        /// 비활성 상태로 만들어 컴포넌트를 붙이고 BalanceData 를 넣은 뒤 생명주기를 부른다.
        /// Awake 까지 불러야 UpgradeState 가 만들어진다.
        /// </summary>
        private static EconomyManager CreateManager(BalanceData balance, out GameObject host)
        {
            host = new GameObject("UpgradeCheckEconomy")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            host.SetActive(false);
            var manager = host.AddComponent<EconomyManager>();
            var field = typeof(EconomyManager).GetField("_balanceData",
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(field != null, "_balanceData 필드를 찾지 못했습니다. 이름이 바뀌었습니까?");
            field.SetValue(manager, balance);
            host.SetActive(true);
            InvokeLifecycle(manager, "Awake");
            InvokeLifecycle(manager, "OnEnable");
            return manager;
        }

        private static void InvokeLifecycle(EconomyManager manager, string methodName)
        {
            var method = typeof(EconomyManager).GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(method != null, methodName + " 을 찾지 못했습니다. 이름이 바뀌었습니까?");
            method.Invoke(manager, null);
        }

        private static UpgradeEffect FindEffect(BalanceData balance, EffectType type, out UpgradeDef owner)
        {
            foreach (var def in balance.Upgrades)
            {
                foreach (var effect in def.Effects)
                {
                    if (effect.Type == type)
                    {
                        owner = def;
                        return effect;
                    }
                }
            }
            owner = null;
            return null;
        }

        private static UpgradeEffect FindNegativePercent(BalanceData balance, out UpgradeDef owner)
        {
            foreach (var def in balance.Upgrades)
            {
                foreach (var effect in def.Effects)
                {
                    if (effect.Type == EffectType.Percent && effect.ValuePerLevel < 0f)
                    {
                        owner = def;
                        return effect;
                    }
                }
            }
            owner = null;
            return null;
        }

        /// <summary>stat 을 둘 이상 건드리는 업그레이드. 없으면 null 이다.</summary>
        private static UpgradeDef FindMultiEffectUpgrade(BalanceData balance)
        {
            foreach (var def in balance.Upgrades)
            {
                if (def.Effects.Count > 1)
                {
                    return def;
                }
            }
            return null;
        }

        private static void AssertThrows<T>(Action action, string message) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }
            throw new InvalidOperationException(message);
        }

        /// <summary>
        /// 부동소수 비교. Mathf.Approximately 는 허용 오차가 값 크기에 비례해 너무 좁아,
        /// 같은 값을 다른 순서로 계산하면 실패한다.
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
