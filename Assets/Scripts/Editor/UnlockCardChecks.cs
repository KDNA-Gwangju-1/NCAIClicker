using System;
using System.Collections.Generic;
using System.Reflection;
using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Interfaces;
using NCAIClicker.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 새 저금통 해금 카드 검증 (이슈 #300).
    ///
    /// 실제 ResultUI 프리팹을 띄워 정산창을 열고, 회차 누적 수입(EarnedTotal)과 이번 런 수입(RunCoin)만 바꿔 가며
    /// 카드가 **해금이 일어난 정산에서만**, **해금 순서대로**, **정산마다 한 번만** 뜨는지 본다. 기준액은
    /// targets.csv 에서 읽는다 — 값이 바뀌어도 검사는 그대로 맞는다.
    ///
    /// 3D 미리보기는 Edit Mode 에서 모델·카메라를 만들고 Destroy 로 지우려 해서 끊어 두고 본다
    /// (외형은 Play Mode 캡처로 확인한다). 카운트업 연출도 꺼서 카드 판단이 같은 호출 안에서 끝나게 한다.
    /// </summary>
    public static class UnlockCardChecks
    {
        private const string PrefabPath = "Assets/Prefabs/Resources/UI/ResultUI.prefab";
        private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

        public static void RunBatch()
        {
            var checkCount = 0;
            checkCount += RunPrefabChecks();
            checkCount += RunSettlementChecks();
            checkCount += RunShownBandChecks();

            Debug.Log("[UnlockCardChecks] PASS " + checkCount + " checks.");
        }

        private static int RunPrefabChecks()
        {
            var checkCount = 0;
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert(prefab != null, PrefabPath + " 가 없습니다.");
            var controller = prefab.GetComponent<ResultUIController>();
            var card = prefab.GetComponentInChildren<UnlockCardView>(true);
            Assert(card != null, "ResultUI 프리팹에 UnlockCardView 가 없습니다. NCAI > UI > 결과 화면 프리팹 생성을 실행하세요.");
            Assert(ReferenceEquals(Get<UnlockCardView>(controller, "_unlockCard"), card),
                   "ResultUIController._unlockCard 가 프리팹의 카드와 이어져 있지 않습니다.");
            checkCount++;

            // 정산창의 모든 것 위에 덮여야 한다 — 같은 부모의 형제는 계층 순서대로 그려진다.
            var parent = card.transform.parent;
            Assert(parent != null && card.transform.GetSiblingIndex() == parent.childCount - 1,
                   "해금 카드는 PanelRoot 의 마지막 자식이어야 합니다 (다른 칸 뒤에 가려집니다).");
            var cardRoot = Get<GameObject>(card, "_cardRoot");
            Assert(cardRoot != null && !cardRoot.activeSelf, "카드는 꺼진 채 프리팹에 저장돼야 합니다.");
            checkCount++;

            // 정산창의 "다음 해금" 미리보기와 같은 자리에 모델을 두면 서로의 카메라에 찍힌다.
            var cardPreview = new UnityEditor.SerializedObject(Get<CreaturePreview>(card, "_preview"));
            var codexPreview = new UnityEditor.SerializedObject(Get<CreaturePreview>(controller, "_codexPreview"));
            var distance = Vector3.Distance(cardPreview.FindProperty("_stageOrigin").vector3Value,
                                            codexPreview.FindProperty("_stageOrigin").vector3Value);
            Assert(distance > 20f, "해금 카드와 정산창 미리보기의 모델 자리가 너무 가깝습니다: " + distance);

            // 도감(#299) 미리보기들과도 떨어져 있어야 한다 — 카메라 먼 쪽 면(10)의 두 배를 기준으로 본다.
            var cardOrigin = cardPreview.FindProperty("_stageOrigin").vector3Value;
            var codex = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/CreatureCodexPanel.prefab");
            if (codex != null)
            {
                foreach (var codexPreviewComponent in codex.GetComponentsInChildren<CreaturePreview>(true))
                {
                    var codexOrigin = new UnityEditor.SerializedObject(codexPreviewComponent).FindProperty("_stageOrigin").vector3Value;
                    Assert(Vector3.Distance(cardOrigin, codexOrigin) > 20f,
                           "해금 카드 미리보기 자리 " + cardOrigin + " 가 도감 미리보기 " + codexOrigin + " 와 너무 가깝습니다.");
                }
            }
            checkCount++;

            // 구간 행은 액면 종류 수만큼 — 가장 큰 액면으로 묶으니 구간 수가 이를 넘지 않는다.
            var balance = Get<BalanceData>(controller, "_balanceData");
            var rows = Get<GameObject[]>(card, "_bandRows");
            Assert(balance != null && rows != null && rows.Length >= balance.Coins.Count,
                   "코인 구간 행이 액면 종류 수보다 적습니다: " + (rows != null ? rows.Length : 0));
            checkCount++;
            return checkCount;
        }

        private static int RunSettlementChecks()
        {
            var checkCount = 0;
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var host = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
            host.hideFlags = HideFlags.HideAndDontSave;
            var controller = host.GetComponent<ResultUIController>();
            var card = host.GetComponentInChildren<UnlockCardView>(true);
            var balance = Get<BalanceData>(controller, "_balanceData");
            var economy = new FakeEconomyService();

            Set(controller, "_countUpDurationSec", 0f);
            Set(controller, "_codexPreview", null);
            Set(card, "_preview", null);
            controller.SetServices(economy, null, null);
            Invoke(card, "OnEnable");

            var closedCount = 0;
            Action onClosed = () => closedCount++;
            card.Closed += onClosed;

            var cardRoot = Get<GameObject>(card, "_cardRoot");
            var dismiss = Get<Button>(card, "_dismissButton");
            var title = Get<TextMeshProUGUI>(card, "_titleText");

            var unlockables = balance.GetUnlockOrder().FindAll(t => t.UnlockEarned > 0L);
            Assert(unlockables.Count >= 2, "해금되는 종류가 둘 이상이어야 검사할 수 있습니다: " + unlockables.Count);
            var first = unlockables[0];
            var second = unlockables[1];

            try
            {
                // 기준에 못 미친 정산 — 카드 없음
                economy.EarnedTotal = first.UnlockEarned - 1L;
                economy.RunCoin = 10L;
                controller.ShowSettlement();
                Assert(!cardRoot.activeSelf, "해금이 없는 정산에서 카드가 떴습니다.");
                checkCount++;

                // 첫 종류 기준을 넘은 정산 — 그 종류 카드. HP 와 코인 구간은 targets.csv·coins.csv 에서 계산한 값이다.
                economy.EarnedTotal = first.UnlockEarned + 5L;
                economy.RunCoin = 10L;
                controller.ShowSettlement();
                Assert(cardRoot.activeSelf, first.DisplayName + " 기준을 넘은 정산인데 카드가 뜨지 않았습니다.");
                Assert(title.text == first.DisplayName + " 해금!", "카드 제목이 다릅니다: " + title.text);
                Assert(Get<TextMeshProUGUI>(card, "_hpValueText").text == first.Hp.ToString(),
                       "HP 가 targets.csv 의 " + first.Hp + " 가 아닙니다: " + Get<TextMeshProUGUI>(card, "_hpValueText").text);
                AssertBands(card, CoinLottery.GetRewardBands(balance, first.MinDenomId, first.MaxDenomId, first.CoinCount));
                // 역할 문구는 도감(#299)과 같은 한 곳에서 나와야 한다 — 두 화면이 다른 말을 하면 안 된다.
                Assert(Get<TextMeshProUGUI>(card, "_roleText").text == CreatureCodexEntry.GetRoleText(first),
                       "역할 문구가 도감의 GetRoleText 와 다릅니다: " + Get<TextMeshProUGUI>(card, "_roleText").text);
                checkCount++;

                // 클릭하면 닫히고 한 번 알린다. 고지서를 닫고 정산창으로 돌아와도(같은 정산) 다시 뜨지 않는다.
                dismiss.onClick.Invoke();
                Assert(!cardRoot.activeSelf && closedCount == 1, "클릭했는데 카드가 닫히지 않았거나 Closed 가 " + closedCount + "번 불렸습니다.");
                controller.ShowSettlement();
                Assert(!cardRoot.activeSelf, "같은 정산으로 돌아왔는데 카드가 또 떴습니다 — 고지서를 볼 때마다 뜹니다.");
                checkCount++;

                // 한 정산에 둘 — 해금 순서대로 한 장씩, 클릭하면 다음 장, 마지막 장에서 닫힌다.
                closedCount = 0;
                economy.EarnedTotal = second.UnlockEarned;
                economy.RunCoin = second.UnlockEarned - (first.UnlockEarned - 1L);
                controller.ShowSettlement();
                Assert(cardRoot.activeSelf && title.text == first.DisplayName + " 해금!" && card.PendingCount == 1,
                       "두 종류가 해금된 정산은 " + first.DisplayName + " 부터 보여 주고 한 장이 남아야 합니다: " + title.text + " / 남은 " + card.PendingCount);
                dismiss.onClick.Invoke();
                Assert(cardRoot.activeSelf && title.text == second.DisplayName + " 해금!" && closedCount == 0,
                       "클릭하면 다음 장(" + second.DisplayName + ")이 나와야 합니다: " + title.text);
                dismiss.onClick.Invoke();
                Assert(!cardRoot.activeSelf && closedCount == 1, "마지막 장을 클릭하면 닫히고 한 번만 알려야 합니다.");
                checkCount++;

                // 파산으로 누적이 0 이 됐는데 런 수입이 남은 날 — 처음부터 있는 종류가 "해금" 으로 잡히면 안 된다.
                economy.EarnedTotal = 0L;
                economy.RunCoin = 50L;
                controller.ShowSettlement();
                Assert(!cardRoot.activeSelf, "누적이 이번 런 수입보다 작은 정산(파산 뒤)에서 카드가 떴습니다.");
                checkCount++;

                // 파산 화면으로 바뀌면 카드는 남지 않는다.
                economy.EarnedTotal = first.UnlockEarned + 7L;
                economy.RunCoin = 10L;
                controller.ShowSettlement();
                Assert(cardRoot.activeSelf, "파산 화면 검사를 위한 카드가 뜨지 않았습니다.");
                controller.ShowBankruptcy();
                Assert(!cardRoot.activeSelf, "파산 화면으로 바뀌었는데 해금 카드가 남아 있습니다.");
                checkCount++;
            }
            finally
            {
                card.Closed -= onClosed;
                Invoke(card, "OnDisable");
                controller.HideAll();
                UnityEngine.Object.DestroyImmediate(host);
            }

            return checkCount;
        }

        /// <summary>
        /// 사실상 나오지 않는 구간(0.1% 미만)은 줄을 내지 않는다. 금광석처럼 코인이 많은 종류에서 "$40 — 전부 $1" 같은
        /// 줄이 나올 수 있는 결과처럼 보이면 안 된다. 0.1% 이상은 남긴다.
        /// </summary>
        private static int RunShownBandChecks()
        {
            var bands = new List<CoinRewardBand>
            {
                new CoinRewardBand("a", 40, 40, 0.0000000013),
                new CoinRewardBand("b", 44, 200, 0.0015),
                new CoinRewardBand("c", 64, 1000, 0.9985),
            };
            var shown = UnlockCardView.GetShownBands(bands);
            Assert(shown.Count == 2 && shown[0].TopDenomId == "b" && shown[1].TopDenomId == "c",
                   "0.1% 미만 구간만 빠지고 나머지는 순서대로 남아야 합니다: " + shown.Count);
            Assert(UnlockCardView.FormatChance(0.0015) == "<1%" && UnlockCardView.FormatChance(0.445) == "45%",
                   "확률 표기가 다릅니다 (<1%, 반올림 45%).");
            return 1;
        }

        private static void AssertBands(UnlockCardView card, IReadOnlyList<CoinRewardBand> allBands)
        {
            var bands = UnlockCardView.GetShownBands(allBands);
            var rows = Get<GameObject[]>(card, "_bandRows");
            var ranges = Get<TextMeshProUGUI[]>(card, "_bandRangeTexts");
            var chances = Get<TextMeshProUGUI[]>(card, "_bandChanceTexts");
            for (var i = 0; i < rows.Length; i++)
            {
                if (i >= bands.Count)
                {
                    Assert(!rows[i].activeSelf, "구간이 " + bands.Count + "개인데 " + (i + 1) + "번째 행이 보입니다.");
                    continue;
                }
                Assert(rows[i].activeSelf, (i + 1) + "번째 구간 행이 숨어 있습니다.");
                Assert(ranges[i].text == UnlockCardView.FormatRange(bands[i]),
                       (i + 1) + "번째 구간 금액이 다릅니다: " + ranges[i].text + " (기대 " + UnlockCardView.FormatRange(bands[i]) + ")");
                Assert(chances[i].text == UnlockCardView.FormatChance(bands[i].Probability),
                       (i + 1) + "번째 구간 확률이 다릅니다: " + chances[i].text);
            }
        }

        private static T Get<T>(object target, string field)
        {
            return (T)target.GetType().GetField(field, Flags).GetValue(target);
        }

        private static void Set(object target, string field, object value)
        {
            target.GetType().GetField(field, Flags).SetValue(target, value);
        }

        private static void Invoke(object target, string method)
        {
            target.GetType().GetMethod(method, Flags).Invoke(target, null);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("[UnlockCardChecks] " + message);
            }
        }

        /// <summary>정산창이 읽는 값만 둔 가짜. 해금 판정은 EarnedTotal 과 RunCoin 두 값으로 끝난다.</summary>
        private sealed class FakeEconomyService : IEconomyService
        {
            public long CurrentCoin { get; set; }
            public long RunCoin { get; set; }
            public long EarnedTotal { get; set; }
            public IReadOnlyList<CoinDrop> RunCoinBreakdown => Array.Empty<CoinDrop>();
            public void AddCoin(decimal rawAmount) { }
            public void AddLoanPrincipal(long amount) { }
            public bool TrySpendCoin(long amount) => false;
        }
    }
}
