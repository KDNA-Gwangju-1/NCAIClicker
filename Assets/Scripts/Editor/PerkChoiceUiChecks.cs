using System;
using System.Reflection;
using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using NCAIClicker.UI;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 퍼크 3장 선택 UI 를 검증한다 (이슈 #92).
    ///
    /// 실제 <see cref="BillManager"/> 를 세우고 <c>TryPay</c> 로 후보를 뽑게 한다 — 후보 배열을
    /// 손으로 만들면 <c>TryChoosePerk</c> 가 볼 상태와 UI 가 볼 상태가 갈라져, 정작 검증하려는
    /// "고른 것이 4.2 에 제대로 전달되는가"를 못 본다. 코인 차감만 FakeEconomyService 로 격리한다
    /// (BillManagerChecks 와 같은 패턴).
    ///
    /// **timeScale 을 건드리는 검증이다.** 어디서 실패하든 finally 에서 원래 값으로 되돌린다 —
    /// 0 인 채로 빠져나가면 에디터가 멈춘 것처럼 보인다.
    ///
    /// 한계: Edit Mode 는 생명주기를 부르지 않아 OnEnable/OnDisable 을 리플렉션으로 직접 부른다.
    /// 실제 클릭 대신 카드의 onClick 을 코드로 호출한다 — 버튼 배선 자체는 프리팹 검사로 본다.
    /// </summary>
    public static class PerkChoiceUiChecks
    {
        private const string PanelPath = "Assets/Prefabs/UI/PerkChoicePanel.prefab";
        private const string ManagersPath = "Assets/Prefabs/Resources/Managers.prefab";

        public static void RunBatch()
        {
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            AssertCondition(balance != null, "BalanceData 에셋을 찾지 못했습니다.");
            AssertCondition(balance.Perks.Count >= 3, "perks.csv 행이 3개 미만이라 3장을 제시할 수 없습니다.");

            var checkCount = RunPrefabChecks();
            checkCount += RunEffectTextChecks(balance);
            checkCount += RunFlowChecks(balance);
            Debug.Log("[PerkChoiceUiChecks] PASS " + checkCount + " checks.");
        }

        // ---------------------------------------------------------------- 프리팹 배선

        /// <summary>
        /// 패널과 Managers 프리팹이 실제로 연결돼 있는지 본다. 스크립트가 멀쩡해도 참조가 비어
        /// 있으면 게임에서는 아무것도 안 뜬다 — #164 가 드러낸 "배선은 아무도 안 본다"와 같은 종류다.
        /// </summary>
        private static int RunPrefabChecks()
        {
            var checkCount = 0;

            var panel = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
            AssertCondition(panel != null, "패널 프리팹을 찾지 못했습니다: " + PanelPath);
            checkCount++;

            var cards = panel.GetComponentsInChildren<PerkCardView>(true);
            AssertCondition(cards.Length == 3,
                            "패널의 PerkCardView 가 3장이 아닙니다: " + cards.Length + " (후보는 3장이다)");
            checkCount++;

            foreach (var card in cards)
            {
                AssertCondition(GetPrivate<UnityEngine.Object>(card, "_nameLabel") != null,
                                card.name + " 의 _nameLabel 이 비어 있습니다.");
                AssertCondition(GetPrivate<UnityEngine.Object>(card, "_effectLabel") != null,
                                card.name + " 의 _effectLabel 이 비어 있습니다.");
                AssertCondition(GetPrivate<UnityEngine.Object>(card, "_button") != null,
                                card.name + " 의 _button 이 비어 있습니다 — 눌러도 아무 일도 일어나지 않습니다.");
            }
            checkCount++;

            // **Game 씬에 EventSystem 이 없다.** 없으면 uGUI 가 클릭을 처리하지 않아 카드가
            // 눌리지 않는다 — 화면만 뜨고 영영 못 고른다. 패널이 자기 것을 들고 다녀야 한다.
            var fallback = panel.GetComponentInChildren<UnityEngine.EventSystems.EventSystem>(true);
            AssertCondition(fallback != null,
                            "패널에 예비 EventSystem 이 없습니다. Game 씬에도 없어 카드를 누를 수 없습니다.");
            AssertCondition(!fallback.gameObject.activeSelf,
                            "예비 EventSystem 이 켜진 채로 저장됐습니다. 씬에 이미 있으면 둘이 되어 Unity 가 한쪽을 꺼 버립니다.");
            AssertCondition(
                fallback.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() != null,
                "예비 EventSystem 에 InputSystemUIInputModule 이 없습니다. 이 프로젝트는 새 Input System 을 씁니다.");
            checkCount++;

            // 패널이 HUD 를 덮는지. 같거나 낮으면 HUD 가 카드 위에 그려진다.
            var panelCanvas = panel.GetComponent<Canvas>();
            AssertCondition(panelCanvas != null, "패널에 Canvas 가 없습니다.");
            AssertCondition(panelCanvas.sortingOrder > 0,
                            "패널 Canvas 의 sortingOrder 가 HUD(0) 보다 높지 않습니다: " + panelCanvas.sortingOrder);
            AssertCondition(panel.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null,
                            "패널에 GraphicRaycaster 가 없어 클릭이 카드에 닿지 않습니다.");
            checkCount++;

            var managers = AssetDatabase.LoadAssetAtPath<GameObject>(ManagersPath);
            AssertCondition(managers != null, "Managers 프리팹을 찾지 못했습니다: " + ManagersPath);
            var controller = managers.GetComponent<PerkChoiceController>();
            AssertCondition(controller != null,
                            "Managers 프리팹에 PerkChoiceController 가 없습니다. 게임에서 퍼크 선택이 뜨지 않습니다.");
            checkCount++;

            AssertCondition(GetPrivate<UnityEngine.Object>(controller, "_balanceData") != null,
                            "PerkChoiceController 의 _balanceData 가 비어 있습니다.");
            AssertCondition(GetPrivate<UnityEngine.Object>(controller, "_panelPrefab") != null,
                            "PerkChoiceController 의 _panelPrefab 이 비어 있습니다.");
            checkCount++;

            return checkCount;
        }

        // ---------------------------------------------------------------- 효과 문구

        /// <summary>
        /// 문구의 숫자가 CSV 값에서 나오는지 본다. 코드에 상수로 박히면 CSV 를 고쳐도 카드가
        /// 낡은 값을 보여 준다 (AGENTS.md "같은 숫자를 문서와 CSV 양쪽에 적지 않는다").
        /// </summary>
        private static int RunEffectTextChecks(BalanceData balance)
        {
            var checkCount = 0;

            foreach (var perk in balance.Perks)
            {
                var text = PerkCardView.DescribeEffect(perk);
                AssertCondition(!string.IsNullOrEmpty(text), perk.Id + " 의 효과 문구가 비어 있습니다.");

                // 값이 문구에 실제로 실렸는지 — 포맷은 달라도 숫자는 CSV 에서 와야 한다.
                var valueText = perk.Value.ToString("0.#");
                AssertCondition(text.Contains(valueText),
                                perk.Id + " 의 효과 문구에 CSV 값(" + valueText + ")이 없습니다: " + text);

                if (perk.Type == PerkType.CoinGainBoost)
                {
                    var durationText = perk.DurationSec.ToString("0.#");
                    AssertCondition(text.Contains(durationText),
                                    perk.Id + " 의 문구에 지속시간(" + durationText + ")이 없습니다: " + text);
                }
            }
            checkCount++;

            AssertCondition(PerkCardView.DescribeEffect(null) == string.Empty,
                            "퍼크가 null 일 때 문구가 빈 문자열이 아닙니다.");
            checkCount++;

            return checkCount;
        }

        // ---------------------------------------------------------------- 제시 → 선택 흐름

        private static int RunFlowChecks(BalanceData balance)
        {
            var checkCount = 0;
            var originalTimeScale = Time.timeScale;
            var originalInstance = BillManager.Instance;

            GameObject billHost = null;
            GameObject controllerHost = null;

            try
            {
                var bills = CreateBillManager(balance, out billHost);
                bills.SetEconomyService(new AlwaysPaysEconomyService());
                SetBillManagerInstance(bills);

                var controller = CreateController(balance, out controllerHost);
                InvokeLifecycle(controller, "OnEnable");

                // 납부해야 후보가 나온다. OnPerkOffered 는 TryPay 안에서 발행된다 (#28).
                bills.BeginRun();
                var bill = bills.ActiveBill;
                AssertCondition(bill != null, "청구서가 발행되지 않아 납부할 수 없습니다.");
                AssertCondition(bills.TryPay(bill), "납부가 실패했습니다. FakeEconomy 설정을 확인하십시오.");

                var offered = bills.OfferedPerkIds;
                AssertCondition(offered != null && offered.Length == 3,
                                "후보가 3장이 아닙니다: " + (offered == null ? "null" : offered.Length.ToString()));
                checkCount++;

                // --- 패널이 떴나
                var panel = GetPrivate<GameObject>(controller, "_panelInstance");
                AssertCondition(panel != null, "OnPerkOffered 를 받고도 패널을 만들지 않았습니다.");
                AssertCondition(panel.activeSelf, "패널이 떠 있지 않습니다.");
                checkCount++;

                // --- 시간이 멈췄나 (GDD 6.9절)
                AssertNear(Time.timeScale, 0f,
                           "퍼크 선택 중인데 시간이 멈추지 않았습니다. timeScale=" + Time.timeScale);
                checkCount++;

                // --- 카드가 후보대로 채워졌나
                var cards = panel.GetComponentsInChildren<PerkCardView>(true);
                for (var i = 0; i < offered.Length; i++)
                {
                    AssertCondition(cards[i].PerkId == offered[i],
                                    i + "번 카드의 퍼크가 후보와 다릅니다: " + cards[i].PerkId + " != " + offered[i]);
                    AssertCondition(cards[i].gameObject.activeSelf, i + "번 카드가 숨겨져 있습니다.");
                }
                checkCount++;

                // --- 후보에 없는 id 는 거부되고 패널이 닫히지 않는다
                InvokeCardChosen(controller, "no_such_perk");
                AssertCondition(panel.activeSelf,
                                "후보에 없는 퍼크를 거부하고도 패널이 닫혔습니다. 고를 기회가 사라집니다.");
                AssertNear(Time.timeScale, 0f, "거부 후에 시간이 다시 흐릅니다.");
                AssertCondition(bills.OfferedPerkIds.Length == 3, "거부가 후보 목록을 건드렸습니다.");
                checkCount++;

                // --- 고르면 닫히고 시간이 돌아온다
                var chosenId = offered[1];
                var chosenCount = 0;
                var lastChosen = string.Empty;
                Action<string> onChosen = id => { chosenCount++; lastChosen = id; };
                GameEvents.OnPerkChosen += onChosen;
                try
                {
                    InvokeCardChosen(controller, chosenId);
                }
                finally
                {
                    GameEvents.OnPerkChosen -= onChosen;
                }

                AssertCondition(chosenCount == 1, "OnPerkChosen 이 정확히 1회 발행되지 않았습니다: " + chosenCount);
                AssertCondition(lastChosen == chosenId, "고른 퍼크가 다릅니다: " + lastChosen);
                checkCount++;

                AssertCondition(!panel.activeSelf, "퍼크를 골랐는데 패널이 닫히지 않았습니다.");
                checkCount++;

                AssertNear(Time.timeScale, originalTimeScale,
                           "퍼크를 골랐는데 시간이 원래대로 돌아오지 않았습니다. timeScale=" + Time.timeScale);
                checkCount++;

                AssertCondition(bills.OfferedPerkIds.Length == 0, "고른 뒤에도 후보가 남아 있습니다.");
                checkCount++;

                // --- 고르지 않은 채 비활성화돼도 시간은 돌아온다 (게임이 영구 정지하는 것을 막는다)
                bills.BeginRun();
                var secondBill = bills.ActiveBill;
                AssertCondition(secondBill != null, "두 번째 청구서가 없습니다.");
                AssertCondition(bills.TryPay(secondBill), "두 번째 납부가 실패했습니다.");
                AssertNear(Time.timeScale, 0f, "두 번째 제시에서 시간이 멈추지 않았습니다.");

                InvokeLifecycle(controller, "OnDisable");
                AssertNear(Time.timeScale, originalTimeScale,
                           "고르지 않은 채 비활성화됐는데 시간이 멈춘 채로 남았습니다 — 게임이 영구 정지합니다.");
                AssertCondition(!panel.activeSelf, "비활성화됐는데 패널이 남아 있습니다.");
                checkCount++;

                // --- 다시 켜지면 아직 안 고른 후보를 조회로 집어 온다 (ARCHITECTURE 3절).
                //     OnPerkOffered 는 이미 지나갔으므로 구독만으로는 영영 못 받는다.
                AssertCondition(bills.OfferedPerkIds.Length == 3, "후보가 아직 남아 있어야 합니다.");
                InvokeLifecycle(controller, "OnEnable");
                AssertCondition(panel.activeSelf,
                                "다시 켰는데 남아 있던 후보를 띄우지 않았습니다 — 고를 기회가 사라집니다.");
                AssertNear(Time.timeScale, 0f, "남아 있던 후보를 띄우고도 시간이 멈추지 않았습니다.");
                checkCount++;

                InvokeLifecycle(controller, "OnDisable");
            }
            finally
            {
                // 무슨 일이 있어도 시간은 되돌린다.
                Time.timeScale = originalTimeScale;
                SetBillManagerInstance(originalInstance);
                // **구독도 반드시 거둔다.** 중간 단언이 실패하면 성공 경로의 OnDisable 을
                // 지나치는데, GameEvents 는 정적이라 파괴된 컨트롤러의 핸들러가 남아
                // 뒤이어 도는 검증까지 오염시킨다.
                TearDownController(controllerHost);
                DestroyHost(billHost);
            }

            return checkCount;
        }

        // ---------------------------------------------------------------- 도구

        private static BillManager CreateBillManager(BalanceData balance, out GameObject host)
        {
            host = new GameObject("PerkChoiceBillHost") { hideFlags = HideFlags.HideAndDontSave };
            host.SetActive(false);
            var manager = host.AddComponent<BillManager>();
            SetPrivate(manager, "_balanceData", balance);
            host.SetActive(true);
            return manager;
        }

        private static PerkChoiceController CreateController(BalanceData balance, out GameObject host)
        {
            host = new GameObject("PerkChoiceControllerHost") { hideFlags = HideFlags.HideAndDontSave };
            host.SetActive(false);
            var controller = host.AddComponent<PerkChoiceController>();
            SetPrivate(controller, "_balanceData", balance);
            SetPrivate(controller, "_panelPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath));
            host.SetActive(true);
            return controller;
        }

        /// <summary>카드를 누른 것과 같은 경로를 탄다. 실제 클릭은 Edit Mode 에서 일으킬 수 없다.</summary>
        private static void InvokeCardChosen(PerkChoiceController controller, string perkId)
        {
            var method = typeof(PerkChoiceController).GetMethod("HandleCardChosen",
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(method != null, "HandleCardChosen 을 찾지 못했습니다. 이름이 바뀌었습니까?");
            method.Invoke(controller, new object[] { perkId });
        }

        private static void SetBillManagerInstance(IBillService service)
        {
            var property = typeof(BillManager).GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static);
            AssertCondition(property != null, "BillManager.Instance 를 찾지 못했습니다.");
            property.SetValue(null, service, null);
        }

        private static void InvokeLifecycle(Component component, string methodName)
        {
            var method = component.GetType().GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(method != null, methodName + " 을 찾지 못했습니다.");
            method.Invoke(component, null);
        }

        private static T GetPrivate<T>(object target, string fieldName) where T : class
        {
            var field = target.GetType().GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(field != null, fieldName + " 필드를 찾지 못했습니다. 이름이 바뀌었습니까?");
            return field.GetValue(target) as T;
        }

        private static void SetPrivate(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(field != null, fieldName + " 필드를 찾지 못했습니다. 이름이 바뀌었습니까?");
            field.SetValue(target, value);
        }

        /// <summary>구독을 거둔 뒤 파괴한다. 순서가 뒤집히면 해제할 대상이 이미 없다.</summary>
        private static void TearDownController(GameObject host)
        {
            if (host == null)
            {
                return;
            }
            var controller = host.GetComponent<PerkChoiceController>();
            if (controller != null)
            {
                InvokeLifecycle(controller, "OnDisable");
            }
            DestroyHost(host);
        }

        private static void DestroyHost(GameObject host)
        {
            if (host != null)
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>코인 차감만 격리한다. 납부는 항상 성공시켜 퍼크 제시까지 도달하게 한다.</summary>
        private class AlwaysPaysEconomyService : IEconomyService
        {
            public long CurrentCoin => 0L;

            public long RunCoin => 0L;

            public void AddCoin(decimal rawAmount)
            {
            }

            public void AddLoanPrincipal(long amount)
            {
            }

            public bool TrySpendCoin(long amount)
            {
                return true;
            }
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
