using System;
using NCAIClicker.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 퍼크 후보 한 장. 이름과 효과를 적고, 눌리면 자기 퍼크 id 를 콜백으로 올린다 (이슈 #92).
    ///
    /// 뽑기·적용은 4.2(#28·#126)의 몫이고 이 클래스는 **표시와 입력만** 맡는다.
    /// 효과 문구의 숫자는 전부 <see cref="PerkDef"/> 에서 읽는다 — perks.csv 가 값의 원본이고
    /// 문서·코드에 같은 숫자를 적지 않는다 (AGENTS.md).
    /// </summary>
    public class PerkCardView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _nameLabel;
        [SerializeField] private TextMeshProUGUI _effectLabel;
        [SerializeField] private Button _button;

        /// <summary>이 카드가 들고 있는 퍼크 id. 비어 있으면 카드가 채워지지 않은 것이다.</summary>
        public string PerkId { get; private set; }

        private Action<string> _onChosen;

        /// <summary>
        /// 카드를 퍼크 하나로 채운다. <paramref name="perk"/> 가 없으면 카드를 숨긴다 —
        /// 후보가 3개보다 적게 오는 경우(퍼크 풀이 줄어든 경우)에도 빈 카드가 남지 않게 한다.
        /// </summary>
        public void Bind(PerkDef perk, Action<string> onChosen)
        {
            _onChosen = onChosen;

            if (perk == null)
            {
                PerkId = null;
                gameObject.SetActive(false);
                return;
            }

            PerkId = perk.Id;
            gameObject.SetActive(true);

            if (_nameLabel != null)
            {
                _nameLabel.text = perk.DisplayName;
            }
            if (_effectLabel != null)
            {
                _effectLabel.text = DescribeEffect(perk);
            }
        }

        /// <summary>
        /// 효과 한 줄. **포맷만 코드에 있고 숫자는 CSV 에서 온다.**
        /// 단위가 타입마다 다르다 — 회복량은 점수, 코인은 배율, 나머지는 퍼센트다
        /// (BalanceData.PerkDef.Value 주석).
        /// </summary>
        public static string DescribeEffect(PerkDef perk)
        {
            if (perk == null)
            {
                return string.Empty;
            }

            switch (perk.Type)
            {
                case PerkType.StaminaRestore:
                    return $"스태미나 +{perk.Value:0.#}";
                case PerkType.CoinGainBoost:
                    return $"코인 {perk.Value:0.#}배 · {perk.DurationSec:0.#}초";
                case PerkType.HitPowerBoost:
                    return $"타격력 +{perk.Value:0.#}%";
                case PerkType.HitRadiusBoost:
                    return $"판정 반경 +{perk.Value:0.#}%";
                default:
                    // 새 PerkType 이 늘었는데 문구를 안 더한 경우. 빈 카드보다 id 라도 보이는 편이 낫다.
                    return perk.Id;
            }
        }

        private void OnEnable()
        {
            if (_button != null)
            {
                _button.onClick.AddListener(HandleClicked);
            }
        }

        private void OnDisable()
        {
            if (_button != null)
            {
                _button.onClick.RemoveListener(HandleClicked);
            }
        }

        private void HandleClicked()
        {
            if (string.IsNullOrEmpty(PerkId))
            {
                return;
            }
            _onChosen?.Invoke(PerkId);
        }
    }
}
