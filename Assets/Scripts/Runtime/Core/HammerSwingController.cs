using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NCAIClicker.Core
{
    /// <summary>
    /// 호버 망치의 상시 자동 스윙과 조준 판정을 담당한다.
    /// 망치는 대상 유무와 무관하게 economy.csv 의 hover_swing_interval_sec 주기로 스윙하며,
    /// 커서가 대상 위에 있을 때만 명중한다. 완료 기준의 정본은 GitHub 이슈 #18.
    /// 커서 위치는 스크린 좌표를 책상 평면(XZ, Y 고정)으로 레이캐스트해 구한다 (GDD 4절).
    /// 마우스 입력은 프로젝트 설정(Active Input Handling = Input System)에 맞춰
    /// 레거시 UnityEngine.Input이 아니라 Input System의 Mouse.current를 쓴다.
    /// </summary>
    public class HammerSwingController : MonoBehaviour
    {
        public const float DefaultReticleRadius = 0.45f;

        [SerializeField] private BalanceData _balanceData;
        [SerializeField] private Camera _aimCamera;
        [SerializeField] private float _deskPlaneY;
        [SerializeField] private LayerMask _hittableLayerMask = ~0;
        [SerializeField] private float _hitRadius = DefaultReticleRadius;

        private float _swingTimer;
        private Plane _deskPlane;

        /// <summary>가장 최근 스윙에서 계산된 커서의 책상 평면 위 월드 좌표.</summary>
        public Vector3 CursorWorldPosition { get; private set; }

        /// <summary>망치 타격 판정 반경.</summary>
        public float HitRadius => _hitRadius;

        private void Awake()
        {
            if (_balanceData == null)
            {
                Debug.LogError("[HammerSwingController] BalanceData 가 연결되지 않았다. 스윙 주기를 계산할 수 없다.");
            }
            if (_aimCamera == null)
            {
                _aimCamera = Camera.main;
            }
            _deskPlane = new Plane(Vector3.up, new Vector3(0f, _deskPlaneY, 0f));
        }

        private void Update()
        {
            if (_balanceData == null || _aimCamera == null)
            {
                return;
            }

            _swingTimer += Time.deltaTime;
            var interval = _balanceData.Economy.HoverSwingIntervalSec;
            if (interval <= 0f)
            {
                return;
            }

            // 프레임이 밀려도 스윙 박자가 어긋나지 않도록 나머지 시간을 이월한다 (0으로 리셋하지 않는다).
            while (_swingTimer >= interval)
            {
                _swingTimer -= interval;
                ResolveSwing();
            }
        }

        /// <summary>스윙 한 번을 판정한다. 명중 여부와 무관하게 OnSwingResolved 를 발행한다.</summary>
        private void ResolveSwing()
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                // 마우스 장치가 없어도 스윙 자체는 계속된다 (완료 기준: 대상 유무와 무관한 상시 스윙).
                GameEvents.PublishSwingResolved(HitSource.Hover, false);
                return;
            }

            var screenPosition = mouse.position.ReadValue();
            var ray = _aimCamera.ScreenPointToRay(screenPosition);

            // 커서의 책상 평면 월드 좌표. 대상이 없어도 항상 계산해 둔다 (미래 커서 마커용).
            if (_deskPlane.Raycast(ray, out var deskDistance))
            {
                CursorWorldPosition = ray.GetPoint(deskDistance);
            }

            var isHit = false;
            var hitColliders = Physics.OverlapSphere(CursorWorldPosition, _hitRadius, _hittableLayerMask);
            IHittable closestTarget = null;
            var minDistanceSqr = float.MaxValue;
            var hitPoint = CursorWorldPosition;

            for (var i = 0; i < hitColliders.Length; i++)
            {
                var col = hitColliders[i];
                var target = col.GetComponentInParent<IHittable>();
                if (target == null || !target.IsAlive)
                {
                    continue;
                }

                var closestPoint = col.ClosestPoint(CursorWorldPosition);
                var distSqr = (closestPoint - CursorWorldPosition).sqrMagnitude;
                if (distSqr < minDistanceSqr)
                {
                    minDistanceSqr = distSqr;
                    closestTarget = target;
                    hitPoint = closestPoint;
                }
            }

            if (closestTarget != null)
            {
                closestTarget.OnHit(new HitInfo(HitSource.Hover, _balanceData.Economy.BaseHitPower, hitPoint));
                isHit = true;
            }

            GameEvents.PublishSwingResolved(HitSource.Hover, isHit);
        }
    }
}
