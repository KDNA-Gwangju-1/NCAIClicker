namespace NCAIClicker.Data
{
    /// <summary>
    /// 한 번 파괴했을 때 나오는 코인 합계의 구간 하나 (이슈 #300 해금 카드).
    /// "뽑힌 코인 중 가장 큰 액면이 TopDenomId 인 경우" 를 묶은 것이다 — 그 경우 합계는 Min~Max 사이이고
    /// 그런 일이 일어날 확률이 Probability 다. CoinLottery.GetRewardBands 가 만든다.
    /// </summary>
    public readonly struct CoinRewardBand
    {
        public string TopDenomId { get; }
        public long Min { get; }
        public long Max { get; }
        public double Probability { get; }

        public CoinRewardBand(string topDenomId, long min, long max, double probability)
        {
            TopDenomId = topDenomId;
            Min = min;
            Max = max;
            Probability = probability;
        }
    }
}
