using UnityEngine;

namespace NCAIClicker.Data
{
    /// <summary>
    /// 타격 대상 파괴 정보 및 원시 보상
    /// </summary>
    public readonly struct BreakInfo
    {
        public string TargetId { get; }
        public decimal RawCoin { get; }
        public float StaminaRestore { get; }
        public Vector3 WorldPos { get; }

        public BreakInfo(string targetId, decimal rawCoin, float staminaRestore, Vector3 worldPos)
        {
            TargetId = targetId;
            RawCoin = rawCoin;
            StaminaRestore = staminaRestore;
            WorldPos = worldPos;
        }
    }
}
