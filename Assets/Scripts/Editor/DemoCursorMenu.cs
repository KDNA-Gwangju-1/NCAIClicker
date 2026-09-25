using System.Collections.Generic;
using NCAIClicker.Demo;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 제출 영상 녹화용 자동 시연 커서 메뉴 (#45).
    /// Project 창에서 DemoScenario 에셋을 선택한 채 시작하면 그 시나리오를, 아니면 기본 시나리오를 쓴다.
    /// </summary>
    public static class DemoCursorMenu
    {
        private const string DefaultScenarioPath = "Assets/GameData/Demo/DefaultDemoScenario.asset";
        private const int DefaultDayCount = 3;

        [MenuItem("NCAI/시연/시작", false, MenuPriority.DemoStart)]
        public static void StartDemo()
        {
            var scenario = Selection.activeObject as DemoScenario;
            if (scenario == null)
            {
                scenario = AssetDatabase.LoadAssetAtPath<DemoScenario>(DefaultScenarioPath);
            }
            if (scenario == null)
            {
                Debug.LogWarning($"[DemoCursor] 시나리오가 없습니다. 먼저 'NCAI/시연/기본 시나리오 생성' 을 실행하세요 ({DefaultScenarioPath}).");
                return;
            }
            DemoCursor.StartDemo(scenario);
        }

        [MenuItem("NCAI/시연/시작", true)]
        private static bool ValidateStartDemo() => EditorApplication.isPlaying;

        [MenuItem("NCAI/시연/일시정지", false, MenuPriority.DemoPause)]
        public static void PauseDemo() => DemoCursor.Instance.Pause();

        [MenuItem("NCAI/시연/일시정지", true)]
        private static bool ValidatePauseDemo() => DemoCursor.Instance != null && !DemoCursor.Instance.IsPaused;

        [MenuItem("NCAI/시연/재개", false, MenuPriority.DemoResume)]
        public static void ResumeDemo() => DemoCursor.Instance.Resume();

        [MenuItem("NCAI/시연/재개", true)]
        private static bool ValidateResumeDemo() => DemoCursor.Instance != null && DemoCursor.Instance.IsPaused;

        [MenuItem("NCAI/시연/중지", false, MenuPriority.DemoStop)]
        public static void StopDemo() => DemoCursor.StopDemo();

        [MenuItem("NCAI/시연/중지", true)]
        private static bool ValidateStopDemo() => DemoCursor.Instance != null;

        /// <summary>
        /// 콘티 5장(시작 → 플레이 → 결과) 한 판을 기본 시나리오로 만든다. 이미 있으면 덮어쓴다.
        /// </summary>
        [MenuItem("NCAI/시연/기본 시나리오 생성", false, MenuPriority.DemoCreateDefault)]
        public static void CreateDefaultScenario()
        {
            var scenario = AssetDatabase.LoadAssetAtPath<DemoScenario>(DefaultScenarioPath);
            if (scenario == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/GameData/Demo"))
                {
                    AssetDatabase.CreateFolder("Assets/GameData", "Demo");
                }
                scenario = ScriptableObject.CreateInstance<DemoScenario>();
                AssetDatabase.CreateAsset(scenario, DefaultScenarioPath);
            }

            var steps = new List<DemoStep>
            {
                new DemoStep(DemoStepKind.Wait, seconds: 1.5f),
                new DemoStep(DemoStepKind.ClickButton, "새 회차 시작", 10f),
                // 저장이 있으면 덮어쓰기 확인창이 뜬다
                new DemoStep(DemoStepKind.ClickButton, "YesButton", 2f),
            };
            for (var day = 0; day < DefaultDayCount; day++)
            {
                AddDaySteps(steps);
            }
            scenario.SetSteps(steps);
            EditorUtility.SetDirty(scenario);
            AssetDatabase.SaveAssets();
            Selection.activeObject = scenario;
            Debug.Log($"[DemoCursor] 기본 시나리오를 만들었습니다: {DefaultScenarioPath}");
        }

        /// <summary>
        /// 하루 한 번의 흐름. 버튼 이름은 실제 Play 에서 확인한 순서다 (#45).
        /// 마감일이 아니면 납부·퍼크 단계의 버튼이 없어 시간 초과로 건너뛴다.
        /// </summary>
        private static void AddDaySteps(List<DemoStep> steps)
        {
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 2f));
            steps.Add(new DemoStep(DemoStepKind.HuntCreatures, "ContinueButton|PayButton", 180f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 3f));
            // 해금 안내 카드가 결과창을 덮는다 ("아무 곳이나 눌러 계속"). 여러 장일 수 있어 두 번 둔다.
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "Backdrop", 1.5f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "Backdrop", 1.5f));
            // 결과창의 납부 → 고지서 탭의 납부하기
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "PayButton", 2f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1.5f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "PayButton", 2f));
            steps.Add(new DemoStep(DemoStepKind.ClickIndex, "CardRow", 5f, 1));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1.5f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "LaterButton", 2f));
            // 업그레이드 탭 (첫 진입 안내 → 구매 가능한 첫 카드)
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "UpgradeButton|TabUpgradeButton", 2f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "SkillTreeNoticeConfirmButton", 2f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1f));
            steps.Add(new DemoStep(DemoStepKind.ClickIndex, "BuyRow", 2f, 0));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1.5f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "ContinueButton", 5f));
        }
    }
}
