using NCAIClicker.Economy;
using NCAIClicker.Events;
using TMPro;
using UnityEngine;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 코인을 보여준다. 이슈 #33 완료 기준의 "코인".
    ///
    /// 런 순수입과 보유 잔액을 **다른 이벤트로** 받는다 (ARCHITECTURE.md "코인 계산·정산 계약" 6번:
    /// "UI는 목적에 맞는 이벤트를 쓰며 배율을 재적용하지 않는다"). 둘은 다른 숫자다 — 단계 목표는
    /// 런 순수입으로 판정하고, 업그레이드 구매는 보유 잔액에서 나간다.
    ///
    /// 초기값은 둘을 다르게 얻는다. 런 순수입은 EconomyManager.BeginRun() 이 0 을 발행해 구독만으로
    /// 들어오지만, **잔액은 런 시작에 발행되지 않는다.** 그래서 OnEnable 에서 EconomyManager.Instance
    /// (IEconomyService) 로 한 번 읽는다 — ARCHITECTURE 3절 "UI는 구독 후 공용 조회 인터페이스로
    /// 초기 상태를 한 번 읽는다" 그대로다. BillHud·DayHud 가 BillManager.Instance 를 쓰는 것과 같다.
    ///
    /// Game 씬이 런마다 새로 로드되어 이 컴포넌트도 매번 새로 생기므로, 이 조회가 없으면
    /// **모든 런에서** 그 런의 첫 코인이 들어올 때까지 잔액이 비어 보였다 (#171).
    /// </summary>
    public class CoinHud : MonoBehaviour
    {
        private const string PlaceholderText = "—";

        [Tooltip("이번 런의 순수입. 단계 목표 판정과 같은 숫자다.")]
        [SerializeField] private TextMeshProUGUI _runCoinLabel;

        [Tooltip("보유 잔액. 비워 두면 표시하지 않는다.")]
        [SerializeField] private TextMeshProUGUI _balanceLabel;

        private long _runCoin;
        private long _balance;
        private bool _hasBalance;

        private void OnEnable()
        {
            GameEvents.OnRunCoinChanged += HandleRunCoinChanged;
            GameEvents.OnBalanceChanged += HandleBalanceChanged;

            // 초기 상태는 구현 클래스가 아니라 EconomyManager.Instance 가 여는 IEconomyService
            // 통로로 읽는다. 런 순수입도 함께 읽어 둔다 — 보통은 BeginRun() 의 발행으로 0 이 들어오지만,
            // 이 컴포넌트가 런 경계 **뒤에** 살아나는 경우(씬만 다시 로드되는 경로)에는 그 발행을
            // 놓쳐 화면과 실제 값이 어긋난다.
            var economyService = EconomyManager.Instance;
            if (economyService != null)
            {
                _runCoin = economyService.RunCoin;
                _balance = economyService.CurrentCoin;
                _hasBalance = true;
            }
            Render();
        }

        private void OnDisable()
        {
            GameEvents.OnRunCoinChanged -= HandleRunCoinChanged;
            GameEvents.OnBalanceChanged -= HandleBalanceChanged;
        }

        private void HandleRunCoinChanged(long runCoin)
        {
            _runCoin = runCoin;
            Render();
        }

        private void HandleBalanceChanged(long balance)
        {
            _balance = balance;
            _hasBalance = true;
            Render();
        }

        private void Render()
        {
            if (_runCoinLabel != null)
            {
                _runCoinLabel.text = $"{_runCoin:N0}";
            }

            if (_balanceLabel != null)
            {
                _balanceLabel.text = _hasBalance ? $"{_balance:N0}" : PlaceholderText;
            }
        }
    }
}
