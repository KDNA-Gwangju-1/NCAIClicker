using NCAIClicker.Data;
using NCAIClicker.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// UpgradeShopPanel 프리팹을 생성하는 에디터 도구 (이슈 #184, 작업 6.12).
    /// 목업 캔버스 "퍽 선택 · 업그레이드 탭 목업" 의 업그레이드 보드를 따른다 — 파란 버튼 폐기,
    /// 구매 버튼은 Base(어두운 면 + 선) 에 금색 숫자, 호버·눌림에서 테두리가 금색으로 바뀐다.
    /// 고지서 패널의 업그레이드 탭(1100×760 상자) 안에 들어가므로 제목·잔고·닫기는 넣지 않는다 —
    /// 탭 이름과 우상단 잔고가 이미 그 역할을 한다.
    /// 카드는 upgrades.csv 의 sort_order 순으로 세로 행 목록에 만들고 id 를 심는다 — 늘어나면 스크롤된다 (#311).
    /// 같은 경로에 덮어써 참조를 유지한다.
    /// </summary>
    public static class UpgradeShopPrefabCreator
    {
        private const string BodyFontPath = "Assets/Materials/Fonts/NanumGothicBoldSDF.asset";
        private const string DisplayFontPath = "Assets/Materials/Fonts/NanumSquareBoldSDF.asset";
        private const string BalancePath = "Assets/GameData/Generated/BalanceData.asset";
        private const string PrefabPath = "Assets/Prefabs/UI/UpgradeShopPanel.prefab";

        private static readonly Color SurfaceFill = new Color(0.086f, 0.075f, 0.059f, 1f); // #16130F
        private static readonly Color PanelBorder = new Color(0.549f, 0.498f, 0.416f, 1f); // #8C7F6A
        private static readonly Color LineSecondary = new Color(0.604f, 0.486f, 0.275f, 1f); // #9A7C46
        private static readonly Color LineDim = new Color(0.290f, 0.259f, 0.224f, 1f); // #4A4239
        private static readonly Color Gold = new Color(0.910f, 0.690f, 0.294f, 1f); // #E8B04B
        private static readonly Color GoldLight = new Color(0.941f, 0.776f, 0.447f, 1f); // #F0C672
        private static readonly Color TextCream = new Color(0.992f, 0.953f, 0.875f, 1f); // #FDF3DF
        private static readonly Color TextMuted = new Color(0.784f, 0.718f, 0.604f, 1f); // #C8B79A
        private static readonly Color TextLoss = new Color(1.000f, 0.706f, 0.635f, 1f); // #FFB4A2

        [MenuItem("NCAI/UI/업그레이드 상점 프리팹 생성")]
        public static void CreatePrefab()
        {
            var bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath);
            var displayFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DisplayFontPath);
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>(BalancePath);
            if (bodyFont == null || displayFont == null || balance == null)
            {
                Debug.LogError($"[UpgradeShopPrefabCreator] 폰트 또는 BalanceData 를 찾을 수 없습니다: {BodyFontPath} / {DisplayFontPath} / {BalancePath}");
                return;
            }

            var rootGo = new GameObject("UpgradeShopPanel", typeof(RectTransform));
            var rootRect = rootGo.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = new Vector2(1100f, 760f);
            var panel = rootGo.AddComponent<UpgradeShopPanel>();

            // 5장부터 넘치던 3열×2행 격자를 세로 행 목록 + 스크롤로 바꿨다 (#311 B안). 5장은 스크롤 없이 한 화면에 든다
            // (4 + 5×128 + 4×8 + 4 = 680). 6장부터 스크롤바가 나타난다.
            var listGo = CreateObject("Entries", rootGo);
            var listRect = listGo.GetComponent<RectTransform>();
            listRect.anchorMin = Vector2.zero;
            listRect.anchorMax = Vector2.one;
            listRect.offsetMin = new Vector2(8f, 40f);
            listRect.offsetMax = new Vector2(-8f, -40f);
            var scroll = listGo.AddComponent<ScrollRect>();

            // RectMask2D 로 자른다 — Mask 는 스텐실용 Image 가 필요하다.
            var viewportGo = CreateObject("Viewport", listGo);
            var viewportRect = viewportGo.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            viewportGo.AddComponent<RectMask2D>();

            var contentGo = CreateObject("Content", viewportGo);
            var contentRect = contentGo.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            var rows = contentGo.AddComponent<VerticalLayoutGroup>();
            // 카드 Outline(±2) 이 Viewport 가장자리에서 잘리지 않게 4 띄운다. 5장 높이 672 + 8 = 680 으로 딱 맞는다.
            rows.padding = new RectOffset(4, 4, 4, 4);
            rows.spacing = 8;
            rows.childAlignment = TextAnchor.UpperCenter;
            rows.childControlWidth = true;
            rows.childControlHeight = true;
            rows.childForceExpandWidth = true;
            rows.childForceExpandHeight = false;
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = contentRect;
            scroll.viewport = viewportRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;
            scroll.verticalScrollbar = CreateScrollbar(listGo);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarSpacing = 4f;

            var defs = balance.Upgrades;
            var entries = new UpgradeShopEntry[defs.Count];
            for (var i = 0; i < defs.Count; i++)
            {
                entries[i] = CreateCard(contentGo, bodyFont, displayFont, defs[i].Id);
            }

            var serialized = new SerializedObject(panel);
            var entriesProp = serialized.FindProperty("_entries");
            entriesProp.arraySize = entries.Length;
            for (var i = 0; i < entries.Length; i++)
            {
                entriesProp.GetArrayElementAtIndex(i).objectReferenceValue = entries[i];
            }
            serialized.FindProperty("_balanceData").objectReferenceValue = balance;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(rootGo, PrefabPath);
            Object.DestroyImmediate(rootGo);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[UpgradeShopPrefabCreator] 프리팹 생성 완료: {PrefabPath} (카드 {entries.Length}장)");
        }

        private static UpgradeShopEntry CreateCard(GameObject parent, TMP_FontAsset bodyFont, TMP_FontAsset displayFont, string upgradeId)
        {
            var cardGo = CreateObject($"Card_{upgradeId}", parent);
            var cardImage = cardGo.AddComponent<Image>();
            cardImage.color = SurfaceFill;
            cardImage.raycastTarget = true;
            var cardOutline = cardGo.AddComponent<Outline>();
            cardOutline.effectColor = PanelBorder;
            cardOutline.effectDistance = new Vector2(2f, -2f);

            cardGo.AddComponent<LayoutElement>().preferredHeight = 128;

            // 한 행 = 왼쪽(이름·Lv) · 가운데(설명·효과) · 오른쪽(사유·구매 버튼). 폭이 넓어 설명·효과를 본문 24px 로 쓴다 (#311).
            var layout = cardGo.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 12, 12);
            layout.spacing = 24;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var leftGo = CreateColumn("Head", cardGo, 4, TextAnchor.MiddleLeft);
            leftGo.GetComponent<LayoutElement>().preferredWidth = 240;
            var nameLabel = CreateLabel("NameLabel", leftGo, displayFont, 32, TextCream, TextAlignmentOptions.MidlineLeft, "이름");
            nameLabel.characterSpacing = 2;
            nameLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
            var levelLabel = CreateLabel("LevelLabel", leftGo, bodyFont, 24, Gold, TextAlignmentOptions.MidlineLeft, "Lv 0 / 0");
            levelLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 32;

            var midGo = CreateColumn("Body", cardGo, 4, TextAnchor.MiddleLeft);
            // 선호 폭 0 — 두면 설명 한 줄 길이가 선호 폭이 되어 합이 넘치고 좌우 칸까지 카드마다 다르게 줄어든다.
            var midElement = midGo.GetComponent<LayoutElement>();
            midElement.preferredWidth = 0;
            midElement.flexibleWidth = 1;
            var descLabel = CreateLabel("DescriptionLabel", midGo, bodyFont, 24, TextMuted, TextAlignmentOptions.TopLeft, "설명");
            descLabel.textWrappingMode = TextWrappingModes.Normal;
            descLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;
            var effectLabel = CreateLabel("EffectLabel", midGo, bodyFont, 24, TextCream, TextAlignmentOptions.TopLeft, "효과");
            effectLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 32;

            var buyRowGo = CreateColumn("BuyRow", cardGo, 4, TextAnchor.MiddleCenter);
            buyRowGo.GetComponent<LayoutElement>().preferredWidth = 200;
            var reasonLabel = CreateLabel("ReasonLabel", buyRowGo, bodyFont, 20, TextLoss, TextAlignmentOptions.MidlineRight, string.Empty);
            reasonLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;

            var purchaseButton = CreateBaseButton("PurchaseButton", buyRowGo, 200f, out var buttonFill);
            purchaseButton.GetComponent<LayoutElement>().preferredHeight = 64;
            var costRow = buttonFill.AddComponent<HorizontalLayoutGroup>();
            costRow.padding = new RectOffset(20, 20, 0, 0);
            costRow.spacing = 4;
            costRow.childAlignment = TextAnchor.MiddleCenter;
            costRow.childControlWidth = true;
            costRow.childControlHeight = true;
            costRow.childForceExpandWidth = false;
            costRow.childForceExpandHeight = true;
            var dollar = CreateLabel("Dollar", buttonFill, bodyFont, 26, Gold, TextAlignmentOptions.MidlineRight, "$");
            dollar.gameObject.AddComponent<LayoutElement>().preferredWidth = 22;
            var costLabel = CreateLabel("CostLabel", buttonFill, bodyFont, 30, Gold, TextAlignmentOptions.MidlineLeft, "0");
            costLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            var entry = cardGo.AddComponent<UpgradeShopEntry>();
            var serialized = new SerializedObject(entry);
            serialized.FindProperty("_upgradeId").stringValue = upgradeId;
            serialized.FindProperty("_nameLabel").objectReferenceValue = nameLabel;
            serialized.FindProperty("_descriptionLabel").objectReferenceValue = descLabel;
            serialized.FindProperty("_levelLabel").objectReferenceValue = levelLabel;
            serialized.FindProperty("_effectLabel").objectReferenceValue = effectLabel;
            serialized.FindProperty("_purchaseButton").objectReferenceValue = purchaseButton;
            serialized.FindProperty("_costLabel").objectReferenceValue = costLabel;
            serialized.FindProperty("_reasonLabel").objectReferenceValue = reasonLabel;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return entry;
        }

        /// <summary>
        /// Base 버튼. 루트 Image 가 테두리이자 Button 의 targetGraphic 이라 틴트가 곧 테두리 색이다 —
        /// 기본 금갈색 선, 호버 금색, 눌림 밝은 금색, 비활성 흐린 선. 면은 2px 안쪽 자식이다.
        /// </summary>
        private static Button CreateBaseButton(string name, GameObject parent, float width, out GameObject fill)
        {
            var btnGo = CreateObject(name, parent);
            btnGo.AddComponent<LayoutElement>().preferredWidth = width;
            var frame = btnGo.AddComponent<Image>();
            frame.color = Color.white;
            frame.raycastTarget = true;

            var button = btnGo.AddComponent<Button>();
            button.targetGraphic = frame;
            var colors = button.colors;
            colors.normalColor = LineSecondary;
            colors.highlightedColor = Gold;
            colors.pressedColor = GoldLight;
            colors.selectedColor = LineSecondary;
            colors.disabledColor = LineDim;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            fill = CreateObject("Fill", btnGo);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(-2f, -2f);
            var fillImage = fill.AddComponent<Image>();
            fillImage.color = SurfaceFill;
            fillImage.raycastTarget = false;
            return button;
        }

        /// <summary>
        /// 세로로 쌓는 칸. 폭은 호출측이 LayoutElement 에 정한다. 유연폭은 0 으로 둔다 — 비워 두면
        /// childForceExpandWidth 때문에 그룹이 유연폭 1 을 보고해 고정 폭 칸도 남는 폭을 나눠 가진다.
        /// </summary>
        private static GameObject CreateColumn(string name, GameObject parent, float spacing, TextAnchor alignment)
        {
            var go = CreateObject(name, parent);
            go.AddComponent<LayoutElement>().flexibleWidth = 0;
            var column = go.AddComponent<VerticalLayoutGroup>();
            column.spacing = spacing;
            column.childAlignment = alignment;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            return go;
        }

        /// <summary>
        /// 오른쪽 세로 스크롤바 (폭 16). 틴트는 Base 버튼과 같은 규칙 — 손잡이 Image 가 targetGraphic 이다.
        /// 목록이 넘칠 때만 보인다 (ScrollRect 의 AutoHideAndExpandViewport).
        /// </summary>
        private static Scrollbar CreateScrollbar(GameObject parent)
        {
            var barGo = CreateObject("Scrollbar", parent);
            var barRect = barGo.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(1f, 0f);
            barRect.anchorMax = Vector2.one;
            barRect.pivot = new Vector2(1f, 0.5f);
            barRect.sizeDelta = new Vector2(16f, 0f);
            var track = barGo.AddComponent<Image>();
            track.color = SurfaceFill;
            var trackOutline = barGo.AddComponent<Outline>();
            trackOutline.effectColor = LineDim;
            trackOutline.effectDistance = new Vector2(2f, -2f);

            var areaGo = CreateObject("SlidingArea", barGo);
            var areaRect = areaGo.GetComponent<RectTransform>();
            areaRect.anchorMin = Vector2.zero;
            areaRect.anchorMax = Vector2.one;
            areaRect.offsetMin = Vector2.zero;
            areaRect.offsetMax = Vector2.zero;

            var handleGo = CreateObject("Handle", areaGo);
            var handleRect = handleGo.GetComponent<RectTransform>();
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            var handle = handleGo.AddComponent<Image>();
            handle.color = Color.white;

            var bar = barGo.AddComponent<Scrollbar>();
            bar.handleRect = handleRect;
            bar.targetGraphic = handle;
            bar.direction = Scrollbar.Direction.BottomToTop;
            var colors = bar.colors;
            colors.normalColor = LineSecondary;
            colors.highlightedColor = Gold;
            colors.pressedColor = GoldLight;
            colors.selectedColor = LineSecondary;
            colors.disabledColor = LineDim;
            colors.fadeDuration = 0.08f;
            bar.colors = colors;
            return bar;
        }

        private static GameObject CreateObject(string name, GameObject parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        private static TextMeshProUGUI CreateLabel(string name, GameObject parent, TMP_FontAsset font, float fontSize, Color color, TextAlignmentOptions align, string text)
        {
            var go = CreateObject(name, parent);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.text = text;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
