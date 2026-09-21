using System;
using System.IO;
using System.Reflection;
using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Interfaces;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 저장 수집·분배 배선을 검증한다 (이슈 #203).
    ///
    /// **이 하네스가 막는 것은 "계약은 있는데 아무도 부르지 않는" 상태다.** #116·#175 가 계약과
    /// 구현까지 만들고도 `SaveManager` 가 부르지 않아 값이 통째로 사라지던 것이 이 카드의 출발점이다.
    /// 배선이 빠지면 화면은 멀쩡하고 검증도 통과하는데 껐다 켜면 아무것도 안 남는다 — 그래서
    /// **왕복(수집 → 저장 → 분배)** 을 통째로 본다.
    ///
    /// 저장 파일을 실제로 건드리므로 시작할 때 원본을 백업하고 finally 에서 되돌린다.
    ///
    /// 한계: Edit Mode 는 생명주기를 부르지 않아 Awake 를 리플렉션으로 부른다.
    /// </summary>
    public static class SavePersistenceChecks
    {
        public static void RunBatch()
        {
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            AssertCondition(balance != null, "BalanceData 에셋을 찾지 못했습니다.");

            var checkCount = RunContractChecks();
            checkCount += RunRoundTripChecks(balance);
            checkCount += RunLengthMismatchChecks(balance);
            Debug.Log("[SavePersistenceChecks] PASS " + checkCount + " checks.");
        }

        // ---------------------------------------------------------------- 계약과 배선

        /// <summary>
        /// 배선이 실제로 붙어 있는지 본다. 구현만 있고 주입이 빠지면 조용히 아무 일도 안 일어난다.
        /// </summary>
        private static int RunContractChecks()
        {
            var checkCount = 0;

            AssertCondition(typeof(IGamePersistence).IsAssignableFrom(typeof(SaveManager)),
                            "SaveManager 가 IGamePersistence 를 구현하지 않습니다.");
            checkCount++;

            // **메서드가 있는지가 아니라 불리는지를 본다.** 아래 왕복 검증은
            // SetPersistenceTargets 를 직접 불러 ManagerBootstrap 을 우회하므로, 주입 호출이
            // 지워져도 왕복은 멀쩡히 통과한다 — 그러면 게임은 아무것도 저장하지 않는데 검증만
            // 초록이다. 이 카드가 막으려는 결함이 정확히 그것이라 호출부를 직접 확인한다.
            var bootstrap = File.ReadAllText("Assets/Scripts/Runtime/ManagerBootstrap.cs");
            AssertCondition(typeof(ManagerBootstrap).GetMethod("WirePersistence",
                                BindingFlags.NonPublic | BindingFlags.Static) != null,
                            "ManagerBootstrap.WirePersistence 를 찾지 못했습니다.");
            AssertCondition(bootstrap.Contains("WirePersistence(_instance)"),
                            "ManagerBootstrap 이 WirePersistence 를 부르지 않습니다. " +
                            "주입이 빠지면 저장 대상이 전부 null 이라 저장이 비어 나갑니다.");
            checkCount++;

            // **복원은 부트스트랩이 한 번만 한다.** 주입만 해 놓고 복원을 안 부르면 저장은
            // 쌓이는데 아무것도 되돌아오지 않는다 — 저장이 되는 것처럼 보여 가장 늦게 들킨다.
            AssertCondition(bootstrap.Contains("save.LoadAndDistribute()"),
                            "ManagerBootstrap 이 LoadAndDistribute 를 부르지 않습니다. " +
                            "저장은 쌓이지만 켤 때 아무것도 되돌아오지 않습니다.");
            checkCount++;

            // GameManager 의 세 지점. 여기가 빠지면 계약이 또 죽은 코드가 된다.
            var source = File.ReadAllText("Assets/Scripts/Runtime/Core/GameManager.cs");
            AssertCondition(source.Contains("CollectAndSave"),
                            "GameManager 가 CollectAndSave 를 부르지 않습니다. 저장 시점이 없습니다.");
            checkCount++;

            // **회귀 방지.** 씬 로드마다 복원하면 매니저가 DontDestroyOnLoad 라서 마지막 저장
            // 이후의 변경이 덮어써진다 — 고지서 화면에서 산 반지가 "다음 날" 을 누르는 순간
            // 사라졌다. 왕복 검증은 이것을 못 잡는다. 복원이 여전히 성공하기 때문이다.
            AssertCondition(!ExtractMethodBody(source, "private void HandleSceneLoaded")
                                 .Contains("LoadAndDistribute"),
                            "HandleSceneLoaded 가 LoadAndDistribute 를 부릅니다. " +
                            "씬 로드마다 복원하면 마지막 저장 이후의 구매가 사라집니다.");
            checkCount++;

            // 새 회차는 파일만 비워선 안 된다 — 매니저가 값을 들고 있어 직전 회차의 성장이 남는다.
            AssertCondition(ExtractMethodBody(source, "public void StartNewRun")
                                .Contains("LoadAndDistribute"),
                            "StartNewRun 이 빈 저장을 분배하지 않습니다. " +
                            "파일만 비우면 업그레이드·반지가 메모리에 남아 새 회차로 넘어갑니다.");
            checkCount++;

            // 업그레이드·반지 상점이 이 버튼 바로 앞 화면에 있다. 여기서 저장하지 않으면
            // 사 놓고 끈 플레이어가 산 것을 잃는다.
            AssertCondition(ExtractMethodBody(source, "public void ContinueRun")
                                .Contains("CollectAndSave"),
                            "ContinueRun 이 저장하지 않습니다. " +
                            "고지서 화면에서 산 업그레이드·반지가 종료 시 사라집니다.");
            checkCount++;

            return checkCount;
        }

        /// <summary>
        /// 메서드 하나의 본문만 잘라낸다. 파일 전체에 Contains 를 걸면 **다른 메서드의 호출이
        /// 대신 걸려** 검사가 통째로 무의미해진다 — 복원 호출을 어느 메서드에 두었는지가
        /// 이 카드의 핵심이라 위치를 봐야 한다.
        /// </summary>
        private static string ExtractMethodBody(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            AssertCondition(start >= 0, signature + " 를 찾지 못했습니다. 이름이 바뀌었습니까?");

            var open = source.IndexOf('{', start);
            AssertCondition(open >= 0, signature + " 의 본문 시작을 찾지 못했습니다.");

            var depth = 0;
            for (var i = open; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return source.Substring(open, i - open + 1);
                    }
                }
            }

            AssertCondition(false, signature + " 의 본문이 닫히지 않았습니다.");
            return string.Empty;
        }

        // ---------------------------------------------------------------- 왕복

        private static int RunRoundTripChecks(BalanceData balance)
        {
            var checkCount = 0;
            GameObject host = null;
            var savedFile = BackupSaveFile();

            try
            {
                var save = CreateRig(balance, out host, out var economy, out var stage);
                var legacy = (ILegacyService)economy;
                var shop = (IRingShop)economy;
                var upgradeShop = (IUpgradeShop)economy;
                var wallet = (IWalletPersistence)economy;
                var persistence = (IGamePersistence)save;

                // 값을 심는다.
                wallet.RestoreWallet(1234L, "0");
                legacy.AddLegacyPoints(50L);
                AssertCondition(shop.TryPurchaseRing(balance.Rings[0].Id), "준비: 반지를 사지 못했습니다.");
                upgradeShop.TryPurchase(balance.Upgrades[0].Id);
                stage.RestoreStage(2);

                var coin = economy.CurrentCoin;
                var points = legacy.CurrentLegacyPoints;
                var ringLevel = shop.GetRingLevel(balance.Rings[0].Id);
                var upgradeLevel = upgradeShop.GetLevel(balance.Upgrades[0].Id);
                var stageIndex = stage.CurrentStageIndex;

                AssertCondition(ringLevel > 0, "준비: 반지 레벨이 0 입니다.");
                AssertCondition(stageIndex == 2, "준비: 단계가 2 가 아닙니다.");
                checkCount++;

                // 저장한다.
                persistence.CollectAndSave();

                var written = ((ISaveService)save).Load();
                AssertCondition(written.TotalCoin == coin, "저장 파일의 코인이 다릅니다: " + written.TotalCoin);
                AssertCondition(written.LegacyPoints == points,
                                "저장 파일의 레거시 포인트가 다릅니다: " + written.LegacyPoints);
                AssertCondition(written.StageIndex == stageIndex, "저장 파일의 단계가 다릅니다.");
                AssertCondition(written.RingLevels != null && written.RingLevels.Length == balance.Rings.Count,
                                "저장 파일의 반지 배열 길이가 반지 수와 다릅니다.");
                AssertCondition(written.UpgradeLevels != null && written.UpgradeLevels.Length == balance.Upgrades.Count,
                                "저장 파일의 업그레이드 배열 길이가 업그레이드 수와 다릅니다.");
                checkCount++;

                // 전부 지운 뒤 되돌린다.
                wallet.RestoreWallet(0L, "0");
                ((ILegacyPersistence)economy).RestoreLegacy(0L, Array.Empty<int>());
                ((IUpgradePersistence)economy).RestoreUpgradeLevels(Array.Empty<int>());
                stage.RestoreStage(0);
                AssertCondition(economy.CurrentCoin == 0L && legacy.CurrentLegacyPoints == 0L,
                                "준비: 지우기가 되지 않았습니다.");

                persistence.LoadAndDistribute();

                AssertCondition(economy.CurrentCoin == coin, "복원 후 코인이 다릅니다: " + economy.CurrentCoin);
                AssertCondition(legacy.CurrentLegacyPoints == points,
                                "복원 후 레거시 포인트가 다릅니다: " + legacy.CurrentLegacyPoints);
                AssertCondition(shop.GetRingLevel(balance.Rings[0].Id) == ringLevel,
                                "복원 후 반지 레벨이 다릅니다.");
                AssertCondition(upgradeShop.GetLevel(balance.Upgrades[0].Id) == upgradeLevel,
                                "복원 후 업그레이드 레벨이 다릅니다.");
                AssertCondition(stage.CurrentStageIndex == stageIndex, "복원 후 단계가 다릅니다.");
                checkCount++;

                // 고지서 상태는 담지 않는다 — IBillService 에 복원 통로가 없어 쓰기만 하면
                // "저장된다"는 착각만 만든다. 담지 않는 것이 의도임을 못 박는다.
                AssertCondition(written.HasActiveBill == false && written.ActiveBill == null,
                                "고지서가 저장에 담겼습니다. 되돌릴 통로가 없어 담지 않기로 했습니다 (#203).");
                checkCount++;
            }
            finally
            {
                DestroyHost(host);
                RestoreSaveFile(savedFile);
            }

            return checkCount;
        }

        // ---------------------------------------------------------------- 길이 불일치

        /// <summary>
        /// **저장 배열이 CSV 행 수와 다를 때** 터지지 않는지 본다. 업그레이드나 반지가 늘거나
        /// 줄면 예전 저장 파일의 길이가 어긋나는데, 그 경로는 예전 저장을 가진 사람에게만
        /// 터져서 개발 중에는 드러나지 않는다.
        /// </summary>
        private static int RunLengthMismatchChecks(BalanceData balance)
        {
            var checkCount = 0;
            GameObject host = null;
            var savedFile = BackupSaveFile();

            try
            {
                var save = CreateRig(balance, out host, out var economy, out var stage);
                var persistence = (IGamePersistence)save;
                var service = (ISaveService)save;

                // 짧은 배열 — 반지·업그레이드가 늘어난 상황
                var data = service.Load();
                data.RingLevels = new int[] { 1 };
                data.UpgradeLevels = new int[] { 1 };
                data.LegacyPoints = 7L;
                data.TotalCoin = 99L;
                data.StageIndex = 1;
                service.Save(data);
                persistence.LoadAndDistribute();
                AssertCondition(((ILegacyService)economy).CurrentLegacyPoints == 7L,
                                "짧은 배열에서 포인트가 복원되지 않았습니다.");
                checkCount++;

                // 긴 배열 — 반지·업그레이드가 줄어든 상황
                data = service.Load();
                data.RingLevels = new int[balance.Rings.Count + 5];
                data.UpgradeLevels = new int[balance.Upgrades.Count + 5];
                service.Save(data);
                persistence.LoadAndDistribute();
                checkCount++;

                // null 배열 — v2 이하 저장
                data = service.Load();
                data.RingLevels = null;
                data.UpgradeLevels = null;
                service.Save(data);
                persistence.LoadAndDistribute();
                AssertCondition(((IRingShop)economy).GetRingLevel(balance.Rings[0].Id) == 0,
                                "null 배열 복원 후 반지 레벨이 0 이 아닙니다.");
                checkCount++;

                // 저장 파일이 아예 없을 때 — Load 는 기본값을 돌려주고 분배도 터지지 않아야 한다
                DeleteSaveFile();
                persistence.LoadAndDistribute();
                AssertCondition(economy.CurrentCoin == 0L, "저장이 없을 때 코인이 0 이 아닙니다.");
                AssertCondition(stage.CurrentStageIndex == 0, "저장이 없을 때 단계가 0 이 아닙니다.");
                checkCount++;
            }
            finally
            {
                DestroyHost(host);
                RestoreSaveFile(savedFile);
            }

            return checkCount;
        }

        // ---------------------------------------------------------------- 도구

        private static SaveManager CreateRig(BalanceData balance, out GameObject host,
                                             out IEconomyService economy, out IStageService stage)
        {
            host = new GameObject("SavePersistenceCheckHost") { hideFlags = HideFlags.HideAndDontSave };
            host.SetActive(false);

            var econ = host.AddComponent<EconomyManager>();
            SetPrivate(econ, "_balanceData", balance);
            var stageGoal = host.AddComponent<StageGoalManager>();
            SetPrivate(stageGoal, "_balanceData", balance);
            var save = host.AddComponent<SaveManager>();
            host.SetActive(true);

            InvokeLifecycle(econ, "Awake");
            InvokeLifecycle(stageGoal, "Awake");
            InvokeLifecycle(save, "Awake");

            economy = econ;
            stage = stageGoal;
            save.SetPersistenceTargets(econ, econ, econ, econ, econ, stageGoal);
            return save;
        }

        private static string SavePath =>
            Path.Combine(Application.persistentDataPath, "save.json");

        /// <summary>실제 저장 파일을 건드리므로 내용을 들고 있다가 되돌린다.</summary>
        private static string BackupSaveFile()
        {
            return File.Exists(SavePath) ? File.ReadAllText(SavePath) : null;
        }

        private static void RestoreSaveFile(string contents)
        {
            if (contents == null)
            {
                DeleteSaveFile();
                return;
            }
            File.WriteAllText(SavePath, contents);
        }

        private static void DeleteSaveFile()
        {
            if (File.Exists(SavePath))
            {
                File.Delete(SavePath);
            }
        }

        private static void InvokeLifecycle(Component component, string methodName)
        {
            var method = component.GetType().GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(method != null, methodName + " 을 찾지 못했습니다.");
            method.Invoke(component, null);
        }

        private static void SetPrivate(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(field != null, fieldName + " 필드를 찾지 못했습니다. 이름이 바뀌었습니까?");
            field.SetValue(target, value);
        }

        private static void DestroyHost(GameObject host)
        {
            if (host != null)
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void AssertCondition(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
