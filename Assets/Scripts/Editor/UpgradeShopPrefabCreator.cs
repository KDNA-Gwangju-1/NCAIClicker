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
    /// 목업 캔버스 "퍽 선택 · 업그레이드 탭 목업" 의 업그레이드 보드를 따른다 — 카드 2×2, 파란 버튼 폐기,
    /// 구매 버튼은 Base(어두운 면 + 선) 에 금색 숫자, 호버·눌림에서 테두리가 금색으로 바뀐다.
    /// 고지서 패널의 업그레이드 탭(1100×760 상자) 안에 들어가므로 제목·잔고·닫기는 넣지 않는다 —
    /// 탭 이름과 우상단 잔고가 이미 그 역할을 한다.
    /// 카드는 upgrades.csv 의 sort_order 순으로 만들고 id 를 심는다. 같은 경로에 덮어써 참조를 유지한다.
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

            var gridGo = CreateObject("Entries", rootGo);
            var gridRect = gridGo.GetComponent<RectTransform>();
            gridRect.anchorMin = Vector2.zero;
            gridRect.anchorMax = Vector2.one;
            gridRect.offsetMin = new Vector2(8f, 40f);
            gridRect.offsetMax = new Vector2(-8f, -40f);
            var grid = gridGo.AddComponent<GridLayoutGroup>();
            // 5장부터 3열 × 2행 (#247 부업 장부 추가, docs/UI_MOCKUPS/upgrade-shop.html A안). 높이는 그대로 두어 설명·효과 2줄을 유지한다.
            grid.cellSize = new Vector2(350f, 328f);
            grid.spacing = new Vector2(16f, 16f);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.MiddleCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;

            var defs = balance.Upgrades;
            var entries = new UpgradeShopEntry[defs.Count];
            for (var i = 0; i < defs.Count; i++)
            {
                entries[i] = CreateCard(gridGo, bodyFont, displayFont, defs[i].Id);
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

            var layout = cardGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 20, 20);
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // 이름 줄: 이름 (왼쪽) · Lv (오른쪽)
            var headGo = CreateObject("Head", cardGo);
            headGo.AddComponent<LayoutElement>().preferredHeight = 52;
            var head = headGo.AddComponent<HorizontalLayoutGroup>();
            head.spacing = 12;
            head.childAlignment = TextAnchor.MiddleLeft;
            head.childControlWidth = true;
            head.childControlHeight = true;
            head.childForceExpandWidth = false;
            head.childForceExpandHeight = true;
            var nameLabel = CreateLabel("NameLabel", headGo, displayFont, 32, TextCream, TextAlignmentOptions.MidlineLeft, "이름");
            nameLabel.characterSpacing = 2;
            nameLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var levelLabel = CreateLabel("LevelLabel", headGo, bodyFont, 22, Gold, TextAlignmentOptions.MidlineRight, "Lv 0 / 0");
            levelLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 110;

            var descLabel = CreateLabel("DescriptionLabel", cardGo, bodyFont, 20, TextMuted, TextAlignmentOptions.TopLeft, "설명");
            descLabel.textWrappingMode = TextWrappingModes.Normal;
            descLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;

            var effectLabel = CreateLabel("EffectLabel", cardGo, bodyFont, 20, TextCream, TextAlignmentOptions.TopLeft, "효과");
            effectLabel.textWrappingMode = TextWrappingModes.Normal;
            effectLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 60;

            // 구매 줄: 사유 (왼쪽) · 구매 버튼 (오른쪽)
            var buyRowGo = CreateObject("BuyRow", cardGo);
            buyRowGo.AddComponent<LayoutElement>().preferredHeight = 64;
            var buyRow = buyRowGo.AddComponent<HorizontalLayoutGroup>();
            buyRow.spacing = 16;
            buyRow.childAlignment = TextAnchor.MiddleLeft;
            buyRow.childControlWidth = true;
            buyRow.childControlHeight = true;
            buyRow.childForceExpandWidth = false;
            buyRow.childForceExpandHeight = true;
            var reasonLabel = CreateLabel("ReasonLabel", buyRowGo, bodyFont, 22, TextLoss, TextAlignmentOptions.MidlineLeft, string.Empty);
            reasonLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            var purchaseButton = CreateBaseButton("PurchaseButton", buyRowGo, 200f, out var buttonFill);
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
