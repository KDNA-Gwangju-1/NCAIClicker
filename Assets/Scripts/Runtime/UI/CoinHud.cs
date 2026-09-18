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
    /// **알려진 한계 — 보유 잔액의 초기값을 읽지 못한다.**
    /// EconomyManager.BeginRun() 은 PublishRunCoinChanged(0) 만 발행하고 잔액은 발행하지 않는다.
    /// 게다가 BillManager.Instance(IBillService)·SaveManager.Instance(ISaveService) 와 달리
    /// EconomyManager 에는 조회 통로(Instance)가 없어서, ARCHITECTURE 3절이 정한
    /// "UI는 구독 후 공용 조회 인터페이스로 초기 상태를 한 번 읽는다" 를 지킬 수가 없다.
    /// 그래서 첫 OnBalanceChanged 가 올 때까지 잔액 자리는 PlaceholderText 로 둔다.
    /// 공용 계약 변경이라 이 카드에서 고치지 않았다 — 별도 이슈로 다룬다.
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
