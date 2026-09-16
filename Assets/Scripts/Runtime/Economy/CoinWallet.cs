using System;
using System.Globalization;

namespace NCAIClicker.Economy
{
    /// <summary>
    /// 코인 지갑의 계산부. 배율 적용과 소수 잔여 보관, 런 순수입 집계만 담당한다.
    /// 이벤트를 발행하지 않는다 — 구독과 발행은 EconomyManager 가 맡는다.
    /// 규칙의 정본은 docs/ARCHITECTURE.md "코인 계산·정산 계약" 4·5·8번이다.
    /// </summary>
    public class CoinWallet
    {
        /// <summary>아직 지갑에 넣지 못한 소수 부분. 회차 안에서 이월된다.</summary>
        private decimal _remainder;

        /// <summary>이번 런의 순수입. 지갑 잔액과 별도로 합산한다 (계약 5번).</summary>
        private decimal _runNet;

        private long _balance;

        public long CurrentCoin => _balance;

        /// <summary>이번 런 순수입의 정수 부분. 단계 목표 판정 기준이다.</summary>
        public long RunCoin => (long)decimal.Floor(_runNet);

        /// <summary>
        /// 저장용 소수 잔여. SaveData.CoinRemainder 와 주고받으며
        /// 한국어 Windows 로케일에서도 깨지지 않도록 InvariantCulture 를 쓴다.
        ///
        /// decimal 은 곱셈에서 소수 자릿수를 물려받아 같은 값이 "0.5" 도 "0.50" 도 될 수 있다.
        /// 저장 문자열이 배율 조합에 따라 달라지면 세이브 비교가 흔들리므로 뒤쪽 0을 떼고 적는다.
        /// </summary>
        public string RemainderText =>
            _remainder.ToString("0.############################", CultureInfo.InvariantCulture);

        /// <summary>
        /// 파괴 보상을 배율과 징수를 거쳐 지갑에 넣는다.
        /// 반환값은 이번에 지갑에 실제로 들어간 정수 증분이며 OnCoinEarned 의 인자가 된다.
        /// </summary>
        /// <param name="rawAmount">배율 적용 전 원시 보상. BreakInfo.RawCoin 을 그대로 넘긴다.</param>
        /// <param name="feverMultiplier">피버 중이 아니면 1</param>
        /// <param name="bonusMultiplier">퍼크·업그레이드 보너스. 기준값은 1</param>
        /// <param name="loanDailyCut">대출 일일 징수율. 대출이 없으면 0</param>
        public long AddEarning(decimal rawAmount, decimal feverMultiplier, decimal bonusMultiplier,
                               decimal loanDailyCut)
        {
            if (rawAmount < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(rawAmount), "파괴 보상은 음수가 될 수 없습니다.");
            }
            if (feverMultiplier < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(feverMultiplier), "피버 배율은 음수가 될 수 없습니다.");
            }
            if (bonusMultiplier < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(bonusMultiplier), "보너스 배율은 음수가 될 수 없습니다.");
            }
            if (loanDailyCut < 0m || loanDailyCut >= 1m)
            {
                throw new ArgumentOutOfRangeException(nameof(loanDailyCut),
                    "대출 징수율은 0 이상 1 미만이어야 합니다. 1 이상이면 수입이 사라집니다.");
            }

            var net = rawAmount * feverMultiplier * bonusMultiplier * (1m - loanDailyCut);

            // 런 순수입은 지갑과 별도로 쌓는다. 전날 잔여와 대출 원금이 단계 목표에 섞이지 않게 하려는 것이다.
            _runNet += net;

            _remainder += net;
            var deposit = decimal.Floor(_remainder);
            _remainder -= deposit;
            _balance += (long)deposit;
            return (long)deposit;
        }

        /// <summary>
        /// 대출 원금을 입금한다. 배율과 징수를 적용하지 않고 런 순수입에도 넣지 않는다 (계약 8번).
        /// </summary>
        public void AddLoanPrincipal(long amount)
        {
            if (amount < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "대출 원금은 음수가 될 수 없습니다.");
            }
            _balance += amount;
        }

        /// <summary>잔액이 모자라면 아무것도 바꾸지 않고 false 를 돌려준다.</summary>
        public bool TrySpendCoin(long amount)
        {
            if (amount < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "지출 금액은 음수가 될 수 없습니다.");
            }
            if (_balance < amount)
            {
                return false;
            }
            _balance -= amount;
            return true;
        }

        /// <summary>
        /// 런을 시작한다. 런 순수입만 0으로 되돌리고 지갑 잔액과 소수 잔여는 유지한다.
        /// 소수 잔여를 같이 지우면 회차 안에서 이월하기로 한 계약 7번이 깨진다.
        /// </summary>
        public void BeginRun()
        {
            _runNet = 0m;
        }

        /// <summary>저장 데이터에서 지갑을 복원한다. 잔여 문자열을 읽지 못하면 0으로 둔다.</summary>
        public void Restore(long balance, string remainderText)
        {
            if (balance < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(balance), "저장된 잔액이 음수입니다.");
            }

            _balance = balance;
            _runNet = 0m;

            decimal parsed;
            if (!decimal.TryParse(remainderText, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                || parsed < 0m || parsed >= 1m)
            {
                parsed = 0m;
            }
            _remainder = parsed;
        }
    }
}
