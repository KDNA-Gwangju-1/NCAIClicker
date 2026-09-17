using NCAIClicker.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace NCAIClicker.Core
{
    /// <summary>
    /// 망치의 예상 타격 위치 바닥 마커와 허공의 망치 스윙 비주얼을 제공한다.
    /// 바닥 원형 마커의 12개 세그먼트가 12시 방향부터 시계방향으로 차오르며 스윙 쿨타임을 표시한다.
    /// 게이지가 100% 완충되면 망치가 바닥을 강하게 내려찍는다.
    /// </summary>
    public class HammerSwingVisual : MonoBehaviour
    {
        private const int SegmentCount = 12;
        private const float ReticleRadius = 0.45f;

        private Camera _aimCamera;
        private Plane _deskPlane;
        private Transform _reticleRoot;
        private Transform _hammerPivot;
        private Renderer[] _segmentRenderers;

        [SerializeField] private float _swingInterval = 0.85f;
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

#if UNITY_EDITOR
            var balance = UnityEditor.AssetDatabase.LoadAssetAtPath<NCAIClicker.Data.BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            if (balance != null && balance.Economy != null && balance.Economy.HoverSwingIntervalSec > 0f)
            {
                _swingInterval = balance.Economy.HoverSwingIntervalSec;
            }
#endif

            BuildVisuals();
        }

        private void BuildVisuals()
        {
            var transparentShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (transparentShader == null)
            {
                transparentShader = Shader.Find("Unlit/Transparent");
            }

            // 1. 레티클 루트
            var reticleRootGo = new GameObject("ReticleRoot");
            reticleRootGo.transform.SetParent(transform);
            _reticleRoot = reticleRootGo.transform;

            // 2. 바닥 기본 베이스 링 (중심점 + 내부 원형 음영 및 실선 테두리)
            var baseQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            baseQuad.name = "BaseReticle";
            baseQuad.transform.SetParent(_reticleRoot);
            baseQuad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            baseQuad.transform.localScale = new Vector3(0.7f, 0.7f, 1f);
            baseQuad.transform.localPosition = new Vector3(0f, 0.012f, 0f);
            Destroy(baseQuad.GetComponent<Collider>());

            var baseRenderer = baseQuad.GetComponent<Renderer>();
            baseRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            baseRenderer.receiveShadows = false;

            var baseMat = CreateTransparentMaterial(transparentShader);
            baseMat.mainTexture = GenerateBaseReticleTexture();
            baseRenderer.material = baseMat;

            // 3. 바깥쪽 12개 쿨타임 세그먼트 게이지 생성 (12시 방향부터 시계방향)
            _segmentRenderers = new Renderer[SegmentCount];
            for (var i = 0; i < SegmentCount; i++)
            {
                var segGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                segGo.name = "Segment_" + i;
                segGo.transform.SetParent(_reticleRoot);
                Destroy(segGo.GetComponent<Collider>());

                // 12시 방향(0도)부터 시계방향 배치
                var angleDeg = (i / (float)SegmentCount) * 360f;
                var angleRad = (90f - angleDeg) * Mathf.Deg2Rad; // 12시 기준 시계방향
                var posX = Mathf.Cos(angleRad) * ReticleRadius;
                var posZ = Mathf.Sin(angleRad) * ReticleRadius;

                segGo.transform.localPosition = new Vector3(posX, 0.016f, posZ);
                segGo.transform.localRotation = Quaternion.Euler(90f, angleDeg, 0f);
                segGo.transform.localScale = new Vector3(0.12f, 0.04f, 1f);

                var segRenderer = segGo.GetComponent<Renderer>();
                segRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                segRenderer.receiveShadows = false;

                var segMat = CreateTransparentMaterial(transparentShader);
                segRenderer.material = segMat;
                _segmentRenderers[i] = segRenderer;
            }

            // 4. 허공의 스윙 망치 피벗 및 모델 생성 (원작 배색: 빨간 손잡이 바 + 짙은 네이비 헤드)
            var pivotGo = new GameObject("HammerPivot");
            pivotGo.transform.SetParent(transform);
            _hammerPivot = pivotGo.transform;

            // 손잡이 막대
            var handleGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            handleGo.name = "HammerHandle";
            handleGo.transform.SetParent(_hammerPivot);
            handleGo.transform.localPosition = new Vector3(0.22f, 0.35f, -0.18f);
            handleGo.transform.localRotation = Quaternion.Euler(38f, -18f, 0f);
            handleGo.transform.localScale = new Vector3(0.06f, 0.35f, 0.06f);
            Destroy(handleGo.GetComponent<Collider>());

            var handleRenderer = handleGo.GetComponent<Renderer>();
            var handleMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            handleMat.color = new Color(0.82f, 0.22f, 0.16f);
            handleRenderer.material = handleMat;

            // 망치 머리
            var headGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            headGo.name = "HammerHead";
            headGo.transform.SetParent(_hammerPivot);
            headGo.transform.localPosition = new Vector3(0.22f, 0.6f, 0.03f);
            headGo.transform.localRotation = Quaternion.Euler(38f, -18f, 0f);
            headGo.transform.localScale = new Vector3(0.18f, 0.16f, 0.32f);
            Destroy(headGo.GetComponent<Collider>());

            var headRenderer = headGo.GetComponent<Renderer>();
            var headMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            headMat.color = new Color(0.18f, 0.22f, 0.28f);
            headRenderer.material = headMat;
        }

        private static Material CreateTransparentMaterial(Shader shader)
        {
            var mat = new Material(shader);
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", 0);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            return mat;
        }

        private static Texture2D GenerateBaseReticleTexture()
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
                    var col = Color.clear;

                    // 중심 하얀 점 (조준점)
                    if (dist < 0.08f)
                    {
                        var t = 1f - (dist / 0.08f);
                        col = new Color(1f, 1f, 1f, Mathf.Clamp01(t * 1.5f));
                    }
                    // 내부 어두운 그림자 영역
                    else if (dist < 0.72f)
                    {
                        var shadowAlpha = Mathf.Lerp(0.35f, 0.08f, dist / 0.72f);
                        col = new Color(0.08f, 0.04f, 0.02f, shadowAlpha);
                    }

                    // 내부 실선 링 (갈색 금색 테두리)
                    var ringDist = Mathf.Abs(dist - 0.74f);
                    if (ringDist < 0.035f)
                    {
                        var a = 1f - (ringDist / 0.035f);
                        col = Color.Lerp(col, new Color(0.88f, 0.7f, 0.48f, 0.95f), a);
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

            // 쿨타임 타이머 갱신 (0.85초 주기)
            _timer += Time.deltaTime;
            while (_timer >= _swingInterval)
            {
                _timer -= _swingInterval;
            }

            var progress = Mathf.Clamp01(_timer / _swingInterval);

            // 1. 바닥 원형 12개 세그먼트 게이지 갱신 (12시 방향부터 시계방향)
            UpdateSegmentGauge(progress);

            // 2. 망치 스윙 모션 연동 (게이지 차오를 때 장전, 100% 도달 시 강타)
            AnimateHammerSwing(progress);
        }

        private void UpdateSegmentGauge(float progress)
        {
            if (_segmentRenderers == null)
            {
                return;
            }

            // progress 비율에 따라 켜질 세그먼트 수 계산
            var filledCount = Mathf.FloorToInt(progress * SegmentCount);

            for (var i = 0; i < SegmentCount; i++)
            {
                var r = _segmentRenderers[i];
                if (r == null)
                {
                    continue;
                }

                if (i <= filledCount)
                {
                    // 차오른 세그먼트: 밝은 아이보리 화이트 점등
                    r.material.color = new Color(0.98f, 0.95f, 0.9f, 0.95f);
                }
                else
                {
                    // 아직 안 찬 세그먼트: 어두운 반투명 색상 대기
                    r.material.color = new Color(0.25f, 0.18f, 0.12f, 0.22f);
                }
            }
        }

        private void UpdateCursorTransform(Vector3 groundPos)
        {
            if (_reticleRoot != null)
            {
                _reticleRoot.position = new Vector3(groundPos.x, 0.015f, groundPos.z);
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

            // progress:
            // 0.0 ~ 0.8: 게이지가 차오르는 동안 망치가 서서히 뒤로 높이 들려올려짐 (Wind up 장전)
            // 0.8 ~ 0.92: 게이지 완충 시점에 바닥을 향해 맹렬하게 쾅! 내리찍음 (Strike 강타)
            // 0.92 ~ 1.0: 타격 반동으로 살짝 튕겨 올라오며 복귀
            float angleX;
            float heightY;

            if (progress < 0.8f)
            {
                var t = progress / 0.8f;
                angleX = Mathf.Lerp(15f, 65f, t);
                heightY = Mathf.Lerp(0.2f, 0.75f, t);
            }
            else if (progress < 0.92f)
            {
                var t = (progress - 0.8f) / 0.12f;
                angleX = Mathf.Lerp(65f, -22f, t);
                heightY = Mathf.Lerp(0.75f, 0.02f, t);
            }
            else
            {
                var t = (progress - 0.92f) / 0.08f;
                angleX = Mathf.Lerp(-22f, 15f, t);
                heightY = Mathf.Lerp(0.02f, 0.2f, t);
            }

            _hammerPivot.localRotation = Quaternion.Euler(angleX, -15f, 0f);
            var pos = _hammerPivot.position;
            pos.y = heightY;
            _hammerPivot.position = pos;
        }
    }
}
