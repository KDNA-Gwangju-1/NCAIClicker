using System.Collections.Generic;
using System.Reflection;
using NCAIClicker.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// ResultUI 프리팹을 생성하는 에디터 헬퍼 (이슈 #34).
    ///
    /// 레이아웃은 좌표를 하나씩 미는 대신 LayoutGroup 으로 짠다. 정산 명세는 행이 여섯 줄이라
    /// 손으로 anchoredPosition 을 밀면 한 줄만 늘어도 아래 전부를 다시 계산해야 한다.
    ///
    /// 프리팹은 Resources 아래 **한 벌만** 저장한다. 두 경로에 저장하면 인스펙터에서 고친 쪽과
    /// 런타임이 읽는 쪽이 갈라진다 (실제로 그렇게 갈라져 있었다).
    /// </summary>
    public static class ResultUIPrefabCreator
    {
        private const string FontPath = "Assets/Materials/Fonts/NanumGothicBoldSDF.asset";
        private const string ResourceDir = "Assets/Prefabs/Resources/UI";
        private const string ResultPrefabPath = ResourceDir + "/ResultUI.prefab";

        // 1920x1080 기준. 타이틀 세이프 90% — 바깥 5% 는 디스플레이가 잘라먹을 수 있다.
        private static readonly Vector2 SafeInset = new Vector2(96f, 54f);

        private static readonly Color Cream = new Color(0.992f, 0.953f, 0.874f);
        private static readonly Color Parchment = new Color(0.894f, 0.827f, 0.706f);
        private static readonly Color Muted = new Color(0.769f, 0.694f, 0.573f);
        private static readonly Color Gold = new Color(1f, 0.816f, 0.478f);
        private static readonly Color Loss = new Color(1f, 0.553f, 0.478f);
        private static readonly Color PanelFill = new Color(0.027f, 0.016f, 0.016f, 0.94f);
        private static readonly Color PanelLine = new Color(0.227f, 0.165f, 0.11f);
        // 알파로 "살짝 밝은 회색" 을 만들면 뒤에 무엇이 깔리느냐에 따라 결과가 달라진다.
        // 실제로 의도(흰색 7%)보다 훨씬 밝게 나왔다. 톤을 직접 지정해 대비를 고정한다.
        // 기준: 본문 4.5:1, 행 구분 같은 비텍스트 요소 3:1 (WCAG 1.4.3 / 1.4.11).
        private static readonly Color RowFill = new Color(0.102f, 0.082f, 0.071f);
        private static readonly Color LossRowFill = new Color(0.216f, 0.063f, 0.055f);
        private static readonly Color NetRowFill = new Color(0.208f, 0.157f, 0.071f);

        [MenuItem("NCAI/UI/결과 화면 프리팹 생성")]
        public static void CreatePrefab()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

            var root = CreateStretchedObject("ResultUI", null);
            var controller = root.AddComponent<ResultUIController>();

            var panelRoot = CreateStretchedObject("PanelRoot", root);
            var panelImage = panelRoot.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.85f);

            var settlement = BuildSettlement(panelRoot, font, out var bound);
            bound["_balanceData"] = AssetDatabase.LoadAssetAtPath<NCAIClicker.Data.BalanceData>(
                "Assets/GameData/Generated/BalanceData.asset");
            var bankruptcy = BuildBankruptcy(panelRoot, font, out var bankruptcyBound);

            // 우상단은 현재 보유액 자리다 (원작). 메인 메뉴는 좌하단으로 작게 물린다.
            // 글자만 두면 뒤의 3D 책상과 겹쳐 읽히지 않는다 — 원작처럼 배경 상자에 담는다.
            var balanceBox = CreateObject("BalanceBox", panelRoot);
            var balanceBoxRect = balanceBox.GetComponent<RectTransform>();
            balanceBoxRect.anchorMin = new Vector2(1f, 1f);
            balanceBoxRect.anchorMax = new Vector2(1f, 1f);
            balanceBoxRect.pivot = new Vector2(1f, 1f);
            balanceBoxRect.sizeDelta = new Vector2(280f, 76f);
            balanceBoxRect.anchoredPosition = new Vector2(-SafeInset.x, -SafeInset.y);
            balanceBox.AddComponent<Image>().color = new Color(0.192f, 0.145f, 0.106f, 0.96f);
            var balanceOutline = balanceBox.AddComponent<Outline>();
            balanceOutline.effectColor = new Color(0.42f, 0.33f, 0.21f);
            balanceOutline.effectDistance = new Vector2(2f, -2f);

            var balanceLabel = CreateLabel("BalanceText", balanceBox, font, 40, Gold, TextAlignmentOptions.Center, "$0");
            var balanceRect = balanceLabel.GetComponent<RectTransform>();
            balanceRect.anchorMin = Vector2.zero;
            balanceRect.anchorMax = Vector2.one;
            balanceRect.offsetMin = new Vector2(16f, 0f);
            balanceRect.offsetMax = new Vector2(-16f, 0f);
            bound["_balanceText"] = balanceLabel;

            var mainMenuButton = CreateButton("MainMenuButton", panelRoot, font, new Vector2(180f, 48f), "메인 메뉴",
                new Color(0.18f, 0.16f, 0.14f), new Color(0.36f, 0.33f, 0.29f), Parchment, 22);
            var mainMenuRect = mainMenuButton.GetComponent<RectTransform>();
            // 좌하단. 스태미나 HUD 와 겹치지 않도록 한 칸 띄운다.
            mainMenuRect.anchorMin = Vector2.zero;
            mainMenuRect.anchorMax = Vector2.zero;
            mainMenuRect.pivot = Vector2.zero;
            mainMenuRect.anchoredPosition = new Vector2(SafeInset.x, SafeInset.y + 70f);

            bound["_panelRoot"] = panelRoot;

            bound["_settlementContainer"] = settlement;
            bound["_bankruptcyContainer"] = bankruptcy;
            bound["_mainMenuButton"] = mainMenuButton;
            foreach (var pair in bankruptcyBound)
            {
                bound[pair.Key] = pair.Value;
            }

            Bind(controller, bound);
            SavePrefab(root, ResultPrefabPath);
        }

        /// <summary>정산 화면. 좌측 명세 / 우측 집계 두 컬럼에 하단 액션 행 (원작 구조).</summary>
        private static GameObject BuildSettlement(GameObject parent, TMP_FontAsset font, out Dictionary<string, object> bound)
        {
            bound = new Dictionary<string, object>();

            // 원작은 화면 가운데 떠 있는 카드다 — 폭 62%, 높이 48% (1920x1080 기준 실측).
            // 세이프존을 다 채우면 뒤의 책상이 가려지고 글자만 커 보인다.
            var settlement = CreateObject("SettlementContainer", parent);
            var settlementRect = settlement.GetComponent<RectTransform>();
            settlementRect.anchorMin = new Vector2(0.5f, 0.5f);
            settlementRect.anchorMax = new Vector2(0.5f, 0.5f);
            settlementRect.pivot = new Vector2(0.5f, 0.5f);
            settlementRect.sizeDelta = new Vector2(1200f, 880f);
            settlementRect.anchoredPosition = Vector2.zero;

            var column = settlement.AddComponent<VerticalLayoutGroup>();
            column.childAlignment = TextAnchor.UpperCenter;
            column.spacing = 18f;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;

            // 머리글: 큰 제목 한 줄과 그 아래 얇은 메타 한 줄.
            var header = CreateVertical("Header", settlement, 4f);
            SetPreferredHeight(header, 118f);
            bound["_titleText"] = CreateLabel("TitleText", header, font, 72, Cream, TextAlignmentOptions.Center, "지친 손!");

            var meta = CreateHorizontal("MetaRow", header, 18f);
            SetPreferredHeight(meta, 30f);
            bound["_dayText"] = CreateLabel("DayText", meta, font, 24, Cream, TextAlignmentOptions.Center, "DAY 1");
            bound["_billStatusText"] = CreateLabel("BillStatusText", meta, font, 24, Muted, TextAlignmentOptions.Center, "고지서: 납부 완료");
            bound["_stageGoalText"] = CreateLabel("StageGoalText", meta, font, 24, Muted, TextAlignmentOptions.Center, "단계 목표: 미달성");

            // 본문 두 컬럼.
            var columns = CreateHorizontal("ColumnsRow", settlement, 28f);
            // 남는 폭을 균등 분배하면 우측 집계가 좌측 명세만큼 넓어진다. 주인공은 좌측이다.
            columns.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            SetPreferredHeight(columns, 560f);

            // 좌 53 : 우 47 (원작 실측 1.12:1). preferredWidth 를 한쪽에만 주면
            // 남는 폭이 그쪽으로 몰린다 — 실제로 우측이 1.8 배가 됐었다.
            var ledger = CreatePanel("LedgerPanel", columns, 20f, 8f);
            SetPreferredWidth(ledger, 0f);
            SetFlexibleWidth(ledger, 1.12f);
            bound["_accuracyText"] = CreateStatRow("AccuracyRow", ledger, font, "정확도:", "71%", RowFill, Cream, 40);
            bound["_runCoinText"] = CreateStatRow("CoinCountRow", ledger, font, "코인:", "102", RowFill, Cream, 40);
            bound["_denomCountTexts"] = CreateDenomRow(ledger, font);
            bound["_grossText"] = CreateStatRow("GrossRow", ledger, font, "합계:", ResultUIController.UnwiredPlaceholder, RowFill, Cream, 40);
            bound["_feeText"] = CreateStatRow("LoanCutRow", ledger, font, "빅 토니 징수", ResultUIController.UnwiredPlaceholder, LossRowFill, Loss, 38);
            bound["_loanCutRow"] = ledger.transform.Find("LoanCutRow").gameObject;
            bound["_loanCutLabelText"] = ledger.transform.Find("LoanCutRow/LabelText").GetComponent<TextMeshProUGUI>();
            bound["_netText"] = CreateStatRow("NetRow", ledger, font, "내 몫:", ResultUIController.UnwiredPlaceholder, NetRowFill, Gold, 48);

            var side = CreateVertical("SideColumn", columns, 18f);
            SetPreferredWidth(side, 0f);
            SetFlexibleWidth(side, 1f);

            var broken = CreatePanel("BrokenPanel", side, 22f, 14f);
            SetPreferredHeight(broken, 200f);
            bound["_brokenCountText"] = CreateStatRow("BrokenHeaderRow", broken, font, "박살낸 저금통:", ResultUIController.UnwiredPlaceholder, Color.clear, Cream, 40);
            bound["_brokenChipTexts"] = CreateBrokenChipRow(broken, font);

            var codex = CreatePanel("CodexPanel", side, 18f, 10f);
            SetFlexibleHeight(codex, 1f);
            var codexIcon = CreateObject("CodexIcon", codex);
            var codexImage = codexIcon.AddComponent<Image>();
            codexImage.color = RowFill;
            SetPreferredHeight(codexIcon, 190f);
            bound["_codexProgressText"] = CreateLabel("CodexProgressText", codex, font, 28, Muted, TextAlignmentOptions.Center, ResultUIController.UnwiredPlaceholder);
            CreateLabel("CodexCaptionText", codex, font, 20, Muted, TextAlignmentOptions.Center, "다음 저금통 해금까지");

            // 하단 액션.
            // 원작 버튼 행은 패널 폭의 63% 를 쓰고 가운데 모인다. 끝까지 늘이면 버튼이 배너가 된다.
            var actions = CreateHorizontal("ActionRow", settlement, 24f);
            var actionsGroup = actions.GetComponent<HorizontalLayoutGroup>();
            actionsGroup.childAlignment = TextAnchor.UpperCenter;
            actionsGroup.childForceExpandWidth = false;
            SetPreferredHeight(actions, 140f);

            // 원작 버튼 둘은 폭이 거의 같다 (365 / 362). flexibleWidth 를 0 으로 못 박지 않으면
            // 남는 폭이 이쪽으로 몰려 버튼 하나만 배너처럼 늘어난다.
            // 원작 정산창은 버튼이 넷이다 — 업그레이드 / 고지서 / 계속 / 도박.
            // 정산창이 하루의 끝이자 다음 하루의 관문이라 여기서 갈라진다. 도박만 MVP 밖이다.
            var upgradeButton = CreateButton("UpgradeButton", actions, font, new Vector2(300f, 104f), "업그레이드",
                new Color(0.08f, 0.06f, 0.05f), new Color(0.36f, 0.27f, 0.15f), Gold, 30);
            SetFlexibleWidth(upgradeButton.gameObject, 0f);
            bound["_upgradeButton"] = upgradeButton;

            var payColumn = CreateVertical("PayColumn", actions, 8f);
            SetPreferredWidth(payColumn, 340f);
            SetFlexibleWidth(payColumn, 0f);
            // 동작이 없는 버튼은 프리팹 단계에서 잠근다. 런타임에 끄면 첫 프레임에 눌릴 수 있다.
            // 버튼 안에 두 줄이 들어간다 — 금액이 크게, 남은 일수가 그 아래 작게 (원작).
            var payButton = CreateButton("PayButton", payColumn, font, new Vector2(340f, 104f), string.Empty,
                new Color(0.49f, 0.12f, 0.1f), new Color(0.7f, 0.25f, 0.21f), new Color(1f, 0.86f, 0.83f), 34);
            payButton.interactable = false;
            bound["_payButton"] = payButton;

            var payTextRoot = CreateVertical("PayLabels", payButton.gameObject, 0f);
            var payTextRect = payTextRoot.GetComponent<RectTransform>();
            payTextRect.anchorMin = Vector2.zero;
            payTextRect.anchorMax = Vector2.one;
            payTextRect.offsetMin = new Vector2(8f, 8f);
            payTextRect.offsetMax = new Vector2(-8f, -8f);
            payTextRoot.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;

            bound["_payButtonLabel"] = CreateLabel("PayAmountText", payTextRoot, font, 38,
                new Color(1f, 0.86f, 0.83f), TextAlignmentOptions.Center, "$0");
            bound["_payDaysLeftText"] = CreateLabel("PayDaysLeftText", payTextRoot, font, 24,
                new Color(0.85f, 0.62f, 0.55f), TextAlignmentOptions.Center, "");
            bound["_payCaptionText"] = CreateLabel("PayCaptionText", payColumn, font, 22, Muted, TextAlignmentOptions.Center, "준비 중");

            var continueButton = CreateButton("ContinueButton", actions, font, new Vector2(300f, 104f), "계속",
                new Color(0.11f, 0.31f, 0.45f), new Color(0.24f, 0.51f, 0.71f), new Color(0.9f, 0.95f, 0.98f), 32);
            SetFlexibleWidth(continueButton.gameObject, 0f);
            bound["_continueButton"] = continueButton;

            return settlement;
        }

        /// <summary>파산 화면. 이번 이슈의 범위가 아니라 기존 구성을 그대로 옮겼다.</summary>
        private static GameObject BuildBankruptcy(GameObject parent, TMP_FontAsset font, out Dictionary<string, object> bound)
        {
            bound = new Dictionary<string, object>();

            var bankruptcy = CreateStretchedObject("BankruptcyContainer", parent);
            var rect = bankruptcy.GetComponent<RectTransform>();
            rect.offsetMin = SafeInset;
            rect.offsetMax = -SafeInset;

            var column = bankruptcy.AddComponent<VerticalLayoutGroup>();
            column.spacing = 22f;
            column.childAlignment = TextAnchor.MiddleCenter;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;

            bound["_bankruptcyTitleText"] = CreateLabel("BankruptcyTitleText", bankruptcy, font, 80, new Color(0.85f, 0.2f, 0.16f), TextAlignmentOptions.Center, "파산");
            bound["_bankruptcyDetailText"] = CreateLabel("BankruptcyDetailText", bankruptcy, font, 30, Cream, TextAlignmentOptions.Center, "고지서 미납으로 파산하였습니다.");
            bound["_bankruptcyCoinLossText"] = CreateLabel("BankruptcyCoinLossText", bankruptcy, font, 24, Loss, TextAlignmentOptions.Center, "보유 코인이 몰수되며 1일차부터 다시 시작합니다.");
            bound["_restartButton"] = CreateButton("RestartButton", bankruptcy, font, new Vector2(320f, 96f), "1일차 재시작",
                new Color(0.49f, 0.12f, 0.1f), new Color(0.7f, 0.25f, 0.21f), new Color(1f, 0.86f, 0.83f), 30);

            return bankruptcy;
        }

        private static TextMeshProUGUI[] CreateDenomRow(GameObject parent, TMP_FontAsset font)
        {
            var row = CreateHorizontal("DenomRow", parent, 10f);
            SetPreferredHeight(row, 60f);
            var image = row.AddComponent<Image>();
            image.color = RowFill;

            var worths = new[] { "$1", "$5", "$25", "$100" };
            var labels = new TextMeshProUGUI[worths.Length];
            for (var i = 0; i < worths.Length; i++)
            {
                var chip = CreateHorizontal($"DenomChip{i}", row, 8f);
                chip.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
                labels[i] = CreateLabel("CountText", chip, font, 30, Parchment, TextAlignmentOptions.Right, ResultUIController.UnwiredPlaceholder);
                CreateLabel("WorthText", chip, font, 20, Muted, TextAlignmentOptions.Left, worths[i]);
            }
            return labels;
        }

        private static TextMeshProUGUI[] CreateBrokenChipRow(GameObject parent, TMP_FontAsset font)
        {
            var row = CreateHorizontal("BrokenChipRow", parent, 18f);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            SetPreferredHeight(row, 54f);

            var labels = new TextMeshProUGUI[3];
            for (var i = 0; i < labels.Length; i++)
            {
                labels[i] = CreateLabel($"BrokenChip{i}", row, font, 28, Parchment, TextAlignmentOptions.Center, ResultUIController.UnwiredPlaceholder);
            }
            return labels;
        }

        /// <summary>라벨은 왼쪽, 값은 오른쪽. 원작 명세의 정렬이 이 한 쌍에서 나온다.</summary>
        private static TextMeshProUGUI CreateStatRow(string name, GameObject parent, TMP_FontAsset font, string label, string value, Color fill, Color valueColor, int valueSize)
        {
            var row = CreateHorizontal(name, parent, 16f);
            row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(18, 18, 6, 6);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            SetPreferredHeight(row, 62f);

            if (fill.a > 0f)
            {
                row.AddComponent<Image>().color = fill;
            }

            var labelText = CreateLabel("LabelText", row, font, 32, Parchment, TextAlignmentOptions.Left, label);
            SetFlexibleWidth(labelText.gameObject, 1f);

            var valueText = CreateLabel("ValueText", row, font, valueSize, valueColor, TextAlignmentOptions.Right, value);
            SetPreferredWidth(valueText.gameObject, 240f);
            return valueText;
        }

        // --- 조립 헬퍼 -------------------------------------------------------

        private static GameObject CreateObject(string name, GameObject parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null)
            {
                go.transform.SetParent(parent.transform, false);
            }
            return go;
        }

        private static GameObject CreateStretchedObject(string name, GameObject parent)
        {
            var go = CreateObject(name, parent);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return go;
        }

        private static GameObject CreateVertical(string name, GameObject parent, float spacing)
        {
            var go = CreateObject(name, parent);
            var group = go.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            return go;
        }

        private static GameObject CreateHorizontal(string name, GameObject parent, float spacing)
        {
            var go = CreateObject(name, parent);
            var group = go.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = true;
            return go;
        }

        private static GameObject CreatePanel(string name, GameObject parent, float padding, float spacing)
        {
            var go = CreateVertical(name, parent, spacing);
            var group = go.GetComponent<VerticalLayoutGroup>();
            var pad = Mathf.RoundToInt(padding);
            group.padding = new RectOffset(pad, pad, pad, pad);

            var image = go.AddComponent<Image>();
            image.color = PanelFill;
            return go;
        }

        private static TextMeshProUGUI CreateLabel(string name, GameObject parent, TMP_FontAsset font, int fontSize, Color color, TextAlignmentOptions alignment, string text)
        {
            var go = CreateObject(name, parent);
            var label = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                label.font = font;
            }
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = alignment;
            label.text = text;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        private static Button CreateButton(string name, GameObject parent, TMP_FontAsset font, Vector2 size, string label, Color fill, Color line, Color textColor, int fontSize)
        {
            var go = CreateObject(name, parent);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;

            var image = go.AddComponent<Image>();
            image.color = fill;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = line;
            outline.effectDistance = new Vector2(2f, -2f);

            var button = go.AddComponent<Button>();
            SetPreferredWidth(go, size.x);
            SetPreferredHeight(go, size.y);

            var text = CreateLabel("Text", go, font, fontSize, textColor, TextAlignmentOptions.Center, label);
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            return button;
        }

        private static LayoutElement EnsureLayoutElement(GameObject go)
        {
            var element = go.GetComponent<LayoutElement>();
            return element != null ? element : go.AddComponent<LayoutElement>();
        }

        private static void SetPreferredWidth(GameObject go, float value) => EnsureLayoutElement(go).preferredWidth = value;

        private static void SetPreferredHeight(GameObject go, float value) => EnsureLayoutElement(go).preferredHeight = value;

        private static void SetFlexibleWidth(GameObject go, float value) => EnsureLayoutElement(go).flexibleWidth = value;

        private static void SetFlexibleHeight(GameObject go, float value) => EnsureLayoutElement(go).flexibleHeight = value;

        /// <summary>직렬화 필드는 private 이라 리플렉션으로 넣는다. 이름이 틀리면 조용히 비니까 검증에서 잡는다.</summary>
        private static void Bind(ResultUIController controller, Dictionary<string, object> bound)
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var type = typeof(ResultUIController);

            foreach (var pair in bound)
            {
                var field = type.GetField(pair.Key, flags);
                if (field == null)
                {
                    Debug.LogError($"[ResultUIPrefabCreator] {pair.Key} 필드가 없다. 이름이 바뀌었는지 확인하라.");
                    continue;
                }
                field.SetValue(controller, pair.Value);
            }
        }

        private static void SavePrefab(GameObject root, string path)
        {
            System.IO.Directory.CreateDirectory(ResourceDir);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh();
            Debug.Log("[ResultUIPrefabCreator] 프리팹 생성 완료: " + path);
        }
    }
}
