namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// NCAI 메뉴의 표시 순서를 한곳에 모은다.
    /// <para>
    /// <c>MenuItem</c> 에 priority 를 주지 않으면 등록 순서대로 붙어, 의도 없이 섞인다.
    /// 값을 각 파일에 흩어 두면 같은 문제가 다시 생기므로 여기서만 정한다.
    /// </para>
    /// <para>
    /// Unity 는 priority 가 <b>11 이상 벌어지면 구분선</b>을 넣는다. 묶음 사이를 그만큼 띄운다.
    /// </para>
    /// GitHub 이슈 #159.
    /// </summary>
    internal static class MenuPriority
    {
        // 데이터
        public const int BalanceImport = 1;

        // 검증
        public const int ValidationWindow = 20;
        public const int RunAll = 21;

        // 환경
        public const int ProjectSetup = 40;

        // 디버그 (Play Mode)
        public const int DebugAdvanceToDueDay = 60;
        public const int DebugDepleteStamina = 61;
        public const int DebugAdvanceOneDay = 62;
        public const int DebugAdvanceStage = 63;
    }
}
