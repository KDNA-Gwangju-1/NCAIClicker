using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using UnityEngine;

namespace NCAIClicker.Economy
{
    /// <summary>
    /// 런 순수입(RunCoin)이 현재 단계의 목표 코인에 도달했는지 판정한다.
    /// 완료 기준의 정본은 GitHub 이슈 #26. 판정 기준은 GDD.md 91번째 줄과
    /// ARCHITECTURE.md 코인 계약 5번 — 전날 잔여·대출 원금·지출은 영향을 주지 않는
    /// "이번 런에서 번 코인"만 본다. 목표값은 StageDef.GoalCoin 이 정본이며
    /// EconomyConfig.StageGoalGrowth 는 여기서 쓰지 않는다 (BALANCE.md 6절, 오프라인 산출 전용).
    ///
    /// ponytail: 목표 달성 시 다음 단계로 넘어가는 처리(단계 인덱스 증가·저장·결과 화면 표시)는
    /// 이 이슈의 완료 기준("판정")을 벗어난다. GDD.md 92번째 줄은 그 전환이 "결과 정산 후"
    /// 일어난다고 적혀 있어 Result 화면(6.x) 쪽 몫으로 보고 여기서는 판정 결과만 이벤트로 알린다.
    /// 단계를 실제로 올리는 진입점이 필요해지면 이 클래스에 메서드를 추가한다.
    ///
    /// Managers 프리팹(Resources/Managers)에 붙인다. 생성은 ManagerBootstrap 이 한다.
    /// </summary>
    public class StageGoalManager : MonoBehaviour, IRunScoped
    {
        [SerializeField] private BalanceData _balanceData;

        /// <summary>0부터 시작하는 배열 인덱스. StageDef.Stage 는 1부터라 +1 해서 조회한다.</summary>
        private int _stageIndex;

        private bool _isGoalReached;

        /// <summary>SaveData.StageIndex 와 같은 기준(0부터)이다.</summary>
        public int CurrentStageIndex => _stageIndex;

        public bool IsGoalReached => _isGoalReached;

        private void Awake()
        {
            if (_balanceData == null)
            {
                Debug.LogError("[StageGoalManager] BalanceData 가 연결되지 않았다. " +
                               "단계 목표를 판정하지 않으니 Managers 프리팹의 참조를 확인하라.");
            }
        }

        // 정적 이벤트는 구독과 해제를 쌍으로 맞춘다. 빠뜨리면 판정이 중복되거나 멈춘다 (AGENTS.md).
        private void OnEnable()
        {
            GameEvents.OnRunCoinChanged += HandleRunCoinChanged;
        }

        private void OnDisable()
        {
            GameEvents.OnRunCoinChanged -= HandleRunCoinChanged;
        }

        /// <summary>런마다 다시 판정할 수 있도록 플래그만 되돌린다. 단계 자체는 유지한다.</summary>
        public void BeginRun()
        {
            _isGoalReached = false;
        }

        public void EndRun()
        {
        }

        private void HandleRunCoinChanged(long runCoin)
        {
            if (_isGoalReached || _balanceData == null)
            {
                return;
            }

            // 설정된 범위를 넘는 단계는 조회하지 않는다 (GDD.md 208번째 줄).
            var stage = _balanceData.GetStage(_stageIndex + 1);
            if (stage == null || runCoin < stage.GoalCoin)
            {
                return;
            }

            _isGoalReached = true;
            GameEvents.PublishStageGoalReached(stage.Stage);
        }
    }
}
