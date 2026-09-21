using System.Collections.Generic;
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

        /// <summary>
        /// 파괴 시 추첨된 액면 내역 (이슈 #178). RawCoin 은 이 목록 값들의 합이다.
        /// 정산 화면이 종류별 개수를 세려면 필요하다. 빈 목록일 수는 있어도 null 은 아니다.
        /// </summary>
        public IReadOnlyList<CoinDrop> Coins { get; }

        public float StaminaRestore { get; }
        public Vector3 WorldPos { get; }

        public BreakInfo(string targetId, decimal rawCoin, IReadOnlyList<CoinDrop> coins,
            float staminaRestore, Vector3 worldPos)
        {
            TargetId = targetId;
            RawCoin = rawCoin;
            Coins = coins ?? System.Array.Empty<CoinDrop>();
            StaminaRestore = staminaRestore;
            WorldPos = worldPos;
        }
    }
}
