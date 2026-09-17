using NCAIClicker.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace NCAIClicker.Core
{
    /// <summary>
    /// 망치의 예상 타격 위치 바닥 마커와 허공의 망치 스윙 비주얼을 제공한다.
    /// 원작의 바닥 조준 레티클(중심 점, 내부 링, 바깥쪽 점선 세그먼트)을 절차적 텍스처로 구현한다.
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
            // 1. 바닥 조준 레티클 생성 (원작 스타일: 중심점 + 내부 링 + 바깥쪽 점선 세그먼트)
            var reticleGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            reticleGo.name = "AimReticle_Decal";
            reticleGo.transform.SetParent(transform);
            reticleGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            reticleGo.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
            Destroy(reticleGo.GetComponent<Collider>());

            var reticleRenderer = reticleGo.GetComponent<Renderer>();
            reticleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            reticleRenderer.receiveShadows = false;

            // 투명 Unlit 머티리얼 적용
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Transparent");
            }
            var reticleMat = new Material(shader);
            reticleMat.SetFloat("_Surface", 1); // Transparent
            reticleMat.SetFloat("_Blend", 0);   // Alpha
            reticleMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            reticleMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            reticleMat.SetInt("_ZWrite", 0);
            reticleMat.renderQueue = 3000;

            reticleMat.mainTexture = GenerateReticleTexture();
            reticleRenderer.material = reticleMat;
            _reticle = reticleGo.transform;

            // 2. 허공의 스윙 망치 피벗 및 모델 생성 (원작 형태의 막대와 헤드)
            var pivotGo = new GameObject("HammerPivot");
            pivotGo.transform.SetParent(transform);
            _hammerPivot = pivotGo.transform;

            // 손잡이 막대 (빨간색 고무 손잡이 + 목 부분)
            var handleGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            handleGo.name = "HammerHandle";
            handleGo.transform.SetParent(_hammerPivot);
            handleGo.transform.localPosition = new Vector3(0.25f, 0.35f, -0.2f);
            handleGo.transform.localRotation = Quaternion.Euler(40f, -20f, 0f);
            handleGo.transform.localScale = new Vector3(0.06f, 0.35f, 0.06f);
            Destroy(handleGo.GetComponent<Collider>());

            var handleRenderer = handleGo.GetComponent<Renderer>();
            var handleMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            handleMat.color = new Color(0.8f, 0.2f, 0.15f); // 원작의 빨간 손잡이
            handleRenderer.material = handleMat;

            // 망치 머리 (짙은 네이비 블루 헤드)
            var headGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            headGo.name = "HammerHead";
            headGo.transform.SetParent(_hammerPivot);
            headGo.transform.localPosition = new Vector3(0.25f, 0.6f, 0.02f);
            headGo.transform.localRotation = Quaternion.Euler(40f, -20f, 0f);
            headGo.transform.localScale = new Vector3(0.18f, 0.16f, 0.32f);
            Destroy(headGo.GetComponent<Collider>());

            var headRenderer = headGo.GetComponent<Renderer>();
            var headMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            headMat.color = new Color(0.18f, 0.22f, 0.28f); // 원작의 짙은 네이비 헤드
            headRenderer.material = headMat;
        }

        /// <summary>
        /// 첨부 이미지와 같은 원작 스타일의 조준 마커 텍스처를 절차적으로 생성한다.
        /// </summary>
        private static Texture2D GenerateReticleTexture()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            var center = size * 0.5f;
            var maxR = size * 0.5f;
            var pixels = new Color[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var dist = Mathf.Sqrt(dx * dx + dy * dy) / maxR;
                    var angle = Mathf.Atan2(dy, dx);
                    if (angle < 0f)
                    {
                        angle += Mathf.PI * 2f;
                    }

                    var col = Color.clear;

                    // 1. 중심 하얀 점 (조준점)
                    if (dist < 0.06f)
                    {
                        var t = 1f - (dist / 0.06f);
                        col = new Color(1f, 1f, 1f, Mathf.Clamp01(t * 1.5f));
                    }
                    // 2. 내부 은은한 어두운 그림자 영역
                    else if (dist < 0.55f)
                    {
                        var shadowAlpha = Mathf.Lerp(0.35f, 0.05f, dist / 0.55f);
                        col = new Color(0.08f, 0.04f, 0.02f, shadowAlpha);
                    }

                    // 3. 내부 실선 링 (갈색 금색 테두리)
                    var innerRingDist = Mathf.Abs(dist - 0.56f);
                    if (innerRingDist < 0.025f)
                    {
                        var a = 1f - (innerRingDist / 0.025f);
                        col = Color.Lerp(col, new Color(0.9f, 0.72f, 0.5f, 0.95f), a);
                    }

                    // 4. 바깥쪽 점선 세그먼트 (12개 분할 조각)
                    if (dist >= 0.68f && dist <= 0.82f)
                    {
                        const float segmentCount = 12f;
                        var seg = (angle / (Mathf.PI * 2f)) * segmentCount;
                        var frac = seg - Mathf.Floor(seg);
                        if (frac < 0.65f) // 65%는 표시, 35%는 공백
                        {
                            var dEdge = Mathf.Min(dist - 0.68f, 0.82f - dist) / 0.07f;
                            var edgeAlpha = Mathf.Clamp01(dEdge * 2f);
                            col = new Color(0.96f, 0.93f, 0.88f, 0.92f * edgeAlpha);
                        }
                    }

                    pixels[y * size + x] = col;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
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
                _reticle.position = new Vector3(groundPos.x, 0.015f, groundPos.z);
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
                heightY = Mathf.Lerp(0.2f, 0.65f, t);
            }
            else if (progress < 0.85f)
            {
                var t = (progress - 0.7f) / 0.15f;
                angleX = Mathf.Lerp(60f, -20f, t);
                heightY = Mathf.Lerp(0.65f, 0.02f, t);
            }
            else
            {
                var t = (progress - 0.85f) / 0.15f;
                angleX = Mathf.Lerp(-20f, 10f, t);
                heightY = Mathf.Lerp(0.02f, 0.2f, t);
            }

            _hammerPivot.localRotation = Quaternion.Euler(angleX, -15f, 0f);
            var pos = _hammerPivot.position;
            pos.y = heightY;
            _hammerPivot.position = pos;
        }
    }
}
