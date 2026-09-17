namespace NCAIClicker.Interfaces
{
    /// <summary>
    /// MainMenu 버튼이 씬 전환을 요청하는 계약. GameManager만 구현한다 (이슈 #90 발견, 계약 변경 #142).
    /// </summary>
    public interface IGameFlowService
    {
        void StartNewRun();
        void ContinueRun();
        void QuitGame();
    }
}
