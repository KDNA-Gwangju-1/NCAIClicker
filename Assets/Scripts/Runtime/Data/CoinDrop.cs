namespace NCAIClicker.Data
{
    /// <summary>
    /// 파괴 보상으로 뽑힌 액면 하나와 그 개수. coins.csv 의 id 를 그대로 쓴다 (이슈 #178).
    /// </summary>
    public readonly struct CoinDrop
    {
        public string DenomId { get; }
        public int Count { get; }

        public CoinDrop(string denomId, int count)
        {
            DenomId = denomId;
            Count = count;
        }
    }
}
