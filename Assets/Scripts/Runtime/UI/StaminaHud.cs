using NCAIClicker.Events;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 스태미나를 막대와 숫자로 함께 보여준다. 이슈 #33 완료 기준이 "스태미나(숫자 병기)" 로
    /// 못 박았다 — 막대만 두면 "얼마나 남았나" 를 눈대중으로만 알 수 있어 업그레이드 효과를
    /// 체감할 수 없다.
    ///
    /// 초기값을 따로 조회하지 않는 이유: StaminaManager.BeginRun() 이 마지막에 PublishChanged()
    /// 를 부르므로, 씬 로드 시점에 구독만 걸어 두면 런 시작과 동시에 현재/최대값이 들어온다.
    /// (StaminaManager 에는 BillManager.Instance 같은 조회 통로가 없다.)
    /// </summary>
    public class StaminaHud : MonoBehaviour
    {
        [Tooltip("Image Type 을 Filled 로 둔 막대. 비워 두면 숫자만 갱신한다.")]
        [SerializeField] private Image _fill;

        [Tooltip("'75/120' 형태로 숫자를 병기할 라벨.")]
        [SerializeField] private TextMeshProUGUI _label;

        private float _current;
        private float _max;

        // 정적 이벤트는 구독과 해제를 쌍으로 맞춘다 (AGENTS.md). 빠뜨리면 씬을 다시 들어갈 때
        // 죽은 인스턴스가 계속 불린다.
        private void OnEnable()
        {
            GameEvents.OnStaminaChanged += HandleStaminaChanged;
            Render();
        }

        private void OnDisable()
        {
            GameEvents.OnStaminaChanged -= HandleStaminaChanged;
        }

        private void HandleStaminaChanged(float current, float max)
        {
            _current = current;
            _max = max;
            Render();
        }

        private void Render()
        {
            if (_fill != null)
            {
                _fill.fillAmount = _max > 0f ? Mathf.Clamp01(_current / _max) : 0f;
            }

            if (_label == null)
            {
                return;
            }

            // 올림으로 표시한다 — 0.4 를 0 으로 보여 주면 아직 스윙이 되는데 다 떨어진 것처럼 읽힌다.
            // 다만 실제로 0 이면 0 을 보여야 하므로 아래를 깎지 않는다.
            var shownCurrent = _current <= 0f ? 0 : Mathf.Max(1, Mathf.CeilToInt(_current));
            _label.text = $"{shownCurrent}/{Mathf.CeilToInt(_max)}";
        }
    }
}
