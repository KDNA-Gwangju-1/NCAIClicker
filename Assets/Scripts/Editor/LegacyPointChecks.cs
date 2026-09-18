using System;
using System.Reflection;
using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 레거시 포인트와 반지를 검증한다 (이슈 #175·#183).
    ///
    /// 이 하네스가 지키는 핵심은 하나다 — **파산해도 포인트와 반지 레벨이 남는다.**
    /// 그 보장은 코드 한 줄이 아니라 "회차 초기화 경로가 이 상태를 건드리지 않는다"는 구조에서
    /// 나오므로, 누군가 파산 처리에 초기화를 한 줄 더하면 조용히 깨진다. 여기서 못 박는다.
    ///
    /// 합성 순서(업그레이드 → 반지)는 **실제 CSV 로는 관측되지 않는다.** 반지 효과가 전부 add 라
    /// 순서를 바꿔도 값이 같기 때문이다. 그래서 percent 를 섞은 임시 BalanceData 를 만들어 본다.
    ///
    /// 한계: Edit Mode 는 생명주기를 부르지 않아 Awake/OnEnable/OnDisable 을 리플렉션으로 부른다.
    /// </summary>
    public static class LegacyPointChecks
    {
        public static void RunBatch()
        {
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            AssertCondition(balance != null, "BalanceData 에셋을 찾지 못했습니다.");

            var checkCount = RunDataChecks(balance);
            checkCount += RunAccrualChecks(balance);
            checkCount += RunRingShopChecks(balance);
            checkCount += RunSurvivalChecks(balance);
            checkCount += RunCompositionOrderChecks();
            Debug.Log("[LegacyPointChecks] PASS " + checkCount + " checks.");
        }

        // ---------------------------------------------------------------- 데이터

        private static int RunDataChecks(BalanceData balance)
        {
            var checkCount = 0;

            AssertCondition(balance.Economy.LegacyPointPerAmount > 0f,
                            "economy.csv 의 legacy_point_per_amount 가 0 이하입니다. 적립이 아예 일어나지 않습니다.");
            checkCount++;

            AssertCondition(balance.Rings.Count > 0,
                            "rings.csv 에 반지가 하나도 없습니다. 포인트를 쓸 데가 없습니다.");
            checkCount++;

            foreach (var ring in balance.Rings)
            {
                AssertCondition(ring.Effects.Count > 0,
                                ring.Id + " 에 효과가 없습니다. 포인트만 먹고 아무 일도 하지 않습니다.");
                AssertCondition(ring.InitCost > 0L, ring.Id + " 의 init_cost 가 0 이하입니다.");
                AssertCondition(ring.MaxLevel > 0, ring.Id + " 의 max_level 이 0 이하라 살 수 없습니다.");
            }
            checkCount++;

            // 반지는 정렬 순서대로 저장 배열에 담긴다. 어긋나면 저장이 다른 반지에 실린다.
            for (var i = 1; i < balance.Rings.Count; i++)
            {
                AssertCondition(balance.Rings[i - 1].SortOrder <= balance.Rings[i].SortOrder,
                                "rings.csv 가 sort_order 로 정렬돼 있지 않습니다. 저장 배열이 어긋납니다.");
            }
            checkCount++;

            return checkCount;
        }

        // ---------------------------------------------------------------- 적립

        private static int RunAccrualChecks(BalanceData balance)
        {
            var checkCount = 0;
            GameObject host = null;
            var savedInstance = EconomyManager.Instance;
            var savedShop = EconomyManager.Shop;

            try
            {
                var economy = CreateEconomy(balance, out host);
                InvokeLifecycle(economy, "OnEnable");
                var legacy = (ILegacyService)economy;

                AssertCondition(legacy.CurrentLegacyPoints == 0L, "초기 포인트가 0 이 아닙니다.");
                checkCount++;

                var perPoint = balance.Economy.LegacyPointPerAmount;

                // 계수의 정확히 3배를 내면 3점이어야 한다.
                var amount = (long)(perPoint * 3f);
                GameEvents.PublishBillPaid(new Bill { Amount = amount, IsPaid = true });
                AssertCondition(legacy.CurrentLegacyPoints == 3L,
                                "납부액 " + amount + " 에 3점이 아닙니다: " + legacy.CurrentLegacyPoints);
                checkCount++;

                // 계수보다 작은 금액은 한 점도 쌓이지 않는다 — 내림이고 이월하지 않는다.
                var before = legacy.CurrentLegacyPoints;
                GameEvents.PublishBillPaid(new Bill { Amount = (long)(perPoint / 2f), IsPaid = true });
                AssertCondition(legacy.CurrentLegacyPoints == before,
                                "계수보다 작은 납부에 포인트가 쌓였습니다. 내림 규칙이 깨졌습니다.");
                checkCount++;

                // 나머지를 이월하지 않는다 — 반쪽 납부를 두 번 해도 한 점도 안 쌓인다.
                GameEvents.PublishBillPaid(new Bill { Amount = (long)(perPoint / 2f), IsPaid = true });
                AssertCondition(legacy.CurrentLegacyPoints == before,
                                "소수 나머지가 이월되고 있습니다. 코인과 달리 포인트는 이월하지 않습니다.");
                checkCount++;

                // 소비
                AssertCondition(legacy.TrySpendLegacyPoints(1L), "1점 소비가 실패했습니다.");
                AssertCondition(legacy.CurrentLegacyPoints == 2L, "소비 후 잔액이 다릅니다.");
                AssertCondition(!legacy.TrySpendLegacyPoints(999L), "잔액보다 많이 쓰는 것이 허용됐습니다.");
                AssertCondition(legacy.CurrentLegacyPoints == 2L, "실패한 소비가 잔액을 건드렸습니다.");
                AssertCondition(!legacy.TrySpendLegacyPoints(0L), "0 소비가 성공으로 처리됐습니다.");
                checkCount++;

                // 구독 해제 후에는 적립되지 않는다 (OnEnable/OnDisable 쌍).
                InvokeLifecycle(economy, "OnDisable");
                GameEvents.PublishBillPaid(new Bill { Amount = amount, IsPaid = true });
                AssertCondition(legacy.CurrentLegacyPoints == 2L,
                                "OnDisable 뒤에도 적립됐습니다. 구독이 남아 있습니다.");
                checkCount++;
            }
            finally
            {
                TearDown(host, savedInstance, savedShop);
            }

            return checkCount;
        }

        // ---------------------------------------------------------------- 반지 구매

        private static int RunRingShopChecks(BalanceData balance)
        {
            var checkCount = 0;
            GameObject host = null;
            var savedInstance = EconomyManager.Instance;
            var savedShop = EconomyManager.Shop;

            try
            {
                var economy = CreateEconomy(balance, out host);
                var legacy = (ILegacyService)economy;
                var shop = (IRingShop)economy;
                var ring = balance.Rings[0];

                AssertCondition(!shop.TryPurchaseRing(ring.Id), "포인트 없이 반지를 샀습니다.");
                AssertCondition(shop.GetRingLevel(ring.Id) == 0, "실패한 구매가 레벨을 올렸습니다.");
                checkCount++;

                AssertCondition(shop.GetNextRingCost("no_such_ring") == long.MaxValue,
                                "없는 반지의 비용이 MaxValue 가 아닙니다.");
                AssertCondition(!shop.TryPurchaseRing("no_such_ring"), "없는 반지를 샀습니다.");
                checkCount++;

                var firstCost = shop.GetNextRingCost(ring.Id);
                AssertCondition(firstCost == ring.InitCost,
                                "첫 레벨 비용이 init_cost 와 다릅니다: " + firstCost + " != " + ring.InitCost);
                checkCount++;

                legacy.AddLegacyPoints(10000L);
                var pointsBefore = legacy.CurrentLegacyPoints;
                AssertCondition(shop.TryPurchaseRing(ring.Id), "포인트가 충분한데 구매가 실패했습니다.");
                AssertCondition(shop.GetRingLevel(ring.Id) == 1, "구매 후 레벨이 1 이 아닙니다.");
                AssertCondition(legacy.CurrentLegacyPoints == pointsBefore - firstCost,
                                "차감액이 비용과 다릅니다.");
                checkCount++;

                // 코인은 건드리지 않는다 — 두 화폐가 섞이면 안 된다.
                AssertCondition(((IEconomyService)economy).CurrentCoin == 0L,
                                "반지를 사면서 코인이 움직였습니다.");
                checkCount++;

                // 최대 레벨에서 멈춘다.
                for (var i = 0; i < ring.MaxLevel + 5; i++)
                {
                    shop.TryPurchaseRing(ring.Id);
                }
                AssertCondition(shop.GetRingLevel(ring.Id) == ring.MaxLevel,
                                "최대 레벨을 넘겼습니다: " + shop.GetRingLevel(ring.Id));
                AssertCondition(shop.GetNextRingCost(ring.Id) == long.MaxValue,
                                "최대 레벨인데 비용이 MaxValue 가 아닙니다.");
                checkCount++;
            }
            finally
            {
                TearDown(host, savedInstance, savedShop);
            }

            return checkCount;
        }

        // ---------------------------------------------------------------- 파산 생존

        /// <summary>
        /// **이 하네스의 존재 이유다.** 파산은 IWalletPersistence 로 코인만 비운다 —
        /// 포인트와 반지 레벨이 거기 휩쓸리면 영구 성장이라는 말이 무의미해진다.
        /// </summary>
        private static int RunSurvivalChecks(BalanceData balance)
        {
            var checkCount = 0;
            GameObject host = null;
            var savedInstance = EconomyManager.Instance;
            var savedShop = EconomyManager.Shop;

            try
            {
                var economy = CreateEconomy(balance, out host);
                var legacy = (ILegacyService)economy;
                var shop = (IRingShop)economy;
                var persistence = (ILegacyPersistence)economy;
                var ring = balance.Rings[0];

                legacy.AddLegacyPoints(500L);
                AssertCondition(shop.TryPurchaseRing(ring.Id), "준비 구매가 실패했습니다.");
                var pointsBefore = legacy.CurrentLegacyPoints;
                var levelBefore = shop.GetRingLevel(ring.Id);

                // 파산이 부르는 바로 그 경로다 (BillManager.ResetRound, #158).
                ((IWalletPersistence)economy).RestoreWallet(0L, "0");

                AssertCondition(legacy.CurrentLegacyPoints == pointsBefore,
                                "파산 경로가 레거시 포인트를 지웠습니다. 영구 성장이 사라집니다.");
                AssertCondition(shop.GetRingLevel(ring.Id) == levelBefore,
                                "파산 경로가 반지 레벨을 지웠습니다.");
                checkCount++;

                // 런 경계도 건드리지 않는다.
                ((IRunScoped)economy).BeginRun();
                ((IRunScoped)economy).EndRun();
                AssertCondition(legacy.CurrentLegacyPoints == pointsBefore,
                                "런 경계가 레거시 포인트를 건드렸습니다.");
                AssertCondition(shop.GetRingLevel(ring.Id) == levelBefore,
                                "런 경계가 반지 레벨을 건드렸습니다.");
                checkCount++;

                // 저장 왕복
                var savedLevels = persistence.CurrentRingLevels;
                AssertCondition(savedLevels.Length == balance.Rings.Count,
                                "저장 배열 길이가 반지 수와 다릅니다.");
                persistence.RestoreLegacy(0L, null);
                AssertCondition(legacy.CurrentLegacyPoints == 0L && shop.GetRingLevel(ring.Id) == 0,
                                "null 복원이 초기화하지 않았습니다.");
                persistence.RestoreLegacy(pointsBefore, savedLevels);
                AssertCondition(legacy.CurrentLegacyPoints == pointsBefore &&
                                shop.GetRingLevel(ring.Id) == levelBefore,
                                "저장 왕복이 값을 잃었습니다.");
                checkCount++;

                // 음수 포인트는 0 으로 막는다.
                persistence.RestoreLegacy(-50L, savedLevels);
                AssertCondition(legacy.CurrentLegacyPoints == 0L, "음수 포인트가 그대로 들어왔습니다.");
                checkCount++;
            }
            finally
            {
                TearDown(host, savedInstance, savedShop);
            }

            return checkCount;
        }

        // ---------------------------------------------------------------- 합성 순서

        /// <summary>
        /// **업그레이드 먼저, 반지 나중.** 실제 CSV 는 둘 다 add 라 순서를 바꿔도 값이 같아
        /// 이 규칙이 깨져도 드러나지 않는다. 그래서 percent 를 섞은 임시 데이터로 본다.
        ///
        /// 업그레이드 add +10, 반지 percent +100% 를 같은 스탯에 걸면
        ///   올바른 순서: (1 + 10) × 2 = 22
        ///   뒤집힌 순서: (1 × 2) + 10 = 12
        /// 로 갈린다.
        /// </summary>
        private static int RunCompositionOrderChecks()
        {
            var checkCount = 0;
            GameObject host = null;
            BalanceData fake = null;
            var savedInstance = EconomyManager.Instance;
            var savedShop = EconomyManager.Shop;

            try
            {
                fake = ScriptableObject.CreateInstance<BalanceData>();
                fake.Economy = new EconomyConfig { LegacyPointPerAmount = 50f, UpgradeCostGrowth = 1f };
                fake.Upgrades.Add(new UpgradeDef
                {
                    Id = "u", DisplayName = "u", InitCost = 1L, CostGrowth = 1f, MaxLevel = 1, SortOrder = 1,
                    Effects = { new UpgradeEffect { Stat = StatId.BaseHitPower, Type = EffectType.Add, ValuePerLevel = 10f } },
                });
                fake.Rings.Add(new RingDef
                {
                    Id = "r", DisplayName = "r", InitCost = 1L, CostGrowth = 1f, MaxLevel = 1, SortOrder = 1,
                    Effects = { new UpgradeEffect { Stat = StatId.BaseHitPower, Type = EffectType.Percent, ValuePerLevel = 100f } },
                });

                var economy = CreateEconomy(fake, out host);
                var legacy = (ILegacyService)economy;

                // 업그레이드는 코인으로, 반지는 포인트로 산다 — 두 화폐를 각각 채운다.
                ((IWalletPersistence)economy).RestoreWallet(1000L, "0");
                ((IUpgradeShop)economy).TryPurchase("u");
                legacy.AddLegacyPoints(100L);
                ((IRingShop)economy).TryPurchaseRing("r");

                AssertCondition(((IUpgradeShop)economy).GetLevel("u") == 1, "준비: 업그레이드를 사지 못했습니다.");
                AssertCondition(((IRingShop)economy).GetRingLevel("r") == 1, "준비: 반지를 사지 못했습니다.");
                checkCount++;

                var actual = ((IUpgradeStats)economy).GetStat(StatId.BaseHitPower, 1f);
                AssertNear(actual, 22f,
                           "합성 순서가 뒤집혔습니다. 업그레이드(add) 를 먼저 얹고 반지(percent) 를 " +
                           "나중에 얹어야 22 인데 " + actual + " 입니다. 12 라면 순서가 반대입니다.");
                checkCount++;
            }
            finally
            {
                TearDown(host, savedInstance, savedShop);
                if (fake != null)
                {
                    UnityEngine.Object.DestroyImmediate(fake);
                }
            }

            return checkCount;
        }

        // ---------------------------------------------------------------- 도구

        private static EconomyManager CreateEconomy(BalanceData balance, out GameObject host)
        {
            host = new GameObject("LegacyPointCheckHost") { hideFlags = HideFlags.HideAndDontSave };
            host.SetActive(false);
            var economy = host.AddComponent<EconomyManager>();
            SetPrivate(economy, "_balanceData", balance);
            host.SetActive(true);
            InvokeLifecycle(economy, "Awake");
            return economy;
        }

        /// <summary>
        /// 구독을 거두고 호스트를 없앤 뒤 정적 통로를 원래대로 돌린다.
        /// Awake 가 EconomyManager.Instance·Shop 을 덮어쓰므로 되돌리지 않으면 뒤이어 도는
        /// 하네스가 파괴된 매니저를 잡는다.
        /// </summary>
        private static void TearDown(GameObject host, IEconomyService savedInstance, IUpgradeShop savedShop)
        {
            if (host != null)
            {
                var economy = host.GetComponent<EconomyManager>();
                if (economy != null)
                {
                    InvokeLifecycle(economy, "OnDisable");
                }
                UnityEngine.Object.DestroyImmediate(host);
            }

            SetStatic("Instance", savedInstance);
            SetStatic("Shop", savedShop);
        }

        private static void SetStatic(string propertyName, object value)
        {
            var property = typeof(EconomyManager).GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.Static);
            property?.SetValue(null, value, null);
        }

        private static void InvokeLifecycle(Component component, string methodName)
        {
            var method = component.GetType().GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(method != null, methodName + " 을 찾지 못했습니다.");
            method.Invoke(component, null);
        }

        private static void SetPrivate(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(field != null, fieldName + " 필드를 찾지 못했습니다. 이름이 바뀌었습니까?");
            field.SetValue(target, value);
        }

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
