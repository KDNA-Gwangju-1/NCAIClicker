using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>검증 하네스 하나의 실행 결과.</summary>
    internal readonly struct ValidationResult
    {
        public readonly string Name;

        /// <summary>실패 사유. 통과했으면 null 이다.</summary>
        public readonly Exception Error;

        public bool IsPassed => Error == null;

        public ValidationResult(string name, Exception error)
        {
            Name = name;
            Error = error;
        }
    }

    /// <summary>실행할 수 있는 검증 하네스 하나.</summary>
    internal readonly struct ValidationEntry
    {
        public readonly string Name;
        public readonly MethodInfo Method;

        public ValidationEntry(string name, MethodInfo method)
        {
            Name = name;
            Method = method;
        }
    }

    /// <summary>
    /// 에디터 검증 하네스(*Checks)를 모아 실행한다.
    /// 클래스를 리플렉션으로 찾으므로 새 *Checks 를 추가해도 이 파일은 고치지 않는다.
    /// 수집·실행은 <see cref="ValidationWindow"/> 와 공유한다 — 규칙이 두 곳으로 갈라지면
    /// 창에는 보이는데 전체 실행에서 빠지는 하네스가 생긴다.
    /// GitHub 이슈 #159.
    /// </summary>
    public static class ValidationRunner
    {
        private const string ClassSuffix = "Checks";
        private const string EntryMethodName = "RunBatch";

        [MenuItem("NCAI/전체 검증 실행", false, MenuPriority.RunAll)]
        public static void RunAll()
        {
            var entries = CollectEntries();
            if (entries.Count == 0)
            {
                Debug.LogError($"[ValidationRunner] 이름이 {ClassSuffix} 로 끝나고 매개변수 없는 " +
                    $"{EntryMethodName}() 를 가진 클래스를 하나도 찾지 못했습니다.");
                return;
            }

            LogSummary(Run(entries));
        }

        /// <summary>
        /// 이 어셈블리에서 매개변수 없는 public static RunBatch() 를 가진 *Checks 클래스를 모은다.
        /// 실행 순서를 이름순으로 고정해, 돌릴 때마다 로그가 같은 차례로 쌓이게 한다.
        /// </summary>
        internal static List<ValidationEntry> CollectEntries()
        {
            return typeof(ValidationRunner).Assembly
                .GetTypes()
                .Where(type => type.IsClass && type.IsAbstract && type.IsSealed)
                .Where(type => type.Name.EndsWith(ClassSuffix, StringComparison.Ordinal))
                .Select(type => new ValidationEntry(
                    type.Name,
                    type.GetMethod(EntryMethodName,
                        BindingFlags.Public | BindingFlags.Static,
                        null, Type.EmptyTypes, null)))
                .Where(entry => entry.Method != null)
                .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// 넘겨받은 하네스를 차례로 실행한다. 하나가 던져도 나머지를 계속 돈다 —
        /// 첫 실패에서 멈추면 한 번 돌릴 때 문제 하나씩만 알게 되고, 에디터 검증은 오래 걸린다.
        /// </summary>
        internal static List<ValidationResult> Run(IReadOnlyList<ValidationEntry> entries)
        {
            var results = new List<ValidationResult>(entries.Count);

            foreach (var entry in entries)
            {
                try
                {
                    entry.Method.Invoke(null, null);
                    results.Add(new ValidationResult(entry.Name, null));
                }
                catch (TargetInvocationException e)
                {
                    // Invoke 는 대상의 예외를 감싼다. 안쪽을 꺼내지 않으면 실제 사유가 사라진다.
                    results.Add(new ValidationResult(entry.Name, e.InnerException ?? e));
                }
                catch (Exception e)
                {
                    results.Add(new ValidationResult(entry.Name, e));
                }
            }

            return results;
        }

        /// <summary>통과·실패를 콘솔에 한 줄로 요약한다.</summary>
        internal static void LogSummary(IReadOnlyList<ValidationResult> results)
        {
            var failures = results.Where(result => !result.IsPassed).ToList();
            var passed = results.Count - failures.Count;

            var summary = new StringBuilder();
            summary.AppendLine($"[ValidationRunner] 통과 {passed} / 실패 {failures.Count} (전체 {results.Count})");

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
