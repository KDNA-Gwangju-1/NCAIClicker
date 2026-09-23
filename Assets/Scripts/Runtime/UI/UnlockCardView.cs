using System;
using System.Collections.Generic;
using System.Globalization;
using NCAIClicker.Data;
using NCAIClicker.Economy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 새 저금통 해금 카드 (이슈 #300). 해금이 일어난 정산창 위에 원작처럼 "{이름} 해금!" 카드를 띄운다 —
    /// 왼쪽은 3D 외형과 빛살, 오른쪽은 HP 와 한 번 박살낼 때 나오는 코인 구간·확률, 그 아래 역할 한 줄이다.
    /// 한 번에 둘 이상 해금되면 받은 순서대로 한 장씩 보여 주고, 클릭하면 다음 장으로 넘어가며 마지막 장에서 닫힌다.
    ///
    /// 무엇이 해금됐는지는 여기서 정하지 않는다 — ResultUIController 가 회차 누적 수입으로 계산해 넘긴다 (#301).
    /// 역할 문구는 도감(#299)과 같은 CreatureCodexEntry.GetRoleText 를 쓴다 — 한 곳에서만 만들어 두 화면이 어긋나지 않는다.
    /// </summary>
    public class UnlockCardView : MonoBehaviour
    {
        /// <summary>1% 미만 구간은 "0%" 로 보이면 없는 것처럼 읽혀 따로 적는다.</summary>
        private const string UnderOnePercentText = "<1%";

        /// <summary>
        /// 이보다 드문 구간은 줄을 내지 않는다. 금광석(40개)의 "$40 — 전부 $1" 은 약 10억 분의 1 이라 사실상 없는데,
        /// 줄로 보이면 나올 수 있는 결과처럼 읽힌다. 0.1% 이상은 "<1%" 로 남긴다.
        /// </summary>
        private const double MinShownProbability = 0.001;

        [SerializeField] private GameObject _cardRoot;
        [SerializeField] private Button _dismissButton;
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private CreaturePreview _preview;
        [SerializeField] private TextMeshProUGUI _hpValueText;
        [SerializeField] private TextMeshProUGUI _roleText;

        [Tooltip("코인 구간 행. 액면 종류 수만큼 두고 남는 행은 숨긴다")]
        [SerializeField] private GameObject[] _bandRows;
        [SerializeField] private TextMeshProUGUI[] _bandRangeTexts;
        [SerializeField] private TextMeshProUGUI[] _bandChanceTexts;

        [SerializeField] private RectTransform _rays;
        [SerializeField] private float _rayTurnSpeedDegPerSec = 18f;

        private readonly Queue<TargetDef> _pending = new Queue<TargetDef>();
        private BalanceData _balanceData;

        public bool IsOpen => _cardRoot != null && _cardRoot.activeSelf;

        /// <summary>지금 보이는 카드 뒤에 남은 장 수.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>마지막 장을 닫았을 때 한 번 알린다.</summary>
        public event Action Closed;

        private void Awake()
        {
            HideImmediate();
        }

        private void OnEnable()
        {
            if (_dismissButton != null)
            {
                _dismissButton.onClick.AddListener(HandleDismissClicked);
            }
        }

        private void OnDisable()
        {
            if (_dismissButton != null)
            {
                _dismissButton.onClick.RemoveListener(HandleDismissClicked);
            }
        }

        private void Update()
        {
            if (IsOpen && _rays != null)
            {
                // 정산창은 시간이 멈춘 상태일 수 있어 실제 시간으로 돈다.
                _rays.Rotate(0f, 0f, -_rayTurnSpeedDegPerSec * Time.unscaledDeltaTime);
            }
        }

        /// <summary>targets 를 해금 순서대로 받아 첫 장을 띄운다. 비어 있으면 아무것도 하지 않는다.</summary>
        public void Show(IReadOnlyList<TargetDef> targets, BalanceData balanceData)
        {
            if (targets == null || targets.Count == 0 || _cardRoot == null)
            {
                return;
            }

            _balanceData = balanceData;
            _pending.Clear();
            foreach (var target in targets)
            {
                if (target != null)
                {
                    _pending.Enqueue(target);
                }
            }
            ShowNext();
        }

        /// <summary>남은 장까지 모두 버리고 닫는다. 정산창이 닫히거나 파산 화면으로 바뀔 때 쓴다 — Closed 는 알리지 않는다.</summary>
        public void HideImmediate()
        {
            _pending.Clear();
            if (_preview != null)
            {
                _preview.Clear();
            }
            if (_cardRoot != null)
            {
                _cardRoot.SetActive(false);
            }
        }

        private void HandleDismissClicked()
        {
            if (_pending.Count > 0)
            {
                ShowNext();
                return;
            }

            HideImmediate();
            OnClosed();
        }

        private void OnClosed()
        {
            Closed?.Invoke();
        }

        private void ShowNext()
        {
            var target = _pending.Dequeue();
            _cardRoot.SetActive(true);

            if (_titleText != null)
            {
                _titleText.text = target.DisplayName + " 해금!";
            }
            if (_hpValueText != null)
            {
                _hpValueText.text = target.Hp.ToString(CultureInfo.InvariantCulture);
            }
            if (_roleText != null)
            {
                _roleText.text = CreatureCodexEntry.GetRoleText(target);
            }
            if (_preview != null)
            {
                _preview.Show(target.Id);
            }
            RenderBands(CoinLottery.GetRewardBands(_balanceData, target.MinDenomId, target.MaxDenomId, target.CoinCount));
        }

        private void RenderBands(IReadOnlyList<CoinRewardBand> allBands)
        {
            var bands = GetShownBands(allBands);
            var rowCount = _bandRows != null ? _bandRows.Length : 0;
            for (var i = 0; i < rowCount; i++)
            {
                var hasBand = i < bands.Count;
                if (_bandRows[i] != null)
                {
                    _bandRows[i].SetActive(hasBand);
                }
                if (!hasBand)
                {
                    continue;
                }
                if (_bandRangeTexts != null && i < _bandRangeTexts.Length && _bandRangeTexts[i] != null)
                {
                    _bandRangeTexts[i].text = FormatRange(bands[i]);
                }
                if (_bandChanceTexts != null && i < _bandChanceTexts.Length && _bandChanceTexts[i] != null)
                {
                    _bandChanceTexts[i].text = FormatChance(bands[i].Probability);
                }
            }
        }

        /// <summary>카드에 줄로 내는 구간 — MinShownProbability 미만은 뺀다. 순서는 그대로(값 오름차순).</summary>
        public static List<CoinRewardBand> GetShownBands(IReadOnlyList<CoinRewardBand> bands)
        {
            var shown = new List<CoinRewardBand>();
            foreach (var band in bands)
            {
                if (band.Probability >= MinShownProbability)
                {
                    shown.Add(band);
                }
            }
            return shown;
        }

        /// <summary>합계가 한 값뿐이면 "$50", 아니면 "$125–$200" (원작 카드와 같은 모양).</summary>
        public static string FormatRange(CoinRewardBand band)
        {
            return band.Min == band.Max
                ? "$" + band.Min.ToString("N0", CultureInfo.InvariantCulture)
                : "$" + band.Min.ToString("N0", CultureInfo.InvariantCulture)
                  + "–$" + band.Max.ToString("N0", CultureInfo.InvariantCulture);
        }

        public static string FormatChance(double probability)
        {
            if (probability < 0.01)
            {
                return UnderOnePercentText;
            }
            // 기본 반올림은 짝수 쪽(44.5 → 44)이라 사람이 계산한 값과 어긋난다.
            return Math.Round(probability * 100.0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%";
        }
    }
}
