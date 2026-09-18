using System;
using System.Collections.Generic;
using System.Reflection;
using NCAIClicker.Core;
using NCAIClicker.Economy;
using NCAIClicker.Interfaces;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 런 경계(IRunScoped)에 **누가 실제로 걸려 있는지**와 **어떤 순서로 불리는지**를 검증한다 (이슈 #164).
    ///
    /// 4.8 이 드러낸 문제가 이것이다. **한동안** `BillManager` 는 `BeginRun`/`EndRun` 메서드를
    /// 가졌으면서 `IRunScoped` 를 선언하지 않아 `GameManager` 가 모으지 못했고, 그동안
    /// **하루 진행·청구서 발행·대출 징수·파산 판정이 전부 게임에서 실행되지 않았다.**
    /// 각 기능의 Edit Mode 검증은 메서드를 직접 불러 전부 통과했다 — 부르는 주체가 없다는
    /// 것은 아무도 보지 않았다. (지금은 붙어 있다. 이 검증이 다시 빠지는 것을 막는다.)
    ///
    /// 그래서 여기서는 로직이 아니라 **배선**을 본다. 새 매니저가 런 경계에 붙어야 하는데
    /// 빠지면 이 검증이 잡는다.
    ///
    /// 한계: Edit Mode 는 생명주기를 부르지 않아 리플렉션으로 직접 부른다.
    /// </summary>
    public static class RunWiringChecks
    {
        public static void RunBatch()
        {
            var checkCount = 0;
            checkCount += RunMembershipChecks();
            checkCount += RunOrderChecks();
            Debug.Log("[RunWiringChecks] PASS " + checkCount + " checks.");
        }

        // ---------------------------------------------------------------- 누가 걸려 있나

        /// <summary>
        /// 런 경계가 필요한 매니저가 `IRunScoped` 를 구현하는지 본다.
        /// 목록을 코드에 적는 것이 아니라 **BeginRun/EndRun 을 가진 매니저는 전부 걸려야 한다**는
        /// 규칙으로 본다 — 새 매니저가 같은 실수를 하면 자동으로 잡힌다.
        /// </summary>
        private static int RunMembershipChecks()
        {
            var checkCount = 0;
            var runScoped = typeof(IRunScoped);
            var missing = new List<string>();

            foreach (var type in runScoped.Assembly.GetTypes())
            {
                if (!typeof(MonoBehaviour).IsAssignableFrom(type) || type.IsAbstract)
                {
                    continue;
                }
                if (runScoped.IsAssignableFrom(type))
                {
                    continue;
                }

                // IRunScoped 를 구현하지 않으면서 두 메서드를 다 가진 클래스 = 배선에서 빠진 것이다.
                var hasBegin = HasPublicNoArgMethod(type, "BeginRun");
                var hasEnd = HasPublicNoArgMethod(type, "EndRun");
                if (hasBegin && hasEnd)
                {
                    missing.Add(type.Name);
                }
            }

            AssertCondition(missing.Count == 0,
                            "BeginRun/EndRun 을 가졌는데 IRunScoped 를 구현하지 않은 매니저가 있습니다. " +
                            "GameManager 가 모으지 못해 런 경계가 불리지 않습니다: " + string.Join(", ", missing));
            checkCount++;

            // BillManager 는 4.8 에서 실제로 빠져 있던 당사자다. 이름으로 한 번 더 못 박는다.
            AssertCondition(runScoped.IsAssignableFrom(typeof(BillManager)),
                            "BillManager 가 IRunScoped 를 구현하지 않습니다 (#164).");
            checkCount++;

            return checkCount;
        }

        private static bool HasPublicNoArgMethod(Type type, string name)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance,
                                        null, Type.EmptyTypes, null);
            return method != null && method.ReturnType == typeof(void);
        }

        // ---------------------------------------------------------------- 어떤 순서로 불리나

        /// <summary>
        /// `GameManager.GetServiceOrder` 가 매기는 순서를 본다.
        ///
        /// **StageGoalManager 가 BillManager 보다 먼저**여야 한다. EndRun 에서 앞은 목표 달성 시
        /// 단계를 올리고 뒤는 파산 시 0 으로 되돌리는데, 뒤집히면 **파산인데 단계가 올라간다.**
        /// 둘 다 기타 순번이면 동점이라 Array.Sort 가 순서를 보장하지 않는다 — 그 상태를 막는다.
        ///
        /// 초기화 순서의 정본은 ARCHITECTURE "초기화 순서"다.
        /// </summary>
        private static int RunOrderChecks()
        {
            var checkCount = 0;
            var method = typeof(GameManager).GetMethod("GetServiceOrder",
                BindingFlags.NonPublic | BindingFlags.Static);
            AssertCondition(method != null, "GetServiceOrder 를 찾지 못했습니다. 이름이 바뀌었습니까?");

            GameObject host = null;
            try
            {
                host = new GameObject("RunWiringCheckHost")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                host.SetActive(false);

                var economy = GetOrder(method, host.AddComponent<EconomyManager>());
                var stamina = GetOrder(method, host.AddComponent<StaminaManager>());
                var fever = GetOrder(method, host.AddComponent<NCAIClicker.Fever.FeverManager>());
                var stage = GetOrder(method, host.AddComponent<StageGoalManager>());
                var bill = GetOrder(method, host.AddComponent<BillManager>());

                // 코인이 가장 먼저다 — 나머지가 그 위에서 계산한다 (ARCHITECTURE 초기화 순서).
                AssertCondition(economy < stamina && economy < fever && economy < bill,
                                "EconomyManager 가 가장 먼저 오지 않습니다.");
                checkCount++;

                // 이 순서가 이 검증의 핵심이다.
                AssertCondition(stage < bill,
                                "StageGoalManager 가 BillManager 보다 먼저 오지 않습니다. " +
                                "파산이 되돌린 단계를 목표 달성이 다시 올려 버립니다 (#164). " +
                                "stage=" + stage + ", bill=" + bill);
                checkCount++;

                // 동점이면 Array.Sort 가 순서를 보장하지 않으므로 서로 달라야 한다.
                AssertCondition(stage != bill, "두 매니저의 순번이 같아 순서가 보장되지 않습니다.");
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

        private static int GetOrder(MethodInfo getServiceOrder, Component service)
        {
            return (int)getServiceOrder.Invoke(null, new object[] { (IRunScoped)service });
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
