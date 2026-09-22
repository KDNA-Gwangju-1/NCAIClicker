using NCAIClicker.Data;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 반지 상점 상단의 레거시 포인트 박스에 붙어, 마우스를 올리는 동안 포인트 규칙을 설명한다 (이슈 #184).
    /// 숫자는 <see cref="BalanceData"/> 의 economy 값에서 읽는다 — economy.csv 가 원본이고 코드에 박지 않는다.
    /// 표시만 하며 매니저를 부르지 않는다.
    /// </summary>
    public class LegacyPointHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private GameObject _hintPanel;
        [SerializeField] private TextMeshProUGUI _hintText;

        private BalanceData _balanceData;

        private void Awake()
        {
            Hide();
        }

        /// <summary>규칙값의 출처를 넣어 준다. 패널이 살아날 때마다 부른다.</summary>
        public void Bind(BalanceData balanceData)
        {
            _balanceData = balanceData;
            RenderText();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            RenderText();
            if (_hintPanel != null)
            {
                _hintPanel.SetActive(true);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Hide();
        }

        public void Hide()
        {
            if (_hintPanel != null)
            {
                _hintPanel.SetActive(false);
            }
        }

        private void RenderText()
        {
            if (_hintText == null)
            {
                return;
            }

            var perAmount = _balanceData != null ? _balanceData.Economy.LegacyPointPerAmount : 0f;
            var rateLine = perAmount > 0f
                ? $"고지서 납부액 ${perAmount:N0}당 1 LP"
                : "고지서를 납부하면 쌓인다";

            _hintText.text = $"레거시 포인트 (LP)\n{rateLine}\n파산해도 사라지지 않는다\n반지를 사는 데만 쓴다";
        }
    }
}
