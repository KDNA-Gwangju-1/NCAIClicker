using System;

namespace NCAIClicker.Data
{
    public enum PostPaymentFlowState
    {
        None,
        PaidFeedback,
        PerkSelection,
        NewBillConfirmation,
        InvestmentMenu
    }

    /// <summary>
    /// 저장 데이터 전송 객체
    /// </summary>
    [Serializable]
    public class SaveData
    {
        public const int CurrentVersion = 5;

        public int Version = CurrentVersion;
        public long TotalCoin;
        public string CoinRemainder = "0";
        public int StageIndex;
        public long BestRunCoin;
        public int[] UpgradeLevels;
        public int CurrentDay = 1;
        public int BillIndex = 1;
        public int CycleIndex = 1;

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
        public PostPaymentFlowState PostPaymentFlowState;
        public string[] PendingPerkIds;

        // 파산을 넘어 남는 영구 성장 (이슈 #175·#183). 위의 다른 값과 달리
        // 회차 초기화에서 건드리지 않는다 — 그것이 이 두 필드의 존재 이유다.
        public long LegacyPoints;
        public int[] RingLevels;

        // 설정 패널(이슈 #196, 계약 #202) 값. 재시작 후에도 유지한다.
        public float BgmVolume = 1f;
        public float SfxVolume = 1f;
        public bool IsFullscreen = true;
        public bool IsScreenShakeEnabled = true;
    }
}
