using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 업그레이드 상점 패널. 카드 여러 장을 한 곳에서 조립하고 함께 다시 그린다.
    /// 이슈 #91(6.8). `MainMenu` 씬에 둔다 — ARCHITECTURE 0절이 메인 메뉴의 역할에
    /// "업그레이드 구매"를 포함시켰다.
    ///
    /// **다시 그리는 일을 카드가 아니라 여기서 하는 이유**: 한 장을 사면 코인이 줄어 **다른 카드의
    /// 구매 가능 여부까지 달라진다.** 카드가 각자 자기만 갱신하면 방금 산 카드만 바뀌고 나머지는
    /// 살 수 있는 것처럼 남아, 눌러 보고서야 실패한다.
    ///
    /// 서비스는 EconomyManager 가 여는 인터페이스 통로로만 잡는다 (이슈 #171) — 구현 클래스를
    /// 직접 참조하지 않는다 (AGENTS.md). 잡은 것을 카드에 넣어 주는 조립 지점도 여기 하나다.
    /// </summary>
    public class UpgradeShopPanel : MonoBehaviour
    {
        [Tooltip("표시할 카드들. upgrades.csv 의 sort_order 순으로 배치한다.")]
        [SerializeField] private UpgradeShopEntry[] _entries;

        [Tooltip("보유 코인. 비워 두면 표시하지 않는다.")]
        [SerializeField] private TextMeshProUGUI _balanceLabel;

        [Tooltip("이름·설명·효과의 출처. Managers 프리팹이 쓰는 것과 같은 에셋을 넣는다.")]
        [SerializeField] private BalanceData _balanceData;

        [Tooltip("닫기 버튼. 연결하면 클릭 시 패널을 닫는다.")]
        [SerializeField] private Button _closeButton;

        public event System.Action Closed;

        public void Close()
        {
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        // 정적 이벤트는 구독과 해제를 쌍으로 맞춘다 (AGENTS.md).
        private void OnEnable()
        {
            GameEvents.OnBalanceChanged += HandleBalanceChanged;

            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(Close);
            }

            BindEntries();
            RefreshAll();
        }

        private void OnDisable()
        {
            GameEvents.OnBalanceChanged -= HandleBalanceChanged;

            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(Close);
            }
        }

        /// <summary>
        /// 카드에 서비스를 넣어 준다. 매니저는 DontDestroyOnLoad 지만 이 패널은 씬과 함께 다시
        /// 생기므로, 캐시하지 않고 살아날 때마다 다시 잡는다.
        /// </summary>
        private void BindEntries()
        {
            var shop = EconomyManager.Shop;
            var economy = EconomyManager.Instance;

            if (shop == null || economy == null)
            {
                Debug.LogWarning("[UpgradeShopPanel] EconomyManager 통로가 아직 없다. " +
                                 "카드는 '상점을 열 수 없다'로 표시된다.", this);
            }

            if (_entries == null)
            {
                return;
            }

            for (var i = 0; i < _entries.Length; i++)
            {
                if (_entries[i] == null)
                {
                    continue;
                }
                _entries[i].Bind(_balanceData, shop, economy, RefreshAll);
            }
        }

        /// <summary>
        /// 카드 전부와 잔액 표시를 다시 그린다. 구매 성공(카드가 부르는 콜백)과
        /// 잔액 변동(OnBalanceChanged) 양쪽에서 들어온다.
        /// </summary>
        private void RefreshAll()
        {
            if (_entries != null)
            {
                for (var i = 0; i < _entries.Length; i++)
                {
                    if (_entries[i] != null)
                    {
                        _entries[i].Refresh();
                    }
                }
            }

            RenderBalance();
        }

        /// <summary>인자를 쓰지 않고 다시 조회하는 이유: 잔액은 IEconomyService 가 단일 출처다.</summary>
        private void HandleBalanceChanged(long balance)
        {
            RefreshAll();
        }

        private void RenderBalance()
        {
            if (_balanceLabel == null)
            {
                return;
            }

            // 초기 상태는 공용 조회 인터페이스로 읽는다 (ARCHITECTURE 3절). 메인 메뉴에서는
            // 런이 돌지 않아 잔액 이벤트가 발행될 일이 거의 없어, 조회가 유일한 출처다.
            var economy = EconomyManager.Instance;
            _balanceLabel.text = economy == null ? "—" : $"{economy.CurrentCoin:N0}";
        }
    }
}
