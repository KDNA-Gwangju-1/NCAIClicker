using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using TMPro;
using UnityEngine;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 청구서 마감(일수)과 금액을 항상 보여주는 최소 HUD. #27 완료 기준 "HUD 는 항상 일수·금액을 보여준다."
    /// OnBillIssued 로 금액을, OnBillDueSoon 으로 남은 일수를 받는다 — 매 프레임 폴링하지 않는다
    /// (ARCHITECTURE.md 3절 "UI는 구독 후 공용 조회 인터페이스로 초기 상태를 한 번 읽는다").
    /// 구현 클래스(BillManager) 를 직접 참조하지 않고 BillManager.Instance 가 여는 IBillService 통로만 쓴다.
    /// </summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class BillHud : MonoBehaviour
    {
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

            // 초기 상태는 공용 조회 인터페이스로 한 번만 읽는다 — BillManager 가 이미 발행한 청구서를
            // OnEnable 시점에 놓쳤을 수 있어서다 (예: HUD 프리팹이 첫 BeginRun 이후 로드되는 경우).
            var billService = BillManager.Instance;
            if (billService != null && billService.DaysLeft > 0)
            {
                _hasBill = true;
                _daysLeft = billService.DaysLeft;
            }
            Render();
        }

        private void OnDisable()
        {
            GameEvents.OnBillIssued -= HandleBillIssued;
            GameEvents.OnBillDueSoon -= HandleBillDueSoon;
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

        private void Render()
        {
            if (_label == null)
            {
                return;
            }

            _label.text = _hasBill ? $"D-{_daysLeft}  {_cachedAmount:N0}원" : "청구서 없음";
        }
    }
}
