using System;
using System.Reflection;
using NCAIClicker.Core;
using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using NCAIClicker.Fever;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 피버 발동이 실제 코인 지급에 반영되는지를 **두 매니저를 함께 돌려** 검증한다 (이슈 #32).
    ///
    /// FeverChecks 는 게이지가 차면 OnFeverStart 를 쏘는지까지만 보고, EconomyManagerChecks 는
    /// 그 이벤트를 손으로 쏜 뒤 배율을 본다. 둘 다 절반씩이라 "적중을 쌓았더니 코인이 실제로
    /// 몇 배가 되더라"는 확인이 어디에도 없었다. #32 의 완료 기준이 그 문장이라 파일을 따로 두었다.
    /// FeverChecks 에 합치지 않은 것은 그쪽이 Fever 네임스페이스만 보는 파일이기 때문이다.
    ///
    /// 한계: Edit Mode 는 Awake/OnEnable/Update 를 부르지 않으므로 리플렉션으로 직접 부른다
    /// (FeverChecks·EconomyManagerChecks 와 같은 제약).
    /// </summary>
    public static class FeverPayoutChecks
    {
        /// <summary>파괴 1회의 원시 보상. 밸런스 수치가 아니라 검증용 입력이다.</summary>
        private const decimal RawCoin = 100m;

        public static void RunBatch()
        {
            // 기대값을 코드에 적지 않는다. CSV 를 고치면 이 검증도 같이 따라가야 한다 (AGENTS.md).
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            AssertCondition(balance != null, "BalanceData 에셋을 찾지 못했습니다.");

            var checkCount = 0;
            checkCount += RunPayoutChecks(balance);
            checkCount += RunUpgradeChecks(balance);
            checkCount += RunStaminaChecks(balance);
            Debug.Log("[FeverPayoutChecks] PASS " + checkCount + " checks.");
        }

        // ---------------------------------------------------------------- 발동 → 배율

        private static int RunPayoutChecks(BalanceData balance)
        {
            var checkCount = 0;
            var multiplier = balance.Fever.CoinMultiplier;
            var durationSec = balance.Fever.DurationSec;

            var endCount = 0;
            Action onEnd = () => endCount++;

            EconomyManager economy = null;
            GameObject economyHost = null;
            FeverManager fever = null;
            GameObject feverHost = null;

            // 정리는 전부 finally 에 둔다. 중간에 실패해서 구독이 남으면 다음 실행에서
            // 매니저가 둘이 되어 같은 파괴에 코인이 두 배로 들어간다.
            try
            {
                GameEvents.OnFeverEnd += onEnd;
                economy = CreateEconomy(balance, out economyHost);
                fever = CreateFever(balance, out feverHost);
                economy.BeginRun();
                fever.BeginRun();

                var expectedNet = 0m;

                // 피버 밖에서는 배율이 걸리지 않는다.
                Break(economy, balance, ref expectedNet, 1f, "피버 밖인데 배율이 걸렸습니다.");
                checkCount++;

                // 적중을 쌓아 발동시킨다. **이벤트를 손으로 쏘지 않는 것**이 이 검증의 핵심이다.
                HitUntilFever(fever, balance);
                Break(economy, balance, ref expectedNet, multiplier, "발동했는데 배율이 걸리지 않았습니다.");
                checkCount++;

                // 지속 시간이 끝나기 직전까지는 배율이 유지된다.
                Tick(fever, durationSec * 0.9f);
                AssertCondition(fever.IsFeverActive, "지속 시간 안인데 피버가 이미 끝났습니다.");
                Break(economy, balance, ref expectedNet, multiplier, "지속 시간 안인데 배율이 풀렸습니다.");
                checkCount++;

                // 지속 시간이 지나면 풀린다. 종료는 한 번만 나간다.
                Tick(fever, durationSec);
                AssertCondition(!fever.IsFeverActive, "지속 시간이 지났는데 피버가 유지됩니다.");
                AssertCondition(endCount == 1, "종료 발행이 한 번이 아닙니다: " + endCount);
                Break(economy, balance, ref expectedNet, 1f, "피버가 끝났는데 배율이 남았습니다.");
                checkCount++;

                // 두 번째 발동도 같은 배율이다. 겹쳐 곱해지면 여기서 어긋난다.
                HitUntilFever(fever, balance);
                Break(economy, balance, ref expectedNet, multiplier, "2회차 발동의 배율이 다릅니다.");
                checkCount++;

                // 피버 중에 런이 끊겨도 배율이 켜진 채로 남지 않는다 (소진·파산으로 끝나는 경우).
                fever.EndRun();
                AssertCondition(endCount == 2, "EndRun 이 종료를 알리지 않았습니다: " + endCount);
                Break(economy, balance, ref expectedNet, 1f, "EndRun 후에도 배율이 남았습니다.");
                checkCount++;

                // 생성된 에셋은 읽기만 한다. 원본을 고치면 다음 임포트까지 값이 어긋난다.
                AssertCondition(!EditorUtility.IsDirty(balance), "BalanceData 가 수정됐습니다.");
                checkCount++;
            }
            finally
            {
                GameEvents.OnFeverEnd -= onEnd;
                TearDown(fever, feverHost);
                TearDown(economy, economyHost);
            }

            return checkCount;
        }

        // ---------------------------------------------------------------- 업그레이드 반영

        /// <summary>
        /// fever_multiplier 업그레이드가 지급액에 반영되는지 본다 (BALANCE 6절).
        /// 같은 업그레이드의 피버 **지속 시간** 쪽은 통로가 없어 여기서 보지 못한다 (#116).
        /// </summary>
        private static int RunUpgradeChecks(BalanceData balance)
        {
            var checkCount = 0;
            var owner = FindUpgradeFor(balance, StatId.FeverMultiplier, out var index);
            AssertCondition(owner != null,
                "upgrade_effects.csv 에 fever_multiplier 를 올리는 업그레이드가 없습니다. " +
                "없앤 것이라면 EconomyManager.GetFeverMultiplier 의 업그레이드 조회도 함께 걷어내야 합니다.");
            AssertCondition(owner.MaxLevel >= 1, owner.Id + " 의 max_level 이 0 이라 효과를 확인할 수 없습니다.");

            EconomyManager economy = null;
            GameObject economyHost = null;
            FeverManager fever = null;
            GameObject feverHost = null;

            try
            {
                economy = CreateEconomy(balance, out economyHost);
                fever = CreateFever(balance, out feverHost);
                economy.BeginRun();
                fever.BeginRun();

                // 레벨을 코드에 적지 않고 상한 안에서 고른다.
                var level = Math.Min(3, owner.MaxLevel);
                var levels = new int[balance.Upgrades.Count];
                levels[index] = level;
                economy.RestoreUpgradeLevels(levels);

                var raised = economy.GetStat(StatId.FeverMultiplier, balance.Fever.CoinMultiplier);
                AssertCondition(raised > balance.Fever.CoinMultiplier,
                                owner.Id + " 레벨 " + level + " 인데 실효 배율이 오르지 않았습니다: " + raised);
                checkCount++;

                // 업그레이드는 피버 밖 지급액을 건드리지 않는다. 배율은 피버에만 걸린다.
                var expectedNet = 0m;
                Break(economy, balance, ref expectedNet, 1f, "피버 밖인데 업그레이드가 지급액을 바꿨습니다.");
                checkCount++;

                // 발동하면 CSV 원본이 아니라 실효 배율로 지급된다.
                HitUntilFever(fever, balance);
                Break(economy, balance, ref expectedNet, raised, "업그레이드가 피버 지급액에 반영되지 않았습니다.");
                checkCount++;

                // 레벨을 되돌리면 배율도 원본으로 돌아간다.
                // 피버는 지속 시간을 넘겨 끝낸다 — EndRun 으로 끝내면 런까지 멈춰 다시 채울 수 없다.
                Tick(fever, balance.Fever.DurationSec * 2f);
                AssertCondition(!fever.IsFeverActive, "지속 시간이 지났는데 피버가 유지됩니다.");
                economy.RestoreUpgradeLevels(new int[balance.Upgrades.Count]);
                HitUntilFever(fever, balance);
                Break(economy, balance, ref expectedNet, balance.Fever.CoinMultiplier,
                      "레벨을 0 으로 되돌렸는데 배율이 원본과 다릅니다.");
                checkCount++;
            }
            finally
            {
                TearDown(fever, feverHost);
                TearDown(economy, economyHost);
            }

            return checkCount;
        }

        // ---------------------------------------------------------------- 스태미나 불간섭

        /// <summary>
        /// 피버 중에도 스태미나 감소 속도가 같은지 본다. 이슈 #32 가 못 박은 조건이다 —
        /// 피버 중 감소를 늦추면 런 길이가 들쭉날쭉해져 시간 제한 설계와 충돌한다.
        /// 지금은 StaminaManager 가 피버 이벤트를 구독하지 않아 구조적으로 지켜지지만,
        /// 나중에 누가 구독을 붙이면 조용히 깨지므로 여기서 잡는다.
        /// </summary>
        private static int RunStaminaChecks(BalanceData balance)
        {
            var checkCount = 0;

            StaminaManager stamina = null;
            GameObject staminaHost = null;
            FeverManager fever = null;
            GameObject feverHost = null;

            try
            {
                stamina = CreateStamina(balance, out staminaHost);
                fever = CreateFever(balance, out feverHost);
                stamina.BeginRun();
                fever.BeginRun();

                var before = stamina.CurrentStamina;
                Tick(stamina, 1f);
                var calmDrain = before - stamina.CurrentStamina;
                AssertCondition(calmDrain > 0f, "피버 밖인데 스태미나가 줄지 않습니다.");

                HitUntilFever(fever, balance);

                before = stamina.CurrentStamina;
                Tick(stamina, 1f);
                var feverDrain = before - stamina.CurrentStamina;
                AssertNear(feverDrain, calmDrain,
                           "피버 중 스태미나 감소 속도가 달라졌습니다: " + feverDrain + " vs " + calmDrain);
                checkCount++;
            }
            finally
            {
                TearDown(fever, feverHost);
                TearDown(stamina, staminaHost);
            }

            return checkCount;
        }

        // ---------------------------------------------------------------- 보조

        /// <summary>
        /// 파괴 한 번을 알리고 지갑 잔액이 기대한 배율을 따랐는지 본다.
        /// 소수 잔여가 회차 안에서 이월되므로 한 번의 지급액이 아니라 누계로 비교한다
        /// — CoinWallet 이 잔액을 만드는 방식과 같다.
        ///
        /// 대출 징수항은 일부러 뺐다. IBillService 를 붙이지 않아 징수율이 0 이고,
        /// 징수가 섞인 식은 CoinWalletChecks 가 본다. 여기서 볼 것은 피버 배율뿐이다.
        /// </summary>
        private static void Break(EconomyManager economy, BalanceData balance, ref decimal expectedNet,
                                  float feverMultiplier, string message)
        {
            expectedNet += RawCoin * (decimal)feverMultiplier * (decimal)balance.Economy.CoinBonusMultiplier;
            GameEvents.PublishTargetBroken(new BreakInfo("fever-payout-check", RawCoin, 0f, Vector3.zero));

            var expected = (long)decimal.Floor(expectedNet);
            AssertCondition(economy.CurrentCoin == expected,
                            message + " 기대 잔액 " + expected + ", 실제 " + economy.CurrentCoin);
        }

        /// <summary>발동할 때까지 적중시킨다. 필요한 횟수를 코드에 적지 않기 위한 보조다.</summary>
        private static void HitUntilFever(FeverManager fever, BalanceData balance)
        {
            var limit = Mathf.CeilToInt(balance.Fever.GaugeMax / balance.Fever.GaugePerHit) + 10;
            for (var i = 0; i < limit && !fever.IsFeverActive; i++)
            {
                GameEvents.PublishSwingResolved(HitSource.Hover, true);
            }
            AssertCondition(fever.IsFeverActive, "적중을 반복해도 피버가 발동하지 않았습니다.");
        }

        /// <summary>해당 stat 을 올리는 업그레이드를 CSV 에서 찾는다. 종류가 바뀌어도 검증이 따라가게 한다.</summary>
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

        private static EconomyManager CreateEconomy(BalanceData balance, out GameObject host)
        {
            // Awake 까지 불러야 UpgradeState 가 만들어진다.
            var manager = CreateManager<EconomyManager>(balance, "FeverPayoutCheckEconomy", out host);
            InvokeLifecycle(manager, "Awake");
            InvokeLifecycle(manager, "OnEnable");
            return manager;
        }

        private static FeverManager CreateFever(BalanceData balance, out GameObject host)
        {
            var manager = CreateManager<FeverManager>(balance, "FeverPayoutCheckFever", out host);
            InvokeLifecycle(manager, "OnEnable");
            return manager;
        }

        private static StaminaManager CreateStamina(BalanceData balance, out GameObject host)
        {
            var manager = CreateManager<StaminaManager>(balance, "FeverPayoutCheckStamina", out host);
            InvokeLifecycle(manager, "OnEnable");
            return manager;
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
            var field = typeof(T).GetField("_balanceData", BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(field != null,
                            typeof(T).Name + " 의 _balanceData 필드를 찾지 못했습니다. 이름이 바뀌었습니까?");
            field.SetValue(manager, balance);
            host.SetActive(true);
            return manager;
        }

        /// <summary>구독을 먼저 풀고 오브젝트를 지운다. 순서를 바꾸면 해제 대상이 이미 파괴돼 있다.</summary>
        private static void TearDown(Component manager, GameObject host)
        {
            if (manager != null)
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
