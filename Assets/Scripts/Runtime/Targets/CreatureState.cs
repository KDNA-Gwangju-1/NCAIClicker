namespace NCAIClicker.Targets
{
    /// <summary>
    /// 크리처의 FSM 행동 상태를 정의한다 (ARCHITECTURE 4절).
    /// Idle: 정지 대기
    /// Moving: 배회 이동
    /// BeingHit: 피격 경직
    /// Fleeing: 피격 후 도망
    /// </summary>
    public enum CreatureState
    {
        Idle,
        Moving,
        BeingHit,
        Fleeing
    }
}
