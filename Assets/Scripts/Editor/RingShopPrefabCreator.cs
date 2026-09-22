using System.Collections.Generic;
using System.Reflection;
using NCAIClicker.Data;
using NCAIClicker.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 반지 상점(보석함) 프리팹을 생성한다 (이슈 #183, #211).
    /// 원작 레퍼런스(보석함 안의 다종 반지 슬롯 + 우측 상세 정보 패널)의 레이아웃을 구성한다.
    /// </summary>
    public static class RingShopPrefabCreator
    {
        private const string FontPath = "Assets/Materials/Fonts/NanumGothicBoldSDF.asset";
        private const string PrefabPath = "Assets/Prefabs/UI/RingShopPanel.prefab";

        private static readonly Color BoxWood = new Color(0.20f, 0.15f, 0.11f);
        private static readonly Color SlotBg = new Color(0.12f, 0.09f, 0.07f);
        private static readonly Color GoldText = new Color(0.95f, 0.78f, 0.38f);
        private static readonly Color OutlineColor = new Color(0.45f, 0.35f, 0.22f);
        private static readonly Color SurfaceFill = new Color(0.086f, 0.075f, 0.059f, 1f); // #16130F
        private static readonly Color PanelFill = new Color(0.106f, 0.078f, 0.063f, 1f); // #1B1410
        private static readonly Color LineSecondary = new Color(0.604f, 0.486f, 0.275f, 1f); // #9A7C46
        private static readonly Color TextCream = new Color(0.992f, 0.953f, 0.875f, 1f); // #FDF3DF
        private static readonly Color TextMuted = new Color(0.784f, 0.718f, 0.604f, 1f); // #C8B79A

        [MenuItem("NCAI/UI/반지 상점 프리팹 생성")]
        public static void CreatePrefab()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");

            var root = CreateObject("RingShopPanel", null);
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(1100f, 760f);

            var panel = root.AddComponent<RingShopPanel>();
            SetPrivate(panel, "_balanceData", balance);

            // 상단 타이틀 — 탭 이름("반지")과 맞춘다 (#184)
            var header = CreateLabel("HeaderTitle", root, font, 32, GoldText, "반지 — 영구 성장");
            var headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(0f, 1f);
            headerRect.pivot = new Vector2(0f, 1f);
            headerRect.sizeDelta = new Vector2(400f, 44f);
            headerRect.anchoredPosition = new Vector2(20f, -10f);

            // 상단 포인트 박스 — Surface 면 + 금색 선, 호버하면 아래에 규칙 힌트가 뜬다 (#184)
            var pointBox = CreateObject("PointBox", root);
            var pointBoxRect = pointBox.GetComponent<RectTransform>();
            pointBoxRect.anchorMin = new Vector2(1f, 1f);
            pointBoxRect.anchorMax = new Vector2(1f, 1f);
            pointBoxRect.pivot = new Vector2(1f, 1f);
            pointBoxRect.sizeDelta = new Vector2(240f, 52f);
            pointBoxRect.anchoredPosition = new Vector2(-20f, -8f);
            pointBox.AddComponent<Image>().color = SurfaceFill;
            var pointBoxOutline = pointBox.AddComponent<Outline>();
            pointBoxOutline.effectColor = LineSecondary;
            pointBoxOutline.effectDistance = new Vector2(2f, -2f);
            var pointHint = pointBox.AddComponent<LegacyPointHint>();
            SetPrivate(panel, "_pointHint", pointHint);

            var pointRow = pointBox.AddComponent<HorizontalLayoutGroup>();
            pointRow.padding = new RectOffset(16, 16, 8, 8);
            pointRow.spacing = 8f;
            pointRow.childAlignment = TextAnchor.MiddleRight;
            pointRow.childControlWidth = true;
            pointRow.childControlHeight = true;
            pointRow.childForceExpandWidth = false;
            pointRow.childForceExpandHeight = true;

            var pointPrefix = CreateLabel("PointPrefix", pointBox, font, 20, TextMuted, "LP", TextAlignmentOptions.MidlineRight);
            pointPrefix.raycastTarget = false;
            SetPreferred(pointPrefix.gameObject, 36f, 0f);
            var pointLabel = CreateLabel("PointLabel", pointBox, font, 30, GoldText, "0", TextAlignmentOptions.MidlineRight);
            pointLabel.raycastTarget = false;
            SetPreferred(pointLabel.gameObject, 150f, 0f);
            SetPrivate(panel, "_pointLabel", pointLabel);

            var hintPanel = CreateObject("PointHintPanel", root);
            var hintRect = hintPanel.GetComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(1f, 1f);
            hintRect.anchorMax = new Vector2(1f, 1f);
            hintRect.pivot = new Vector2(1f, 1f);
            hintRect.sizeDelta = new Vector2(360f, 148f);
            hintRect.anchoredPosition = new Vector2(-20f, -68f);
            var hintImage = hintPanel.AddComponent<Image>();
            hintImage.color = PanelFill;
            hintImage.raycastTarget = false;
            var hintOutline = hintPanel.AddComponent<Outline>();
            hintOutline.effectColor = OutlineColor;
            hintOutline.effectDistance = new Vector2(2f, -2f);
            var hintText = CreateLabel("PointHintText", hintPanel, font, 20, TextCream, "레거시 포인트 (LP)", TextAlignmentOptions.TopLeft, 0f, true);
            hintText.raycastTarget = false;
            var hintTextRect = hintText.GetComponent<RectTransform>();
            hintTextRect.anchorMin = Vector2.zero;
            hintTextRect.anchorMax = Vector2.one;
            hintTextRect.offsetMin = new Vector2(16f, 12f);
            hintTextRect.offsetMax = new Vector2(-16f, -12f);
            SetPrivate(pointHint, "_hintPanel", hintPanel);
            SetPrivate(pointHint, "_hintText", hintText);
            hintPanel.SetActive(false);

            // 우측 툴팁 패널 (원작의 정보 패널)
            var tooltipGo = CreateObject("TooltipPanel", root);
            var tooltipRect = tooltipGo.GetComponent<RectTransform>();
            tooltipRect.anchorMin = new Vector2(1f, 0.5f);
            tooltipRect.anchorMax = new Vector2(1f, 0.5f);
            tooltipRect.pivot = new Vector2(1f, 0.5f);
            tooltipRect.sizeDelta = new Vector2(340f, 660f);
            tooltipRect.anchoredPosition = new Vector2(-15f, -25f);
            tooltipGo.AddComponent<Image>().color = new Color(0.10f, 0.08f, 0.06f, 0.95f);
            var tooltipOutline = tooltipGo.AddComponent<Outline>();
            tooltipOutline.effectColor = OutlineColor;
            tooltipOutline.effectDistance = new Vector2(2f, -2f);

            var tooltip = tooltipGo.AddComponent<RingTooltip>();
            SetPrivate(panel, "_tooltip", tooltip);

            var tooltipLayout = tooltipGo.AddComponent<VerticalLayoutGroup>();
            tooltipLayout.padding = new RectOffset(20, 20, 24, 24);
            tooltipLayout.spacing = 14f;
            tooltipLayout.childControlWidth = true;
            tooltipLayout.childControlHeight = false;
            tooltipLayout.childForceExpandWidth = true;
            tooltipLayout.childForceExpandHeight = false;

            var tipHeader = CreateLabel("TipHeader", tooltipGo, font, 22, GoldText, "ⓘ 반지 정보");
            var tipTitle = CreateLabel("TipTitle", tooltipGo, font, 28, Color.white, "반지 이름");
            CreateDivider("Divider1", tooltipGo);
            var tipDesc = CreateLabel("TipDesc", tooltipGo, font, 18, new Color(0.80f, 0.75f, 0.68f), "설명 문구", TextAlignmentOptions.TopLeft, 90f, true);
            CreateDivider("Divider2", tooltipGo);
            var tipLevel = CreateLabel("TipLevel", tooltipGo, font, 20, GoldText, "현재 레벨: Lv 0 / 5");
            var tipEffect = CreateLabel("TipEffect", tooltipGo, font, 20, new Color(0.60f, 0.85f, 0.60f), "효과 수치", TextAlignmentOptions.TopLeft, 110f, true);
            CreateDivider("Divider3", tooltipGo);
            var tipCost = CreateLabel("TipCost", tooltipGo, font, 22, GoldText, "다음 레벨: 3 포인트");

            SetPrivate(tooltip, "_rootPanel", tooltipGo);
            SetPrivate(tooltip, "_titleLabel", tipTitle);
            SetPrivate(tooltip, "_descriptionLabel", tipDesc);
            SetPrivate(tooltip, "_effectLabel", tipEffect);
            SetPrivate(tooltip, "_levelLabel", tipLevel);
            SetPrivate(tooltip, "_costLabel", tipCost);

            // 좌측 보석함 상자 (그리드)
            var boxGo = CreateObject("JewelryBox", root);
            var boxRect = boxGo.GetComponent<RectTransform>();
            boxRect.anchorMin = new Vector2(0f, 0.5f);
            boxRect.anchorMax = new Vector2(0f, 0.5f);
            boxRect.pivot = new Vector2(0f, 0.5f);
            boxRect.sizeDelta = new Vector2(710f, 660f);
            boxRect.anchoredPosition = new Vector2(15f, -25f);
            boxGo.AddComponent<Image>().color = BoxWood;
            var boxOutline = boxGo.AddComponent<Outline>();
            boxOutline.effectColor = OutlineColor;
            boxOutline.effectDistance = new Vector2(3f, -3f);

            var gridGo = CreateObject("SlotGrid", boxGo);
            var gridRect = gridGo.GetComponent<RectTransform>();
            gridRect.anchorMin = Vector2.zero;
            gridRect.anchorMax = Vector2.one;
            gridRect.offsetMin = new Vector2(16f, 16f);
            gridRect.offsetMax = new Vector2(-16f, -16f);

            var grid = gridGo.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(155f, 295f);
            grid.spacing = new Vector2(16f, 16f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            grid.childAlignment = TextAnchor.MiddleCenter;

            // 8종 반지 슬롯 생성
            var entriesList = new List<RingShopEntry>();
            var ringDefs = balance != null ? balance.Rings : new List<RingDef>();

            for (var i = 0; i < ringDefs.Count; i++)
            {
                var ringDef = ringDefs[i];
                var slot = CreateSlot($"RingSlot_{ringDef.Id}", gridGo, font, ringDef, tooltip);
                entriesList.Add(slot);
            }

            SetPrivate(panel, "_entries", entriesList.ToArray());

            System.IO.Directory.CreateDirectory("Assets/Prefabs/UI");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh();
            Debug.Log("[RingShopPrefabCreator] 반지 상점 프리팹 생성 완료: " + PrefabPath);
        }

        private static RingShopEntry CreateSlot(string name, GameObject parent, TMP_FontAsset font, RingDef def, RingTooltip tooltip)
        {
            var slot = CreateObject(name, parent);
            slot.AddComponent<Image>().color = SlotBg;
            var outline = slot.AddComponent<Outline>();
            outline.effectColor = OutlineColor;
            outline.effectDistance = new Vector2(2f, -2f);

            var entry = slot.AddComponent<RingShopEntry>();
            entry.SetTooltip(tooltip);
            entry.SetRingId(def.Id);

            var layout = slot.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 14, 14);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var nameLabel = CreateLabel("NameLabel", slot, font, 20, GoldText, def.DisplayName);
            SetPrivate(entry, "_nameLabel", nameLabel);

            // 반지 장식 비주얼 상자
            var iconBox = CreateObject("RingVisual", slot);
            iconBox.AddComponent<Image>().color = new Color(0.24f, 0.18f, 0.14f);
            var iconOutline = iconBox.AddComponent<Outline>();
            iconOutline.effectColor = GoldText;
            iconOutline.effectDistance = new Vector2(1f, -1f);
            SetPreferred(iconBox, 130f, 90f);

            var gem = CreateLabel("GemText", iconBox, font, 36, GoldText, "◆");
            var gemRect = gem.GetComponent<RectTransform>();
            gemRect.anchorMin = Vector2.zero;
            gemRect.anchorMax = Vector2.one;
            gemRect.offsetMin = Vector2.zero;
            gemRect.offsetMax = Vector2.zero;

            var levelLabel = CreateLabel("LevelLabel", slot, font, 18, Color.white, $"Lv 0 / {def.MaxLevel}");
            SetPrivate(entry, "_levelLabel", levelLabel);

            var reasonLabel = CreateLabel("ReasonLabel", slot, font, 16, new Color(0.90f, 0.45f, 0.45f), string.Empty);
            SetPrivate(entry, "_reasonLabel", reasonLabel);

            // 구매 버튼
            var btn = CreateButton("BuyButton", slot, font, new Vector2(130f, 48f), "구매",
                new Color(0.25f, 0.19f, 0.12f), GoldText, Color.white, 18);
            SetPrivate(entry, "_purchaseButton", btn);

            var costLabel = CreateLabel("CostText", btn.gameObject, font, 18, GoldText, $"{def.InitCost:N0}");
            var costRect = costLabel.GetComponent<RectTransform>();
            costRect.anchorMin = Vector2.zero;
            costRect.anchorMax = Vector2.one;
            costRect.offsetMin = Vector2.zero;
            costRect.offsetMax = Vector2.zero;
            SetPrivate(entry, "_costLabel", costLabel);

            return entry;
        }

        private static GameObject CreateObject(string name, GameObject parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null)
            {
                go.transform.SetParent(parent.transform, false);
            }
            return go;
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
            return button;
        }

        private static void CreateDivider(string name, GameObject parent)
        {
            var go = CreateObject(name, parent);
            go.AddComponent<Image>().color = OutlineColor;
            SetPreferred(go, 0f, 2f);
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

        private static void SetPrivate(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(target, value);
            }
        }
    }
}
