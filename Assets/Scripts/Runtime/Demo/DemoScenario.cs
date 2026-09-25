#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace NCAIClicker.Demo
{
    /// <summary>
    /// 자동 시연 커서가 따라갈 단계 목록. 제출 영상 녹화용 에디터 전용 에셋이다 (#45).
    /// </summary>
    [CreateAssetMenu(fileName = "DemoScenario", menuName = "NCAI/Demo Scenario")]
    public class DemoScenario : ScriptableObject
    {
        [SerializeField] private List<DemoStep> _steps = new List<DemoStep>();

        public IReadOnlyList<DemoStep> Steps => _steps;

        /// <summary>에디터 메뉴가 기본 시나리오를 채울 때 쓴다.</summary>
        public void SetSteps(IEnumerable<DemoStep> steps)
        {
            _steps = new List<DemoStep>(steps);
        }
    }
}
#endif
