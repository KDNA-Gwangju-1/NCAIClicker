using UnityEngine;

namespace NCAIClicker.Economy
{
    /// <summary>
    /// Coin.prefab 의 Visual 자식 아래 액면별 광석 모델 4종 중 하나만 활성화한다.
    /// coins.csv 는 5개 액면(c1/c5/c25/c100/c1000)이지만 시각 자원은 4종뿐이라
    /// 가장 희귀한 두 액면(c100/c1000)이 Gold 를 공유한다 (#235 이슈 코멘트로 합의).
    /// </summary>
    public class CoinVisual : MonoBehaviour
    {
        [SerializeField] private GameObject _ironVisual;
        [SerializeField] private GameObject _copperVisual;
        [SerializeField] private GameObject _silverVisual;
        [SerializeField] private GameObject _goldVisual;

        public void SetDenomination(string denomId)
        {
            var active = denomId switch
            {
                "c1" => _ironVisual,
                "c5" => _copperVisual,
                "c25" => _silverVisual,
                "c100" => _goldVisual,
                "c1000" => _goldVisual,
                _ => null
            };

            SetActiveIfNotNull(_ironVisual, active == _ironVisual);
            SetActiveIfNotNull(_copperVisual, active == _copperVisual);
            SetActiveIfNotNull(_silverVisual, active == _silverVisual);
            SetActiveIfNotNull(_goldVisual, active == _goldVisual);
        }

        private static void SetActiveIfNotNull(GameObject go, bool isActive)
        {
            if (go != null)
            {
                go.SetActive(isActive);
            }
        }
    }
}
