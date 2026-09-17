namespace NCAIClicker.Core
{
    /// <summary>GameManager 가 관리하는 런 상태. 전이 규칙은 PATTERNS.md 4절.</summary>
    public enum RunState
    {
        MainMenu,
        Running,
        Result
    }
}
