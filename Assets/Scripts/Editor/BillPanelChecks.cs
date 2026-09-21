using System;
using System.Reflection;
using NCAIClicker.Data;
using NCAIClicker.Interfaces;
using NCAIClicker.UI;
using UnityEngine;
using UnityEngine.UI;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 고지서 패널 검증 (이슈 #34).
    ///
    /// 같은 패널이 모달과 탭 두 상태로 쓰이므로, 상태마다 무엇이 보이고 무엇이 감춰지는지를
    /// 못 박아 둔다. 눈으로 보고 넘어가면 한쪽 상태만 고쳐진 채 지나간다.
    /// </summary>
    public static class BillPanelChecks
    {
        private const string PrefabPath = "Assets/Prefabs/Resources/UI/BillPanel.prefab";

        public static void RunBatch()
        {
            var checkCount = 0;
            checkCount += RunPrefabChecks();
            checkCount += RunModeChecks();
            checkCount += RunDueDayChecks();
            checkCount += RunNameChecks();

            Debug.Log("[BillPanelChecks] PASS " + checkCount + " checks.");
        }

        private static int RunPrefabChecks()
        {
            var checkCount = 0;

            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert(prefab != null, PrefabPath + " 가 없습니다. NCAI > UI > 고지서 패널 프리팹 생성 을 실행하세요.");
            checkCount++;

            var controller = prefab.GetComponent<BillPanelController>();
            Assert(controller != null, "프리팹 루트에 BillPanelController 가 없습니다.");
            checkCount++;

            // 직렬화 참조는 이름만 어긋나도 조용히 null 이 된다. 그러면 칸이 통째로 비는데 에러는 안 난다.
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var field in typeof(BillPanelController).GetFields(flags))
            {
                if (!field.IsDefined(typeof(SerializeField), true))
                {
                    continue;
                }
                Assert(field.GetValue(controller) as UnityEngine.Object != null,
                       field.Name + " 참조가 프리팹에서 비어 있습니다.");
            }
            checkCount++;

            // 자발적 파산이 #175 에서 붙었다. 이제는 **있는지**와 **확인창이 딸려 있는지**를 본다 —
            // 되돌릴 수 없는 버튼이라 확인창 없이 눌리면 회차가 통째로 날아간다.
            var bankruptcy = FindButton(prefab, "DeclareBankruptcyButton");
            Assert(bankruptcy != null, "DeclareBankruptcyButton 이 프리팹에 없습니다 (#175).");
            checkCount++;

            Assert(FindButton(prefab, "BankruptcyConfirmYesButton") != null &&
                   FindButton(prefab, "BankruptcyConfirmNoButton") != null,
                   "파산 선고 확인창의 예/아니오 버튼이 프리팹에 없습니다. " +
                   "되돌릴 수 없는 선택이라 확인 절차가 있어야 합니다 (#175).");
            checkCount++;

            return checkCount;
        }

        private static int RunModeChecks()
        {
            var checkCount = 0;
            var host = BuildHost(out var controller, out var parts);

            try
            {
                controller.ShowAsModal();
                Assert(controller.IsOpen, "ShowAsModal 후 패널이 열려야 합니다.");
                Assert(controller.CurrentMode == BillPanelController.Mode.Modal, "모드가 Modal 이어야 합니다.");
                Assert(!parts.TabBar.activeSelf, "모달에서는 탭 줄이 보이면 안 됩니다.");
                Assert(!parts.ContinueRow.activeSelf, "모달에서는 계속하기가 보이면 안 됩니다.");
                checkCount++;

                controller.ShowAsTab();
                Assert(controller.CurrentMode == BillPanelController.Mode.Tab, "모드가 Tab 이어야 합니다.");
                Assert(parts.TabBar.activeSelf, "탭 상태에서는 탭 줄이 보여야 합니다.");
                Assert(parts.ContinueRow.activeSelf, "탭 상태에서는 계속하기가 보여야 합니다.");
                checkCount++;

                controller.Close();
                Assert(!controller.IsOpen, "Close 후 패널이 닫혀야 합니다.");
                checkCount++;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }

            return checkCount;
        }

        /// <summary>
        /// 기한 당일이면 "지금 납부!" 로 바뀐다 (원작). [아직] 버튼은 기한 당일에도 계속 보인다
        /// (#212) — 미루는 선택지를 막는 규칙은 이 버튼의 숨김이 아니라
        /// <c>BillManager.IsBillOverdue()</c> 의 날짜 비교가 맡는다. 숨기면 납부 실패 시 모달에서
        /// 빠져나갈 길이 없어진다. 잔액 부족으로 납부가 실패하면 부족액을 캡션으로 보여준다.
        /// </summary>
        private static int RunDueDayChecks()
        {
            var checkCount = 0;
            var host = BuildHost(out var controller, out var parts);

            try
            {
                var service = new FakeBillService { DaysLeft = 3 };
                service.ActiveBill = new Bill { Amount = 5800, IssuedDay = 1, DueDay = 4, IsPaid = false };
                controller.SetServices(service);

                controller.ShowAsModal();
                Assert(parts.LaterButton.activeSelf, "기한이 남았으면 [아직] 이 보여야 합니다.");
                Assert(parts.DueValue.text.Contains("3"), "남은 일수가 표시돼야 합니다: " + parts.DueValue.text);
                checkCount++;

                service.DaysLeft = 1;
                controller.ShowAsModal();
                Assert(parts.LaterButton.activeSelf,
                       "기한 당일에도 [아직] 은 보여야 합니다 (#212) — 납부 실패 시 빠져나갈 길이 없어지면 안 됩니다.");
                Assert(parts.DueValue.text == "지금 납부!", "기한 당일 표기가 다릅니다: " + parts.DueValue.text);
                checkCount++;

                service.ShouldFailPay = true;
                // onClick.Invoke() 로 클릭을 흉내내지 않는다 — 리스너는 OnEnable 에서 잡히는데,
                // OnEnable 은 ExecuteAlways 가 없는 한 에디터 모드(플레이 모드 밖)에서는 돌지
                // 않는다. 이 검증은 플레이 모드 없이 돈다. 실제 클릭이 부르는 메서드를 직접 호출한다.
                var handlePayClicked = typeof(BillPanelController).GetMethod(
                    "HandlePayClicked", BindingFlags.NonPublic | BindingFlags.Instance);
                handlePayClicked.Invoke(controller, null);
                Assert(parts.PayCaption != null && parts.PayCaption.text.Contains("부족"),
                       "납부 실패 시 부족액 안내가 떠야 합니다 (#212): " +
                       (parts.PayCaption != null ? parts.PayCaption.text : "null"));
                checkCount++;

                service.ShouldFailPay = false;
                service.ActiveBill.IsPaid = true;
                controller.ShowAsModal();
                Assert(!parts.PayButton.activeSelf, "납부 완료면 납부 버튼이 사라져야 합니다.");
                Assert(parts.DueValue.text == "납부 완료", "납부 완료 표기가 다릅니다: " + parts.DueValue.text);
                checkCount++;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }

            return checkCount;
        }

        /// <summary>
        /// 고지서 이름은 고지서마다 달라지되 같은 고지서면 늘 같아야 한다.
        /// 열 때마다 바뀌면 "아까 그 고지서가 맞나" 를 의심하게 된다.
        /// </summary>
        private static int RunNameChecks()
        {
            var checkCount = 0;
            var balance = UnityEditor.AssetDatabase.LoadAssetAtPath<BalanceData>(
                "Assets/GameData/Generated/BalanceData.asset");

            Assert(balance != null && balance.BillNames.Count >= 4,
                   "bill_names.csv 가 4행 미만입니다. 고지서 이름이 금방 반복됩니다.");
            checkCount++;

            foreach (var name in balance.BillNames)
            {
                Assert(!string.IsNullOrWhiteSpace(name.Issuer), "발신처가 빈 행이 있습니다: " + name.Id);
                Assert(!string.IsNullOrWhiteSpace(name.Title), "제목이 빈 행이 있습니다: " + name.Id);
            }
            checkCount++;

            // 같은 씨앗값이면 같은 이름.
            Assert(ReferenceEquals(balance.GetBillName(12345), balance.GetBillName(12345)),
                   "같은 고지서인데 이름이 달라졌습니다.");
            checkCount++;

            // 씨앗값이 이웃해도 결과가 몰리면 안 된다. 20개를 뽑아 최소 3종은 나와야 한다.
            var seen = new System.Collections.Generic.HashSet<string>();
            for (var seed = 0; seed < 20; seed++)
            {
                seen.Add(balance.GetBillName(seed).Id);
            }
            Assert(seen.Count >= 3, "씨앗값을 바꿔도 이름이 " + seen.Count + "종뿐입니다. 해시가 몰립니다.");
            checkCount++;

            return checkCount;
        }

        private struct Parts
        {
            public GameObject TabBar;
            public GameObject ContinueRow;
            public GameObject LaterButton;
            public GameObject PayButton;
            public TMPro.TextMeshProUGUI DueValue;
            public TMPro.TextMeshProUGUI PayCaption;
        }

        private static GameObject BuildHost(out BillPanelController controller, out Parts parts)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var host = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
            host.hideFlags = HideFlags.HideAndDontSave;
            controller = host.GetComponent<BillPanelController>();

            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            parts = new Parts
            {
                TabBar = (GameObject)typeof(BillPanelController).GetField("_tabBar", flags).GetValue(controller),
                ContinueRow = (GameObject)typeof(BillPanelController).GetField("_continueRow", flags).GetValue(controller),
                LaterButton = ((Button)typeof(BillPanelController).GetField("_laterButton", flags).GetValue(controller)).gameObject,
                PayButton = ((Button)typeof(BillPanelController).GetField("_payButton", flags).GetValue(controller)).gameObject,
                DueValue = (TMPro.TextMeshProUGUI)typeof(BillPanelController).GetField("_dueValueText", flags).GetValue(controller),
                PayCaption = (TMPro.TextMeshProUGUI)typeof(BillPanelController).GetField("_payCaptionText", flags).GetValue(controller),
            };
            return host;
        }

        private static Button FindButton(GameObject root, string name)
        {
            foreach (var button in root.GetComponentsInChildren<Button>(true))
            {
                if (button.name == name)
                {
                    return button;
                }
            }
            return null;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("[BillPanelChecks] " + message);
            }
        }

        /// <summary>고지서 상태만 바꿔 가며 화면을 확인하려고 쓰는 가짜 서비스.</summary>
        private sealed class FakeBillService : IBillService
        {
            public int CurrentDay { get; set; } = 1;
            public int DaysLeft { get; set; }
            public float LoanDailyCut { get; set; }
            public Bill ActiveBill { get; set; }
            public string[] OfferedPerkIds => Array.Empty<string>();

            /// <summary>true면 TryPay 가 실패한다 — 잔액 부족 캡션 표시를 검증하려고 둔 스위치.</summary>
            public bool ShouldFailPay { get; set; }

            public bool TryPay(Bill bill) => !ShouldFailPay;
            public bool TryTakeLoan(long amount) => false;
            public bool TryRepayLoan() => false;
            public bool TryChoosePerk(string perkId) => false;

            /// <summary>자발적 파산 호출 횟수 (계약 #175). 확인창을 거치지 않고 불리면 여기서 드러난다.</summary>
            public int DeclaredBankruptcyCount { get; private set; }

            public void DeclareBankruptcy()
            {
                DeclaredBankruptcyCount++;
            }
        }
    }
}
