using UnityEngine;
using UnityEngine.Pool;

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

        private static ObjectPool<Fragment> Pool =>
            _pool ??= new ObjectPool<Fragment>(Create, OnGet, OnRelease, OnDestroyPooled, maxSize: MaxPoolSize);

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
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[Fragment] 'Universal Render Pipeline/Lit' 셰이더를 찾지 못했다. " +
                               "Project Settings > Graphics > Always Included Shaders 를 확인하라.");
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Fragment (Pooled)";
            go.transform.localScale = Vector3.one * 0.12f;

            if (shader != null)
            {
                go.GetComponent<MeshRenderer>().material = new Material(shader)
                {
                    color = new Color(0.55f, 0.42f, 0.3f)
                };
            }

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.05f;

            var fragment = go.AddComponent<Fragment>();
            fragment._rigidbody = rb;
            return fragment;
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
