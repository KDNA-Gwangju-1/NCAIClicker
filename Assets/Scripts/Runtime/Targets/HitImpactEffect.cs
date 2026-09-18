using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.SceneManagement;

namespace NCAIClicker.Targets
{
    /// <summary>
    /// 타격 지점에 짧게 튀는 스파크·크랙 버스트. 런당 300회 타격이 발생하므로 풀에서 재사용한다
    /// (docs/PATTERNS.md 7절, 이슈 #35).
    /// </summary>
    public class HitImpactEffect : MonoBehaviour
    {
        private const float Duration = 0.18f;
        private const int MaxPoolSize = 32;
        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        private static ObjectPool<HitImpactEffect> _pool;
        private static Texture2D _sharedBurstTexture;

        private static ObjectPool<HitImpactEffect> Pool =>
            _pool ??= new ObjectPool<HitImpactEffect>(Create, OnGet, OnRelease, OnDestroyPooled, maxSize: MaxPoolSize);

        /// <summary>
        /// static 풀은 씬이 아니라 도메인 수명이라, 씬이 통째로 다시 로드돼도 그대로 남는다.
        /// GameManager.StartNewRun/ContinueRun 이 Game 씬을 SceneManager.LoadScene 으로
        /// 다시 로드하면 풀에 쌓여 있던 오브젝트는 전부 파괴되는데 풀 자체는 그 사실을 모른다 —
        /// 참조를 갱신하지 않으면 다음 Spawn 이 이미 파괴된 인스턴스를 꺼내 MissingReferenceException 이 난다.
        /// 씬이 내려갈 때마다 참조를 버려 새 풀을 만들게 한다.
        /// </summary>
        static HitImpactEffect()
        {
            SceneManager.sceneUnloaded += _ => _pool = null;
        }

        private MeshRenderer _renderer;
        private Material _material;
        private Camera _mainCamera;
        private float _elapsed;

        /// <summary>풀에서 이펙트를 꺼내 타격 지점에 재생한다.</summary>
        public static void Spawn(Vector3 worldPosition)
        {
            var fx = Pool.Get();
            fx.transform.position = worldPosition;
            fx.ResetState();
        }

        private void ResetState()
        {
            _elapsed = 0f;
            transform.localScale = Vector3.one * 0.35f;

            if (_material == null)
            {
                return;
            }
            var c = _material.GetColor(_baseColorId);
            c.a = 1f;
            _material.SetColor(_baseColorId, c);
        }

        private static HitImpactEffect Create()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogError("[HitImpactEffect] 'Universal Render Pipeline/Unlit' 셰이더를 찾지 못했다. " +
                               "Project Settings > Graphics > Always Included Shaders 를 확인하라.");
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "HitImpactEffect (Pooled)";
            Destroy(go.GetComponent<Collider>());

            var fx = go.AddComponent<HitImpactEffect>();
            fx._renderer = go.GetComponent<MeshRenderer>();
            fx._renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fx._renderer.receiveShadows = false;

            if (shader != null)
            {
                var mat = new Material(shader);
                mat.SetFloat("_Surface", 1);
                mat.SetFloat("_Blend", 0);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3000;
                // 모든 인스턴스가 완전히 동일한 그림이므로, 인스턴스마다 다시 굽지 않고 하나만 만들어 공유한다.
                mat.mainTexture = _sharedBurstTexture ??= GenerateBurstTexture();
                mat.SetColor(_baseColorId, new Color(1f, 0.95f, 0.75f, 1f));
                fx._renderer.material = mat;
                fx._material = mat;
            }

            fx._mainCamera = Camera.main;
            return fx;
        }

        private static void OnGet(HitImpactEffect fx) => fx.gameObject.SetActive(true);

        private static void OnRelease(HitImpactEffect fx) => fx.gameObject.SetActive(false);

        private static void OnDestroyPooled(HitImpactEffect fx)
        {
            if (fx == null)
            {
                return;
            }
            Destroy(fx.gameObject);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            var progress = Mathf.Clamp01(_elapsed / Duration);

            transform.localScale = Vector3.one * Mathf.Lerp(0.35f, 0.9f, progress);

            if (_material != null)
            {
                var c = _material.GetColor(_baseColorId);
                c.a = Mathf.Lerp(1f, 0f, progress);
                _material.SetColor(_baseColorId, c);
            }

            if (_elapsed >= Duration)
            {
                Pool.Release(this);
            }
        }

        private void LateUpdate()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            if (_mainCamera != null)
            {
                transform.rotation = _mainCamera.transform.rotation;
            }
        }

        /// <summary>중심에서 방사형으로 뻗는 균열·스파크 모양의 단순 텍스처를 만든다.</summary>
        private static Texture2D GenerateBurstTexture()
        {
            const int size = 64;
            const int rayCount = 6;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var center = size * 0.5f;
            var pixels = new Color[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var dist = Mathf.Sqrt(dx * dx + dy * dy) / center;
                    var angle = Mathf.Atan2(dy, dx);

                    var col = Color.clear;

                    if (dist < 0.18f)
                    {
                        col = new Color(1f, 1f, 1f, 1f - dist / 0.18f);
                    }

                    var raySharpness = Mathf.Abs(Mathf.Repeat(angle / (Mathf.PI * 2f / rayCount), 1f) - 0.5f) * 2f;
                    if (dist < 0.9f && raySharpness > 0.82f)
                    {
                        var rayAlpha = (1f - dist) * ((raySharpness - 0.82f) / 0.18f);
                        col = Color.Lerp(col, new Color(1f, 0.9f, 0.6f, 1f), Mathf.Clamp01(rayAlpha));
                    }

                    pixels[y * size + x] = col;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
