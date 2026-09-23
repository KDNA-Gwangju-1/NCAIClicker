using NCAIClicker.Data;
using NCAIClicker.Economy;
using TMPro;
using UnityEngine;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 저금통 도감 탭 (#299). 원작 상점의 저금통 선반에 대응한다 — 해금 순서대로 카드가 놓이고 잠긴 칸은 실루엣이다.
    ///
    /// **이벤트를 구독하지 않는다.** 누적 수입은 정산(EndRun) 때만 바뀌고 이 탭은 그 뒤 메뉴에서 열린다.
    /// 탭을 펼칠 때마다 켜지므로 살아날 때 한 번 읽으면 충분하다 (RingShopPanel 과 같은 이유).
    /// </summary>
    public class CreatureCodexPanel : MonoBehaviour
    {
        [Tooltip("해금 순서(BalanceData.GetUnlockOrder)대로 배치한 카드. 프리팹 생성기가 채운다.")]
        [SerializeField] private CreatureCodexEntry[] _entries;

        [Tooltip("이번 회차 누적 수입. 비워 두면 표시하지 않는다.")]
        [SerializeField] private TextMeshProUGUI _earnedLabel;

        [SerializeField] private BalanceData _balanceData;

        private void OnEnable()
        {
            if (_balanceData == null)
            {
                _balanceData = Resources.Load<BalanceData>("BalanceData");
            }
            if (_entries == null || _entries.Length == 0)
            {
                _entries = GetComponentsInChildren<CreatureCodexEntry>(true);
            }
            Render();
        }

        private void Render()
        {
            var economy = EconomyManager.Instance;
            var earned = economy != null ? economy.EarnedTotal : 0L;

            foreach (var entry in _entries)
            {
                if (entry != null)
                {
                    entry.Render(_balanceData, earned);
                }
            }

            if (_earnedLabel != null)
            {
                _earnedLabel.text = $"이번 회차 누적 ${earned:N0}";
            }
        }
    }
}
