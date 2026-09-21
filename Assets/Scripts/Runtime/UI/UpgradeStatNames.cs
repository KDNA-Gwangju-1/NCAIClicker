using NCAIClicker.Data;

namespace NCAIClicker.UI
{
    /// <summary>
    /// `StatId` 를 화면에 띄울 한글 이름으로 바꾼다. 상점 카드(6.8)가 효과 줄을 만들 때 쓴다.
    ///
    /// **여기 있는 것은 이름뿐이고 수치는 하나도 없다.** 효과의 값·단위·부호는 전부
    /// `upgrade_effects.csv` 에서 읽는다 (AGENTS.md "밸런스 수치의 원본은 CSV").
    /// 표시 문자열은 밸런스 값이 아니라 UI 의 몫이라 UI·연출 쪽에 둔다.
    ///
    /// 이름의 근거는 `docs/BALANCE.md` 6절의 "stat 이름이 가리키는 기준값" 표다.
    /// `StatId` 에 항목이 늘면 여기도 한 줄 추가한다 — 빠뜨리면 enum 이름이 그대로 노출되므로
    /// 화면에서 바로 눈에 띈다 (조용히 빈칸이 되지 않게 한 것이다).
    /// </summary>
    public static class UpgradeStatNames
    {
        public static string Get(StatId stat)
        {
            switch (stat)
            {
                case StatId.BaseHitPower: return "타격 피해";
                case StatId.HitRadius: return "대상 크기";
                case StatId.AutoHammerCount: return "자동 망치";
                case StatId.AutoHammerPower: return "자동 망치 피해";
                case StatId.AutoHammerHitsPerSec: return "자동 망치 속도";
                case StatId.FeverDuration: return "피버 지속";
                case StatId.FeverMultiplier: return "피버 배율";
                case StatId.FeverGaugePerHit: return "피버 게이지";
                case StatId.MaxStamina: return "최대 스태미나";
                case StatId.IdleDrainPerSec: return "초당 소모";
                case StatId.MoveDrainPerUnit: return "이동 소모";
                case StatId.HitDrainPerSwing: return "스윙 소모";
                case StatId.CoinBonusMultiplier: return "코인 보너스";
                case StatId.SpawnCount: return "동시 출현";
                case StatId.SpawnIntervalSec: return "재등장 대기";
                case StatId.ExtraSpawnChance: return "추가 생성 확률";
                default: return stat.ToString();
            }
        }
    }
}
