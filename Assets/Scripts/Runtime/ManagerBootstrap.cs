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
        }
    }
}
