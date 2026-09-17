using NCAIClicker.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace NCAIClicker.Core
{
    /// <summary>
    /// 망치의 예상 타격 위치 바닥 마커와 허공의 망치 스윙 비주얼을 제공한다.
    /// 씬 파일 수정 없이도 작동하도록 Play Mode 시 자동 생성된다.
    /// </summary>
    public class HammerSwingVisual : MonoBehaviour
    {
        private Camera _aimCamera;
        private Plane _deskPlane;
        private Transform _reticle;
        private Transform _hammerPivot;
        private float _swingInterval = 0.5f;
        private float _timer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInject()
        {
            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.name != "Game")
            {
                return;
            }

            if (FindFirstObjectByType<HammerSwingVisual>() != null)
            {
                return;
            }

            var go = new GameObject("HammerSwingVisual (Auto)");
            go.AddComponent<HammerSwingVisual>();
        }

        private void Awake()
        {
            _aimCamera = Camera.main;
            _deskPlane = new Plane(Vector3.up, Vector3.zero);
            BuildVisuals();
        }

        private void BuildVisuals()
        {
            // 1. 바닥 조준 원형 마커 생성
            var reticleGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            reticleGo.name = "AimReticle";
            reticleGo.transform.SetParent(transform);
            reticleGo.transform.localScale = new Vector3(0.5f, 0.005f, 0.5f);
            Destroy(reticleGo.GetComponent<Collider>());

            var reticleRenderer = reticleGo.GetComponent<Renderer>();
            var reticleMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            reticleMat.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            reticleRenderer.material = reticleMat;
            _reticle = reticleGo.transform;

            // 2. 허공의 스윙 망치 피벗 및 모델 생성
            var pivotGo = new GameObject("HammerPivot");
            pivotGo.transform.SetParent(transform);
            _hammerPivot = pivotGo.transform;

            // 손잡이 막대 (Cylinder)
            var handleGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            handleGo.name = "HammerHandle";
            handleGo.transform.SetParent(_hammerPivot);
            handleGo.transform.localPosition = new Vector3(0f, 0.35f, -0.15f);
            handleGo.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
            handleGo.transform.localScale = new Vector3(0.06f, 0.3f, 0.06f);
            Destroy(handleGo.GetComponent<Collider>());

            var handleRenderer = handleGo.GetComponent<Renderer>();
            var handleMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            handleMat.color = new Color(0.5f, 0.3f, 0.15f);
            handleRenderer.material = handleMat;

            // 망치 머리 (Cube)
            var headGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            headGo.name = "HammerHead";
            headGo.transform.SetParent(_hammerPivot);
            headGo.transform.localPosition = new Vector3(0f, 0.55f, 0.05f);
            headGo.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
            headGo.transform.localScale = new Vector3(0.18f, 0.14f, 0.28f);
            Destroy(headGo.GetComponent<Collider>());

            var headRenderer = headGo.GetComponent<Renderer>();
            var headMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            headMat.color = new Color(0.35f, 0.35f, 0.4f);
            headRenderer.material = headMat;
        }

        private void Update()
        {
            if (_aimCamera == null)
            {
                _aimCamera = Camera.main;
                if (_aimCamera == null)
                {
                    return;
                }
            }

            // 마우스 커서 위치 레이캐스트
            var mouse = Mouse.current;
            if (mouse != null)
            {
                var screenPos = mouse.position.ReadValue();
                var ray = _aimCamera.ScreenPointToRay(screenPos);
                if (_deskPlane.Raycast(ray, out var enter))
                {
                    var hitPoint = ray.GetPoint(enter);
                    UpdateCursorTransform(hitPoint);
                }
            }

            // 스윙 타이머 및 내리찍기 애니메이션
            _timer += Time.deltaTime;
            while (_timer >= _swingInterval)
            {
                _timer -= _swingInterval;
            }

            AnimateHammerSwing(_timer / _swingInterval);
        }

        private void UpdateCursorTransform(Vector3 groundPos)
        {
            if (_reticle != null)
            {
                _reticle.position = new Vector3(groundPos.x, 0.01f, groundPos.z);
            }

            if (_hammerPivot != null)
            {
                _hammerPivot.position = new Vector3(groundPos.x, 0.1f, groundPos.z);
            }
        }

        private void AnimateHammerSwing(float progress)
        {
            if (_hammerPivot == null)
            {
                return;
            }

            // progress: 0.0 ~ 0.7 위로 들기, 0.7 ~ 0.85 강하게 내리찍기, 0.85 ~ 1.0 반동 복귀
            float angleX;
            float heightY;

            if (progress < 0.7f)
            {
                var t = progress / 0.7f;
                angleX = Mathf.Lerp(10f, 60f, t);
                heightY = Mathf.Lerp(0.3f, 0.7f, t);
            }
            else if (progress < 0.85f)
            {
                var t = (progress - 0.7f) / 0.15f;
                angleX = Mathf.Lerp(60f, -15f, t);
                heightY = Mathf.Lerp(0.7f, 0.05f, t);
            }
            else
            {
                var t = (progress - 0.85f) / 0.15f;
                angleX = Mathf.Lerp(-15f, 10f, t);
                heightY = Mathf.Lerp(0.05f, 0.3f, t);
            }

            _hammerPivot.localRotation = Quaternion.Euler(angleX, 0f, 0f);
            var pos = _hammerPivot.position;
            pos.y = heightY;
            _hammerPivot.position = pos;
        }
    }
}
