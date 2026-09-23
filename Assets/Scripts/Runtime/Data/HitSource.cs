namespace NCAIClicker.Data
{
    /// <summary>
    /// 타격 발신원 구분
    /// </summary>
    public enum HitSource
    {
        Hover,
        AutoHammer,

        /// <summary>화난 저금통이 다른 저금통에 부딪힌 타격 (#297). 호버 전용 집계(피버·정확도·자동 망치 발동)에 들어가지 않는다.</summary>
        Charge
    }
}
