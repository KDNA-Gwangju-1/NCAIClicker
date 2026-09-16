using System;

namespace NCAIClicker.Data
{
    /// <summary>
    /// 청구서 데이터
    /// </summary>
    [Serializable]
    public class Bill
    {
        public long Amount;
        public int IssuedDay;
        public int DueDay;
        public bool IsPaid;
    }
}
