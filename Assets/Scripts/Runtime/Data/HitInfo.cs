using UnityEngine;

namespace NCAIClicker.Data
{
    /// <summary>
    /// 단일 타격 정보
    /// </summary>
    public readonly struct HitInfo
    {
        public HitSource Source { get; }
        public float Damage { get; }
        public Vector3 WorldPos { get; }

        public HitInfo(HitSource source, float damage, Vector3 worldPos)
        {
            Source = source;
            Damage = damage;
            WorldPos = worldPos;
        }
    }
}
