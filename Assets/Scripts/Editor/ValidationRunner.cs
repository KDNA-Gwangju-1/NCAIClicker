using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 에디터 검증 하네스(*Checks)를 전부 모아 한 번에 실행한다.
    /// 클래스를 리플렉션으로 찾으므로 새 *Checks 를 추가해도 이 파일은 고치지 않는다.
    /// GitHub 이슈 #159.
    /// </summary>
    public static class ValidationRunner
    {
        private const string ClassSuffix = "Checks";
        private const string EntryMethodName = "RunBatch";

        [MenuItem("NCAI/전체 검증 실행")]
        public static void RunAll()
        {
            var entries = CollectEntries();
            if (entries.Count == 0)
            {
                Debug.LogError($"[ValidationRunner] 이름이 {ClassSuffix} 로 끝나고 매개변수 없는 " +
                    $"{EntryMethodName}() 를 가진 클래스를 하나도 찾지 못했습니다.");
                return;
            }

            var failures = new List<(string Name, Exception Error)>();

            foreach (var entry in entries)
            {
                try
                {
                    entry.Method.Invoke(null, null);
                }
                catch (TargetInvocationException e)
                {
                    // 검증은 실패를 예외로 던진다. 안쪽 예외라야 실제 원인이 보인다.
                    failures.Add((entry.Name, e.InnerException ?? e));
                }
                catch (Exception e)
                {
                    failures.Add((entry.Name, e));
                }
            }

            ReportSummary(entries.Count, failures);
        }

        /// <summary>
        /// 이 어셈블리에서 매개변수 없는 public static RunBatch() 를 가진 *Checks 클래스를 모은다.
        /// 실행 순서를 이름순으로 고정해, 돌릴 때마다 로그가 같은 차례로 쌓이게 한다.
        /// </summary>
        private static List<(string Name, MethodInfo Method)> CollectEntries()
        {
            return typeof(ValidationRunner).Assembly
                .GetTypes()
                .Where(type => type.IsClass && type.IsAbstract && type.IsSealed)
                .Where(type => type.Name.EndsWith(ClassSuffix, StringComparison.Ordinal))
                .Select(type => (
                    Name: type.Name,
                    Method: type.GetMethod(EntryMethodName,
                        BindingFlags.Public | BindingFlags.Static,
                        null, Type.EmptyTypes, null)))
                .Where(entry => entry.Method != null)
                .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .ToList();
        }

        private static void ReportSummary(int total, List<(string Name, Exception Error)> failures)
        {
            var passed = total - failures.Count;
            var summary = new StringBuilder();
            summary.AppendLine($"[ValidationRunner] 통과 {passed} / 실패 {failures.Count} (전체 {total})");

            if (failures.Count == 0)
            {
                Debug.Log(summary.ToString().TrimEnd());
                return;
            }

            foreach (var failure in failures)
            {
                summary.AppendLine($"  - {failure.Name}: {failure.Error.Message}");
            }

            Debug.LogError(summary.ToString().TrimEnd());
        }
    }
}
