using System.IO;
using NCAIClicker.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// MainMenuPanel 프리팹을 생성하고 MainMenu 씬에 배치하는 에디터 도구 (이슈 #184, 작업 6.12).
    /// 통합 목업(MainMenu · OverwriteConfirm 보드)과 디자인 시스템 2차(2026-09-22)를 따른다 —
    /// 버튼은 Primary(금 채움) 하나와 Base(어두운 면 + 패널 선색) 셋뿐이고, 암전은 알파가 아니라
    /// 불투명 단색이다. 치수는 목업 1280×720 값의 1.5배(1920×1080), 4배수 그리드.
    /// 팔레트 상수 이름은 PausePanelPrefabCreator 와 같은 규칙을 쓴다.
    /// </summary>
    public static class MainMenuPrefabCreator
    {
        private const string BodyFontPath = "Assets/Materials/Fonts/NanumGothicBoldSDF.asset";
        private const string DisplayFontPath = "Assets/Materials/Fonts/NanumSquareBoldSDF.asset";
        private const string SettingsPrefabPath = "Assets/Prefabs/UI/SettingsPanel.prefab";
        private const string OutputDir = "Assets/Prefabs/UI";
        private const string PrefabPath = OutputDir + "/MainMenuPanel.prefab";
        private const string ScenePath = "Assets/Scenes/MainMenu.unity";
        private const string SceneCanvasName = "Canvas";

        // 바탕 · 선 (불투명 단색 — 알파로 톤을 만들지 않음)
        private static readonly Color DimBackground = new Color(0.039f, 0.027f, 0.020f, 1f); // #0A0705
        private static readonly Color PanelFill = new Color(0.106f, 0.078f, 0.063f, 1f); // #1B1410
        private static readonly Color PanelBorder = new Color(0.549f, 0.498f, 0.416f, 1f); // #8C7F6A
        private static readonly Color DangerBoxFill = new Color(0.165f, 0.082f, 0.071f, 1f); // #2A1512
        private static readonly Color DangerBoxBorder = new Color(0.753f, 0.541f, 0.353f, 1f); // #C08A5A

        // 글자
        private static readonly Color TextCream = new Color(0.992f, 0.953f, 0.875f, 1f); // #FDF3DF
        private static readonly Color TextMuted = new Color(0.784f, 0.718f, 0.604f, 1f); // #C8B79A
        private static readonly Color TextLoss = new Color(1.000f, 0.706f, 0.635f, 1f); // #FFB4A2
        private static readonly Color TextDark = new Color(0.086f, 0.075f, 0.059f, 1f); // #16130F
        private static readonly Color TextGold = new Color(0.910f, 0.690f, 0.294f, 1f); // #E8B04B

        // 버튼 2종
        private static readonly Color PrimaryBtnFill = new Color(0.910f, 0.690f, 0.294f, 1f); // #E8B04B
        private static readonly Color PrimaryBtnBorder = new Color(0.941f, 0.776f, 0.447f, 1f); // #F0C672
        private static readonly Color BaseBtnFill = new Color(0.086f, 0.075f, 0.059f, 1f); // #16130F
        private static readonly Color BaseBtnBorder = PanelBorder;

        [MenuItem("NCAI/UI/메인 메뉴 프리팹 생성")]
        public static void CreatePrefab()
        {
            var bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath);
            var displayFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DisplayFontPath);
            if (bodyFont == null || displayFont == null)
            {
                Debug.LogError($"[MainMenuPrefabCreator] 폰트 에셋을 찾을 수 없습니다: {BodyFontPath} / {DisplayFontPath}");
                return;
            }

            if (!Directory.Exists(OutputDir))
            {
                Directory.CreateDirectory(OutputDir);
            }

            // 1. Root Canvas
            var rootGo = new GameObject("MainMenuPanel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = rootGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = rootGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var controller = rootGo.AddComponent<MainMenuController>();

            // 2. 바탕 (전체 화면 불투명)
            var backgroundGo = CreateStretchedObject("Background", rootGo);
            var backgroundImage = backgroundGo.AddComponent<Image>();
            backgroundImage.color = DimBackground;
            backgroundImage.raycastTarget = false;

            // 3. 타이틀 블록 — 좌측 상단 (목업 96,104 → 144,156)
            var titleBlockGo = CreateObject("TitleBlock", rootGo);
            var titleBlockRect = titleBlockGo.GetComponent<RectTransform>();
            SetTopLeft(titleBlockRect, 144f, 156f, 900f, 340f);
            var titleLayout = titleBlockGo.AddComponent<VerticalLayoutGroup>();
            titleLayout.spacing = 8;
            titleLayout.childAlignment = TextAnchor.UpperLeft;
            titleLayout.childControlWidth = true;
            titleLayout.childControlHeight = true;
            titleLayout.childForceExpandWidth = true;
            titleLayout.childForceExpandHeight = false;

            // 위계: 게임 이름(대제목) → 장르(소제목) → 원작 표기(본문, 흐린 색). 게임 이름은 NCAI CLICKER 이고
            // 원작 「Bills Must Be Paid」의 이름을 제목처럼 쓰지 않는다 — 모작임은 맨 아래 줄에서 밝힌다 (#45).
            var title = CreateLabel("TitleText", titleBlockGo, displayFont, 112, TextCream, TextAlignmentOptions.Left, "NCAI\nCLICKER");
            title.lineSpacing = -12;
            title.characterSpacing = 4;
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 248;

            var tagline = CreateLabel("TaglineText", titleBlockGo, bodyFont, 28, TextGold, TextAlignmentOptions.Left, "시간 제한형 액티브 인크리멘탈");
            tagline.gameObject.AddComponent<LayoutElement>().preferredHeight = 36;

            var credit = CreateLabel("OriginalCreditText", titleBlockGo, bodyFont, 24, TextMuted, TextAlignmentOptions.Left, "「Bills Must Be Paid」 (Rike Games) 모작");
            credit.gameObject.AddComponent<LayoutElement>().preferredHeight = 32;

            // 4. 버튼 열 — 좌측 (목업 96,372 · 폭 400 → 144,558 · 폭 600)
            var buttonColumnGo = CreateObject("ButtonColumn", rootGo);
            var buttonColumnRect = buttonColumnGo.GetComponent<RectTransform>();
            SetTopLeft(buttonColumnRect, 144f, 558f, 600f, 396f);
            var columnLayout = buttonColumnGo.AddComponent<VerticalLayoutGroup>();
            columnLayout.spacing = 24;
            columnLayout.childAlignment = TextAnchor.UpperCenter;
            columnLayout.childControlWidth = true;
            columnLayout.childControlHeight = true;
            columnLayout.childForceExpandWidth = true;
            columnLayout.childForceExpandHeight = false;

            var newRunButton = CreateButton("NewRunButton", buttonColumnGo, displayFont, 96, "새 회차 시작", PrimaryBtnFill, PrimaryBtnBorder, TextDark, 40, out _);
            var continueButton = CreateButton("ContinueButton", buttonColumnGo, bodyFont, 84, "이어하기", BaseBtnFill, BaseBtnBorder, TextCream, 32, out var continueLabel);
            var settingsButton = CreateButton("SettingsButton", buttonColumnGo, bodyFont, 84, "설정", BaseBtnFill, BaseBtnBorder, TextCream, 32, out _);
            var quitButton = CreateButton("QuitButton", buttonColumnGo, bodyFont, 84, "종료", BaseBtnFill, BaseBtnBorder, TextCream, 32, out _);

            // 이어하기 옆 "· N일차" 캡션은 라벨 안에 리치 텍스트로 넣는다 — 컨트롤러가 채운다
            continueLabel.richText = true;

            // 5. 우하단 캡션 (목업 right 96 · bottom 56 → 144 · 84)
            var footerGo = CreateObject("FooterText", rootGo);
            var footerRect = footerGo.GetComponent<RectTransform>();
            footerRect.anchorMin = new Vector2(1f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(1f, 0f);
            footerRect.anchoredPosition = new Vector2(-144f, 84f);
            footerRect.sizeDelta = new Vector2(720f, 72f);
            var footerText = footerGo.AddComponent<TextMeshProUGUI>();
            footerText.font = bodyFont;
            footerText.fontSize = 24;
            footerText.color = TextMuted;
            footerText.alignment = TextAlignmentOptions.BottomRight;
            footerText.text = "저장 없음\nv0.1 · NCAI Team Two";
            footerText.raycastTarget = false;

            // 6. 덮어쓰기 확인 (불투명 암전 + 패널 750)
            var confirmRootGo = CreateStretchedObject("OverwriteConfirmPanel", rootGo);
            var confirmDim = confirmRootGo.AddComponent<Image>();
            confirmDim.color = DimBackground;
            confirmDim.raycastTarget = true;
            confirmRootGo.SetActive(false);

            var confirmBoxGo = CreateObject("DialogBox", confirmRootGo);
            var confirmBoxRect = confirmBoxGo.GetComponent<RectTransform>();
            confirmBoxRect.anchorMin = new Vector2(0.5f, 0.5f);
            confirmBoxRect.anchorMax = new Vector2(0.5f, 0.5f);
            confirmBoxRect.pivot = new Vector2(0.5f, 0.5f);
            confirmBoxRect.anchoredPosition = Vector2.zero;
            confirmBoxRect.sizeDelta = new Vector2(750f, 0f);
            var confirmFitter = confirmBoxGo.AddComponent<ContentSizeFitter>();
            confirmFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var confirmBoxImage = confirmBoxGo.AddComponent<Image>();
            confirmBoxImage.color = PanelFill;
            confirmBoxImage.raycastTarget = true;
            var confirmOutline = confirmBoxGo.AddComponent<Outline>();
            confirmOutline.effectColor = PanelBorder;
            confirmOutline.effectDistance = new Vector2(2f, -2f);

            var confirmLayout = confirmBoxGo.AddComponent<VerticalLayoutGroup>();
            confirmLayout.padding = new RectOffset(60, 60, 48, 48);
            confirmLayout.spacing = 32;
            confirmLayout.childAlignment = TextAnchor.UpperCenter;
            confirmLayout.childControlWidth = true;
            confirmLayout.childControlHeight = true;
            confirmLayout.childForceExpandWidth = true;
            confirmLayout.childForceExpandHeight = false;

            var confirmHeaderGo = CreateObject("Header", confirmBoxGo);
            var headerLayout = confirmHeaderGo.AddComponent<VerticalLayoutGroup>();
            headerLayout.spacing = 8;
            headerLayout.childAlignment = TextAnchor.UpperCenter;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = true;
            headerLayout.childForceExpandHeight = false;
            var confirmTitle = CreateLabel("ConfirmTitleText", confirmHeaderGo, displayFont, 60, TextCream, TextAlignmentOptions.Center, "새 회차 시작");
            confirmTitle.characterSpacing = 8;
            confirmTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 72;
            var confirmSubtitle = CreateLabel("ConfirmSubtitleText", confirmHeaderGo, bodyFont, 26, TextMuted, TextAlignmentOptions.Center, "저장된 회차가 있다");
            confirmSubtitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 36;

            var dividerGo = CreateObject("Divider", confirmBoxGo);
            dividerGo.AddComponent<Image>().color = PanelBorder;
            dividerGo.AddComponent<LayoutElement>().preferredHeight = 2;

            var warningBoxGo = CreateObject("WarningBox", confirmBoxGo);
            warningBoxGo.AddComponent<Image>().color = DangerBoxFill;
            var warningOutline = warningBoxGo.AddComponent<Outline>();
            warningOutline.effectColor = DangerBoxBorder;
            warningOutline.effectDistance = new Vector2(1f, -1f);
            var warningLayout = warningBoxGo.AddComponent<VerticalLayoutGroup>();
            warningLayout.padding = new RectOffset(24, 24, 20, 20);
            warningLayout.childControlWidth = true;
            warningLayout.childControlHeight = true;
            warningLayout.childForceExpandWidth = true;
            warningLayout.childForceExpandHeight = false;
            var warningText = CreateLabel("WarningText", warningBoxGo, bodyFont, 24, TextLoss, TextAlignmentOptions.Center,
                "새 회차를 시작하면 지금 저장된 회차를 덮어쓴다.\n반지와 레거시 포인트는 남는다.");
            warningText.gameObject.AddComponent<LayoutElement>().preferredHeight = 72;

            var confirmRowGo = CreateObject("ButtonRow", confirmBoxGo);
            confirmRowGo.AddComponent<LayoutElement>().preferredHeight = 84;
            var rowLayout = confirmRowGo.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 24;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = true;
            var cancelButton = CreateButton("NoButton", confirmRowGo, bodyFont, 84, "취소", BaseBtnFill, BaseBtnBorder, TextCream, 32, out _);
            var overwriteButton = CreateButton("YesButton", confirmRowGo, bodyFont, 84, "덮어쓰기", BaseBtnFill, BaseBtnBorder, TextCream, 32, out _);

            var escHint = CreateLabel("EscHintText", confirmBoxGo, bodyFont, 24, TextMuted, TextAlignmentOptions.Center, "ESC — 취소");
            escHint.gameObject.AddComponent<LayoutElement>().preferredHeight = 32;

            // 7. SettingsPanel 프리팹 자식으로 조립 (#196 · PausePanel 과 같은 방식)
            SettingsPanelController settingsController = null;
            var settingsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SettingsPrefabPath);
            if (settingsPrefab != null)
            {
                var settingsInstance = (GameObject)PrefabUtility.InstantiatePrefab(settingsPrefab, rootGo.transform);
                settingsInstance.name = "SettingsPanel";
                settingsInstance.SetActive(false);
                settingsController = settingsInstance.GetComponent<SettingsPanelController>();
                var settingsRect = settingsInstance.GetComponent<RectTransform>();
                if (settingsRect != null)
                {
                    settingsRect.anchorMin = new Vector2(0.5f, 0.5f);
                    settingsRect.anchorMax = new Vector2(0.5f, 0.5f);
                    settingsRect.pivot = new Vector2(0.5f, 0.5f);
                    settingsRect.anchoredPosition = Vector2.zero;
                    settingsRect.localScale = Vector3.one;
                }
            }
            else
            {
                Debug.LogWarning($"[MainMenuPrefabCreator] SettingsPanel 프리팹을 찾을 수 없습니다: {SettingsPrefabPath}");
            }

            // 8. MainMenuController 필드 바인딩
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("_newRunButton").objectReferenceValue = newRunButton;
            serialized.FindProperty("_continueButton").objectReferenceValue = continueButton;
            serialized.FindProperty("_quitButton").objectReferenceValue = quitButton;
            serialized.FindProperty("_overwriteConfirmPanel").objectReferenceValue = confirmRootGo;
            serialized.FindProperty("_overwriteConfirmYesButton").objectReferenceValue = overwriteButton;
            serialized.FindProperty("_overwriteConfirmNoButton").objectReferenceValue = cancelButton;
            serialized.FindProperty("_settingsButton").objectReferenceValue = settingsButton;
            serialized.FindProperty("_settingsPanel").objectReferenceValue = settingsController;
            serialized.FindProperty("_continueLabel").objectReferenceValue = continueLabel;
            serialized.FindProperty("_saveStatusText").objectReferenceValue = footerText;
            serialized.FindProperty("_overwriteConfirmSubtitle").objectReferenceValue = confirmSubtitle;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(rootGo, PrefabPath);
            Object.DestroyImmediate(rootGo);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[MainMenuPrefabCreator] 프리팹 생성 완료: {PrefabPath}");
        }

        /// <summary>
        /// MainMenu 씬의 기존 그레이박스 Canvas 를 MainMenuPanel 프리팹 인스턴스로 교체하고 저장한다.
        /// Main Camera · EventSystem 은 그대로 둔다.
        /// </summary>
        [MenuItem("NCAI/UI/메인 메뉴 씬에 배치")]
        public static void PlaceInScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[MainMenuPrefabCreator] 프리팹이 없습니다. 먼저 '메인 메뉴 프리팹 생성'을 실행하세요: {PrefabPath}");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var replaced = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == SceneCanvasName || root.name == prefab.name)
                {
                    Object.DestroyImmediate(root);
                    replaced = true;
                }
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = prefab.name;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[MainMenuPrefabCreator] 씬 배치 완료: {ScenePath} (기존 Canvas 교체: {replaced})");
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
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return go;
        }

        private static void SetTopLeft(RectTransform rect, float left, float top, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(width, height);
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

        private static Button CreateButton(string name, GameObject parent, TMP_FontAsset font, float height, string labelText,
            Color fillColor, Color borderColor, Color textColor, float fontSize, out TextMeshProUGUI label)
        {
            var btnGo = CreateObject(name, parent);
            btnGo.AddComponent<LayoutElement>().preferredHeight = height;

            var img = btnGo.AddComponent<Image>();
            img.color = fillColor;
            img.raycastTarget = true;

            var outline = btnGo.AddComponent<Outline>();
            outline.effectColor = borderColor;
            outline.effectDistance = new Vector2(2f, -2f);

            var button = btnGo.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            button.colors = colors;

            label = CreateLabel("Label", btnGo, font, fontSize, textColor, TextAlignmentOptions.Center, labelText);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            return button;
        }
    }
}
