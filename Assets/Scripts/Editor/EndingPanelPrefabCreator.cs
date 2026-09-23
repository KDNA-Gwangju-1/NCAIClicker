using NCAIClicker.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// EndingPanel 프리팹을 생성하는 에디터 도구 (이슈 #271).
    /// 원작의 ① "모든 고지서를 냈다" ③ 통계 ④ "계속 부숴도 된다" 를 한 장에 합친다.
    /// 색·폰트·암전 규칙은 PerkChoicePanelPrefabCreator 를 따른다 — 뒤 화면은 불투명 단색으로 가린다.
    /// 같은 경로에 덮어써서 Managers.prefab 의 참조(GUID)를 유지한다.
    /// </summary>
    public static class EndingPanelPrefabCreator
    {
        private const string BodyFontPath = "Assets/Materials/Fonts/NanumGothicBoldSDF.asset";
        private const string DisplayFontPath = "Assets/Materials/Fonts/NanumSquareBoldSDF.asset";
        private const string PrefabPath = "Assets/Prefabs/UI/EndingPanel.prefab";

        private static readonly Color DimBackground = new Color(0.039f, 0.027f, 0.020f, 1f); // #0A0705
        private static readonly Color SurfaceFill = new Color(0.086f, 0.075f, 0.059f, 1f); // #16130F
        private static readonly Color PanelBorder = new Color(0.549f, 0.498f, 0.416f, 1f); // #8C7F6A
        private static readonly Color Gold = new Color(0.910f, 0.690f, 0.294f, 1f); // #E8B04B
        private static readonly Color GoldLight = new Color(0.941f, 0.776f, 0.447f, 1f); // #F0C672
        private static readonly Color TextDark = new Color(0.141f, 0.102f, 0.071f, 1f); // #241A12
        private static readonly Color TextCream = new Color(0.992f, 0.953f, 0.875f, 1f); // #FDF3DF
        private static readonly Color TextMuted = new Color(0.784f, 0.718f, 0.604f, 1f); // #C8B79A

        [MenuItem("NCAI/UI/엔딩 패널 프리팹 생성")]
        public static void CreatePrefab()
        {
            var bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath);
            var displayFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DisplayFontPath);
            if (bodyFont == null || displayFont == null)
            {
                Debug.LogError($"[EndingPanelPrefabCreator] 폰트 에셋을 찾을 수 없습니다: {BodyFontPath} / {DisplayFontPath}");
                return;
            }

            // 퍽 선택(100) 위 — 엔딩은 퍽 뒤, 새 고지서 모달 앞에 뜬다.
            var rootGo = new GameObject("EndingPanel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = rootGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 110;

            var scaler = rootGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var fallbackEsGo = new GameObject("FallbackEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            fallbackEsGo.transform.SetParent(rootGo.transform, false);
            fallbackEsGo.SetActive(false);

            var dimGo = CreateStretchedObject("Dim", rootGo);
            var dimImage = dimGo.AddComponent<Image>();
            dimImage.color = DimBackground;
            dimImage.raycastTarget = true;

            var stackGo = CreateStretchedObject("Stack", rootGo);
            var stack = stackGo.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(96, 96, 96, 96);
            stack.spacing = 48;
            stack.childAlignment = TextAnchor.MiddleCenter;
            stack.childControlWidth = true;
            stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;

            var eyebrow = CreateLabel("Eyebrow", stackGo, bodyFont, 24, Gold, "모든 고지서 완납");
            eyebrow.characterSpacing = 12;
            eyebrow.gameObject.AddComponent<LayoutElement>().preferredHeight = 32;

            var title = CreateLabel("Title", stackGo, displayFont, 72, TextCream, "고지서는 다 냈다");
            title.characterSpacing = 6;
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 84;

            var subtitle = CreateLabel("Subtitle", stackGo, bodyFont, 28, TextMuted, "더 벌고 싶으면 계속 부숴도 된다");
            subtitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;

            var statRowGo = CreateObject("StatRow", stackGo);
            statRowGo.AddComponent<LayoutElement>().preferredHeight = 176;
            var statRow = statRowGo.AddComponent<HorizontalLayoutGroup>();
            statRow.spacing = 32;
            statRow.childAlignment = TextAnchor.MiddleCenter;
            statRow.childControlWidth = false;
            statRow.childControlHeight = false;
            statRow.childForceExpandWidth = false;
            statRow.childForceExpandHeight = false;

            var daysText = CreateStat("DaysStat", statRowGo, bodyFont, displayFont, "걸린 날", "0일");
            var cycleText = CreateStat("CycleStat", statRowGo, bodyFont, displayFont, "회차", "1회차");
            var totalPaidText = CreateStat("TotalPaidStat", statRowGo, bodyFont, displayFont, "총 납부액", "$0");

            var buttonRowGo = CreateObject("ButtonRow", stackGo);
            buttonRowGo.AddComponent<LayoutElement>().preferredHeight = 80;
            var buttonRow = buttonRowGo.AddComponent<HorizontalLayoutGroup>();
            buttonRow.childAlignment = TextAnchor.MiddleCenter;
            buttonRow.childControlWidth = false;
            buttonRow.childControlHeight = false;
            buttonRow.childForceExpandWidth = false;
            buttonRow.childForceExpandHeight = false;
            var continueButton = CreateButton("ContinueButton", buttonRowGo, displayFont, "계속 부수기");

            var view = rootGo.AddComponent<EndingPanelView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("_daysText").objectReferenceValue = daysText;
            serialized.FindProperty("_cycleText").objectReferenceValue = cycleText;
            serialized.FindProperty("_totalPaidText").objectReferenceValue = totalPaidText;
            serialized.FindProperty("_continueButton").objectReferenceValue = continueButton;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(rootGo, PrefabPath);
            Object.DestroyImmediate(rootGo);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[EndingPanelPrefabCreator] 프리팹 생성 완료: {PrefabPath}");
        }

        /// <summary>통계 칸 하나 (400×176). 위에 이름, 아래에 금색 값. 값 라벨을 돌려준다.</summary>
        private static TextMeshProUGUI CreateStat(string name, GameObject parent, TMP_FontAsset bodyFont, TMP_FontAsset displayFont, string label, string placeholder)
        {
            var boxGo = CreateObject(name, parent);
            boxGo.GetComponent<RectTransform>().sizeDelta = new Vector2(400f, 176f);
            var boxImage = boxGo.AddComponent<Image>();
            boxImage.color = SurfaceFill;
            boxImage.raycastTarget = false;
            var outline = boxGo.AddComponent<Outline>();
            outline.effectColor = PanelBorder;
            outline.effectDistance = new Vector2(2f, -2f);

            var layout = boxGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 28, 28);
            layout.spacing = 12;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var labelText = CreateLabel("Label", boxGo, bodyFont, 24, TextMuted, label);
            labelText.gameObject.AddComponent<LayoutElement>().preferredHeight = 32;
            var valueText = CreateLabel("Value", boxGo, displayFont, 56, Gold, placeholder);
            valueText.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;
            return valueText;
        }

        /// <summary>금색 면 버튼 (360×80). 눌림에서만 밝아진다.</summary>
        private static Button CreateButton(string name, GameObject parent, TMP_FontAsset font, string label)
        {
            var buttonGo = CreateObject(name, parent);
            buttonGo.GetComponent<RectTransform>().sizeDelta = new Vector2(360f, 80f);
            var image = buttonGo.AddComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = true;

            var button = buttonGo.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Gold;
            colors.highlightedColor = GoldLight;
            colors.pressedColor = GoldLight;
            colors.selectedColor = Gold;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            var text = CreateLabel("Label", buttonGo, font, 32, TextDark, label);
            StretchToParent(text.GetComponent<RectTransform>());
            return button;
        }

        private static GameObject CreateObject(string name, GameObject parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        private static GameObject CreateStretchedObject(string name, GameObject parent)
        {
            var go = CreateObject(name, parent);
            StretchToParent(go.GetComponent<RectTransform>());
            return go;
        }

        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static TextMeshProUGUI CreateLabel(string name, GameObject parent, TMP_FontAsset font, float fontSize, Color color, string text)
        {
            var go = CreateObject(name, parent);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.text = text;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
