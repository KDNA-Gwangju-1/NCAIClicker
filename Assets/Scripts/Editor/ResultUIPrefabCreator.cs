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
        // 버튼 (디자인 시스템 2차, #184): Base 면 #16130F · 선 #8C7F6A · 호버 #E8B04B · 눌림 #F0C672 · 비활성 #4A4239
        private static readonly Color BaseFill = new Color(0.086f, 0.075f, 0.059f);
        private static readonly Color BaseLine = new Color(0.549f, 0.498f, 0.416f);
        private static readonly Color HoverLine = new Color(0.910f, 0.690f, 0.294f);
        private static readonly Color PressLine = new Color(0.941f, 0.776f, 0.447f);
        private static readonly Color DisabledLine = new Color(0.290f, 0.259f, 0.224f);
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

            // 해금 카드 (#300). 정산창의 모든 것 위에 덮여야 하므로 PanelRoot 의 마지막 자식으로 둔다.
            bound["_unlockCard"] = BuildUnlockCard(panelRoot, font);

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
            // 고지서 정보는 한 줄 (#184). StageGoalText 는 컨트롤러가 비워 두며 호환용으로만 남긴다.
            bound["_billStatusText"] = CreateLabel("BillStatusText", meta, font, 24, Muted, TextAlignmentOptions.Center, "1단계 고지서 $0 · 마감 0일 남음");
            var stageGoalLabel = CreateLabel("StageGoalText", meta, font, 24, Muted, TextAlignmentOptions.Center, string.Empty);
            stageGoalLabel.gameObject.SetActive(false);
            bound["_stageGoalText"] = stageGoalLabel;

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
            bound["_codexPreview"] = AttachCreaturePreview(codexIcon);
            bound["_codexProgressText"] = CreateLabel("CodexProgressText", codex, font, 28, Muted, TextAlignmentOptions.Center, ResultUIController.UnwiredPlaceholder);
            bound["_codexCaptionText"] = CreateLabel("CodexCaptionText", codex, font, 20, Muted, TextAlignmentOptions.Center, "고지서 납부 시 해금");

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
            // 버튼 셋은 전부 Base 톤이다 (디자인 시스템 2차: 금색은 수치에만, 위험은 색이 아니라 확인창).
            var upgradeButton = CreateButton("UpgradeButton", actions, font, new Vector2(300f, 104f), "업그레이드",
                BaseFill, BaseLine, Cream, 30);
            SetFlexibleWidth(upgradeButton.gameObject, 0f);
            bound["_upgradeButton"] = upgradeButton;

            var payColumn = CreateVertical("PayColumn", actions, 8f);
            SetPreferredWidth(payColumn, 340f);
            SetFlexibleWidth(payColumn, 0f);
            // 동작이 없는 버튼은 프리팹 단계에서 잠근다. 런타임에 끄면 첫 프레임에 눌릴 수 있다.
            // 버튼 안에 두 줄이 들어간다 — 금액이 크게, 남은 일수가 그 아래 작게 (원작).
            var payButton = CreateButton("PayButton", payColumn, font, new Vector2(340f, 104f), string.Empty,
                BaseFill, BaseLine, Cream, 34);
            payButton.interactable = false;
            bound["_payButton"] = payButton;

            var payTextRoot = CreateVertical("PayLabels", payButton.transform.Find("Fill").gameObject, 0f);
            var payTextRect = payTextRoot.GetComponent<RectTransform>();
            payTextRect.anchorMin = Vector2.zero;
            payTextRect.anchorMax = Vector2.one;
            payTextRect.offsetMin = new Vector2(8f, 8f);
            payTextRect.offsetMax = new Vector2(-8f, -8f);
            payTextRoot.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;

            bound["_payButtonLabel"] = CreateLabel("PayAmountText", payTextRoot, font, 38,
                Gold, TextAlignmentOptions.Center, "$0");
            bound["_payDaysLeftText"] = CreateLabel("PayDaysLeftText", payTextRoot, font, 24,
                Muted, TextAlignmentOptions.Center, "");
            bound["_payCaptionText"] = CreateLabel("PayCaptionText", payColumn, font, 22, Muted, TextAlignmentOptions.Center, "준비 중");

            var continueButton = CreateButton("ContinueButton", actions, font, new Vector2(300f, 104f), "계속",
                BaseFill, BaseLine, Cream, 32);
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

        /// <summary>
        /// 새 저금통 해금 카드 (#300). 원작 캡처 구성 — 위 "{이름} 해금!" 띠, 왼쪽 3D 외형과 뒤에서 도는 빛살,
        /// 오른쪽 HP 와 코인 구간·확률, 그 아래 역할 한 줄(도감 #299 와 같은 GetRoleText).
        /// 배경 전체가 닫기 버튼이고 나머지는 그 자식이라 어디를 눌러도 넘어간다.
        /// 배경은 불투명하다 — 반투명으로 정산창을 비치게 하면 UiGuidelineChecks 가 알파 톤으로 잡는다.
        /// </summary>
        private static UnlockCardView BuildUnlockCard(GameObject panelRoot, TMP_FontAsset font)
        {
            var host = CreateStretchedObject("UnlockCard", panelRoot);
            var view = host.AddComponent<UnlockCardView>();

            var cardRoot = CreateStretchedObject("CardRoot", host);
            var backdrop = CreateStretchedObject("Backdrop", cardRoot);
            var backdropImage = backdrop.AddComponent<Image>();
            backdropImage.color = new Color(0.024f, 0.02f, 0.016f);
            var dismiss = backdrop.AddComponent<Button>();
            dismiss.targetGraphic = backdropImage;
            dismiss.transition = Selectable.Transition.None;

            // 제목 띠
            var banner = CreateObject("TitleBanner", backdrop);
            var bannerRect = banner.GetComponent<RectTransform>();
            bannerRect.anchorMin = new Vector2(0.5f, 1f);
            bannerRect.anchorMax = new Vector2(0.5f, 1f);
            bannerRect.pivot = new Vector2(0.5f, 1f);
            bannerRect.sizeDelta = new Vector2(960f, 112f);
            bannerRect.anchoredPosition = new Vector2(0f, -SafeInset.y - 24f);
            banner.AddComponent<Image>().color = new Color(0.086f, 0.067f, 0.047f);
            var title = CreateLabel("TitleText", banner, font, 64, Gold, TextAlignmentOptions.Center, "해금!");
            StretchToParent(title.gameObject);

            // 빛살 — 텍스처 없이 가는 막대를 돌려 겹친다. UnlockCardView 가 천천히 돌린다.
            var rays = CreateObject("Rays", backdrop);
            var raysRect = rays.GetComponent<RectTransform>();
            raysRect.sizeDelta = new Vector2(640f, 640f);
            raysRect.anchoredPosition = new Vector2(-400f, -40f);
            for (var i = 0; i < 12; i++)
            {
                var bar = CreateObject("Ray" + i, rays);
                var barRect = bar.GetComponent<RectTransform>();
                barRect.sizeDelta = new Vector2(36f, 640f);
                barRect.localRotation = Quaternion.Euler(0f, 0f, i * 15f);
                var barImage = bar.AddComponent<Image>();
                barImage.color = new Color(0.227f, 0.165f, 0.094f);
                barImage.raycastTarget = false;
            }

            var previewFrame = CreateObject("PreviewFrame", backdrop);
            var previewRect = previewFrame.GetComponent<RectTransform>();
            previewRect.sizeDelta = new Vector2(480f, 480f);
            previewRect.anchoredPosition = new Vector2(-400f, -40f);
            var preview = AttachCreaturePreview(previewFrame);
            // 다른 미리보기와 같은 자리에 모델을 두면 서로의 카메라에 찍힌다 — 정산창 "다음 해금" 은 (0, −500),
            // 도감(#299)은 x 40 간격으로 y −500 줄을 쓴다. 카메라 먼 쪽 면(10)보다 한참 먼 y −700 줄을 따로 쓴다.
            var previewSerialized = new SerializedObject(preview);
            previewSerialized.FindProperty("_stageOrigin").vector3Value = new Vector3(0f, -700f, 0f);
            previewSerialized.ApplyModifiedPropertiesWithoutUndo();

            // 오른쪽 수치 판
            var stats = CreateVertical("StatsPanel", backdrop, 12f);
            var statsRect = stats.GetComponent<RectTransform>();
            statsRect.sizeDelta = new Vector2(640f, 480f);
            statsRect.anchoredPosition = new Vector2(380f, 20f);
            var statsGroup = stats.GetComponent<VerticalLayoutGroup>();
            statsGroup.padding = new RectOffset(40, 40, 32, 32);
            statsGroup.childAlignment = TextAnchor.UpperLeft;
            stats.AddComponent<Image>().color = RowFill;

            var hpRow = CreateHorizontal("HpRow", stats, 16f);
            SetPreferredHeight(hpRow, 52f);
            SetFlexibleWidth(CreateLabel("HpLabel", hpRow, font, 40, Muted, TextAlignmentOptions.Left, "HP:").gameObject, 1f);
            var hpValue = CreateLabel("HpValue", hpRow, font, 40, Cream, TextAlignmentOptions.Right, "0");
            SetPreferredWidth(hpValue.gameObject, 200f);

            var coinLabel = CreateLabel("CoinLabel", stats, font, 40, Muted, TextAlignmentOptions.Left, "코인:");
            SetPreferredHeight(coinLabel.gameObject, 52f);

            // 액면 종류만큼 행을 만든다 — 가장 큰 액면으로 묶으니 구간은 액면 수를 넘지 않는다.
            var coinKinds = 5;
            var balance = AssetDatabase.LoadAssetAtPath<NCAIClicker.Data.BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            if (balance != null && balance.Coins != null && balance.Coins.Count > 0)
            {
                coinKinds = balance.Coins.Count;
            }
            var bandRows = new GameObject[coinKinds];
            var bandRanges = new TextMeshProUGUI[coinKinds];
            var bandChances = new TextMeshProUGUI[coinKinds];
            for (var i = 0; i < coinKinds; i++)
            {
                var row = CreateHorizontal("BandRow" + i, stats, 16f);
                SetPreferredHeight(row, 44f);
                bandRanges[i] = CreateLabel("RangeText", row, font, 34, Cream, TextAlignmentOptions.Left, "$0");
                SetFlexibleWidth(bandRanges[i].gameObject, 1f);
                bandChances[i] = CreateLabel("ChanceText", row, font, 34, Cream, TextAlignmentOptions.Right, "0%");
                SetPreferredWidth(bandChances[i].gameObject, 140f);
                bandRows[i] = row;
            }

            // 역할 한 줄 — 원작 카드의 아래 칸. 수치 판과 같은 폭으로 그 아래에 둔다.
            var rolePanel = CreateObject("RolePanel", backdrop);
            var roleRect = rolePanel.GetComponent<RectTransform>();
            roleRect.sizeDelta = new Vector2(640f, 104f);
            roleRect.anchoredPosition = new Vector2(380f, -296f);
            rolePanel.AddComponent<Image>().color = RowFill;
            var roleText = CreateLabel("RoleText", rolePanel, font, 30, Cream, TextAlignmentOptions.Left, "기본형");
            roleText.textWrappingMode = TextWrappingModes.Normal;
            var roleTextRect = roleText.GetComponent<RectTransform>();
            roleTextRect.anchorMin = Vector2.zero;
            roleTextRect.anchorMax = Vector2.one;
            roleTextRect.offsetMin = new Vector2(40f, 12f);
            roleTextRect.offsetMax = new Vector2(-40f, -12f);

            var hint = CreateLabel("HintText", backdrop, font, 24, Muted, TextAlignmentOptions.Center, "아무 곳이나 눌러 계속");
            var hintRect = hint.GetComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(0.5f, 0f);
            hintRect.anchorMax = new Vector2(0.5f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.sizeDelta = new Vector2(600f, 40f);
            hintRect.anchoredPosition = new Vector2(0f, SafeInset.y + 24f);

            var serialized = new SerializedObject(view);
            serialized.FindProperty("_cardRoot").objectReferenceValue = cardRoot;
            serialized.FindProperty("_dismissButton").objectReferenceValue = dismiss;
            serialized.FindProperty("_titleText").objectReferenceValue = title;
            serialized.FindProperty("_preview").objectReferenceValue = preview;
            serialized.FindProperty("_hpValueText").objectReferenceValue = hpValue;
            serialized.FindProperty("_roleText").objectReferenceValue = roleText;
            serialized.FindProperty("_rays").objectReferenceValue = raysRect;
            SetArray(serialized.FindProperty("_bandRows"), bandRows);
            SetArray(serialized.FindProperty("_bandRangeTexts"), bandRanges);
            SetArray(serialized.FindProperty("_bandChanceTexts"), bandChances);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            cardRoot.SetActive(false);
            return view;
        }

        private static void StretchToParent(GameObject go)
        {
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetArray(SerializedProperty property, Object[] values)
        {
            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static TextMeshProUGUI[] CreateDenomRow(GameObject parent, TMP_FontAsset font)
        {
            var row = CreateHorizontal("DenomRow", parent, 10f);
            SetPreferredHeight(row, 60f);
            var image = row.AddComponent<Image>();
            image.color = RowFill;

            // 칸은 coins.csv 행 수만큼, 라벨은 그 값으로 만든다 (#326). 예전에는 "$1~$100" 네 칸을 박아 두어
            // $1,000 이 나오면 개수 칸(코인 합)에는 잡히고 액면 칸에는 안 보여 합계가 내역으로 설명되지 않았다.
            var balance = AssetDatabase.LoadAssetAtPath<NCAIClicker.Data.BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            var worths = new List<string>();
            if (balance != null && balance.Coins != null)
            {
                foreach (var coin in balance.Coins)
                {
                    worths.Add("$" + coin.Value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            if (worths.Count == 0)
            {
                Debug.LogError("[ResultUIPrefabCreator] BalanceData 의 coins 를 읽지 못해 액면 칸을 만들지 못했다. 밸런스 CSV 를 먼저 임포트하라.");
            }
            var labels = new TextMeshProUGUI[worths.Count];
            for (var i = 0; i < worths.Count; i++)
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

        /// <summary>
        /// 버튼. 루트 Image 가 테두리이자 Button 의 targetGraphic 이라 **틴트가 곧 테두리 색**이다 —
        /// 기본 <paramref name="line"/>, 호버 금색, 눌림 밝은 금색, 비활성 흐린 선. 면은 2px 안쪽 자식이다.
        /// 예전처럼 면에 흰색 틴트를 걸면 어두운 면에서는 호버가 보이지 않아 눌리지 않는 것처럼 읽혔다 (#184).
        /// </summary>
        private static Button CreateButton(string name, GameObject parent, TMP_FontAsset font, Vector2 size, string label, Color fill, Color line, Color textColor, int fontSize)
        {
            var go = CreateObject(name, parent);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;

            var frame = go.AddComponent<Image>();
            frame.color = Color.white;

            var button = go.AddComponent<Button>();
            button.targetGraphic = frame;
            var colors = button.colors;
            colors.normalColor = line;
            colors.highlightedColor = HoverLine;
            colors.pressedColor = PressLine;
            colors.selectedColor = line;
            colors.disabledColor = DisabledLine;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            SetPreferredWidth(go, size.x);
            SetPreferredHeight(go, size.y);

            var fillGo = CreateObject("Fill", go);
            var fillRect = fillGo.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(-2f, -2f);
            var fillImage = fillGo.AddComponent<Image>();
            fillImage.color = fill;
            fillImage.raycastTarget = false;

            // 글자는 면(Fill) 의 자식이다 — 대비 검사기가 배경을 가장 가까운 Image 로 잡기 때문이다.
            var text = CreateLabel("Text", fillGo, font, fontSize, textColor, TextAlignmentOptions.Center, label);
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
        /// <summary>
        /// 다음 해금 크리처의 3D 외형 칸 (#247). CodexIcon 위를 RawImage 로 덮고 CreaturePreview 를 붙인다.
        /// 프리팹 목록은 Managers 프리팹의 CreatureManager 목록을 그대로 복사한다 — 기준은 한 곳이다.
        /// 기존 프리팹을 다시 만들지 않고 고칠 때도 이 메서드를 쓴다.
        /// </summary>
        public static CreaturePreview AttachCreaturePreview(GameObject codexIcon)
        {
            var existing = codexIcon.transform.Find("CodexPreview");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var previewGo = CreateStretchedObject("CodexPreview", codexIcon);
            var rawImage = previewGo.AddComponent<RawImage>();
            rawImage.raycastTarget = false;
            rawImage.enabled = false;
            var preview = previewGo.AddComponent<CreaturePreview>();

            var serialized = new SerializedObject(preview);
            serialized.FindProperty("_image").objectReferenceValue = rawImage;
            var entries = serialized.FindProperty("_prefabs");
            var managers = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Resources/Managers.prefab");
            var creatureManager = managers != null ? managers.GetComponentInChildren<NCAIClicker.Core.CreatureManager>(true) : null;
            if (creatureManager == null)
            {
                Debug.LogError("[ResultUIPrefabCreator] Managers 프리팹의 CreatureManager 를 찾지 못해 미리보기 목록이 비었다.");
            }
            else
            {
                var source = new SerializedObject(creatureManager).FindProperty("_targetPrefabs");
                entries.arraySize = source.arraySize;
                for (var i = 0; i < source.arraySize; i++)
                {
                    var from = source.GetArrayElementAtIndex(i);
                    var to = entries.GetArrayElementAtIndex(i);
                    to.FindPropertyRelative("_targetId").stringValue = from.FindPropertyRelative("_targetId").stringValue;
                    to.FindPropertyRelative("_prefab").objectReferenceValue = from.FindPropertyRelative("_prefab").objectReferenceValue;
                }
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return preview;
        }

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
