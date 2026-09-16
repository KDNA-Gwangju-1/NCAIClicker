using System;
using NCAIClicker.Economy;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// CoinWallet 의 배율 적용과 소수 잔여 처리를 검증한다.
    /// 이벤트 배선은 EconomyManagerChecks 가 본다.
    /// 규칙의 정본은 docs/ARCHITECTURE.md "코인 계산·정산 계약" 4·5·8번이다.
    /// </summary>
    public static class CoinWalletChecks
    {
        public static void RunBatch()
        {
            var checkCount = 0;

            // 배율이 모두 1이면 원시값이 그대로 들어간다.
            var wallet = new CoinWallet();
            AssertCondition(wallet.AddEarning(4m, 1m, 1m, 0m) == 4L, "기본 지급액이 다릅니다.");
            AssertCondition(wallet.CurrentCoin == 4L, "잔액이 다릅니다.");
            AssertCondition(wallet.RunCoin == 4L, "런 순수입이 다릅니다.");
            checkCount++;

            // 소수는 버리지 않고 이월한다. 두 번째 지급에서 정수 1이 완성된다.
            wallet = new CoinWallet();
            AssertCondition(wallet.AddEarning(0.5m, 1m, 1m, 0m) == 0L, "소수만 들어왔는데 입금됐습니다.");
            AssertCondition(wallet.CurrentCoin == 0L, "소수가 잔액에 반영됐습니다.");
            AssertCondition(wallet.AddEarning(0.5m, 1m, 1m, 0m) == 1L, "이월된 소수가 합쳐지지 않았습니다.");
            checkCount++;

            // 피버 배율과 대출 징수가 곱해진다.
            wallet = new CoinWallet();
            AssertCondition(wallet.AddEarning(4m, 3m, 1m, 0m) == 12L, "피버 배율이 적용되지 않았습니다.");
            wallet = new CoinWallet();
            AssertCondition(wallet.AddEarning(10m, 1m, 1m, 0.1m) == 9L, "대출 징수가 적용되지 않았습니다.");
            checkCount++;

            // 복합 계산: 10 × 3 × 1.5 × 0.9 = 40.5 → 40 입금, 0.5 이월
            wallet = new CoinWallet();
            AssertCondition(wallet.AddEarning(10m, 3m, 1.5m, 0.1m) == 40L, "복합 배율 계산이 다릅니다.");
            AssertCondition(wallet.RemainderText == "0.5", "이월 잔여가 다릅니다: " + wallet.RemainderText);
            checkCount++;

            // CSV 의 float 배율을 decimal 로 바꿔 곱하므로 오차가 쌓이지 않는다.
            // float 로 계산하면 0.1 을 열 번 더해도 1 에 못 미쳐 입금이 0 이 된다.
            wallet = new CoinWallet();
            var deposited = 0L;
            for (var i = 0; i < 10; i++)
            {
                deposited += wallet.AddEarning((decimal)0.1f, 1m, 1m, 0m);
            }
            AssertCondition(deposited == 1L, "소수 누적 정밀도가 어긋났습니다: " + deposited);
            AssertCondition(wallet.RemainderText == "0", "누적 후 잔여가 0 이 아닙니다: " + wallet.RemainderText);
            checkCount++;

            // 대출 원금은 배율을 타지 않고 런 순수입에도 들어가지 않는다.
            wallet = new CoinWallet();
            wallet.AddEarning(5m, 1m, 1m, 0m);
            wallet.AddLoanPrincipal(100L);
            AssertCondition(wallet.CurrentCoin == 105L, "대출 원금이 잔액에 반영되지 않았습니다.");
            AssertCondition(wallet.RunCoin == 5L, "대출 원금이 런 순수입에 섞였습니다.");
            checkCount++;

            // 잔액이 모자라면 아무것도 바꾸지 않는다.
            wallet = new CoinWallet();
            wallet.AddEarning(50m, 1m, 1m, 0m);
            AssertCondition(!wallet.TrySpendCoin(51L), "잔액보다 많이 썼습니다.");
            AssertCondition(wallet.CurrentCoin == 50L, "실패한 지출이 잔액을 바꿨습니다.");
            AssertCondition(wallet.TrySpendCoin(50L), "잔액만큼의 지출이 거부됐습니다.");
            AssertCondition(wallet.CurrentCoin == 0L, "지출이 반영되지 않았습니다.");
            checkCount++;

            // 런을 다시 시작하면 순수입만 0 이 되고 지갑의 소수 잔여는 살아 있다.
            // 이 둘을 한 변수로 합치면 여기서 값이 어긋난다.
            wallet = new CoinWallet();
            wallet.AddEarning(0.5m, 1m, 1m, 0m);
            wallet.BeginRun();
            AssertCondition(wallet.RunCoin == 0L, "런 시작 후 순수입이 0 이 아닙니다.");
            AssertCondition(wallet.AddEarning(0.5m, 1m, 1m, 0m) == 1L, "런을 넘어 이월된 소수가 사라졌습니다.");
            AssertCondition(wallet.RunCoin == 0L, "지난 런의 소수가 이번 런 순수입에 섞였습니다.");
            checkCount++;

            // 저장 왕복. 잔여 문자열이 그대로 돌아온다.
            wallet = new CoinWallet();
            wallet.AddEarning(10.25m, 1m, 1m, 0m);
            var restored = new CoinWallet();
            restored.Restore(wallet.CurrentCoin, wallet.RemainderText);
            AssertCondition(restored.CurrentCoin == wallet.CurrentCoin, "복원된 잔액이 다릅니다.");
            AssertCondition(restored.RemainderText == wallet.RemainderText, "복원된 잔여가 다릅니다.");
            AssertCondition(restored.AddEarning(0.75m, 1m, 1m, 0m) == 1L, "복원된 잔여가 합산되지 않았습니다.");
            checkCount++;

            // 망가진 저장값은 예외 대신 0 으로 떨어뜨린다. 로드 실패로 게임이 멈추면 안 된다.
            restored = new CoinWallet();
            restored.Restore(7L, "이건숫자가아니다");
            AssertCondition(restored.RemainderText == "0", "잘못된 잔여 문자열이 0 으로 처리되지 않았습니다.");
            restored.Restore(7L, "1.5");
            AssertCondition(restored.RemainderText == "0", "범위를 벗어난 잔여가 0 으로 처리되지 않았습니다.");
            checkCount++;

            // 계약을 깨는 인자는 조용히 넘기지 않고 예외로 세운다.
            AssertThrows(() => new CoinWallet().AddEarning(-1m, 1m, 1m, 0m), "음수 보상");
            AssertThrows(() => new CoinWallet().AddEarning(1m, 1m, 1m, 1m), "징수율 1");
            AssertThrows(() => new CoinWallet().AddEarning(1m, 1m, 1m, -0.1m), "음수 징수율");
            AssertThrows(() => new CoinWallet().AddLoanPrincipal(-1L), "음수 대출 원금");
            checkCount++;

            Debug.Log("[CoinWalletChecks] PASS " + checkCount + " checks.");
        }

        private static void AssertThrows(Action action, string label)
        {
            try
            {
                action();
            }
            catch (ArgumentOutOfRangeException)
            {
                return;
            }
            throw new InvalidOperationException("예외가 나야 하는데 통과했습니다: " + label);
        }

        private static void AssertCondition(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
