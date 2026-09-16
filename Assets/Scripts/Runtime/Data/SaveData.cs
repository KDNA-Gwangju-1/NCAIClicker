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

        // JsonUtility는 참조 필드의 null을 직렬화하지 못해 빈 객체로 되살린다.
        // Has* 플래그로 저장 파일에서만 있음/없음을 구분하고, SaveManager가
        // Load/Save 시 이 플래그와 null을 서로 변환한다 (이슈 #76).
        public bool HasActiveBill;
        public Bill ActiveBill;
        public bool HasActiveLoan;
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
