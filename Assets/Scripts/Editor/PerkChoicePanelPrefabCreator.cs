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
    /// PerkChoicePanel 프리팹을 생성하는 에디터 도구 (이슈 #184, 작업 6.12).
    /// 목업 캔버스 "퍽 선택 · 업그레이드 탭 목업" 의 퍽 선택 보드를 따른다 — 뒤 화면은 불투명 단색으로
    /// 완전히 가리고(알파 금지), 카드 3장은 Surface 면 + 패널 선, 호버·눌림에서만 금색 선.
    /// 같은 경로에 덮어써서 Managers.prefab 의 참조(GUID)를 유지한다. 구조 계약은 PerkChoiceUiChecks 가 검사한다.
    /// </summary>
    public static class PerkChoicePanelPrefabCreator
    {
        private const string BodyFontPath = "Assets/Materials/Fonts/NanumGothicBoldSDF.asset";
        private const string DisplayFontPath = "Assets/Materials/Fonts/NanumSquareBoldSDF.asset";
        private const string PrefabPath = "Assets/Prefabs/UI/PerkChoicePanel.prefab";

        private static readonly Color DimBackground = new Color(0.039f, 0.027f, 0.020f, 1f); // #0A0705
        private static readonly Color SurfaceFill = new Color(0.086f, 0.075f, 0.059f, 1f); // #16130F
        private static readonly Color ThumbFill = new Color(0.141f, 0.102f, 0.071f, 1f); // #241A12
        private static readonly Color PanelBorder = new Color(0.549f, 0.498f, 0.416f, 1f); // #8C7F6A
        private static readonly Color LineDim = new Color(0.290f, 0.259f, 0.224f, 1f); // #4A4239
        private static readonly Color Gold = new Color(0.910f, 0.690f, 0.294f, 1f); // #E8B04B
        private static readonly Color GoldLight = new Color(0.941f, 0.776f, 0.447f, 1f); // #F0C672
        private static readonly Color TextCream = new Color(0.992f, 0.953f, 0.875f, 1f); // #FDF3DF
        private static readonly Color TextMuted = new Color(0.784f, 0.718f, 0.604f, 1f); // #C8B79A

        [MenuItem("NCAI/UI/퍽 선택 패널 프리팹 생성")]
        public static void CreatePrefab()
        {
            var bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath);
            var displayFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DisplayFontPath);
            if (bodyFont == null || displayFont == null)
            {
                Debug.LogError($"[PerkChoicePanelPrefabCreator] 폰트 에셋을 찾을 수 없습니다: {BodyFontPath} / {DisplayFontPath}");
                return;
            }

            // 1. Root Canvas — HUD(0) 위, PausePanel(90) 위
            var rootGo = new GameObject("PerkChoicePanel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = rootGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = rootGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // 2. 예비 EventSystem (기본 비활성)
            var fallbackEsGo = new GameObject("FallbackEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            fallbackEsGo.transform.SetParent(rootGo.transform, false);
            fallbackEsGo.SetActive(false);

            // 3. 암전 — 불투명 단색으로 뒤를 완전히 가린다
            var dimGo = CreateStretchedObject("Dim", rootGo);
            var dimImage = dimGo.AddComponent<Image>();
            dimImage.color = DimBackground;
            dimImage.raycastTarget = true;

            // 4. 세로 스택: 제목 블록 / 카드 행 / 안내
            var stackGo = CreateStretchedObject("Stack", rootGo);
            var stack = stackGo.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(96, 96, 96, 96);
            stack.spacing = 48;
            stack.childAlignment = TextAnchor.MiddleCenter;
            stack.childControlWidth = true;
            stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;

            var headerGo = CreateObject("Header", stackGo);
            var headerLayout = headerGo.AddComponent<VerticalLayoutGroup>();
            headerLayout.spacing = 12;
            headerLayout.childAlignment = TextAnchor.UpperCenter;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = true;
            headerLayout.childForceExpandHeight = false;
            var title = CreateLabel("Title", headerGo, displayFont, 72, TextCream, TextAlignmentOptions.Center, "퍽을 한 장 고르세요");
            title.characterSpacing = 6;
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 84;
            var subtitle = CreateLabel("Subtitle", headerGo, bodyFont, 27, TextMuted, TextAlignmentOptions.Center, "하루가 끝났다 · 셋 중 하나만 오늘 하루 동안 붙는다");
            subtitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 36;

            var cardRowGo = CreateObject("CardRow", stackGo);
            cardRowGo.AddComponent<LayoutElement>().preferredHeight = 600;
            var cardRow = cardRowGo.AddComponent<HorizontalLayoutGroup>();
            cardRow.spacing = 36;
            cardRow.childAlignment = TextAnchor.MiddleCenter;
            cardRow.childControlWidth = false;
            cardRow.childControlHeight = false;
            cardRow.childForceExpandWidth = false;
            cardRow.childForceExpandHeight = false;

            for (var i = 1; i <= 3; i++)
            {
                CreateCard($"PerkCard{i}", cardRowGo, bodyFont, displayFont, i);
            }

            var hint = CreateLabel("Hint", stackGo, bodyFont, 24, TextMuted, TextAlignmentOptions.Center, "고르면 바로 적용된다 · 되돌릴 수 없다");
            hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 32;

            PrefabUtility.SaveAsPrefabAsset(rootGo, PrefabPath);
            Object.DestroyImmediate(rootGo);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[PerkChoicePanelPrefabCreator] 프리팹 생성 완료: {PrefabPath}");
        }

        /// <summary>
        /// 카드 한 장 (480×600). 카드 전체가 버튼이다.
        /// 루트 Image 가 **테두리**이고 Button 의 targetGraphic 이라, 틴트 색이 곧 테두리 색이다 —
        /// 기본 패널 선, 호버 금색, 눌림 밝은 금색. 면은 3px 안쪽의 자식 Image 다. Outline 컴포넌트는
        /// Button Transition 이 건드리지 못해 이 구조를 쓴다.
        /// </summary>
        private static void CreateCard(string name, GameObject parent, TMP_FontAsset bodyFont, TMP_FontAsset displayFont, int index)
        {
            var cardGo = CreateObject(name, parent);
            cardGo.GetComponent<RectTransform>().sizeDelta = new Vector2(480f, 600f);

            var frameImage = cardGo.AddComponent<Image>();
            frameImage.color = Color.white;
            frameImage.raycastTarget = true;

            var button = cardGo.AddComponent<Button>();
            button.targetGraphic = frameImage;
            var colors = button.colors;
            colors.normalColor = PanelBorder;
            colors.highlightedColor = Gold;
            colors.pressedColor = GoldLight;
            colors.selectedColor = PanelBorder;
            colors.disabledColor = LineDim;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            var fillGo = CreateStretchedObject("Fill", cardGo);
            var fillRect = fillGo.GetComponent<RectTransform>();
            fillRect.offsetMin = new Vector2(3f, 3f);
            fillRect.offsetMax = new Vector2(-3f, -3f);
            var fillImage = fillGo.AddComponent<Image>();
            fillImage.color = SurfaceFill;
            fillImage.raycastTarget = false;

            var cardLayout = fillGo.AddComponent<VerticalLayoutGroup>();
            cardLayout.padding = new RectOffset(40, 40, 40, 36);
            cardLayout.spacing = 24;
            cardLayout.childAlignment = TextAnchor.UpperCenter;
            cardLayout.childControlWidth = true;
            cardLayout.childControlHeight = true;
            cardLayout.childForceExpandWidth = true;
            cardLayout.childForceExpandHeight = false;

            var eyebrow = CreateLabel("Eyebrow", fillGo, bodyFont, 21, Gold, TextAlignmentOptions.Center, $"퍽 · {index}");
            eyebrow.characterSpacing = 12;
            eyebrow.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;

            var thumbGo = CreateObject("Thumb", fillGo);
            var thumbImage = thumbGo.AddComponent<Image>();
            thumbImage.color = ThumbFill;
            thumbImage.raycastTarget = false;
            var thumbOutline = thumbGo.AddComponent<Outline>();
            thumbOutline.effectColor = LineDim;
            thumbOutline.effectDistance = new Vector2(1f, -1f);
            var thumbLayout = thumbGo.AddComponent<LayoutElement>();
            thumbLayout.preferredHeight = 144;
            thumbLayout.preferredWidth = 144;
            var thumbMark = CreateLabel("Mark", thumbGo, displayFont, 64, Gold, TextAlignmentOptions.Center, "◆");
            StretchToParent(thumbMark.GetComponent<RectTransform>());

            var nameLabel = CreateLabel("NameLabel", fillGo, displayFont, 45, TextCream, TextAlignmentOptions.Center, "퍽 이름");
            nameLabel.characterSpacing = 3;
            nameLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 60;

            var dividerGo = CreateObject("Divider", fillGo);
            var dividerImage = dividerGo.AddComponent<Image>();
            dividerImage.color = PanelBorder;
            dividerImage.raycastTarget = false;
            dividerGo.AddComponent<LayoutElement>().preferredHeight = 2;

            var effectLabel = CreateLabel("EffectLabel", fillGo, bodyFont, 39, Gold, TextAlignmentOptions.Center, "효과");
            effectLabel.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;

            var durationLabel = CreateLabel("DurationLabel", fillGo, bodyFont, 24, TextMuted, TextAlignmentOptions.Center, "오늘 하루 동안");
            durationLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 32;

            var view = cardGo.AddComponent<PerkCardView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("_nameLabel").objectReferenceValue = nameLabel;
            serialized.FindProperty("_effectLabel").objectReferenceValue = effectLabel;
            serialized.FindProperty("_button").objectReferenceValue = button;
            serialized.ApplyModifiedPropertiesWithoutUndo();
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

        private static TextMeshProUGUI CreateLabel(string name, GameObject parent, TMP_FontAsset font, float fontSize, Color color, TextAlignmentOptions align, string text)
        {
            var go = CreateObject(name, parent);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.text = text;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
