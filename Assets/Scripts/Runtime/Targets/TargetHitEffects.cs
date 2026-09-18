using System.Collections;
using NCAIClicker.Data;
using UnityEngine;

namespace NCAIClicker.Targets
{
    /// <summary>
    /// 타격 시 대상의 스쿼시&스트레치, 타격 임팩트, 파괴 시 파편 버스트를 담당한다
    /// (GDD 4절 "호버 타격" 연출, 이슈 #35).
    /// </summary>
    [RequireComponent(typeof(Target))]
    public class TargetHitEffects : MonoBehaviour
    {
        private static readonly Vector3 _squashScale = new Vector3(1.25f, 0.7f, 1.25f);
        private const float SquashDuration = 0.12f;
        private const int FragmentBurstCount = 6;

        private Target _target;
        private Coroutine _squashCoroutine;

        private void Awake()
        {
            _target = GetComponent<Target>();
        }

        private void OnEnable()
        {
            _target.HitReceived += HandleHitReceived;
        }

        private void OnDisable()
        {
            _target.HitReceived -= HandleHitReceived;

            if (_squashCoroutine != null)
            {
                StopCoroutine(_squashCoroutine);
                _squashCoroutine = null;
            }
            if (_target.Visual != null)
            {
                _target.Visual.localScale = Vector3.one;
            }
        }

        private void HandleHitReceived(HitInfo info)
        {
            PlaySquash();
            HitImpactEffect.Spawn(info.WorldPos);

            // HitReceived 는 _isAlive 가 false 로 바뀌기 전에 발행되므로, 이번 타격이 파괴로
            // 이어졌는지는 남은 내구도로 판별한다 (Target.OnHit 의 발행 순서 참고).
            if (_target.CurrentHp <= 0f)
            {
                Fragment.SpawnBurst(transform.position, FragmentBurstCount);
            }
        }

        private void PlaySquash()
        {
            if (_target.Visual == null)
            {
                return;
            }

            if (_squashCoroutine != null)
            {
                StopCoroutine(_squashCoroutine);
            }
            _squashCoroutine = StartCoroutine(SquashRoutine());
        }

        private IEnumerator SquashRoutine()
        {
            var visual = _target.Visual;
            var baseScale = Vector3.one;
            var elapsed = 0f;

            while (elapsed < SquashDuration)
            {
                elapsed += Time.deltaTime;
                var t = elapsed / SquashDuration;
                visual.localScale = Vector3.Lerp(_squashScale, baseScale, t);
                yield return null;
            }

            visual.localScale = baseScale;
            _squashCoroutine = null;
        }
    }
}
