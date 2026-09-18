using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 검증 하네스를 골라서 돌리는 창.
    /// <para>
    /// 15종을 전부 도는 데는 시간이 걸린다. 경제 쪽만 고쳤으면 경제 하네스만 돌릴 수 있어야
    /// 검증을 자주 하게 된다. 전부 아니면 전무면 결국 안 돌리게 된다.
    /// </para>
    /// 수집과 실행은 <see cref="ValidationRunner"/> 를 그대로 쓴다.
    /// GitHub 이슈 #159.
    /// </summary>
    public sealed class ValidationWindow : EditorWindow
    {
        /// <summary>선택 상태를 창 너머로 기억한다. 창을 닫았다 열 때마다 다시 고르면 쓰지 않게 된다.</summary>
        private const string SelectionPrefsKey = "NCAIClicker.ValidationWindow.Unselected";

        private const char PrefsSeparator = ';';

        private List<ValidationEntry> _entries;
        private readonly HashSet<string> _unselected = new HashSet<string>();
        private List<ValidationResult> _results;
        private Vector2 _scroll;

        [MenuItem("NCAI/검증 창...", false, MenuPriority.ValidationWindow)]
        public static void Open()
        {
            var window = GetWindow<ValidationWindow>("검증");
            window.minSize = new Vector2(320f, 320f);
        }

        private void OnEnable()
        {
            Reload();
            LoadSelection();
        }

        private void OnDisable()
        {
            SaveSelection();
        }

        /// <summary>
        /// 하네스 목록을 다시 수집한다. 스크립트를 고쳐 도메인이 리로드되면 창이 살아남으므로,
        /// 새로 만든 하네스가 목록에 없을 때 사람이 직접 새로 고칠 수 있어야 한다.
        /// </summary>
        private void Reload()
        {
            _entries = ValidationRunner.CollectEntries();
            _results = null;
        }

        private void OnGUI()
        {
            if (_entries == null || _entries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "검증 하네스를 찾지 못했습니다.\n" +
                    "이름이 Checks 로 끝나는 static 클래스에 매개변수 없는 public static RunBatch() 가 있어야 합니다.",
                    MessageType.Warning);

                if (GUILayout.Button("다시 수집"))
                {
                    Reload();
                }

                return;
            }

            DrawToolbar();
            DrawEntries();
            DrawRunButton();
            DrawSummary();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("전체 선택", EditorStyles.toolbarButton))
                {
                    _unselected.Clear();
                }

                if (GUILayout.Button("전체 해제", EditorStyles.toolbarButton))
                {
                    foreach (var entry in _entries)
                    {
                        _unselected.Add(entry.Name);
                    }
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("다시 수집", EditorStyles.toolbarButton))
                {
                    Reload();
                }
            }
        }

        private void DrawEntries()
        {
            using (var scope = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scope.scrollPosition;

                foreach (var entry in _entries)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var wasSelected = !_unselected.Contains(entry.Name);
                        var isSelected = EditorGUILayout.ToggleLeft(entry.Name, wasSelected);

                        if (isSelected != wasSelected)
                        {
                            if (isSelected)
                            {
                                _unselected.Remove(entry.Name);
                            }
                            else
                            {
                                _unselected.Add(entry.Name);
                            }
                        }

                        DrawResultBadge(entry.Name);
                    }
                }
            }
        }

        /// <summary>마지막 실행에서 이 하네스가 어땠는지 줄 끝에 표시한다.</summary>
        private void DrawResultBadge(string entryName)
        {
            if (_results == null)
            {
                return;
            }

            var result = _results.FirstOrDefault(item => item.Name == entryName);
            if (result.Name == null)
            {
                return;
            }

            var label = result.IsPassed ? "통과" : "실패";
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = result.IsPassed ? new Color(0.3f, 0.7f, 0.3f) : new Color(0.85f, 0.3f, 0.3f) },
                alignment = TextAnchor.MiddleRight,
            };

            GUILayout.Label(label, style, GUILayout.Width(40f));
        }

        private void DrawRunButton()
        {
            var selected = _entries.Where(entry => !_unselected.Contains(entry.Name)).ToList();

            using (new EditorGUI.DisabledScope(selected.Count == 0))
            {
                if (GUILayout.Button($"선택 항목 실행 ({selected.Count}개)", GUILayout.Height(28f)))
                {
                    SaveSelection();
                    _results = ValidationRunner.Run(selected);
                    ValidationRunner.LogSummary(_results);
                }
            }
        }

        private void DrawSummary()
        {
            if (_results == null)
            {
                return;
            }

            var failures = _results.Where(result => !result.IsPassed).ToList();
            var passed = _results.Count - failures.Count;

            if (failures.Count == 0)
            {
                EditorGUILayout.HelpBox($"통과 {passed} / 실패 0", MessageType.Info);
                return;
            }

            // 실패 사유를 창 안에 그대로 보여 준다. 콘솔에서 찾게 하면 한 단계가 늘고,
            // 다른 로그에 묻힌다.
            var detail = string.Join("\n", failures.Select(failure => $"{failure.Name}: {failure.Error.Message}"));
            EditorGUILayout.HelpBox($"통과 {passed} / 실패 {failures.Count}\n\n{detail}", MessageType.Error);
        }

        /// <summary>
        /// 해제한 것만 저장한다. 선택한 쪽을 저장하면 새로 추가된 하네스가 기본 해제로 보여
        /// 조용히 빠진다 — 새 하네스는 기본으로 돌아야 한다.
        /// </summary>
        private void SaveSelection()
        {
            EditorPrefs.SetString(SelectionPrefsKey, string.Join(PrefsSeparator.ToString(), _unselected));
        }

        private void LoadSelection()
        {
            _unselected.Clear();

            var saved = EditorPrefs.GetString(SelectionPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(saved))
            {
                return;
            }

            foreach (var name in saved.Split(PrefsSeparator))
            {
                if (!string.IsNullOrEmpty(name))
                {
                    _unselected.Add(name);
                }
            }
        }
    }
}
