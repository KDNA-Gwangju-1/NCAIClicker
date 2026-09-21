namespace NCAIClicker.Interfaces
{
    /// <summary>
    /// 단계(Stage) 진행 상태를 제공하는 공용 조회 계약.
    /// StageGoalManager 가 구현하고 ManagerBootstrap 이 CreatureManager·BillManager·SaveManager 에 주입한다.
    /// SaveManager 는 단계 저장·복원에만 쓴다 (이슈 #203).
    /// 완료 기준의 정본은 GitHub 이슈 #150 (3.7).
    /// </summary>
    public interface IStageService
    {
        /// <summary>0부터 시작하는 단계 인덱스 (0 = 1단계, 1 = 2단계, 2 = 3단계).</summary>
        int CurrentStageIndex { get; }

        /// <summary>1부터 시작하는 단계 번호 (CurrentStageIndex + 1).</summary>
        int CurrentStageNumber { get; }

        /// <summary>
        /// 현재 단계의 고지서를 납부했는지 여부. **고지서 납부가 곧 단계 클리어다** (GDD 5절).
        /// 별도의 목표 코인은 두지 않는다 — 같은 숫자를 두 축으로 나누면 "고지서는 냈는데
        /// 단계는 안 올랐다" 같은 설명할 수 없는 상태가 생긴다.
        /// </summary>
        bool IsStageCleared { get; }

        /// <summary>마지막 단계에 도달했는지 여부.</summary>
        bool IsMaxStage { get; }

        /// <summary>다음 단계로 진행한다. 이미 최대 단계면 false 를 반환한다.</summary>
        bool AdvanceStage();

        /// <summary>단계를 특정 인덱스로 복원하거나 초기화한다 (저장 복원 및 파산 처리용).</summary>
        void RestoreStage(int stageIndex);
    }
}
