using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.SceneManagement;

namespace NCAIClicker.Targets
{
    /// <summary>
    /// 대상 파괴 시 튀어나가는 파편 조각 하나. 물리(리지드바디)로 튕겨나간다
    /// (GDD 4절: "물리는 파편과 코인에만 쓴다"). 런당 파괴가 반복되므로 풀에서 재사용한다
    /// (docs/PATTERNS.md 7절, 이슈 #35).
    /// </summary>
    public class Fragment : MonoBehaviour
    {
        private const float Lifetime = 0.9f;
        private const int MaxPoolSize = 64;
        private const int DefaultBurstCount = 6;

        private static ObjectPool<Fragment> _pool;
        private static Material _sharedMaterial;

        private static ObjectPool<Fragment> Pool =>
            _pool ??= new ObjectPool<Fragment>(Create, OnGet, OnRelease, OnDestroyPooled, maxSize: MaxPoolSize);

        /// <summary>
        /// static 풀은 씬이 아니라 도메인 수명이라, 씬이 통째로 다시 로드돼도 그대로 남는다.
        /// GameManager.StartNewRun/ContinueRun 이 Game 씬을 SceneManager.LoadScene 으로
        /// 다시 로드하면 풀에 쌓여 있던 오브젝트는 전부 파괴되는데 풀 자체는 그 사실을 모른다 —
        /// 참조를 갱신하지 않으면 다음 SpawnBurst 가 이미 파괴된 인스턴스를 꺼내
        /// MissingReferenceException 이 난다. 씬이 내려갈 때마다 참조를 버려 새 풀을 만들게 한다.
        /// </summary>
        static Fragment()
        {
            SceneManager.sceneUnloaded += _ => _pool = null;
        }

        private Rigidbody _rigidbody;
        private float _elapsed;

        /// <summary>파괴 지점에서 파편 여러 개를 한 번에 터뜨린다.</summary>
        public static void SpawnBurst(Vector3 worldPosition, int count = DefaultBurstCount)
        {
            for (var i = 0; i < count; i++)
            {
                var fragment = Pool.Get();
                fragment.transform.SetPositionAndRotation(worldPosition, Random.rotation);
                fragment.ResetState();

                var direction = (Random.insideUnitSphere + Vector3.up).normalized;
                fragment._rigidbody.AddForce(direction * Random.Range(1.5f, 3.5f), ForceMode.Impulse);
                fragment._rigidbody.AddTorque(Random.insideUnitSphere * Random.Range(2f, 5f), ForceMode.Impulse);
            }
        }

        private void ResetState()
        {
            _elapsed = 0f;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
        }

        private static Fragment Create()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Fragment (Pooled)";
            go.transform.localScale = Vector3.one * 0.12f;

            var sharedMat = GetSharedMaterial();
            if (sharedMat != null)
            {
                // 파편은 색이 고정이라 인스턴스마다 새로 만들지 않고 하나를 공유한다 —
                // 풀 크기(최대 64개)만큼 머티리얼을 중복 생성하는 낭비를 없앤다.
                go.GetComponent<MeshRenderer>().sharedMaterial = sharedMat;
            }

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.05f;

            var fragment = go.AddComponent<Fragment>();
            fragment._rigidbody = rb;
            return fragment;
        }

        private static Material GetSharedMaterial()
        {
            if (_sharedMaterial != null)
            {
                return _sharedMaterial;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[Fragment] 'Universal Render Pipeline/Lit' 셰이더를 찾지 못했다. " +
                               "Project Settings > Graphics > Always Included Shaders 를 확인하라.");
                return null;
            }

            _sharedMaterial = new Material(shader) { color = new Color(0.55f, 0.42f, 0.3f) };
            return _sharedMaterial;
        }

        private static void OnGet(Fragment fragment) => fragment.gameObject.SetActive(true);

        private static void OnRelease(Fragment fragment) => fragment.gameObject.SetActive(false);

        private static void OnDestroyPooled(Fragment fragment)
        {
            if (fragment == null)
            {
                return;
            }
            Destroy(fragment.gameObject);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            if (_elapsed >= Lifetime)
            {
                Pool.Release(this);
            }
        }
    }
}
