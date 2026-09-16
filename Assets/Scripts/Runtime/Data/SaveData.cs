using System;

namespace NCAIClicker.Data
{
    /// <summary>
    /// 저장 데이터 전송 객체
    /// </summary>
    [Serializable]
    public class SaveData
    {
        public int Version = 2;
        public long TotalCoin;
        public string CoinRemainder = "0";
        public int StageIndex;
        public long BestRunCoin;
        public int[] UpgradeLevels;
        public int CurrentDay = 1;
        public int BillIndex = 1;
        public Bill ActiveBill;
        public Loan ActiveLoan;
        public int LastLoanRepaidDay = -1;
        public ResumePoint ResumePoint;
        public long LastRunCoin;
        public int LastCompletedDay;
        public bool WasBankrupt;
        public bool IsCompleted;
        public string[] OfferedPerkIds;
        public string[] PendingPerkIds;
    }
}
