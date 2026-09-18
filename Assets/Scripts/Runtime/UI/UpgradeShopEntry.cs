using System.Text;
using NCAIClicker.Data;
using NCAIClicker.Interfaces;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 업그레이드 상점의 카드 한 장. 한 종류(`upgrades.csv` 의 id 하나)를 맡아 이름·설명·효과·현재
    /// 레벨·다음 비용을 보여주고 구매 버튼을 낸다. 이슈 #91(6.8) 완료 기준의 표시·구매 부분이다.
    ///
    /// **표시 값을 이 컴포넌트에 적어 두지 않는다** (이슈 본문). 이름·설명·최대 레벨은
    /// BalanceData(= upgrades.csv 산출물)에서, 레벨과 다음 비용은 IUpgradeShop 에서 그때그때 읽는다.
    ///
    /// **지갑을 직접 깎지 않는다.** 구매는 IUpgradeShop.TryPurchase 가 하고, 그 안에서
    /// IEconomyService.TrySpendCoin 으로 차감된다 — 코인 계산 경로를 둘로 만들지 않는다
    /// (AGENTS.md "코인 배율과 대출 징수는 EconomyManager 안에서만").
    ///
    /// 서비스는 부모(UpgradeShopPanel)가 넣어 준다. 이 카드가 스스로 매니저를 찾지 않는 이유는,
    /// 4장이 각자 찾으면 같은 조회가 4번 일어나고 "누가 언제 잡았나"가 흩어지기 때문이다.
    /// </summary>
    public class UpgradeShopEntry : MonoBehaviour
    {
        [Tooltip("upgrades.csv 의 id. 이 카드가 맡을 업그레이드 한 종류를 가리킨다.")]
        [SerializeField] private string _upgradeId;

        [SerializeField] private TextMeshProUGUI _nameLabel;
        [SerializeField] private TextMeshProUGUI _descriptionLabel;

        [Tooltip("'Lv 3 / 20' 형태.")]
        [SerializeField] private TextMeshProUGUI _levelLabel;

        [Tooltip("'타격 피해 +0.35/Lv · 대상 크기 +2%/Lv' 형태. upgrade_effects.csv 에서 만든다.")]
        [SerializeField] private TextMeshProUGUI _effectLabel;

        [SerializeField] private Button _purchaseButton;
        [SerializeField] private TextMeshProUGUI _costLabel;

        [Tooltip("살 수 없는 이유. 살 수 있으면 비운다.")]
        [SerializeField] private TextMeshProUGUI _reasonLabel;

        private BalanceData _balanceData;
        private IUpgradeShop _shop;
        private IEconomyService _economy;
        private UpgradeDef _definition;

        /// <summary>구매가 성공하면 부모가 4장을 모두 다시 그리도록 알린다.</summary>
        private System.Action _onPurchased;

        public string UpgradeId => _upgradeId;

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

        /// <summary>
        /// 부모가 서비스를 넣어 준다. 조립(wiring) 통로이며 서비스 계약이 아니다
        /// (ARCHITECTURE "SetBillService" 문단과 같은 성격).
        /// </summary>
        public void Bind(BalanceData balanceData, IUpgradeShop shop, IEconomyService economy, System.Action onPurchased)
        {
            _balanceData = balanceData;
            _shop = shop;
            _economy = economy;
            _onPurchased = onPurchased;
            _definition = _balanceData == null ? null : _balanceData.GetUpgrade(_upgradeId);

            if (_definition == null)
            {
                Debug.LogWarning($"[UpgradeShopEntry] upgrades.csv 에 '{_upgradeId}' 가 없다. " +
                                 "카드의 Upgrade Id 를 확인하라.", this);
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
                var level = _shop == null ? 0 : _shop.GetLevel(_upgradeId);
                _levelLabel.text = $"Lv {level} / {_definition.MaxLevel}";
            }

            if (_shop == null || _economy == null)
            {
                SetInteractable(false, "상점을 열 수 없다", string.Empty);
                return;
            }

            var cost = _shop.GetNextCost(_upgradeId);

            // IUpgradeShop 계약: 최대 레벨이거나 없는 id 면 long.MaxValue 를 돌려준다
            // (EconomyManager.GetNextCost 주석). 숫자로 쓰지 않고 "더 살 수 없다"로만 읽는다.
            if (cost == long.MaxValue)
            {
                SetInteractable(false, "최대 레벨", "—");
                return;
            }

            var balance = _economy.CurrentCoin;
            if (balance < cost)
            {
                SetInteractable(false, $"코인 {cost - balance:N0} 부족", $"{cost:N0}");
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
                _effectLabel.text = BuildEffectText(_definition);
            }
        }

        /// <summary>
        /// upgrade_effects.csv 의 효과를 한 줄로 만든다. **값과 단위는 CSV 에서 읽고**, 스탯의
        /// 한글 이름만 이 클래스가 붙인다 (UpgradeStatNames).
        ///
        /// CSV 의 note 열을 쓰지 않는 이유: 임포터가 그 열을 읽지 않아 UpgradeEffect 에 없다.
        /// 읽게 하려면 BalanceImporter 와 생성 에셋 스키마를 바꿔야 해서 이 카드 범위를 넘는다.
        /// </summary>
        private static string BuildEffectText(UpgradeDef definition)
        {
            if (definition.Effects == null || definition.Effects.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            for (var i = 0; i < definition.Effects.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append("  ·  ");
                }

                var effect = definition.Effects[i];
                var sign = effect.ValuePerLevel >= 0f ? "+" : string.Empty;
                var unit = effect.Type == EffectType.Percent ? "%" : string.Empty;

                builder.Append(UpgradeStatNames.Get(effect.Stat));
                builder.Append(' ');
                builder.Append(sign);

                // 0.35 는 "0.35", 1 은 "1" 로 — 정수 효과에 소수점을 붙이면 눈에 거슬린다.
                builder.Append(effect.ValuePerLevel.ToString("0.##"));
                builder.Append(unit);
                builder.Append("/Lv");
            }
            return builder.ToString();
        }

        private void HandlePurchaseClicked()
        {
            if (_shop == null || !_shop.TryPurchase(_upgradeId))
            {
                // 버튼이 눌릴 때 이미 못 사는 상태였을 수 있다 (다른 카드를 먼저 사서 코인이 빠진 경우).
                // 실패도 정상 흐름이므로 조용히 다시 그리기만 한다.
                Refresh();
                return;
            }

            // 성공하면 잔액이 바뀌어 다른 카드의 구매 가능 여부도 달라진다. 4장을 함께 다시 그린다.
            _onPurchased?.Invoke();
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
