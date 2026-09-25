#if UNITY_EDITOR
using System;
using UnityEngine;

namespace NCAIClicker.Demo
{
    /// <summary>
    /// 시연 시나리오의 한 단계. 필드의 의미는 <see cref="DemoStepKind"/> 에 따라 다르다.
    /// </summary>
    [Serializable]
    public class DemoStep
    {
        [SerializeField] private DemoStepKind _kind;

        [Tooltip("버튼 라벨·오브젝트 이름·부모 이름 중 하나에 포함되면 일치. '|' 로 후보를 나열한다.")]
        [SerializeField] private string _label;

        [Tooltip("Wait·HuntCreatures 는 지속 시간, ClickButton·ClickIndex·WaitUntilButton 은 최대 대기 시간(초).")]
        [SerializeField] private float _seconds = 5f;

        [Tooltip("ClickIndex 의 순번 (0부터).")]
        [SerializeField] private int _index;

        [Tooltip("MoveTo 의 화면 비율 좌표 (0~1).")]
        [SerializeField] private Vector2 _viewportPoint = new Vector2(0.5f, 0.5f);

        public DemoStepKind Kind => _kind;
        public string Label => _label;
        public float Seconds => _seconds;
        public int Index => _index;
        public Vector2 ViewportPoint => _viewportPoint;

        public DemoStep(DemoStepKind kind, string label = "", float seconds = 5f, int index = 0)
        {
            _kind = kind;
            _label = label;
            _seconds = seconds;
            _index = index;
        }
    }
}
#endif
