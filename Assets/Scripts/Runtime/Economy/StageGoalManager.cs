using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using UnityEngine;

namespace NCAIClicker.Economy
{
    /// <summary>
    /// 단계 클리어를 판정한다. **기준은 고지서 납부다** (GDD 5절).
    ///
    /// 예전에는 "런 순수입이 stages.csv 의 goal_coin 에 도달" 로 판정했다. 그런데 goal_coin 과
    /// bill_amount 가 모든 단계에서 같은 값이었다 — 같은 개념을 두 열로 적어 둔 것이다.
    /// 두 축으로 나누면 이월한 코인으로 납부했을 때 "고지서는 냈는데 단계는 안 올랐다" 가 된다.
    ///
    /// Managers 프리팹(Resources/Managers)에 붙인다. 생성은 ManagerBootstrap 이 한다.
    /// </summary>
    public class StageGoalManager : MonoBehaviour, IRunScoped, IStageService
    {
        [SerializeField] private BalanceData _balanceData;

        /// <summary>0부터 시작하는 배열 인덱스. StageDef.Stage 는 1부터라 +1 해서 조회한다.</summary>
        private int _stageIndex;

        private bool _isStageCleared;

        /// <summary>SaveData.StageIndex 와 같은 기준(0부터)이다.</summary>
        public int CurrentStageIndex => _stageIndex;

        public int CurrentStageNumber => _stageIndex + 1;

        public bool IsStageCleared => _isStageCleared;

        public bool IsMaxStage => _balanceData != null && _stageIndex >= _balanceData.Stages.Count - 1;

        public bool AdvanceStage()
        {
            if (_balanceData == null || _stageIndex >= _balanceData.Stages.Count - 1)
            {
                return false;
            }

            _stageIndex++;
            Debug.Log($"[StageGoalManager] 목표 달성으로 다음 단계로 진행: {_stageIndex + 1}단계");
            return true;
        }

        public void RestoreStage(int stageIndex)
        {
            if (_balanceData != null && stageIndex >= _balanceData.Stages.Count)
            {
                stageIndex = _balanceData.Stages.Count - 1;
            }
            _stageIndex = Mathf.Max(0, stageIndex);
            _isStageCleared = false;
        }

        private void Awake()
        {
            if (_balanceData == null)
            {
                Debug.LogError("[StageGoalManager] BalanceData 가 연결되지 않았다. " +
                               "단계를 판정하지 않으니 Managers 프리팹의 참조를 확인하라.");
            }
        }

        // 정적 이벤트는 구독과 해제를 쌍으로 맞춘다. 빠뜨리면 판정이 중복되거나 멈춘다 (AGENTS.md).
        private void OnEnable()
        {
            GameEvents.OnBillPaid += HandleBillPaid;
        }

        private void OnDisable()
        {
            GameEvents.OnBillPaid -= HandleBillPaid;
        }

        /// <summary>새 고지서를 받을 런이므로 클리어 표시를 되돌린다. 단계 자체는 유지한다.</summary>
        public void BeginRun()
        {
            _isStageCleared = false;
        }

        /// <summary>단계 진행은 납부 시점에 끝난다. 런 종료에는 할 일이 없다.</summary>
        public void EndRun()
        {
        }

        /// <summary>
        /// 고지서를 내면 그 단계는 끝난다. 다음 런에서 BillManager 가 새 고지서를 발행할 때
        /// 이미 올라간 단계의 금액과 기한을 읽도록, 단계 상승을 여기서 바로 처리한다.
        /// </summary>
        private void HandleBillPaid(Bill bill)
        {
            if (_isStageCleared || _balanceData == null)
            {
                return;
            }

            // 설정된 범위를 넘는 단계는 조회하지 않는다 (GDD 5절).
            var stage = _balanceData.GetStage(_stageIndex + 1);
            if (stage == null)
            {
                return;
            }

            _isStageCleared = true;
            GameEvents.PublishStageGoalReached(stage.Stage);
            AdvanceStage();
        }

    }
}
