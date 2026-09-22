using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.UI;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NCAIClicker.Core
{
    /// <summary>
    /// 커서를 따라 카메라가 살짝 기울고, 적중 시 Cinemachine Impulse 로 흔들린다 (이슈 #35 추가 범위).
    ///
    /// 이 컴포넌트는 가상 카메라(CinemachineCamera) 오브젝트에 붙는다. CinemachineImpulseListener 는
    /// CinemachineExtension 이라 최종 카메라 상태(State)만 흔들고 이 오브젝트 자체의 Transform 은
    /// 건드리지 않는다 — 그래서 여기서 계산하는 tilt-only 자세가 "흔들림이 빠진 기준 자세"가 되고,
    /// HammerSwingController._aimCamera 를 (실제 렌더링 카메라가 아니라) 이 오브젝트에 얹은 보조
    /// Camera 로 잡아야 조준 판정이 흔들림에 어긋나지 않는다 (#109·#132 재발 방지).
    ///
    /// 설정 패널의 화면 흔들림 스위치(이슈 #196, 계약 #202)는 AudioManager.Instance(IAudioService)의
    /// ScreenShakeEnabled 를 조회만 한다. 새 이벤트를 추가하지 않는다.
    /// </summary>
    [RequireComponent(typeof(CinemachineImpulseSource))]
    public class HammerCameraRig : MonoBehaviour
    {
        [SerializeField] private float _maxTiltAngleDeg = 4f;
        [SerializeField] private float _tiltSmoothTime = 0.15f;
        [SerializeField] private float _hitImpulseForce = 0.3f;

        private CinemachineImpulseSource _impulseSource;
        private Quaternion _baseRotation;
        private Vector2 _currentTilt;
        private Vector2 _tiltVelocity;

        private void Awake()
        {
            _impulseSource = GetComponent<CinemachineImpulseSource>();
            _baseRotation = transform.localRotation;
        }

        // 정적 이벤트는 구독과 해제를 쌍으로 맞춘다 (AGENTS.md).
        private void OnEnable()
        {
            GameEvents.OnSwingResolved += HandleSwingResolved;
        }

        private void OnDisable()
        {
            GameEvents.OnSwingResolved -= HandleSwingResolved;
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            var screenPos = mouse.position.ReadValue();
            var normX = Mathf.Clamp01(screenPos.x / Screen.width) * 2f - 1f;
            var normY = Mathf.Clamp01(screenPos.y / Screen.height) * 2f - 1f;

            var targetTilt = new Vector2(normX, normY) * _maxTiltAngleDeg;
            _currentTilt.x = Mathf.SmoothDamp(_currentTilt.x, targetTilt.x, ref _tiltVelocity.x, _tiltSmoothTime);
            _currentTilt.y = Mathf.SmoothDamp(_currentTilt.y, targetTilt.y, ref _tiltVelocity.y, _tiltSmoothTime);

            // 커서가 오른쪽/위로 갈수록 카메라가 그쪽을 살짝 내려다보듯 기운다.
            transform.localRotation = _baseRotation * Quaternion.Euler(-_currentTilt.y, _currentTilt.x, 0f);
        }

        private void HandleSwingResolved(HitSource source, bool isHit)
        {
            // 호버 타격만 흔든다. 흔들림은 플레이어 조작에 대한 피드백이라, 자동 망치까지 흔들면
            // 초당 1회씩 상시로 흔들려 그 피드백이 묽어진다 (자동 망치 연출은 AutoHammerVisual 이 맡는다).
            if (isHit && source == HitSource.Hover && (AudioManager.Instance?.IsScreenShakeEnabled ?? true))
            {
                _impulseSource.GenerateImpulseWithForce(_hitImpulseForce);
            }
        }
    }
}
