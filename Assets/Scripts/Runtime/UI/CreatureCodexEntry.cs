using NCAIClicker.Data;
using NCAIClicker.Economy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 저금통 도감 카드 한 장 (#299). 해금됐으면 외형·이름·역할·HP·기대 코인을, 잠겼으면 실루엣과
    /// 해금 기준 누적 수입·진행률을 보여 준다. 판정은 BalanceData.IsUnlocked 만 쓴다 — 목록을 코드에 두지 않는다.
    /// </summary>
    public class CreatureCodexEntry : MonoBehaviour
    {
        [Tooltip("targets.csv 의 id")]
        [SerializeField] private string _targetId;

        [SerializeField] private CreaturePreview _preview;

        [Tooltip("미리보기 RawImage. 잠기면 색을 검정으로 곱해 실루엣으로 만든다.")]
        [SerializeField] private RawImage _previewImage;

        [SerializeField] private TextMeshProUGUI _nameLabel;
        [SerializeField] private TextMeshProUGUI _roleLabel;
        [SerializeField] private TextMeshProUGUI _statLabel;
        [SerializeField] private TextMeshProUGUI _unlockLabel;

        public string TargetId => _targetId;

        public void SetTargetId(string targetId)
        {
            _targetId = targetId;
        }

        public void Render(BalanceData balanceData, long earnedTotal)
        {
            var target = balanceData != null ? balanceData.GetTarget(_targetId) : null;
            if (target == null)
            {
                gameObject.SetActive(false);
                return;
            }

            var isUnlocked = BalanceData.IsUnlocked(target, earnedTotal);
            if (_preview != null)
            {
                _preview.Show(target.Id);
            }
            if (_previewImage != null)
            {
                _previewImage.color = isUnlocked ? Color.white : Color.black;
            }

            SetText(_nameLabel, isUnlocked ? target.DisplayName : "???");
            SetText(_roleLabel, isUnlocked ? KeepWords(GetRoleText(target)) : string.Empty);
            var expected = CoinLottery.GetExpectedValue(balanceData, target.MinDenomId, target.MaxDenomId, target.CoinCount);
            SetText(_statLabel, isUnlocked ? $"HP {target.Hp} · 기대 ${expected:N0}" : string.Empty);
            SetText(_unlockLabel, isUnlocked
                ? string.Empty
                : $"누적 ${target.UnlockEarned:N0} 해금\n{GetUnlockPercent(balanceData, target, earnedTotal)}%");
        }

        /// <summary>
        /// 역할 한 줄. CSV 에 문구 열을 두지 않고 수치에서 만든다 — 수치를 바꾸면 문구도 따라온다 (#299).
        /// </summary>
        public static string GetRoleText(TargetDef target)
        {
            if (target.InstantBreakChance > 0f)
            {
                return $"타격마다 {target.InstantBreakChance * 100f:0}% 즉시 파괴";
            }
            if (target.ChargeSpeed > 0f)
            {
                return "맞으면 분노해 다른 저금통에 돌진";
            }
            if (target.StaminaRestore > 0f)
            {
                return "부수면 스태미나 회복";
            }
            // ponytail: 0.1 은 targets.csv 의 거치형과 이동형 move_speed 사이를 가르는 표시용 경계일 뿐이다.
            // 거치형 속도를 0.1 이상으로 올리면 문구가 "기본형"으로 바뀐다 — 그때 CSV 역할 열을 공용 계약으로 발의한다.
            return target.MoveSpeed < 0.1f ? "거의 안 움직이는 거치형" : "기본형";
        }

        /// <summary>
        /// 진행률 = 직전 해금 기준액부터 이 종류 기준액까지 구간에서 누적 수입이 온 비율 (0~99).
        /// 정산창 "다음 해금" 칸과 같은 셈이라 두 화면의 % 가 어긋나지 않는다.
        /// </summary>
        public static int GetUnlockPercent(BalanceData balanceData, TargetDef target, long earnedTotal)
        {
            var previous = 0L;
            foreach (var other in balanceData.GetUnlockOrder())
            {
                if (other.UnlockEarned < target.UnlockEarned)
                {
                    previous = System.Math.Max(previous, other.UnlockEarned);
                }
            }
            var span = System.Math.Max(1L, target.UnlockEarned - previous);
            return Mathf.Clamp(Mathf.FloorToInt(100f * (earnedTotal - previous) / span), 0, 99);
        }

        /// <summary>TMP 는 한글을 글자마다 끊는다 — 어절을 &lt;nobr&gt; 로 묶어 띄어쓰기에서만 줄을 바꾼다.</summary>
        private static string KeepWords(string text) => "<nobr>" + text.Replace(" ", "</nobr> <nobr>") + "</nobr>";

        private static void SetText(TextMeshProUGUI label, string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }
    }
}
