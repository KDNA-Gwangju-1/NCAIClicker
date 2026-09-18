using NCAIClicker.Events;
using UnityEngine;
using UnityEngine.UI;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 피버 게이지를 막대로 보여주고, 발동 중에는 색으로 구분한다. 이슈 #33 완료 기준의 "피버 게이지".
    ///
    /// 게이지 값과 발동 상태를 따로 받는 이유: 게이지가 가득 찼다고 발동한 것이 아니고
    /// (FeverManager 가 지속 시간 동안 유지한다), 발동 중에는 게이지가 줄어도 배율은 살아 있다.
    /// 둘을 한 이벤트로 합치면 그 구분이 사라진다.
    ///
    /// 초기값은 FeverManager.BeginRun() 의 PublishChanged() 로 들어온다 — StaminaHud 와 같은 이유로
    /// 별도 조회를 하지 않는다.
    /// </summary>
    public class FeverHud : MonoBehaviour
    {
        [Tooltip("Image Type 을 Filled 로 둔 막대.")]
        [SerializeField] private Image _fill;

        [SerializeField] private Color _normalColor = new Color(0.36f, 0.62f, 0.95f);

        [Tooltip("피버 발동 중 막대 색.")]
        [SerializeField] private Color _activeColor = new Color(1f, 0.55f, 0.1f);

        private float _current;
        private float _max;
        private bool _isFeverActive;

        private void OnEnable()
        {
            GameEvents.OnFeverGaugeChanged += HandleGaugeChanged;
            GameEvents.OnFeverStart += HandleFeverStart;
            GameEvents.OnFeverEnd += HandleFeverEnd;
            Render();
        }

        private void OnDisable()
        {
            GameEvents.OnFeverGaugeChanged -= HandleGaugeChanged;
            GameEvents.OnFeverStart -= HandleFeverStart;
            GameEvents.OnFeverEnd -= HandleFeverEnd;
        }

        private void HandleGaugeChanged(float current, float max)
        {
            _current = current;
            _max = max;
            Render();
        }

        private void HandleFeverStart()
        {
            _isFeverActive = true;
            Render();
        }

        private void HandleFeverEnd()
        {
            _isFeverActive = false;
            Render();
        }

        private void Render()
        {
            if (_fill == null)
            {
                return;
            }

            _fill.fillAmount = _max > 0f ? Mathf.Clamp01(_current / _max) : 0f;
            _fill.color = _isFeverActive ? _activeColor : _normalColor;
        }
    }
}
