using System;
using System.Collections.Generic;
using System.IO;
using NCAIClicker.Demo;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 제출 영상 녹화용 자동 시연 커서 메뉴 (#45, #344).
    /// Project 창에서 DemoScenario 에셋을 선택한 채 시작하면 그 시나리오를, 아니면 기본 시나리오(5장 한 판)를 쓴다.
    /// <para>
    /// "녹화하며 시작" 은 Recorder 로 1080p 영상을 찍고, 같은 이름의 자막 파일(.ass)을 함께 남긴다.
    /// 게임 사운드는 담지 않는다 — 배경음은 편집 단계에서 깐다.
    /// </para>
    /// </summary>
    public static class DemoCursorMenu
    {
        private const string ScenarioFolder = "Assets/GameData/Demo";
        private const string DefaultScenarioPath = ScenarioFolder + "/DefaultDemoScenario.asset";
        private const string RecordingFolder = "Recordings";
        private const int RecordingFrameRate = 30;
        private const double StopDelaySeconds = 2.0;

        private static RecorderController _recorder;
        private static double _stopAt = -1.0;

        [MenuItem("NCAI/시연/시작", false, MenuPriority.DemoStart)]
        public static void StartDemo()
        {
            var scenario = GetSelectedScenario();
            if (scenario != null)
            {
                DemoCursor.StartDemo(scenario);
            }
        }

        [MenuItem("NCAI/시연/시작", true)]
        private static bool ValidateStartDemo() => EditorApplication.isPlaying;

        /// <summary>
        /// 녹화를 켜고 시나리오를 시작한다. 시나리오가 끝나면 잠시 뒤 녹화를 멈춘다.
        /// 산출물: Recordings/{시나리오}_{시각}.mp4 와 같은 이름의 .ass.
        /// </summary>
        [MenuItem("NCAI/시연/녹화하며 시작", false, MenuPriority.DemoRecord)]
        public static void StartDemoWithRecording()
        {
            var scenario = GetSelectedScenario();
            if (scenario == null)
            {
                return;
            }

            var basePath = Path.Combine(RecordingFolder, $"{scenario.name}_{DateTime.Now:yyyyMMdd_HHmmss}");
            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = scenario.name;
            movie.Enabled = true;
            movie.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
            movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1920, OutputHeight = 1080 };
            movie.CaptureAudio = false;
            movie.OutputFile = basePath;

            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            settings.AddRecorderSettings(movie);
            settings.SetRecordModeToManual();
            settings.FrameRate = RecordingFrameRate;

            _recorder = new RecorderController(settings);
            _recorder.PrepareRecording();
            if (!_recorder.StartRecording())
            {
                Debug.LogWarning("[DemoCursor] 녹화를 시작하지 못했습니다.");
                _recorder = null;
                return;
            }

            DemoCursor.ScenarioFinished -= OnScenarioFinished;
            DemoCursor.ScenarioFinished += OnScenarioFinished;
            DemoCursor.StartDemo(scenario, basePath + ".ass", RecordingFrameRate);
            Debug.Log($"[DemoCursor] 녹화 시작: {basePath}.mp4");
        }

        [MenuItem("NCAI/시연/녹화하며 시작", true)]
        private static bool ValidateStartDemoWithRecording() => EditorApplication.isPlaying && _recorder == null;

        [MenuItem("NCAI/시연/일시정지", false, MenuPriority.DemoPause)]
        public static void PauseDemo() => DemoCursor.Instance.Pause();

        [MenuItem("NCAI/시연/일시정지", true)]
        private static bool ValidatePauseDemo() => DemoCursor.Instance != null && !DemoCursor.Instance.IsPaused;

        [MenuItem("NCAI/시연/재개", false, MenuPriority.DemoResume)]
        public static void ResumeDemo() => DemoCursor.Instance.Resume();

        [MenuItem("NCAI/시연/재개", true)]
        private static bool ValidateResumeDemo() => DemoCursor.Instance != null && DemoCursor.Instance.IsPaused;

        [MenuItem("NCAI/시연/중지", false, MenuPriority.DemoStop)]
        public static void StopDemo()
        {
            StopRecording();
            DemoCursor.StopDemo();
        }

        [MenuItem("NCAI/시연/중지", true)]
        private static bool ValidateStopDemo() => DemoCursor.Instance != null || _recorder != null;

        private static DemoScenario GetSelectedScenario()
        {
            var scenario = Selection.activeObject as DemoScenario;
            if (scenario == null)
            {
                scenario = AssetDatabase.LoadAssetAtPath<DemoScenario>(DefaultScenarioPath);
            }
            if (scenario == null)
            {
                Debug.LogWarning($"[DemoCursor] 시나리오가 없습니다. 먼저 'NCAI/시연/시나리오 에셋 생성' 을 실행하세요 ({DefaultScenarioPath}).");
            }
            return scenario;
        }

        private static void OnScenarioFinished()
        {
            DemoCursor.ScenarioFinished -= OnScenarioFinished;
            // 마지막 장면이 잘리지 않게 조금 더 찍고 멈춘다.
            _stopAt = EditorApplication.timeSinceStartup + StopDelaySeconds;
            EditorApplication.update -= StopWhenDue;
            EditorApplication.update += StopWhenDue;
        }

        private static void StopWhenDue()
        {
            if (EditorApplication.timeSinceStartup < _stopAt)
            {
                return;
            }
            EditorApplication.update -= StopWhenDue;
            StopRecording();
        }

        private static void StopRecording()
        {
            if (_recorder == null)
            {
                return;
            }
            if (_recorder.IsRecording())
            {
                _recorder.StopRecording();
            }
            _recorder = null;
            Debug.Log("[DemoCursor] 녹화를 멈췄습니다.");
        }

        /// <summary>
        /// 시연 시나리오 에셋 4종을 만든다. 이미 있으면 덮어쓴다.
        /// 자막 문구는 docs/SUBMISSION_VIDEO.md 5·6장 초안을 따른다.
        /// </summary>
        [MenuItem("NCAI/시연/시나리오 에셋 생성", false, MenuPriority.DemoCreateDefault)]
        public static void CreateScenarios()
        {
            if (!AssetDatabase.IsValidFolder(ScenarioFolder))
            {
                AssetDatabase.CreateFolder("Assets/GameData", "Demo");
            }
            SaveScenario(DefaultScenarioPath, BuildFullRun());
            SaveScenario(ScenarioFolder + "/DemoBankruptcyCycle.asset", BuildBankruptcyCycle());
            SaveScenario(ScenarioFolder + "/DemoLoan.asset", BuildLoan());
            SaveScenario(ScenarioFolder + "/DemoEnding.asset", BuildEnding());
            AssetDatabase.SaveAssets();
            Debug.Log($"[DemoCursor] 시나리오 에셋 4종을 만들었습니다: {ScenarioFolder}");
        }

        private static void SaveScenario(string path, List<DemoStep> steps)
        {
            var scenario = AssetDatabase.LoadAssetAtPath<DemoScenario>(path);
            if (scenario == null)
            {
                scenario = ScriptableObject.CreateInstance<DemoScenario>();
                AssetDatabase.CreateAsset(scenario, path);
            }
            scenario.SetSteps(steps);
            EditorUtility.SetDirty(scenario);
        }

        /// <summary>콘티 5장 — 시작 화면부터 다음 날까지 한 번에.</summary>
        private static List<DemoStep> BuildFullRun()
        {
            var steps = new List<DemoStep>();
            AddNewRun(steps);
            Caption(steps, "하루가 시작됩니다\n스태미나가 곧 남은 시간입니다", 4f);
            steps.Add(new DemoStep(DemoStepKind.StartHunting));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 4.5f));
            Caption(steps, "스태미나는 계속 줄어듭니다", 3f);
            WaitThenCaption(steps, "TargetBroken", 15f, "부수면 코인이 쏟아집니다");
            WaitThenCaption(steps, "FeverStart", 20f, "적중을 쌓으면 피버 — 수입이 늘어납니다");
            WaitThenCaption(steps, "StaminaDepleted", 180f, "스태미나가 바닥나면 하루가 끝납니다");
            AddResultAndPay(steps, "하루 수입을 정산합니다", "마감일에는 고지서를 내야 합니다");
            WaitThenCaption(steps, "PerkOffered", 3f, "납부하면 단계 클리어, 퍼크 하나를 고릅니다");
            AddPerkAndUpgrade(steps, "번 돈으로 업그레이드하고 다음 날로");
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 3f));
            return steps;
        }

        /// <summary>
        /// 콘티 6장 파산·레거시 포인트·반지 — 사흘 납부로 포인트를 모은 뒤 스스로 파산하고 반지를 산다.
        /// 포인트는 고지서마다 납부액 $50 당 1점(버림)이라 $20·$45 고지서는 0점이다. 세 번째($200)까지 내야
        /// 가장 싼 반지(3 LP)를 산다.
        /// </summary>
        private static List<DemoStep> BuildBankruptcyCycle()
        {
            var steps = new List<DemoStep>();
            AddNewRun(steps);
            AddQuietDay(steps);
            AddQuietDay(steps);
            AddQuietDay(steps);
            steps.Add(new DemoStep(DemoStepKind.HuntCreatures, "ContinueButton|PayButton", 180f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 2f));
            AddCloseUnlockCards(steps);
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "PayButton", 3f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1.5f));
            Caption(steps, "마감을 못 넘기면 파산 — 스스로 선언할 수도 있습니다", 3.5f);
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "DeclareBankruptcyButton", 3f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1.5f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "BankruptcyConfirmYesButton", 3f));
            WaitThenCaption(steps, "Bankrupt", 5f, "파산하면 1일차로 돌아갑니다");
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 3f));
            // 파산하면 반지 상점(프레스티지 화면)이 바로 열린다
            Caption(steps, "파산해도 레거시 포인트와 반지는 남습니다", 3.5f);
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1.5f));
            steps.Add(new DemoStep(DemoStepKind.ClickIndex, "BuyButton", 3f, 0));
            Caption(steps, "다음 회차가 조금 더 쉬워집니다", 3f);
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 2.5f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "RestartButton|ContinueButton", 5f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 2f));
            Caption(steps, "새 회차 — 1일차부터 다시", 3f);
            steps.Add(new DemoStep(DemoStepKind.StartHunting));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 8f));
            steps.Add(new DemoStep(DemoStepKind.StopHunting));
            return steps;
        }

        /// <summary>
        /// 콘티 6장 대출 — 두 번째 고지서부터 빅 토니에게 빌리고, 다음 정산에서 징수를 보여 준다.
        /// 대출은 잔액이 모자랄 때만 열린다. 디버그로 단계를 올려 두 번째 고지서를 잔액보다 크게 만든다.
        /// </summary>
        private static List<DemoStep> BuildLoan()
        {
            var steps = new List<DemoStep>();
            AddNewRun(steps);
            for (var i = 0; i < 3; i++)
            {
                steps.Add(new DemoStep(DemoStepKind.MenuItem, "NCAI/디버그/단계 +1"));
            }
            AddQuietDay(steps);
            steps.Add(new DemoStep(DemoStepKind.HuntCreatures, "ContinueButton|PayButton", 180f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 2f));
            AddCloseUnlockCards(steps);
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "PayButton", 3f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1.5f));
            Caption(steps, "돈이 모자라면 빅 토니에게 빌릴 수 있지만", 3.5f);
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "LoanButton", 3f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1.5f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "LoanShortfallPresetButton|LoanFullPresetButton", 3f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "LoanConfirmButton", 3f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1.5f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "PayButton", 3f));
            steps.Add(new DemoStep(DemoStepKind.ClickIndex, "CardRow", 5f, 1));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1.5f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "LaterButton", 2f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "ContinueButton", 5f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 2f));
            steps.Add(new DemoStep(DemoStepKind.HuntCreatures, "ContinueButton|PayButton", 180f));
            AddCloseUnlockCards(steps);
            Caption(steps, "갚을 때까지 매일 수입을 떼 갑니다", 4f);
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 4.5f));
            return steps;
        }

        /// <summary>
        /// 콘티 6장 엔딩 — 디버그 메뉴로 마지막 단계(11)까지 올린 뒤 손에 든 첫 고지서를 낸다.
        /// 엔딩은 "마지막 단계에서 고지서를 낸" 순간(OnStageGoalReached 가 올리기 전 단계 번호를 보낸다)이라
        /// 10단계에서 내면 11단계 고지서만 새로 온다.
        /// </summary>
        private static List<DemoStep> BuildEnding()
        {
            var steps = new List<DemoStep>();
            AddNewRun(steps);
            for (var i = 0; i < 10; i++)
            {
                steps.Add(new DemoStep(DemoStepKind.MenuItem, "NCAI/디버그/단계 +1"));
            }
            steps.Add(new DemoStep(DemoStepKind.HuntCreatures, "ContinueButton|PayButton", 180f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 2f));
            AddCloseUnlockCards(steps);
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "PayButton", 3f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1.5f));
            Caption(steps, "마지막 고지서까지 내면", 3f);
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "PayButton", 3f));
            steps.Add(new DemoStep(DemoStepKind.ClickIndex, "CardRow", 5f, 1));
            steps.Add(new DemoStep(DemoStepKind.WaitUntilButton, "계속 부수기", 5f));
            Caption(steps, "엔딩 — 걸린 날과 총 납부액을 보여 줍니다", 4f);
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 5f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "계속 부수기", 3f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 2f));
            return steps;
        }

        private static void AddNewRun(List<DemoStep> steps)
        {
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1.5f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "새 회차 시작", 10f));
            // 저장이 있으면 덮어쓰기 확인창이 뜬다
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "YesButton", 2f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1.5f));
        }

        /// <summary>자막 없이 하루를 넘긴다 (사냥 → 납부 → 퍼크 → 계속).</summary>
        private static void AddQuietDay(List<DemoStep> steps)
        {
            steps.Add(new DemoStep(DemoStepKind.HuntCreatures, "ContinueButton|PayButton", 180f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1.5f));
            AddCloseUnlockCards(steps);
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "PayButton", 2f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "PayButton", 2f));
            steps.Add(new DemoStep(DemoStepKind.ClickIndex, "CardRow", 5f, 1));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "LaterButton", 2f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "ContinueButton", 5f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1.5f));
        }

        private static void AddResultAndPay(List<DemoStep> steps, string resultCaption, string billCaption)
        {
            steps.Add(new DemoStep(DemoStepKind.StopHunting));
            steps.Add(new DemoStep(DemoStepKind.WaitUntilButton, "ContinueButton|PayButton|Backdrop", 10f));
            Caption(steps, resultCaption, 3f);
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 3f));
            AddCloseUnlockCards(steps);
            Caption(steps, billCaption, 3f);
            // 결과창의 납부 → 고지서 탭의 납부하기
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "PayButton", 2f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1.5f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "PayButton", 2f));
        }

        private static void AddPerkAndUpgrade(List<DemoStep> steps, string upgradeCaption)
        {
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 2f));
            steps.Add(new DemoStep(DemoStepKind.ClickIndex, "CardRow", 5f, 1));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1.5f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "LaterButton", 2f));
            // 업그레이드 탭 (첫 진입 안내 → 구매 가능한 첫 카드)
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "UpgradeButton|TabUpgradeButton", 2f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "SkillTreeNoticeConfirmButton", 2f));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1f));
            Caption(steps, upgradeCaption, 3f);
            steps.Add(new DemoStep(DemoStepKind.ClickIndex, "BuyRow", 2f, 0));
            steps.Add(new DemoStep(DemoStepKind.Wait, seconds: 1.5f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "ContinueButton", 5f));
        }

        /// <summary>해금 안내 카드가 결과창을 덮는다 ("아무 곳이나 눌러 계속"). 여러 장일 수 있어 두 번 둔다.</summary>
        private static void AddCloseUnlockCards(List<DemoStep> steps)
        {
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "Backdrop", 1.5f));
            steps.Add(new DemoStep(DemoStepKind.ClickButton, "Backdrop", 1.5f));
        }

        private static void Caption(List<DemoStep> steps, string text, float seconds)
        {
            steps.Add(new DemoStep(DemoStepKind.Caption, text, seconds));
        }

        /// <summary>이벤트를 기다렸다가, 실제로 일어났을 때만 자막을 남긴다.</summary>
        private static void WaitThenCaption(List<DemoStep> steps, string eventName, float timeout, string text)
        {
            steps.Add(new DemoStep(DemoStepKind.WaitForEvent, eventName, timeout));
            steps.Add(new DemoStep(DemoStepKind.Caption, text, 3f, 1));
        }
    }
}
