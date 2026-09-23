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
    /// 저금통 도감 탭 프리팹을 생성한다 (#299). 원작 상점의 저금통 선반처럼 해금 순서대로 카드를 한 줄로 놓는다.
    /// 카드 목록은 BalanceData.GetUnlockOrder 에서 만든다 — targets.csv 를 고쳤으면 이 메뉴를 다시 돌린다
    /// (UnlockChecks 가 어긋남을 잡는다). 고지서 패널 프리팹보다 먼저 만들어야 탭에 연결된다.
    /// </summary>
    public static class CreatureCodexPrefabCreator
    {
        public const string PrefabPath = "Assets/Prefabs/UI/CreatureCodexPanel.prefab";
        private const string FontPath = "Assets/Materials/Fonts/NanumGothicBoldSDF.asset";

        private const float CardWidth = 196f;
        private const float CardHeight = 448f;
        private const float CardSpacing = 16f;

        /// <summary>
        /// 카드마다 미리보기 모델을 둘 월드 좌표 간격. CreaturePreview 카메라의 far clip(10)보다 멀어야
        /// 옆 카드 모델이 찍히지 않는다. 결과 화면 미리보기는 x 0 을 쓴다.
        /// </summary>
        private const float StageSpacing = 40f;

        private static readonly Color SlotBg = new Color(0.12f, 0.09f, 0.07f);
        private static readonly Color PreviewBg = new Color(0.20f, 0.15f, 0.11f);
        private static readonly Color OutlineColor = new Color(0.45f, 0.35f, 0.22f);
        private static readonly Color GoldText = new Color(0.95f, 0.78f, 0.38f);
        private static readonly Color TextCream = new Color(0.992f, 0.953f, 0.875f, 1f); // #FDF3DF
        private static readonly Color TextMuted = new Color(0.784f, 0.718f, 0.604f, 1f); // #C8B79A

        [MenuItem("NCAI/UI/저금통 도감 프리팹 생성")]
        public static void CreatePrefab()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");

            var root = CreateObject("CreatureCodexPanel", null);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(1100f, 760f);
            var panel = root.AddComponent<CreatureCodexPanel>();
            SetPrivate(panel, "_balanceData", balance);

            var header = CreateLabel("HeaderTitle", root, font, 32, GoldText, "저금통 도감", TextAlignmentOptions.MidlineLeft);
            Place(header.gameObject, new Vector2(0f, 1f), new Vector2(480f, 44f), new Vector2(20f, -12f));

            var earned = CreateLabel("EarnedLabel", root, font, 24, TextCream, "이번 회차 누적 $0", TextAlignmentOptions.MidlineRight);
            Place(earned.gameObject, new Vector2(1f, 1f), new Vector2(480f, 44f), new Vector2(-20f, -12f));
            SetPrivate(panel, "_earnedLabel", earned);

            var caption = CreateLabel("RuleCaption", root, font, 20, TextMuted,
                "이번 회차 누적 수입으로 해금된다. 파산하면 처음부터 다시 모은다.", TextAlignmentOptions.MidlineLeft);
            Place(caption.gameObject, new Vector2(0f, 1f), new Vector2(1000f, 28f), new Vector2(20f, -60f));

            var shelf = CreateObject("Shelf", root);
            var shelfRect = shelf.GetComponent<RectTransform>();
            shelfRect.anchorMin = new Vector2(0.5f, 1f);
            shelfRect.anchorMax = new Vector2(0.5f, 1f);
            shelfRect.pivot = new Vector2(0.5f, 1f);
            shelfRect.sizeDelta = new Vector2(1060f, CardHeight);
            shelfRect.anchoredPosition = new Vector2(0f, -112f);
            var row = shelf.AddComponent<HorizontalLayoutGroup>();
            row.spacing = CardSpacing;
            row.childAlignment = TextAnchor.UpperCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            var entries = new List<CreatureCodexEntry>();
            var order = balance != null ? balance.GetUnlockOrder() : new List<TargetDef>();
            for (var i = 0; i < order.Count; i++)
            {
                entries.Add(CreateCard(shelf, font, order[i].Id, i));
            }
            SetPrivate(panel, "_entries", entries.ToArray());

            System.IO.Directory.CreateDirectory("Assets/Prefabs/UI");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh();
            Debug.Log("[CreatureCodexPrefabCreator] 저금통 도감 프리팹 생성 완료: " + PrefabPath);
        }

        private static CreatureCodexEntry CreateCard(GameObject parent, TMP_FontAsset font, string targetId, int index)
        {
            var card = CreateObject("Card_" + targetId, parent);
            card.AddComponent<Image>().color = SlotBg;
            var outline = card.AddComponent<Outline>();
            outline.effectColor = OutlineColor;
            outline.effectDistance = new Vector2(2f, -2f);
            SetPreferred(card, CardWidth, CardHeight);

            // 자식 높이는 preferredHeight 로 정한다 — childControlHeight 가 false 면 기본 높이 100 이 쓰인다 (#261).
            var layout = card.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var entry = card.AddComponent<CreatureCodexEntry>();
            entry.SetTargetId(targetId);

            var previewBox = CreateObject("PreviewBox", card);
            previewBox.AddComponent<Image>().color = PreviewBg;
            SetPreferred(previewBox, 0f, 196f);
            var preview = ResultUIPrefabCreator.AttachCreaturePreview(previewBox);
            var serialized = new SerializedObject(preview);
            serialized.FindProperty("_stageOrigin").vector3Value = new Vector3(StageSpacing * (index + 1), -500f, 0f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            SetPrivate(entry, "_preview", preview);
            SetPrivate(entry, "_previewImage", preview.GetComponent<RawImage>());

            SetPrivate(entry, "_nameLabel", CreateLabel("NameLabel", card, font, 28, GoldText, "???", TextAlignmentOptions.Center, 36f));
            SetPrivate(entry, "_roleLabel", CreateLabel("RoleLabel", card, font, 20, TextCream, string.Empty, TextAlignmentOptions.Top, 56f, true));
            SetPrivate(entry, "_statLabel", CreateLabel("StatLabel", card, font, 20, TextMuted, string.Empty, TextAlignmentOptions.Center, 28f));
            SetPrivate(entry, "_unlockLabel", CreateLabel("UnlockLabel", card, font, 20, GoldText, string.Empty, TextAlignmentOptions.Top, 56f, true));
            return entry;
        }

        private static void Place(GameObject go, Vector2 corner, Vector2 size, Vector2 position)
        {
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = corner;
            rect.anchorMax = corner;
            rect.pivot = corner;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
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
            label.raycastTarget = false;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            if (height > 0f)
            {
                SetPreferred(go, 0f, height);
            }
            return label;
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
            if (field == null)
            {
                Debug.LogError($"[CreatureCodexPrefabCreator] {target.GetType().Name}.{fieldName} 필드가 없다.");
                return;
            }
            field.SetValue(target, value);
        }
    }
}
