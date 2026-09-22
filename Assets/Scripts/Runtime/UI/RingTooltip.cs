using System.Text;
using NCAIClicker.Data;
using TMPro;
using UnityEngine;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 반지 슬롯에 마우스 오버 시 표시되는 상세 정보 툴팁 패널.
    /// 원작 레퍼런스의 우측 정보 패널 역할을 수행한다.
    /// </summary>
    public class RingTooltip : MonoBehaviour
    {
        [Header("UI 요소")]
        [SerializeField] private GameObject _rootPanel;
        [SerializeField] private TextMeshProUGUI _titleLabel;
        [SerializeField] private TextMeshProUGUI _descriptionLabel;
        [SerializeField] private TextMeshProUGUI _effectLabel;
        [SerializeField] private TextMeshProUGUI _levelLabel;
        [SerializeField] private TextMeshProUGUI _costLabel;

        private void Awake()
        {
            Hide();
        }

        public void Show(RingDef definition, int currentLevel, long cost, long currentPoints)
        {
            if (definition == null)
            {
                Hide();
                return;
            }

            if (_rootPanel != null)
            {
                _rootPanel.SetActive(true);
            }

            if (_titleLabel != null)
            {
                _titleLabel.text = definition.DisplayName;
            }

            if (_descriptionLabel != null)
            {
                _descriptionLabel.text = definition.Description;
            }

            if (_levelLabel != null)
            {
                _levelLabel.text = $"현재 레벨: Lv {currentLevel} / {definition.MaxLevel}";
            }

            if (_effectLabel != null)
            {
                _effectLabel.text = FormatEffectDetails(definition, currentLevel);
            }

            if (_costLabel != null)
            {
                if (currentLevel >= definition.MaxLevel)
                {
                    _costLabel.text = "<color=#E5C07B>최대 레벨 도달</color>";
                }
                else if (cost > currentPoints)
                {
                    _costLabel.text = $"다음 레벨: <color=#E06C75>{cost:N0} 포인트 (부족)</color>";
                }
                else
                {
                    _costLabel.text = $"다음 레벨: <color=#98C379>{cost:N0} 포인트</color>";
                }
            }
        }

        public void Hide()
        {
            if (_rootPanel != null)
            {
                _rootPanel.SetActive(false);
            }
        }

        private static string FormatEffectDetails(RingDef definition, int currentLevel)
        {
            if (definition.Effects == null || definition.Effects.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            for (var i = 0; i < definition.Effects.Count; i++)
            {
                var effect = definition.Effects[i];
                var statName = UpgradeStatNames.Get(effect.Stat);
                var unit = effect.Type == EffectType.Percent ? "%" : string.Empty;
                var currentVal = effect.ValuePerLevel * currentLevel;
                var nextVal = effect.ValuePerLevel * (currentLevel + 1);

                if (sb.Length > 0)
                {
                    sb.Append('\n');
                }

                // 현재값과 다음값을 줄로 나눈다 — 툴팁 폭(300)에서 한 줄이면 괄호 안에서 꺾인다 (#261).
                sb.Append($"• {statName}: +{currentVal:0.##}{unit}\n  (다음: +{nextVal:0.##}{unit})");
            }
            return sb.ToString();
        }
    }
}
