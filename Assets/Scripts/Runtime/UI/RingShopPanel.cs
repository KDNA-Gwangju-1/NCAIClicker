using NCAIClicker.Data;
using NCAIClicker.Economy;
using TMPro;
using UnityEngine;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 반지 상점 패널 (이슈 #183). <see cref="UpgradeShopPanel"/> 과 짝이다.
    ///
    /// **다시 그리는 일을 카드가 아니라 여기서 한다** — 한 장을 사면 포인트가 줄어 다른 카드의
    /// 구매 가능 여부까지 달라진다. 카드가 각자 자기만 갱신하면 방금 산 카드만 바뀌고 나머지는
    /// 살 수 있는 것처럼 남아, 눌러 보고서야 실패한다.
    ///
    /// **포인트 변동 이벤트를 구독하지 않는다.** 코인과 달리 레거시 포인트는 고지서를 낼 때와
    /// 반지를 살 때만 바뀐다. 둘 다 이 화면이 살아 있는 동안 일어나고 후자는 콜백으로 들어오므로,
    /// 살아날 때 한 번 읽는 것으로 충분하다 (ARCHITECTURE 3절). 새 이벤트를 늘리지 않는다.
    /// </summary>
    public class RingShopPanel : MonoBehaviour
    {
        [Tooltip("표시할 카드들. rings.csv 의 sort_order 순으로 배치한다.")]
        [SerializeField] private RingShopEntry[] _entries;

        [Tooltip("보유 레거시 포인트. 비워 두면 표시하지 않는다.")]
        [SerializeField] private TextMeshProUGUI _pointLabel;

        [Tooltip("마우스 호버 시 상세 정보를 띄울 툴팁 패널")]
        [SerializeField] private RingTooltip _tooltip;

        [Tooltip("이름·설명·효과의 출처. Managers 프리팹이 쓰는 것과 같은 에셋을 넣는다.")]
        [SerializeField] private BalanceData _balanceData;

        private void OnEnable()
        {
            if (_tooltip == null)
            {
                _tooltip = GetComponentInChildren<RingTooltip>(true);
            }
            _tooltip?.Hide();
            EnsureBalanceData();
            BindEntries();
            RefreshAll();
        }

        private void OnDisable()
        {
            _tooltip?.Hide();
        }

        private void EnsureBalanceData()
        {
            if (_balanceData == null)
            {
                _balanceData = Resources.Load<BalanceData>("BalanceData");
            }
        }

        /// <summary>
        /// 카드에 서비스를 넣어 준다. 매니저는 DontDestroyOnLoad 지만 이 패널은 탭을 펼칠 때
        /// 생기므로, 캐시하지 않고 살아날 때마다 다시 잡는다.
        /// </summary>
        private void BindEntries()
        {
            var shop = EconomyManager.RingShop;
            var legacy = EconomyManager.Legacy;

            if (shop == null || legacy == null)
            {
                Debug.LogWarning("[RingShopPanel] EconomyManager 통로가 아직 없다. " +
                                 "카드는 '상점을 열 수 없다'로 표시된다.", this);
            }

            if (_entries == null || _entries.Length == 0)
            {
                _entries = GetComponentsInChildren<RingShopEntry>(true);
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
                if (_tooltip != null)
                {
                    _entries[i].SetTooltip(_tooltip);
                }
                _entries[i].Bind(_balanceData, shop, legacy, RefreshAll);
            }
        }

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

            RenderPoints();
        }

        private void RenderPoints()
        {
            if (_pointLabel == null)
            {
                return;
            }

            var legacy = EconomyManager.Legacy;
            _pointLabel.text = legacy == null ? "—" : $"{legacy.CurrentLegacyPoints:N0}";
        }
    }
}
