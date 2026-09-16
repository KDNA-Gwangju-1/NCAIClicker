using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace NCAIClicker.Tests.PlayMode
{
    /// <summary>어느 씬에서 Play 해도 Managers 오브젝트가 정확히 하나 살아 있는지 확인한다.</summary>
    public class ManagerBootstrapTests
    {
        private const string ManagersName = "Managers";

        [UnityTest]
        public IEnumerator KeepsSingleManagersAcrossScenes()
        {
            SceneManager.LoadScene("MainMenu");
            yield return null;

            var inMainMenu = FindManagers();
            Assert.AreEqual(1, inMainMenu.Length, "MainMenu 씬에서 Managers 는 정확히 1개여야 한다");

            SceneManager.LoadScene("Game");
            yield return null;

            var inGame = FindManagers();
            Assert.AreEqual(1, inGame.Length, "Game 씬에서 Managers 는 정확히 1개여야 한다");
            Assert.AreSame(inMainMenu[0], inGame[0], "씬 전환 뒤에도 같은 인스턴스여야 한다");
        }

        private static GameObject[] FindManagers()
        {
            return Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)
                .Where(go => go.transform.parent == null && go.name == ManagersName)
                .ToArray();
        }
    }
}
