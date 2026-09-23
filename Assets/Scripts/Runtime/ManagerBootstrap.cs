using NCAIClicker.Core;
using NCAIClicker.Economy;
using NCAIClicker.Fever;
using NCAIClicker.Interfaces;
using UnityEngine;

namespace NCAIClicker
{
    /// <summary>
    /// 어느 씬에서 Play 해도 매니저 프리팹(Resources/Managers)이 존재하도록 씬 로드 전에 한 번 생성한다.
    /// 부트스트랩 씬을 두지 않는 이유는 ARCHITECTURE 0절.
    /// </summary>
    public static class ManagerBootstrap
    {
        private const string PrefabName = "Managers";

        private static GameObject _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateManagers()
        {
            if (_instance != null)
            {
                return;
            }

            var prefab = Resources.Load<GameObject>(PrefabName);
            if (prefab == null)
            {
                Debug.LogError($"[ManagerBootstrap] Resources/{PrefabName} 프리팹이 없어 매니저를 생성하지 못했다.");
                return;
            }

            _instance = Object.Instantiate(prefab);
            _instance.name = PrefabName;
            Object.DontDestroyOnLoad(_instance);

            // BillManager 를 EconomyManager 에 연결한다 — 구현 클래스끼리 직접 참조하지 않도록
            // IBillService·IWalletPersistence 통로로만 넘긴다 (AGENTS.md). 둘 다 없으면 조용히 건너뛴다.
            var economyManager = _instance.GetComponent<EconomyManager>();
            var billManager = _instance.GetComponent<BillManager>();
            if (economyManager != null && billManager != null)
            {
                economyManager.SetBillService(billManager);
                billManager.SetEconomyService(economyManager);
                // 파산 시 지갑을 비우는 통로 (이슈 #158). IWalletPersistence 는 SaveManager·BillManager 만 쓴다.
                billManager.SetWalletPersistence(economyManager);
                // 파산 시 업그레이드 레벨을 비우는 통로 (이슈 #250). 업그레이드는 회차 층이다 (GDD 파산 절).
                billManager.SetUpgradePersistence(economyManager);
                // 파산 시 크리처 해금을 되돌리는 통로 (#301). 해금은 회차 층이다.
                billManager.SetUnlockPersistence(economyManager);
            }

            WireUpgradeStats(_instance);
            WireStageService(_instance);
            WirePersistence(_instance);
        }

        /// <summary>
        /// 저장 대상 통로를 SaveManager 에 넣는다 (이슈 #203).
        ///
        /// **SaveManager 가 스스로 찾지 않게 하려고 여기서 넣는다.** 매니저가 매니저를 뒤지기
        /// 시작하면 조립 지점이 흩어진다 — ARCHITECTURE 는 "조립하는 지점 한 곳만 구현 클래스를
        /// 안다"로 정했고 그 한 곳이 여기다. 덕분에 새 공용 통로를 열 필요도 없었다.
        ///
        /// 공급자는 전부 인터페이스로 찾는다. 지금은 EconomyManager 하나가 앞의 다섯을 모두
        /// 구현하지만, 나뉘어도 이 코드는 그대로다.
        /// </summary>
        private static void WirePersistence(GameObject managers)
        {
            var save = managers.GetComponentInChildren<SaveManager>(true);
            if (save == null)
            {
                Debug.LogWarning("[ManagerBootstrap] SaveManager 가 없어 저장·복원이 연결되지 않는다.");
                return;
            }

            save.SetPersistenceTargets(
                managers.GetComponentInChildren<IEconomyService>(true),
                managers.GetComponentInChildren<IWalletPersistence>(true),
                managers.GetComponentInChildren<IUpgradePersistence>(true),
                managers.GetComponentInChildren<ILegacyService>(true),
                managers.GetComponentInChildren<ILegacyPersistence>(true),
                managers.GetComponentInChildren<IStageService>(true),
                managers.GetComponentInChildren<IBillPersistence>(true));

            save.SetUnlockPersistence(managers.GetComponentInChildren<IUnlockPersistence>(true));

            var billManager = managers.GetComponentInChildren<BillManager>(true);
            if (billManager != null)
            {
                billManager.SetPersistence(save);
            }

            // **복원은 여기서 한 번만 한다.** 매니저는 DontDestroyOnLoad 라 씬을 다시 로드해도
            // 값을 들고 있으므로, 씬마다 복원하면 마지막 저장 이후의 변경(고지서 화면에서 산
            // 업그레이드·반지)이 덮어써진다. Instantiate 가 Awake 를 이미 돌린 뒤이고 첫 씬은
            // 아직 로드되지 않았으므로, 어느 BeginRun 보다도 앞선다 (ARCHITECTURE 초기화 순서 1).
            save.LoadAndDistribute();
        }

        /// <summary>
        /// 업그레이드 실효값 조회 통로를 프리팹 안의 소비처에 넣는다 (#131, #140).
        /// 공급자는 인터페이스로만 찾으므로 여기서 EconomyManager 를 다시 알 필요가 없다.
        ///
        /// 씬에 사는 소비처(HammerSwingController)는 여기서 닿지 못한다 —
        /// 이 메서드는 씬 로드 **전**에 돌기 때문이다. 그쪽은 GameManager 가 런 시작 때 넣는다.
        /// </summary>
        private static void WireUpgradeStats(GameObject managers)
        {
            var upgradeStats = managers.GetComponentInChildren<IUpgradeStats>(true);
            if (upgradeStats == null)
            {
                Debug.LogWarning("[ManagerBootstrap] IUpgradeStats 공급자가 없어 업그레이드 효과가 반영되지 않는다.");
                return;
            }

            var stamina = managers.GetComponentInChildren<StaminaManager>(true);
            if (stamina != null)
            {
                stamina.SetUpgradeStats(upgradeStats);
            }

            var fever = managers.GetComponentInChildren<FeverManager>(true);
            if (fever != null)
            {
                fever.SetUpgradeStats(upgradeStats);
            }

            var creatures = managers.GetComponentInChildren<CreatureManager>(true);
            if (creatures != null)
            {
                creatures.SetUpgradeStats(upgradeStats);
            }

            // 자동 망치가 빠져 있어 auto_hammer 업그레이드가 게임에 반영되지 않았다 (이슈 #258).
            var autoHammer = managers.GetComponentInChildren<AutoHammerController>(true);
            if (autoHammer != null)
            {
                autoHammer.SetUpgradeStats(upgradeStats);
            }
        }

        /// <summary>
        /// 단계 진행 상태 조회 통로를 프리팹 안의 소비처에 넣는다 (이슈 #150).
        /// StageGoalManager 가 IStageService 를 공급하고, CreatureManager 와 BillManager 가 소비한다.
        /// </summary>
        private static void WireStageService(GameObject managers)
        {
            var stageService = managers.GetComponentInChildren<IStageService>(true);
            if (stageService == null)
            {
                Debug.LogWarning("[ManagerBootstrap] IStageService 공급자가 없어 단계 진행이 연동되지 않는다.");
                return;
            }

            var creatures = managers.GetComponentInChildren<CreatureManager>(true);
            if (creatures != null)
            {
                creatures.SetStageService(stageService);
                // 해금된 종류만 뽑으려면 회차 누적 수입을 읽어야 한다 (#301).
                creatures.SetEconomyService(managers.GetComponentInChildren<IEconomyService>(true));
            }

            var bills = managers.GetComponentInChildren<BillManager>(true);
            if (bills != null)
            {
                bills.SetStageService(stageService);
            }
        }
    }
}
