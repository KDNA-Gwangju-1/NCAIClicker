using System;

namespace NCAIClicker.Data
{
    /// <summary>
    /// 대출 데이터
    /// </summary>
    [Serializable]
    public class Loan
    {
        public long Principal;
        public long Owed;
        public float DailyCut;
    }
}
