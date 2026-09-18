using NCAIClicker.Economy;
using NCAIClicker.Events;
using TMPro;
using UnityEngine;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 현재 날짜를 보여준다. 이슈 #33 완료 기준의 "날짜".
    ///
    /// **날짜 이벤트를 세지 않고 IBillService 에서 다시 읽는 이유 (#150, 3.7 이후).**
    /// 3.7 에서 "하루 = 한 런" 이 확정되면서 BillManager.BeginRun() 이 호출마다 날짜를 올린다.
    /// 그런데
    ///   - OnDayEnded(completedDay) 는 **방금 끝난 날**이다. 이걸로 날짜를 올리면 하루씩 밀린다.
    ///   - 날짜가 실제로 올라가는 BeginRun() 은 날짜 이벤트를 발행하지 않고 OnBillDueSoon 만 낸다.
    /// 그래서 OnBillDueSoon 을 "하루가 시작됐다" 신호로 쓰고, 날짜 자체는 단일 출처인
    /// IBillService.CurrentDay 에서 다시 읽는다. BillManager.BeginRun 의 주석이 가리키는 방식이다.
    ///
    /// **알려진 한계 — 지금은 날짜가 움직이지 않는다.**
    /// BillManager 가 IRunScoped 를 구현하지 않아 GameManager 가 BeginRun/EndRun 을 부르지 못한다
    /// (BillManager.HandleBankruptcy 주석, docs/TECH_NOTES/billing.md "알려진 한계").
    /// 배선이 붙는 순간 이 위젯은 고치지 않아도 맞게 움직인다.
    /// </summary>
    public class DayHud : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _label;

        [Tooltip("하루가 마감된 뒤 날짜 뒤에 붙일 문구. 비우면 붙이지 않는다.")]
        [SerializeField] private string _dayEndedSuffix = "마감";

        private int _day = 1;
        private bool _isDayEnded;

        private void Awake()
        {
            if (_label == null)
            {
                _label = GetComponent<TextMeshProUGUI>();
            }
        }

        private void OnEnable()
        {
            GameEvents.OnBillDueSoon += HandleDayAdvanced;
            GameEvents.OnDayEnded += HandleDayEnded;

            // 초기 상태는 공용 조회 인터페이스로 한 번만 읽는다 (ARCHITECTURE.md 3절).
            // 구현 클래스가 아니라 BillManager.Instance 가 여는 IBillService 통로만 쓴다.
            var billService = BillManager.Instance;
            if (billService != null)
            {
                _day = billService.CurrentDay;
            }
            _isDayEnded = false;
            Render();
        }

        private void OnDisable()
        {
            GameEvents.OnBillDueSoon -= HandleDayAdvanced;
            GameEvents.OnDayEnded -= HandleDayEnded;
        }

        /// <summary>인자(남은 일수)는 BillHud 가 쓴다. 여기서는 "하루가 시작됐다" 신호로만 쓴다.</summary>
        private void HandleDayAdvanced(int daysLeft)
        {
            var billService = BillManager.Instance;
            if (billService != null)
            {
                _day = billService.CurrentDay;
            }
            _isDayEnded = false;
            Render();
        }

        private void HandleDayEnded(int completedDay)
        {
            _day = completedDay;
            _isDayEnded = true;
            Render();
        }

        private void Render()
        {
            if (_label == null)
            {
                return;
            }

            var suffix = _isDayEnded && !string.IsNullOrEmpty(_dayEndedSuffix)
                ? $" {_dayEndedSuffix}"
                : string.Empty;
            _label.text = $"Day {_day}{suffix}";
        }
    }
}
