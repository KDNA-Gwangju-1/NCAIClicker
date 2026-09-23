using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using TMPro;
using UnityEngine;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 고지서 마감(일수)과 금액을 항상 보여주는 최소 HUD. #27 완료 기준 "HUD 는 항상 일수·금액을 보여준다."
    /// OnBillIssued 로 금액을, OnBillDueSoon 으로 남은 일수를 받는다 — 매 프레임 폴링하지 않는다
    /// (ARCHITECTURE.md 3절 "UI는 구독 후 공용 조회 인터페이스로 초기 상태를 한 번 읽는다").
    /// 구현 클래스(BillManager) 를 직접 참조하지 않고 BillManager.Instance 가 여는 IBillService 통로만 쓴다.
    ///
    /// 이슈 #33(6.1 인게임 HUD)에서 두 가지를 더했다.
    ///   - 마감이 다가오면 색으로 강조한다 (완료 기준: "고지서는 남은 일수를 상시 노출하고 마감 하루 전부터 강조").
    ///   - OnEnable 에서 금액도 IBillService.ActiveBill 로 읽는다. 종전에는 일수만 읽어서,
    ///     HUD 가 붙기 전에 발행된 고지서의 금액이 0 으로 남았다.
    /// </summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class BillHud : MonoBehaviour
    {
        /// <summary>
        /// 이 값 이하로 남으면 강조한다. 기본 2 인 이유: DaysLeft 는 마감일을 포함해 세므로
        /// (max(0, DueDay - CurrentDay + 1)) 마감 당일이 1, 그 하루 전이 2 다.
        /// "마감 하루 전부터" 를 숫자로 옮기면 2 가 된다.
        /// </summary>
        [SerializeField] private int _emphasisDaysLeft = 2;

        [SerializeField] private Color _normalColor = Color.white;

        [Tooltip("마감이 임박했을 때 색.")]
        [SerializeField] private Color _dueSoonColor = new Color(0.94f, 0.27f, 0.27f);

        private TextMeshProUGUI _label;
        private long _cachedAmount;
        private int _daysLeft;
        private bool _hasBill;

        private void Awake()
        {
            _label = GetComponent<TextMeshProUGUI>();
        }

        private void OnEnable()
        {
            GameEvents.OnBillIssued += HandleBillIssued;
            GameEvents.OnBillDueSoon += HandleBillDueSoon;
            GameEvents.OnBillPaid += HandleBillPaid;

            // 초기 상태는 공용 조회 인터페이스로 한 번만 읽는다 — BillManager 가 이미 발행한 고지서를
            // OnEnable 시점에 놓쳤을 수 있어서다 (예: HUD 프리팹이 첫 BeginRun 이후 로드되는 경우).
            var billService = BillManager.Instance;
            var activeBill = billService?.ActiveBill;
            if (activeBill != null)
            {
                _hasBill = true;
                _cachedAmount = activeBill.Amount;
                _daysLeft = billService.DaysLeft;
            }
            Render();
        }

        private void OnDisable()
        {
            GameEvents.OnBillIssued -= HandleBillIssued;
            GameEvents.OnBillDueSoon -= HandleBillDueSoon;
            GameEvents.OnBillPaid -= HandleBillPaid;
        }

        private void HandleBillIssued(Bill bill)
        {
            _cachedAmount = bill.Amount;
            _hasBill = true;
            Render();
        }

        private void HandleBillDueSoon(int daysLeft)
        {
            _daysLeft = daysLeft;
            _hasBill = true;
            Render();
        }

        /// <summary>납부하면 강조를 거둔다. 낸 고지서를 붉게 두면 아직 안 낸 것처럼 읽힌다.</summary>
        private void HandleBillPaid(Bill bill)
        {
            _hasBill = false;
            Render();
        }

        private void Render()
        {
            if (_label == null)
            {
                return;
            }

            _label.text = _hasBill ? $"고지서 D-{_daysLeft}  ${_cachedAmount:N0}" : "고지서 없음";
            _label.color = _hasBill && _daysLeft <= _emphasisDaysLeft ? _dueSoonColor : _normalColor;
        }
    }
}
