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
    /// 고지서 패널 프리팹을 만든다 (이슈 #34).
    ///
    /// 원작은 화면 가운데 세로로 긴 종이 한 장이고, 그 아래에 버튼이 쌓인다.
    /// 우측 가장자리에 "파산 선고" 탭이 붙는다. 실측 비율은 종이 폭 33% / 높이 72%.
    /// </summary>
    public static class BillPanelPrefabCreator
    {
        private const string FontPath = "Assets/Materials/Fonts/NanumGothicBoldSDF.asset";
        private const string PrefabPath = "Assets/Prefabs/Resources/UI/BillPanel.prefab";

        private static readonly Color Paper = new Color(0.925f, 0.867f, 0.749f);
        private static readonly Color Ink = new Color(0.141f, 0.11f, 0.078f);
        private static readonly Color InkSoft = new Color(0.322f, 0.267f, 0.192f);
        private static readonly Color InkFaint = new Color(0.482f, 0.42f, 0.31f);
        private static readonly Color Warn = new Color(0.62f, 0.184f, 0.133f);

        [MenuItem("NCAI/UI/고지서 패널 프리팹 생성")]
        public static void CreatePrefab()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            var bound = new Dictionary<string, object>();

            var root = CreateStretched("BillPanel", null);
            var controller = root.AddComponent<BillPanelController>();
            bound["_balanceData"] = AssetDatabase.LoadAssetAtPath<NCAIClicker.Data.BalanceData>(
                "Assets/GameData/Generated/BalanceData.asset");

            var panelRoot = CreateStretched("PanelRoot", root);
            panelRoot.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.8f);
            bound["_panelRoot"] = panelRoot;

            // 탭 줄 — 탭 상태에서만 보인다.
            var tabBar = CreateRow("TabBar", panelRoot, 48f);
            var tabRect = tabBar.GetComponent<RectTransform>();
            tabRect.anchorMin = new Vector2(0.5f, 1f);
            tabRect.anchorMax = new Vector2(0.5f, 1f);
            tabRect.pivot = new Vector2(0.5f, 1f);
            tabRect.sizeDelta = new Vector2(900f, 60f);
            tabRect.anchoredPosition = new Vector2(0f, -30f);
            CreateLabel("TabBill", tabBar, font, 30, Color.white, "고지서");
            CreateLabel("TabUpgrade", tabBar, font, 30, InkFaint, "업그레이드");
            CreateLabel("TabCodex", tabBar, font, 30, InkFaint, "저금통 도감");
            bound["_tabBar"] = tabBar;

            // 보유 코인 — 낼 수 있는지 판단하려면 지금 얼마를 들고 있는지가 같이 보여야 한다.
            var balanceLabel = CreateLabel("BalanceText", panelRoot, font, 34, new Color(0.992f, 0.953f, 0.874f), "보유 $0");
            var balanceRect = balanceLabel.GetComponent<RectTransform>();
            balanceRect.anchorMin = new Vector2(1f, 1f);
            balanceRect.anchorMax = new Vector2(1f, 1f);
            balanceRect.pivot = new Vector2(1f, 1f);
            balanceRect.sizeDelta = new Vector2(360f, 56f);
            balanceRect.anchoredPosition = new Vector2(-60f, -40f);
            bound["_balanceText"] = balanceLabel;

            // 종이. 원작 실측 비율 (1920x1080 기준 폭 630 / 높이 780).
            var paper = CreateObject("Paper", panelRoot);
            var paperRect = paper.GetComponent<RectTransform>();
            paperRect.anchorMin = new Vector2(0.5f, 0.5f);
            paperRect.anchorMax = new Vector2(0.5f, 0.5f);
            paperRect.sizeDelta = new Vector2(630f, 780f);
            paperRect.anchoredPosition = new Vector2(0f, 40f);
            paperRect.localRotation = Quaternion.Euler(0f, 0f, -1.2f);
            paper.AddComponent<Image>().color = Paper;

            var paperColumn = paper.AddComponent<VerticalLayoutGroup>();
            paperColumn.padding = new RectOffset(44, 44, 36, 28);
            paperColumn.spacing = 14f;
            paperColumn.childControlWidth = true;
            paperColumn.childControlHeight = true;
            paperColumn.childForceExpandWidth = true;
            paperColumn.childForceExpandHeight = false;

            bound["_issuerText"] = CreateLabel("IssuerText", paper, font, 20, InkFaint, "시립 전력공사", TextAlignmentOptions.Left);
            bound["_titleText"] = CreateLabel("TitleText", paper, font, 44, Ink, "전기 요금", TextAlignmentOptions.Left);
            CreateDivider("Divider1", paper);
            CreateLabel("NoticeText", paper, font, 22, InkSoft,
                "공지: 고지서를 납부하지 않으면 파산에 이르게 될 것입니다…", TextAlignmentOptions.TopLeft, 70f, true);
            CreateDivider("Divider2", paper);

            CreateLabel("AmountLabel", paper, font, 24, Warn, "납부 금액");
            bound["_amountText"] = CreateLabel("AmountText", paper, font, 76, Ink, "$1,000");
            CreateDivider("Divider3", paper);

            bound["_dueLabelText"] = CreateLabel("DueLabel", paper, font, 24, Warn, "납부 기한");
            bound["_dueValueText"] = CreateLabel("DueValue", paper, font, 52, Ink, "3일");
            CreateLabel("FooterText", paper, font, 18, InkFaint, "즉시 납부 바랍니다");

            // 버튼은 종이 아래에 쌓인다.
            var actions = CreateObject("ActionColumn", panelRoot);
            var actionsRect = actions.GetComponent<RectTransform>();
            actionsRect.anchorMin = new Vector2(0.5f, 0f);
            actionsRect.anchorMax = new Vector2(0.5f, 0f);
            actionsRect.pivot = new Vector2(0.5f, 0f);
            actionsRect.sizeDelta = new Vector2(760f, 180f);
            actionsRect.anchoredPosition = new Vector2(0f, 40f);
            var actionsColumn = actions.AddComponent<VerticalLayoutGroup>();
            actionsColumn.spacing = 12f;
            actionsColumn.childAlignment = TextAnchor.UpperCenter;
            actionsColumn.childControlWidth = true;
            actionsColumn.childControlHeight = true;
            actionsColumn.childForceExpandWidth = false;
            actionsColumn.childForceExpandHeight = false;

            var payRow = CreateRow("PayRow", actions, 16f);
            SetPreferred(payRow, 640f, 76f);
            bound["_laterButton"] = CreateButton("LaterButton", payRow, font, new Vector2(300f, 76f), "아직",
                new Color(0.23f, 0.21f, 0.19f), new Color(0.37f, 0.33f, 0.29f), new Color(0.91f, 0.87f, 0.8f), 30);
            bound["_payButton"] = CreateButton("PayButton", payRow, font, new Vector2(300f, 76f), "납부하기",
                new Color(0.086f, 0.075f, 0.059f), new Color(0.604f, 0.486f, 0.275f), new Color(0.992f, 0.953f, 0.874f), 30);

            var loanColumn = CreateObject("LoanColumn", actions);
            var loanGroup = loanColumn.AddComponent<VerticalLayoutGroup>();
            loanGroup.spacing = 2f;
            loanGroup.childAlignment = TextAnchor.UpperCenter;
            loanGroup.childControlWidth = true;
            loanGroup.childControlHeight = true;
            loanGroup.childForceExpandWidth = false;
            loanGroup.childForceExpandHeight = false;
            SetPreferred(loanColumn, 420f, 80f);
            bound["_loanButton"] = CreateButton("LoanButton", loanColumn, font, new Vector2(420f, 56f), "빅 토니에게 전화하기",
                new Color(0.141f, 0.102f, 0.071f), new Color(0.36f, 0.27f, 0.15f), new Color(0.784f, 0.663f, 0.471f), 24);
            bound["_loanCaptionText"] = CreateLabel("LoanCaption", loanColumn, font, 18, InkFaint, "대출 불가");

            // 다음 런으로 가는 버튼 — 탭 상태에서만 보인다.
            var continueRow = CreateObject("ContinueRow", panelRoot);
            var continueRect = continueRow.GetComponent<RectTransform>();
            continueRect.anchorMin = new Vector2(1f, 0f);
            continueRect.anchorMax = new Vector2(1f, 0f);
            continueRect.pivot = new Vector2(1f, 0f);
            continueRect.sizeDelta = new Vector2(260f, 72f);
            continueRect.anchoredPosition = new Vector2(-60f, 40f);
            bound["_continueRow"] = continueRow;
            bound["_continueButton"] = CreateButton("ContinueButton", continueRow, font, new Vector2(260f, 72f), "계속하기",
                new Color(0.11f, 0.31f, 0.45f), new Color(0.24f, 0.51f, 0.71f), new Color(0.9f, 0.95f, 0.98f), 28);

            // 파산 선고 — 우측 가장자리 탭. 자발적 파산은 #175 범위라 잠근 채 자리만 둔다.
            var bankruptcy = CreateButton("DeclareBankruptcyButton", panelRoot, font, new Vector2(240f, 110f), "파산 선고",
                new Color(0.369f, 0.102f, 0.094f), new Color(0.753f, 0.541f, 0.353f), new Color(1f, 0.843f, 0.812f), 32);
            var bankruptcyRect = bankruptcy.GetComponent<RectTransform>();
            bankruptcyRect.anchorMin = new Vector2(1f, 0.5f);
            bankruptcyRect.anchorMax = new Vector2(1f, 0.5f);
            bankruptcyRect.pivot = new Vector2(1f, 0.5f);
            bankruptcyRect.anchoredPosition = new Vector2(0f, 60f);
            bankruptcy.interactable = false;
            bound["_declareBankruptcyButton"] = bankruptcy;

            // 잠긴 버튼은 이유가 보이지 않으면 고장으로 읽힌다. 왜 못 누르는지 옆에 적는다.
            var bankruptcyCaption = CreateLabel("BankruptcyCaption", panelRoot, font, 18, InkFaint, "준비 중");
            var captionRect = bankruptcyCaption.GetComponent<RectTransform>();
            captionRect.anchorMin = new Vector2(1f, 0.5f);
            captionRect.anchorMax = new Vector2(1f, 0.5f);
            captionRect.pivot = new Vector2(1f, 1f);
            captionRect.sizeDelta = new Vector2(240f, 28f);
            captionRect.anchoredPosition = new Vector2(0f, 0f);
            bound["_declareBankruptcyCaptionText"] = bankruptcyCaption;

            Bind(controller, bound);

            System.IO.Directory.CreateDirectory("Assets/Prefabs/Resources/UI");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh();
            Debug.Log("[BillPanelPrefabCreator] 프리팹 생성 완료: " + PrefabPath);
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

        private static GameObject CreateStretched(string name, GameObject parent)
        {
            var go = CreateObject(name, parent);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return go;
        }

        private static GameObject CreateRow(string name, GameObject parent, float spacing)
        {
            var go = CreateObject(name, parent);
            var group = go.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            return go;
        }

        private static void CreateDivider(string name, GameObject parent)
        {
            var go = CreateObject(name, parent);
            go.AddComponent<Image>().color = new Color(0.725f, 0.651f, 0.498f);
            SetPreferred(go, 0f, 2f);
        }

        private static TextMeshProUGUI CreateLabel(string name, GameObject parent, TMP_FontAsset font, int size,
            Color color, string text, TextAlignmentOptions alignment = TextAlignmentOptions.Center,
            float height = 0f, bool wrap = false)
        {
            var go = CreateObject(name, parent);
            var label = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                label.font = font;
            }
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.text = text;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            if (height > 0f)
            {
                SetPreferred(go, 0f, height);
            }
            return label;
        }

        private static Button CreateButton(string name, GameObject parent, TMP_FontAsset font, Vector2 size,
            string label, Color fill, Color line, Color textColor, int fontSize)
        {
            var go = CreateObject(name, parent);
            go.GetComponent<RectTransform>().sizeDelta = size;
            go.AddComponent<Image>().color = fill;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = line;
            outline.effectDistance = new Vector2(2f, -2f);
            var button = go.AddComponent<Button>();
            SetPreferred(go, size.x, size.y);

            var text = CreateLabel("Text", go, font, fontSize, textColor, label);
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            return button;
        }

        private static void SetPreferred(GameObject go, float width, float height)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (width > 0f)
            {
                element.preferredWidth = width;
            }
            if (height > 0f)
            {
                element.preferredHeight = height;
            }
        }

        private static void Bind(BillPanelController controller, Dictionary<string, object> bound)
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var pair in bound)
            {
                var field = typeof(BillPanelController).GetField(pair.Key, flags);
                if (field == null)
                {
                    Debug.LogError($"[BillPanelPrefabCreator] {pair.Key} 필드가 없다. 이름이 바뀌었는지 확인하라.");
                    continue;
                }
                field.SetValue(controller, pair.Value);
            }
        }
    }
}
