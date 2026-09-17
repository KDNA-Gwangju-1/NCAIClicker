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
            // IBillService 통로로만 넘긴다 (AGENTS.md). 둘 다 없으면 조용히 건너뛴다.
            var economyManager = _instance.GetComponent<EconomyManager>();
            var billManager = _instance.GetComponent<BillManager>();
            if (economyManager != null && billManager != null)
            {
                economyManager.SetBillService(billManager);
                billManager.SetEconomyService(economyManager);
            }

            WireUpgradeStats(_instance);
        }

        /// <summary>
        /// 업그레이드 실효값 조회 통로를 프리팹 안의 소비처에 넣는다 (#131).
        /// 공급자는 인터페이스로만 찾으므로 여기서 EconomyManager 를 다시 알 필요가 없다.
        ///
        /// 씬에 사는 소비처(HammerSwingController·CreatureManager)는 여기서 닿지 못한다 —
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
        }
    }
}
