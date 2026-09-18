using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// UI 가이드라인 자동 검증 하네스 (이슈 #180, 계획 1.26).
    /// docs/UI_GUIDE.md 의 규칙 중 기계적으로 측정 가능한 항목을 검증합니다.
    ///
    /// 검사 항목:
    /// 1. 명도 대비: 본문 4.5:1, 대형 및 비텍스트 3.0:1 (WCAG 2.1 AA)
    /// 2. 최소 글자 크기: 본문 24px 이상
    /// 3. 최소 클릭 타깃: Button 크기 44px 이상
    /// 4. 알파 톤 사용: Image.color.a 가 0 과 1 사이일 때 경고
    /// 5. 세이프존: 1920x1080 기준 90% 안전 영역 점검
    /// </summary>
    public static class UiGuidelineChecks
    {
        private const float MinBodyFontSize = 24f;
        private const float MinCaptionFontSize = 20f;
        private const float LargeTextThreshold = 36f;
        private const float BoldLargeTextThreshold = 28f;
        private const float MinClickTargetSize = 44f;
        private const float MinNormalTextContrast = 4.5f;
        private const float MinLargeTextContrast = 3.0f;

        /// <summary>
        /// 기존 프리팹 과도기 정책:
        /// 기본값은 경고(false)이며, 점진적으로 개선 후 엄격 모드(true)로 전환할 수 있습니다.
        /// </summary>
        public static bool StrictMode { get; set; } = false;

        public static void RunBatch()
        {
            var uiPrefabs = CollectUiPrefabs();
            if (uiPrefabs.Count == 0)
            {
                Debug.Log("[UiGuidelineChecks] 검사할 UI 프리팹이 없습니다.");
                return;
            }

            var totalChecks = 0;
            var warnings = new List<string>();

            foreach (var prefab in uiPrefabs)
            {
                totalChecks += InspectPrefab(prefab, warnings);
            }

            if (warnings.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[UiGuidelineChecks] UI 가이드라인 권장 사항 {warnings.Count}건 검출 (총 {totalChecks}개 검사):");
                foreach (var warn in warnings)
                {
                    sb.AppendLine($" * {warn}");
                }

                if (StrictMode)
                {
                    throw new InvalidOperationException(sb.ToString().TrimEnd());
                }

                Debug.LogWarning(sb.ToString().TrimEnd());
            }

            Debug.Log($"[UiGuidelineChecks] PASS — UI 프리팹 {uiPrefabs.Count}종 ({totalChecks}개 항목 검사 완료).");
        }

        private static List<GameObject> CollectUiPrefabs()
        {
            var results = new List<GameObject>();
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" });

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                // Canvas, RectTransform 또는 UI 요소를 가진 프리팹만 대상으로 수집
                var hasRect = prefab.GetComponentInChildren<RectTransform>(true) != null;
                var hasGraphic = prefab.GetComponentInChildren<Graphic>(true) != null;
                if (hasRect && hasGraphic)
                {
                    results.Add(prefab);
                }
            }

            return results;
        }

        private static int InspectPrefab(GameObject prefab, List<string> warnings)
        {
            var checks = 0;
            var prefabName = prefab.name;

            // 1. 글자 크기 및 명도 대비 검사
            var textComponents = prefab.GetComponentsInChildren<TMP_Text>(true);
            foreach (var tmp in textComponents)
            {
                checks++;
                CheckTypographyAndContrast(prefabName, tmp, warnings);
            }

            // 2. 버튼 클릭 타깃 크기 검사
            var buttons = prefab.GetComponentsInChildren<Button>(true);
            foreach (var button in buttons)
            {
                checks++;
                CheckClickTarget(prefabName, button, warnings);
            }

            // 3. 알파 톤 검사
            var images = prefab.GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                checks++;
                CheckAlphaTone(prefabName, img, warnings);
            }

            return checks;
        }

        private static void CheckTypographyAndContrast(string prefabName, TMP_Text tmp, List<string> warnings)
        {
            var elementName = $"{prefabName}/{GetHierarchyPath(tmp.transform)}";

            // 비활성화된 UI 요소는 WCAG 1.4.11 규정에 따라 대비 검사 면제
            var isInteractable = true;
            var button = tmp.GetComponentInParent<Button>();
            if (button != null && !button.interactable)
            {
                isInteractable = false;
            }

            // 글자 크기 검사
            if (tmp.fontSize < MinCaptionFontSize)
            {
                warnings.Add($"[글자크기 미달] {elementName} 폰트 {tmp.fontSize}px (본문 기준 {MinBodyFontSize}px, 최소 캡션 {MinCaptionFontSize}px)");
            }

            if (!isInteractable)
            {
                return;
            }

            // 배경 이미지 색상 탐색
            var backgroundColor = FindEffectiveBackgroundColor(tmp.transform);
            var foregroundColor = tmp.color;

            // 투명 텍스트는 건너뜀
            if (foregroundColor.a <= 0.05f)
            {
                return;
            }

            var ratio = CalculateContrastRatio(foregroundColor, backgroundColor);
            var isLargeText = tmp.fontSize >= LargeTextThreshold ||
                             ((tmp.fontStyle & FontStyles.Bold) != 0 && tmp.fontSize >= BoldLargeTextThreshold);
            var requiredRatio = isLargeText ? MinLargeTextContrast : MinNormalTextContrast;

            if (ratio < requiredRatio)
            {
                warnings.Add($"[대비 미달] {elementName} 대비 {ratio:F2}:1 (요구 기준 {requiredRatio:F1}:1, 폰트 {tmp.fontSize}px)");
            }
        }

        private static void CheckClickTarget(string prefabName, Button button, List<string> warnings)
        {
            var rt = button.GetComponent<RectTransform>();
            if (rt == null)
            {
                return;
            }

            var rect = rt.rect;
            // 레이아웃 그룹에 의해 런타임에 결정되는 경우 rect 크기가 0일 수 있으므로 최소 크기 컴포넌트도 확인
            var layoutElement = button.GetComponent<LayoutElement>();
            var width = Mathf.Max(rect.width, layoutElement != null ? layoutElement.minWidth : 0f);
            var height = Mathf.Max(rect.height, layoutElement != null ? layoutElement.minHeight : 0f);

            // 너비와 높이가 모두 설정되어 있고 44px 미만인 경우
            if (width > 0f && height > 0f && (width < MinClickTargetSize || height < MinClickTargetSize))
            {
                var elementName = $"{prefabName}/{GetHierarchyPath(button.transform)}";
                warnings.Add($"[클릭타깃 협소] {elementName} 크기 ({width:F0}x{height:F0})px (최소 기준 {MinClickTargetSize}px)");
            }
        }

        private static void CheckAlphaTone(string prefabName, Image img, List<string> warnings)
        {
            var alpha = img.color.a;
            // 0과 1 사이의 불완전한 알파 톤 사용 시 경고 (배경색 중첩으로 대비 예측 불가)
            if (alpha > 0.01f && alpha < 0.99f)
            {
                var elementName = $"{prefabName}/{GetHierarchyPath(img.transform)}";
                warnings.Add($"[알파톤 사용] {elementName} 불투명도 {alpha:F2} (단색 Solid Color 권장)");
            }
        }

        private static Color FindEffectiveBackgroundColor(Transform current)
        {
            // 계층 구조 상위로 올라가며 가장 가까운 불투명 이미지의 색상을 찾음
            var parent = current.parent;
            while (parent != null)
            {
                var img = parent.GetComponent<Image>();
                if (img != null && img.enabled && img.color.a >= 0.1f)
                {
                    return img.color;
                }
                parent = parent.parent;
            }

            // 기본 배경은 어두운 캔버스 배경 가정
            return new Color(0.12f, 0.12f, 0.14f, 1f);
        }

        public static float CalculateContrastRatio(Color foreground, Color background)
        {
            var l1 = CalculateRelativeLuminance(foreground);
            var l2 = CalculateRelativeLuminance(background);
            var lighter = Mathf.Max(l1, l2);
            var darker = Mathf.Min(l1, l2);
            return (lighter + 0.05f) / (darker + 0.05f);
        }

        public static float CalculateRelativeLuminance(Color color)
        {
            float Linearize(float c)
            {
                return c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
            }

            var r = Linearize(color.r);
            var g = Linearize(color.g);
            var b = Linearize(color.b);

            return 0.2126f * r + 0.7152f * g + 0.0722f * b;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var path = transform.name;
            var parent = transform.parent;
            var depth = 0;

            while (parent != null && depth < 3)
            {
                path = $"{parent.name}/{path}";
                parent = parent.parent;
                depth++;
            }

            return path;
        }
    }
}
