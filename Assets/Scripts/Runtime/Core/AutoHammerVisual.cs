using System.Collections.Generic;
using NCAIClicker.Economy;
using UnityEngine;

namespace NCAIClicker.Core
{
    /// <summary>
    /// 자동 망치의 틱 연출. 망치마다 이번 틱에 맡은 대상 위에 서서 글로벌 타이머 진행도에 맞춰
    /// 같은 박자로 내려찍는다.
    ///
    /// 망치 수와 적중 수가 1:1 이다 (팀장 결정 2026-09-22, #258). 10기면 10마리를 동시에 때리고
    /// 화면에도 망치 10개가 뜬다 — 대상이 모자라면 겹쳐서 같은 대상을 노린다.
    ///
    /// 대상은 틱마다 다시 고르므로 망치는 틱 경계에서 다음 대상으로 순간이동한다. 한 대상만 계속
    /// 노리면 편향이 생겨(auto-hammer.md) 무작위 선택 쪽을 유지한 결과다.
    ///
    /// 판정에는 관여하지 않는다 — 여기를 지워도 게임 규칙은 그대로다.
    /// 모델과 모션은 호버 망치와 HammerRig 를 공유한다.
    /// </summary>
    public class AutoHammerVisual : MonoBehaviour
    {
        private AutoHammerController _controller;
        private readonly List<Transform> _pivots = new List<Transform>();

        private void Awake()
        {
            _controller = FindFirstObjectByType<AutoHammerController>(FindObjectsInactive.Include);
            if (_controller == null)
            {
                Debug.LogWarning("[AutoHammerVisual] AutoHammerController 가 없어 자동 망치를 그리지 않는다.");
            }
        }

        private void LateUpdate()
        {
            if (_controller == null)
            {
                return;
            }

            // 발동 중이 아니면 망치는 화면에 없다 (3.13). 상시 틱이 아니라 호버 적중에 얹히는
            // 한 번짜리 사이클이라, 끝나면 사라지는 것이 정상이다.
            var needed = _controller.IsSwinging ? _controller.PendingTargetCount : 0;
            EnsurePivots(needed);

            if (needed == 0)
            {
                SetVisibleFrom(0);
                return;
            }

            HammerRig.EvaluateSwing(_controller.TickProgress, out var angleX, out var heightY);

            var shown = 0;
            for (var i = 0; i < needed && i < _pivots.Count; i++)
            {
                if (_pivots[i] == null || !_controller.TryGetPendingTargetPosition(i, out var targetPos))
                {
                    continue;
                }

                // 같은 대상에 겹친 망치는 조금씩 돌려 세워 서로 가리지 않게 한다.
                var yaw = -15f + i * 37f;
                _pivots[i].position = new Vector3(targetPos.x, heightY, targetPos.z);
                _pivots[i].localRotation = Quaternion.Euler(angleX, yaw, 0f);

                if (!_pivots[i].gameObject.activeSelf)
                {
                    _pivots[i].gameObject.SetActive(true);
                }
                shown = i + 1;
            }

            SetVisibleFrom(shown);
        }

        /// <summary>
        /// 필요한 만큼 망치를 짓는다. 보유 수는 런 시작에 굳으므로(#258) 런 도중에는 늘지 않고,
        /// 한 번 지은 망치는 지우지 않고 숨겼다가 다시 쓴다 — 매 틱 만들고 부수면 GC 가 쌓인다.
        /// </summary>
        private void EnsurePivots(int needed)
        {
            while (_pivots.Count < needed)
            {
                var pivot = HammerRig.Build(transform, "AutoHammerPivot" + _pivots.Count);
                if (pivot == null)
                {
                    return;
                }
                _pivots.Add(pivot);
            }
        }

        /// <summary>index 이후의 망치를 숨긴다. 대상이 줄어든 틱에 남은 망치가 허공에 서 있지 않게.</summary>
        private void SetVisibleFrom(int index)
        {
            for (var i = index; i < _pivots.Count; i++)
            {
                if (_pivots[i] != null && _pivots[i].gameObject.activeSelf)
                {
                    _pivots[i].gameObject.SetActive(false);
                }
            }
        }
    }
}
