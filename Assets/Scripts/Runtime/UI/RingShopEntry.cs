using System;
using NCAIClicker.Data;
using NCAIClicker.Interfaces;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 반지 카드 한 장 (이슈 #183). <see cref="UpgradeShopEntry"/> 와 짝이고
    /// **다른 것은 화폐뿐이다** — 이쪽은 코인이 아니라 레거시 포인트로 산다.
    /// 마우스 오버 시 <see cref="RingTooltip"/> 에 상세 정보를 띄운다.
    /// </summary>
    public class RingShopEntry : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("rings.csv 의 id")]
        [SerializeField] private string _ringId;

        [SerializeField] private TextMeshProUGUI _nameLabel;
        [SerializeField] private TextMeshProUGUI _descriptionLabel;
        [SerializeField] private TextMeshProUGUI _levelLabel;
        [SerializeField] private TextMeshProUGUI _effectLabel;
        [SerializeField] private Button _purchaseButton;
        [SerializeField] private TextMeshProUGUI _costLabel;

        [Tooltip("못 사는 이유. 비면 살 수 있다는 뜻이다.")]
        [SerializeField] private TextMeshProUGUI _reasonLabel;

        [Tooltip("마우스 호버 시 정보를 보여줄 툴팁")]
        [SerializeField] private RingTooltip _tooltip;

        private BalanceData _balanceData;
        private IRingShop _shop;
        private ILegacyService _legacy;
        private Action _onPurchased;
        private RingDef _definition;

        private void OnEnable()
        {
            if (_purchaseButton != null)
            {
                _purchaseButton.onClick.AddListener(HandlePurchaseClicked);
            }
        }

        private void OnDisable()
        {
            if (_purchaseButton != null)
            {
                _purchaseButton.onClick.RemoveListener(HandlePurchaseClicked);
            }
        }

        public void SetTooltip(RingTooltip tooltip)
        {
            _tooltip = tooltip;
        }

        public void SetRingId(string ringId)
        {
            _ringId = ringId;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            UpdateTooltip();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_tooltip != null)
            {
                _tooltip.Hide();
            }
        }

        private void UpdateTooltip()
        {
            if (_tooltip == null || _definition == null)
            {
                return;
            }

            var level = _shop == null ? 0 : _shop.GetRingLevel(_ringId);
            var cost = _shop == null ? 0 : _shop.GetNextRingCost(_ringId);
            var points = _legacy == null ? 0 : _legacy.CurrentLegacyPoints;
            _tooltip.Show(_definition, level, cost, points);
        }

        public void Bind(BalanceData balanceData, IRingShop shop, ILegacyService legacy, Action onPurchased)
        {
            _balanceData = balanceData;
            _shop = shop;
            _legacy = legacy;
            _onPurchased = onPurchased;
            _definition = _balanceData == null ? null : _balanceData.GetRing(_ringId);

            if (_definition == null)
            {
                Debug.LogWarning($"[RingShopEntry] rings.csv 에 '{_ringId}' 가 없다. " +
                                 "카드의 Ring Id 를 확인하라.", this);
            }

            RenderStaticParts();
            Refresh();
        }

        /// <summary>레벨·비용·구매 가능 여부만 다시 그린다. 이름·설명·효과는 바뀌지 않는다.</summary>
        public void Refresh()
        {
            if (_definition == null)
            {
                SetInteractable(false, "정의 없음", string.Empty);
                return;
            }

            if (_levelLabel != null)
            {
                var level = _shop == null ? 0 : _shop.GetRingLevel(_ringId);
                _levelLabel.text = $"Lv {level} / {_definition.MaxLevel}";
            }

            if (_shop == null || _legacy == null)
            {
                SetInteractable(false, "상점을 열 수 없다", string.Empty);
                return;
            }

            var cost = _shop.GetNextRingCost(_ringId);

            // IRingShop 계약: 최대 레벨이거나 없는 id 면 long.MaxValue 다. 숫자로 쓰지 않고
            // "더 살 수 없다"로만 읽는다 (IUpgradeShop 과 같은 약속).
            if (cost == long.MaxValue)
            {
                SetInteractable(false, "최대 레벨", "—");
                return;
            }

            var points = _legacy.CurrentLegacyPoints;
            if (points < cost)
            {
                SetInteractable(false, $"포인트 {cost - points:N0} 부족", $"{cost:N0}");
                return;
            }

            SetInteractable(true, string.Empty, $"{cost:N0}");
        }

        private void RenderStaticParts()
        {
            if (_definition == null)
            {
                return;
            }

            if (_nameLabel != null)
            {
                _nameLabel.text = _definition.DisplayName;
            }
            if (_descriptionLabel != null)
            {
                _descriptionLabel.text = _definition.Description;
            }
            if (_effectLabel != null)
            {
                _effectLabel.text = DescribeEffects(_definition);
            }
        }

        /// <summary>
        /// 효과 요약. **수치는 CSV 에서 읽는다** — 코드에도 프리팹에도 적지 않는다 (AGENTS.md).
        /// 스탯의 한글 이름은 업그레이드 카드와 같은 표를 쓴다.
        /// </summary>
        private static string DescribeEffects(RingDef definition)
        {
            var lines = new System.Text.StringBuilder();
            foreach (var effect in definition.Effects)
            {
                if (lines.Length > 0)
                {
                    lines.Append('\n');
                }

                var statName = UpgradeStatNames.Get(effect.Stat);
                var suffix = effect.Type == EffectType.Percent ? "%" : string.Empty;
                lines.Append($"{statName} +{effect.ValuePerLevel:0.##}{suffix} / Lv");
            }
            return lines.ToString();
        }

        private void HandlePurchaseClicked()
        {
            if (_shop == null || !_shop.TryPurchaseRing(_ringId))
            {
                // 표시가 최신이 아니었다는 뜻이다. 다시 그려 이유를 보여 준다.
                Refresh();
                UpdateTooltip();
                return;
            }

            _onPurchased?.Invoke();
            UpdateTooltip();
        }

        private void SetInteractable(bool canBuy, string reason, string costText)
        {
            if (_purchaseButton != null)
            {
                _purchaseButton.interactable = canBuy;
            }
            if (_costLabel != null)
            {
                _costLabel.text = costText;
            }
            if (_reasonLabel != null)
            {
                _reasonLabel.text = reason;
            }
        }
    }
}
