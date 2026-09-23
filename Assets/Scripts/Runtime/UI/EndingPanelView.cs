using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 엔딩 패널 프리팹의 루트. 통계 글자와 계속하기 버튼만 잡는다 (이슈 #271).
    /// 문구는 프리팹에 고정이고, 여기서는 숫자만 채운다.
    /// </summary>
    public class EndingPanelView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _daysText;
        [SerializeField] private TextMeshProUGUI _cycleText;
        [SerializeField] private TextMeshProUGUI _totalPaidText;
        [SerializeField] private Button _continueButton;

        private Action _onContinue;

        private void Awake()
        {
            if (_continueButton != null)
            {
                _continueButton.onClick.AddListener(() => _onContinue?.Invoke());
            }
        }

        public void Bind(int days, int cycle, long totalPaid, Action onContinue)
        {
            _onContinue = onContinue;
            if (_daysText != null)
            {
                _daysText.text = $"{days:N0}일";
            }
            if (_cycleText != null)
            {
                _cycleText.text = $"{cycle:N0}회차";
            }
            if (_totalPaidText != null)
            {
                _totalPaidText.text = $"${totalPaid:N0}";
            }
        }
    }
}
