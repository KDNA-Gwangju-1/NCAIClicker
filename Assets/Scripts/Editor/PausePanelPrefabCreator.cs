using System.IO;
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
    /// PausePanel 프리팹을 생성하는 에디터 도구 (이슈 #192, 작업 6.13).
    /// docs/UI_GUIDE.md 의 4배수 그리드, 불투명 단색 팔레트, WCAG 대비 규격을 준수합니다.
    /// SettingsPanel (#196) 프리팹을 자식으로 포함하여 일시정지 중 설정을 열 수 있도록 조립합니다.
    /// </summary>
    public static class PausePanelPrefabCreator
    {
        private const string FontPath = "Assets/Materials/Fonts/NanumGothicBoldSDF.asset";
        private const string SettingsPrefabPath = "Assets/Prefabs/UI/SettingsPanel.prefab";
        private const string OutputDir = "Assets/Prefabs/Resources/UI";
        private const string PrefabPath = OutputDir + "/PausePanel.prefab";

        // 불투명 색상 팔레트 (알파로 톤을 만들지 않음)
        private static readonly Color DimBackground = new Color(0.039f, 0.027f, 0.020f, 1f); // #0A0705
        private static readonly Color PanelFill = new Color(0.106f, 0.078f, 0.063f, 1f); // #1B1410
        private static readonly Color PanelBorder = new Color(0.549f, 0.498f, 0.416f, 1f); // #8C7F6A

        private static readonly Color TextCream = new Color(0.992f, 0.953f, 0.875f, 1f); // #FDF3DF
        private static readonly Color TextMuted = new Color(0.784f, 0.718f, 0.604f, 1f); // #C8B79A
        private static readonly Color TextNeutral = new Color(0.910f, 0.867f, 0.796f, 1f); // #E8DDCB
        private static readonly Color TextLoss = new Color(1.000f, 0.706f, 0.635f, 1f); // #FFB4A2
        private static readonly Color TextDark = new Color(0.086f, 0.075f, 0.059f, 1f); // #16130F

        // 버튼 강조 (계속하기)
        private static readonly Color PrimaryBtnFill = new Color(0.910f, 0.690f, 0.294f, 1f); // #E8B04B
        private static readonly Color PrimaryBtnBorder = new Color(0.941f, 0.776f, 0.447f, 1f); // #F0C672

        // 버튼 보조 (설정)
        private static readonly Color SecondaryBtnFill = new Color(0.086f, 0.075f, 0.059f, 1f); // #16130F
        private static readonly Color SecondaryBtnBorder = new Color(0.604f, 0.486f, 0.275f, 1f); // #9A7C46

        // 버튼 중립 (메인 메뉴)
        private static readonly Color NeutralBtnFill = new Color(0.231f, 0.208f, 0.188f, 1f); // #3B3530

        // 버튼 위험 (종료, 확인)
        private static readonly Color DangerBtnFill = new Color(0.165f, 0.082f, 0.071f, 1f); // #2A1512
        private static readonly Color DangerBtnBorder = new Color(0.753f, 0.541f, 0.353f, 1f); // #C08A5A

        [MenuItem("NCAI/UI/일시정지 패널 프리팹 생성")]
        public static void CreatePrefab()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font == null)
            {
                Debug.LogError($"[PausePanelPrefabCreator] 폰트 에셋을 찾을 수 없습니다: {FontPath}");
                return;
            }

            if (!Directory.Exists(OutputDir))
            {
                Directory.CreateDirectory(OutputDir);
            }

            // 1. Root Canvas
            var rootGo = new GameObject("PausePanel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var rootRect = rootGo.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(1920f, 1080f);

            var canvas = rootGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90; // HUD 위에 표시

            var scaler = rootGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var controller = rootGo.AddComponent<PausePanelController>();

            // 2. 예비 EventSystem (기본 비활성화)
            var fallbackEsGo = new GameObject("FallbackEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            fallbackEsGo.transform.SetParent(rootGo.transform, false);
            fallbackEsGo.SetActive(false);

            // 3. Panel Root (전체 화면 오버레이 + 암전 배경)
            var panelRootGo = CreateStretchedObject("PanelRoot", rootGo);
            var bgImage = panelRootGo.AddComponent<Image>();
            bgImage.color = DimBackground;
            bgImage.raycastTarget = true;

            // 4. 메인 패널 상자 (가로 720, 세로 740)
            var mainBoxGo = CreateObject("MainBox", panelRootGo);
            var mainBoxRect = mainBoxGo.GetComponent<RectTransform>();
            mainBoxRect.sizeDelta = new Vector2(720f, 740f);
            mainBoxRect.anchorMin = new Vector2(0.5f, 0.5f);
            mainBoxRect.anchorMax = new Vector2(0.5f, 0.5f);
            mainBoxRect.pivot = new Vector2(0.5f, 0.5f);
            mainBoxRect.anchoredPosition = Vector2.zero;

            var mainBoxImage = mainBoxGo.AddComponent<Image>();
            mainBoxImage.color = PanelFill;
            mainBoxImage.raycastTarget = true;

            var mainOutline = mainBoxGo.AddComponent<Outline>();
            mainOutline.effectColor = PanelBorder;
            mainOutline.effectDistance = new Vector2(2f, -2f);

            // 메인 박스 레이아웃 (childControlHeight = true 로 각 요소의 preferredHeight 적용)
            var mainLayout = mainBoxGo.AddComponent<VerticalLayoutGroup>();
            mainLayout.padding = new RectOffset(56, 56, 44, 40);
            mainLayout.spacing = 16;
            mainLayout.childAlignment = TextAnchor.UpperCenter;
            mainLayout.childControlWidth = true;
            mainLayout.childControlHeight = true;
            mainLayout.childForceExpandWidth = true;
            mainLayout.childForceExpandHeight = false;

            // 5. 헤더 (제목 + 부제)
            var titleLabel = CreateLabel("TitleText", mainBoxGo, font, 56, TextCream, TextAlignmentOptions.Center, "일시정지");
            titleLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;

            var subtitleLabel = CreateLabel("SubtitleText", mainBoxGo, font, 24, TextMuted, TextAlignmentOptions.Center, "1일차 · 스태미나 100 / 100");
            subtitleLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 32;

            // 구분선 (높이 2px 정밀 적용)
            var dividerGo = CreateObject("Divider", mainBoxGo);
            var dividerImage = dividerGo.AddComponent<Image>();
            dividerImage.color = PanelBorder;
            var dividerLayout = dividerGo.AddComponent<LayoutElement>();
            dividerLayout.preferredHeight = 2;

            // 6. 버튼들 (중간 컨테이너 없이 MainBox 직접 자식으로 배치하여 레이아웃 겹침 방지)
            var resumeButton = CreateButton("ResumeButton", mainBoxGo, font, 72, "계속하기", PrimaryBtnFill, PrimaryBtnBorder, TextDark, 32);
            var settingsButton = CreateButton("SettingsButton", mainBoxGo, font, 64, "설정", SecondaryBtnFill, SecondaryBtnBorder, TextCream, 28);
            var mainMenuButton = CreateButton("MainMenuButton", mainBoxGo, font, 64, "메인 메뉴", NeutralBtnFill, PanelBorder, TextNeutral, 28);
            var quitButton = CreateButton("QuitButton", mainBoxGo, font, 64, "종료", DangerBtnFill, DangerBtnBorder, TextLoss, 28);

            // 7. 경고 상자
            var warningBox = CreateObject("WarningBox", mainBoxGo);
            var warningBoxImage = warningBox.AddComponent<Image>();
            warningBoxImage.color = DangerBtnFill;
            var warningOutline = warningBox.AddComponent<Outline>();
            warningOutline.effectColor = DangerBtnBorder;
            warningOutline.effectDistance = new Vector2(2f, -2f);

            var warnLayout = warningBox.AddComponent<VerticalLayoutGroup>();
            warnLayout.padding = new RectOffset(20, 20, 14, 14);
            warnLayout.childControlWidth = true;
            warnLayout.childControlHeight = true;
            warnLayout.childForceExpandWidth = true;
            warnLayout.childForceExpandHeight = true;
            warningBox.AddComponent<LayoutElement>().preferredHeight = 76;

            CreateLabel("WarningText", warningBox, font, 22, TextLoss, TextAlignmentOptions.Center,
                "메인 메뉴·종료는 오늘 벌어들인 코인을 버립니다. 업그레이드와 납부 기록은 남습니다.");

            // 8. ESC 안내
            var escGuideLabel = CreateLabel("EscGuideText", mainBoxGo, font, 22, TextMuted, TextAlignmentOptions.Center, "ESC : 계속하기");
            escGuideLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;

            // 9. 확인 대화상자 (Confirm Dialog)
            var confirmDialogGo = CreateStretchedObject("ConfirmDialog", rootGo);
            var confirmDimImage = confirmDialogGo.AddComponent<Image>();
            confirmDimImage.color = DimBackground;
            confirmDimImage.raycastTarget = true;
            confirmDialogGo.SetActive(false);

            var confirmBoxGo = CreateObject("ConfirmBox", confirmDialogGo);
            var confirmBoxRect = confirmBoxGo.GetComponent<RectTransform>();
            confirmBoxRect.sizeDelta = new Vector2(640f, 360f);
            confirmBoxRect.anchorMin = new Vector2(0.5f, 0.5f);
            confirmBoxRect.anchorMax = new Vector2(0.5f, 0.5f);
            confirmBoxRect.pivot = new Vector2(0.5f, 0.5f);
            confirmBoxRect.anchoredPosition = Vector2.zero;

            var confirmBoxImage = confirmBoxGo.AddComponent<Image>();
            confirmBoxImage.color = PanelFill;
            confirmBoxImage.raycastTarget = true;

            var confirmOutline = confirmBoxGo.AddComponent<Outline>();
            confirmOutline.effectColor = PanelBorder;
            confirmOutline.effectDistance = new Vector2(2f, -2f);

            var confirmLayout = confirmBoxGo.AddComponent<VerticalLayoutGroup>();
            confirmLayout.padding = new RectOffset(40, 40, 36, 36);
            confirmLayout.spacing = 20;
            confirmLayout.childAlignment = TextAnchor.UpperCenter;
            confirmLayout.childControlWidth = true;
            confirmLayout.childControlHeight = false;
            confirmLayout.childForceExpandWidth = true;
            confirmLayout.childForceExpandHeight = false;

            var confirmTitleLabel = CreateLabel("ConfirmTitleText", confirmBoxGo, font, 36, TextCream, TextAlignmentOptions.Center, "정말 나가시겠습니까?");
            confirmTitleLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;

            var confirmMsgLabel = CreateLabel("ConfirmMessageText", confirmBoxGo, font, 24, TextLoss, TextAlignmentOptions.Center,
                "오늘 벌어들인 코인은 모두 사라집니다.\n계속 진행하시겠습니까?");
            confirmMsgLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;

            var confirmBtnRow = CreateObject("ConfirmButtonRow", confirmBoxGo);
            confirmBtnRow.AddComponent<LayoutElement>().preferredHeight = 60;
            var rowLayout = confirmBtnRow.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 20;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = true;

            var confirmOkBtn = CreateButton("ConfirmOkButton", confirmBtnRow, font, 56, "확인", DangerBtnFill, DangerBtnBorder, TextLoss, 26);
            var confirmCancelBtn = CreateButton("ConfirmCancelButton", confirmBtnRow, font, 56, "취소", NeutralBtnFill, PanelBorder, TextNeutral, 26);

            // 10. SettingsPanel 프리팹 자식으로 인스턴스화하여 조립 (#196)
            SettingsPanelController settingsController = null;
            var settingsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SettingsPrefabPath);
            if (settingsPrefab != null)
            {
                var settingsInstance = Object.Instantiate(settingsPrefab, rootGo.transform, false);
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
                Debug.LogWarning($"[PausePanelPrefabCreator] SettingsPanel 프리팹을 찾을 수 없습니다: {SettingsPrefabPath}");
            }

            // 11. PausePanelController 필드 바인딩
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("_panelRoot").objectReferenceValue = panelRootGo;
            serialized.FindProperty("_mainPanel").objectReferenceValue = mainBoxGo;
            serialized.FindProperty("_titleText").objectReferenceValue = titleLabel;
            serialized.FindProperty("_subtitleText").objectReferenceValue = subtitleLabel;

            serialized.FindProperty("_resumeButton").objectReferenceValue = resumeButton;
            serialized.FindProperty("_settingsButton").objectReferenceValue = settingsButton;
            serialized.FindProperty("_mainMenuButton").objectReferenceValue = mainMenuButton;
            serialized.FindProperty("_quitButton").objectReferenceValue = quitButton;

            serialized.FindProperty("_settingsPanel").objectReferenceValue = settingsController;

            serialized.FindProperty("_confirmDialogRoot").objectReferenceValue = confirmDialogGo;
            serialized.FindProperty("_confirmTitleText").objectReferenceValue = confirmTitleLabel;
            serialized.FindProperty("_confirmMessageText").objectReferenceValue = confirmMsgLabel;
            serialized.FindProperty("_confirmOkButton").objectReferenceValue = confirmOkBtn;
            serialized.FindProperty("_confirmCancelButton").objectReferenceValue = confirmCancelBtn;

            serialized.FindProperty("_fallbackEventSystem").objectReferenceValue = fallbackEsGo;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            // 프리팹 저장
            PrefabUtility.SaveAsPrefabAsset(rootGo, PrefabPath);
            Object.DestroyImmediate(rootGo);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[PausePanelPrefabCreator] 프리팹 생성 완료: {PrefabPath}");
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

        private static Button CreateButton(string name, GameObject parent, TMP_FontAsset font, float height, string labelText, Color fillColor, Color borderColor, Color textColor, float fontSize)
        {
            var btnGo = CreateObject(name, parent);
            var layout = btnGo.AddComponent<LayoutElement>();
            layout.preferredHeight = height;

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
            button.colors = colors;

            var label = CreateLabel("Label", btnGo, font, fontSize, textColor, TextAlignmentOptions.Center, labelText);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            return button;
        }
    }
}
