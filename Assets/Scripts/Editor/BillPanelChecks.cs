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
            checkCount += RunPostPaymentFlowChecks();
            checkCount += RunCurrencyHudChecks();
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

            Assert(FindButton(prefab, "SkillTreeNoticeConfirmButton") != null,
                   "스킬 트리 최초 투자 안내 팝업의 확인 버튼이 프리팹에 없습니다 (#249).");
            checkCount++;

            // 원작 기준 납부 버튼 붉은색 유지 검증 (이슈 249)
            var payBtn = FindButton(prefab, "PayButton");
            Assert(payBtn != null, "PayButton 이 프리팹에 없습니다.");
            var payImg = payBtn.GetComponent<Image>();
            Assert(payImg != null && payImg.color.r > 0.4f && payImg.color.g < 0.3f,
                   "PayButton 배경색이 원작 기준 붉은색이어야 합니다: " + (payImg != null ? payImg.color.ToString() : "null"));
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

                // 반지는 파산 후 프레스티지 화면에서만 산다 (#291). 매일 여는 탭 화면에서는 반지 탭 버튼이
                // 없어야 하고, **코드로 반지 탭을 억지로 열어도** 반지 상점이 보이면 안 된다.
                Assert(!parts.RingTabButton.activeSelf, "탭 상태에서 반지 탭 버튼이 보입니다. 반지는 파산 후에만 삽니다.");
                controller.ShowAsTab(BillPanelController.Tab.Ring);
                Assert(!parts.RingTabRoot.activeSelf, "탭 상태에서 반지 탭을 열었더니 반지 상점이 보입니다.");
                checkCount++;

                controller.ShowAsPrestige(3);
                Assert(controller.IsOpen, "ShowAsPrestige 후 패널이 열려야 합니다.");
                Assert(controller.CurrentMode == BillPanelController.Mode.PrestigeOnly, "모드가 PrestigeOnly 이어야 합니다.");
                Assert(!parts.TabBar.activeSelf, "프레스티지 화면에서는 탭 줄이 숨겨져야 합니다.");
                Assert(parts.ContinueRow.activeSelf, "프레스티지 화면에서는 사이클 시작 버튼이 보여야 합니다.");
                Assert(parts.RingTabRoot.activeSelf, "프레스티지 화면인데 반지 상점이 보이지 않습니다.");
                checkCount++;

                controller.Close();
                Assert(!controller.IsOpen, "Close 후 패널이 닫혀야 합니다.");
                checkCount++;

                // 계속하기 클릭 시 정산창이 다시 뜨지 않도록 Closed 이벤트가 발행되지 않는지 검증 (이슈 249)
                var closedFired = false;
                Action onClosed = () => closedFired = true;
                controller.Closed += onClosed;
                controller.ShowAsTab();
                var handleContinue = typeof(BillPanelController).GetMethod("HandleContinueClicked", BindingFlags.NonPublic | BindingFlags.Instance);
                handleContinue.Invoke(controller, null);
                Assert(!controller.IsOpen, "계속하기 클릭 후 패널이 닫혀야 합니다.");
                Assert(!closedFired, "계속하기 클릭 시 정산창 복귀를 막기 위해 Closed 이벤트가 발행되지 않아야 합니다.");
                controller.Closed -= onClosed;
                checkCount += 2;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }

            return checkCount;
        }

        /// <summary>
        /// 기한 당일이면 "지금 납부!" 로 바뀌고 [아직] 버튼은 사라진다 (원작).
        /// 잔액 부족으로 납부가 실패하면 부족액을 표시하고 대출 버튼으로 고지서 전액을 빌릴 수 있다.
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
                InvokeLifecycle(controller, "OnEnable");

                controller.ShowAsModal();
                Assert(parts.LaterButton.activeSelf, "기한이 남았으면 [아직] 이 보여야 합니다.");
                Assert(parts.DueValue.text.Contains("3"), "남은 일수가 표시돼야 합니다: " + parts.DueValue.text);
                checkCount++;

                // 평소 모달 상태에서도 [아직] 클릭 시 탭 모드 및 스킬 트리 탭으로 정상 전환되어야 합니다
                var normalLaterBtn = parts.LaterButton.GetComponent<Button>();
                normalLaterBtn.onClick.Invoke();
                Assert(controller.CurrentMode == BillPanelController.Mode.Tab, "평소 모달에서 아직 클릭 시 탭 모드로 전환되어야 합니다.");
                Assert(controller.CurrentTab == BillPanelController.Tab.Upgrade, "평소 모달에서 아직 클릭 시 업그레이드 탭이어야 합니다.");
                checkCount++;
                controller.ShowAsModal();

                // 기한이 남았을 때 납부 실패는 부족액 캡션을 띄우고 [아직] 버튼을 유지한다.
                service.ShouldFailPay = true;
                var handlePayClicked = typeof(BillPanelController).GetMethod(
                    "HandlePayClicked", BindingFlags.NonPublic | BindingFlags.Instance);
                handlePayClicked.Invoke(controller, null);
                Assert(parts.PayCaption != null && parts.PayCaption.text.Contains("부족"),
                       "기한 전 납부 실패 시 부족액 안내가 떠야 합니다: " +
                       (parts.PayCaption != null ? parts.PayCaption.text : "null"));
                checkCount++;

                // 기한 당일에는 [아직] 버튼이 사라지고 납부·대출 선택만 남는다 (원작 규칙).
                service.DaysLeft = 1;
                controller.ShowAsModal();
                Assert(!parts.LaterButton.activeSelf,
                       "기한 당일에는 [아직] 버튼이 숨겨져야 합니다 — 납부·대출만 선택할 수 있어야 합니다.");
                Assert(parts.DueValue.text == "지금 납부!", "기한 당일 표기가 다릅니다: " + parts.DueValue.text);
                checkCount++;

                // 기한 당일 납부 실패 시에도 즉시 파산하지 않고 부족액 캡션을 띄워 대출 기회를 남긴다.
                service.ShouldFailPay = true;
                handlePayClicked.Invoke(controller, null);
                Assert(service.DeclaredBankruptcyCount == 0,
                       "기한 당일 납부 실패 시 즉시 파산하면 안 됩니다 — 대출 기회가 보장되어야 합니다.");
                Assert(parts.PayCaption != null && parts.PayCaption.text.Contains("부족"),
                       "기한 당일 납부 실패 시에도 부족액 안내가 떠야 합니다: " +
                       (parts.PayCaption != null ? parts.PayCaption.text : "null"));
                checkCount++;

                // 대출 거절 사유 구분 (이슈 #272 DoD): "대출 실패" 하나로 뭉치지 않고 사유별로 캡션을 나눈다.
                service.IsLoanUnlocked = false;
                controller.ShowAsModal();
                Assert(parts.LoanCaption != null && parts.LoanCaption.text.Contains("번째 고지서부터"),
                       "해금 순번 미달이면 안내 캡션이 떠야 합니다: " +
                       (parts.LoanCaption != null ? parts.LoanCaption.text : "null"));
                checkCount++;

                service.IsLoanUnlocked = true;
                service.LoanCooldownDaysRemaining = 3;
                controller.ShowAsModal();
                Assert(parts.LoanCaption != null && parts.LoanCaption.text == "3일 후 가능",
                       "쿨다운 중이면 남은 일수 캡션이 떠야 합니다: " +
                       (parts.LoanCaption != null ? parts.LoanCaption.text : "null"));
                checkCount++;
                service.LoanCooldownDaysRemaining = 0;
                controller.ShowAsModal(); // 다음 상호작용 검증 전에 정상 상태로 다시 그린다.

                // 이번 회귀 원인은 리스너 누락이었으므로 실제 Button.onClick 경로를 검증한다.
                // Edit Mode에서는 수명주기가 자동 실행되지 않으므로 한 번 정리한 뒤 명시적으로 배선한다.
                service.ShouldSucceedLoan = true;
                InvokeLifecycle(controller, "OnDisable");
                InvokeLifecycle(controller, "OnEnable");
                var loanButton = parts.LoanButton.GetComponent<Button>();
                Assert(loanButton.interactable, "마감 당일 미납이고 활성 대출이 없으면 대출 버튼이 활성화돼야 합니다.");
                loanButton.onClick.Invoke();
                Assert(service.LoanAttemptCount == 1,
                       "대출 버튼 클릭 1회당 TryTakeLoan 이 정확히 한 번 호출돼야 합니다: " + service.LoanAttemptCount);
                Assert(service.LastLoanTakenAmount == service.ActiveBill.Amount,
                       "대출 시 고지서 전액을 빌려야 합니다: " + service.LastLoanTakenAmount);
                Assert(parts.LoanCaption != null && parts.LoanCaption.text == "대출 완료",
                       "대출 성공 시 대출 완료 캡션이 표시되어야 합니다.");
                checkCount++;

                // 상환 버튼 (이슈 #272): 활성 대출이 있을 때만 보이고, 상환액을 캡션에 보여준다.
                service.LoanOwedAmount = 1234L;
                controller.ShowAsModal();
                Assert(parts.RepayButton.activeSelf, "활성 대출이 있으면 상환 버튼이 보여야 합니다.");
                Assert(parts.RepayCaption != null && parts.RepayCaption.text.Contains("1,234"),
                       "상환 캡션에 상환액이 표시돼야 합니다: " +
                       (parts.RepayCaption != null ? parts.RepayCaption.text : "null"));
                checkCount++;

                // 잔액 부족으로 상환 실패 시 부족액 캡션이 뜨고, 대출이 남아 있으니 버튼도 그대로 보인다.
                service.ShouldSucceedRepay = false;
                var repayButton = parts.RepayButton.GetComponent<Button>();
                repayButton.onClick.Invoke();
                Assert(service.RepayAttemptCount == 1,
                       "상환 버튼 클릭 1회당 TryRepayLoan 이 정확히 한 번 호출돼야 합니다: " + service.RepayAttemptCount);
                Assert(parts.RepayCaption != null && parts.RepayCaption.text.Contains("부족"),
                       "상환 실패 시 부족액 안내가 떠야 합니다: " +
                       (parts.RepayCaption != null ? parts.RepayCaption.text : "null"));
                Assert(parts.RepayButton.activeSelf, "상환 실패로 대출이 남아 있으면 상환 버튼이 계속 보여야 합니다.");
                checkCount++;

                // 상환 성공 시 대출이 사라지고, 상환 버튼과 "대출 완료" 캡션도 함께 사라진다 (DoD).
                service.ShouldSucceedRepay = true;
                repayButton.onClick.Invoke();
                Assert(!parts.RepayButton.activeSelf, "상환 성공 후에는 상환 버튼이 사라져야 합니다.");
                Assert(parts.LoanCaption != null && parts.LoanCaption.text != "대출 완료",
                       "상환 성공 후에는 대출 완료 캡션이 사라져야 합니다: " + parts.LoanCaption?.text);
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
                InvokeLifecycle(controller, "OnDisable");
                UnityEngine.Object.DestroyImmediate(host);
            }

            return checkCount;
        }

        /// <summary>
        /// 납부 후 화면 전환 흐름을 검증한다 (이슈 #249).
        /// 1. 납부 성공 시 즉시 기존 고지서가 "납부 완료"로 갱신되고 패널은 닫히거나 탭으로 도망가지 않는다.
        /// 2. 새 고지서 발행 시 모달로 갱신된다.
        /// 3. 새 고지서의 [아직] 클릭 시 스킬 트리 탭으로 전환된다.
        /// 4. 스킬 트리 진입 시 최초 1회 투자 안내 팝업이 뜨고, 확인 후에는 다시 뜨지 않는다.
        /// </summary>
        private static int RunPostPaymentFlowChecks()
        {
            var checkCount = 0;
            var host = BuildHost(out var controller, out var parts);
            var noticeKey = "HasSeenSkillTreeNotice";
            var originalNoticeVal = PlayerPrefs.GetInt(noticeKey, 0);

            try
            {
                PlayerPrefs.DeleteKey(noticeKey);

                var service = new FakeBillService { DaysLeft = 3 };
                service.ActiveBill = new Bill { Amount = 1000, IssuedDay = 1, DueDay = 4, IsPaid = false };
                controller.SetServices(service);

                InvokeLifecycle(controller, "OnEnable");
                controller.ShowAsModal();

                // 1. 납부 성공 시 즉시 납부 완료가 표시되며 모달 상태를 유지한다 (ShowBillTab으로 나가지 않음)
                service.ShouldFailPay = false;
                var payButton = parts.PayButton.GetComponent<Button>();
                payButton.onClick.Invoke();

                Assert(parts.DueValue.text == "납부 완료",
                       "납부 성공 직후 납부 완료 표기가 떠야 합니다: " + parts.DueValue.text);
                Assert(controller.CurrentMode == BillPanelController.Mode.Modal,
                       "납부 완료 직후에는 탭 화면으로 나가지 않고 모달 상태를 유지해야 합니다 (#249).");
                checkCount++;

                // 2. 새 고지서가 발행되면 모달 상태로 새 고지서가 표시된다
                var newBill = new Bill { Amount = 2500, IssuedDay = 1, DueDay = 4, IsPaid = false };
                service.ActiveBill = newBill;
                service.PaymentFlowState = PostPaymentFlowState.NewBillConfirmation;
                NCAIClicker.Events.GameEvents.PublishBillIssued(newBill);

                Assert(controller.CurrentMode == BillPanelController.Mode.Modal, "새 고지서 발행 시 모달 모드여야 합니다.");
                Assert(parts.LaterButton.activeSelf, "새 고지서 화면에 [아직] 버튼이 보여야 합니다.");
                checkCount++;

                // 3. [아직] 클릭 시 스킬 트리(Upgrade) 탭으로 이동하고 최초 안내 팝업이 뜬다
                var laterButton = parts.LaterButton.GetComponent<Button>();
                laterButton.onClick.Invoke();

                Assert(service.PaymentFlowState == PostPaymentFlowState.InvestmentMenu, "아직 클릭 시 InvestmentMenu 상태로 전이되어야 합니다 (#249).");
                Assert(controller.CurrentMode == BillPanelController.Mode.Tab, "아직 클릭 후 탭 모드여야 합니다.");
                Assert(controller.CurrentTab == BillPanelController.Tab.Upgrade, "아직 클릭 시 스킬 트리 탭이 선택되어야 합니다 (#249).");
                Assert(parts.SkillTreeNoticePanel != null && parts.SkillTreeNoticePanel.activeSelf,
                       "최초 스킬 트리 탭 진입 시 투자 안내 팝업이 떠야 합니다 (#249).");
                checkCount++;

                // 4. 안내 팝업 확인 버튼 클릭 시 팝업이 닫히고, 이후 다시 [아직]을 눌러도 강제 표시되지 않는다
                var noticeConfirm = parts.SkillTreeNoticeConfirmButton.GetComponent<Button>();
                noticeConfirm.onClick.Invoke();

                Assert(!parts.SkillTreeNoticePanel.activeSelf, "안내 팝업 확인 후 팝업이 닫혀야 합니다.");
                Assert(PlayerPrefs.GetInt(noticeKey, 0) == 1, "안내 확인 여부가 PlayerPrefs에 기록되어야 합니다.");
                checkCount++;

                // 재진입 시 강제 표시되지 않음 확인
                controller.ShowAsModal();
                laterButton.onClick.Invoke();
                Assert(!parts.SkillTreeNoticePanel.activeSelf, "이미 확인한 안내 팝업은 재진입 시 다시 뜨지 않아야 합니다 (#249).");
                checkCount++;
            }
            finally
            {
                if (originalNoticeVal == 1)
                {
                    PlayerPrefs.SetInt(noticeKey, 1);
                }
                else
                {
                    PlayerPrefs.DeleteKey(noticeKey);
                }
                PlayerPrefs.Save();

                InvokeLifecycle(controller, "OnDisable");
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
            public GameObject LoanButton { get; set; }
            public GameObject RepayButton { get; set; }
            public GameObject SkillTreeNoticePanel;
            public GameObject SkillTreeNoticeConfirmButton;
            public GameObject RingTabButton { get; set; }
            public GameObject RingTabRoot { get; set; }
            public TMPro.TextMeshProUGUI DueValue;
            public TMPro.TextMeshProUGUI PayCaption;
            public TMPro.TextMeshProUGUI LoanCaption { get; set; }
            public TMPro.TextMeshProUGUI RepayCaption { get; set; }
            public TMPro.TextMeshProUGUI BalanceText;
            public TMPro.TextMeshProUGUI LegacyPointText;
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
                LoanButton = ((Button)typeof(BillPanelController).GetField("_loanButton", flags).GetValue(controller)).gameObject,
                RepayButton = ((Button)typeof(BillPanelController).GetField("_repayButton", flags).GetValue(controller)).gameObject,
                SkillTreeNoticePanel = (GameObject)typeof(BillPanelController).GetField("_skillTreeNoticePanel", flags).GetValue(controller),
                SkillTreeNoticeConfirmButton = ((Button)typeof(BillPanelController).GetField("_skillTreeNoticeConfirmButton", flags).GetValue(controller)).gameObject,
                RingTabButton = ((Button)typeof(BillPanelController).GetField("_ringTabButton", flags).GetValue(controller)).gameObject,
                RingTabRoot = (GameObject)typeof(BillPanelController).GetField("_ringTabRoot", flags).GetValue(controller),
                DueValue = (TMPro.TextMeshProUGUI)typeof(BillPanelController).GetField("_dueValueText", flags).GetValue(controller),
                PayCaption = (TMPro.TextMeshProUGUI)typeof(BillPanelController).GetField("_payCaptionText", flags).GetValue(controller),
                LoanCaption = (TMPro.TextMeshProUGUI)typeof(BillPanelController).GetField("_loanCaptionText", flags).GetValue(controller),
                RepayCaption = (TMPro.TextMeshProUGUI)typeof(BillPanelController).GetField("_repayCaptionText", flags).GetValue(controller),
                BalanceText = (TMPro.TextMeshProUGUI)typeof(BillPanelController).GetField("_balanceText", flags).GetValue(controller),
                LegacyPointText = (TMPro.TextMeshProUGUI)typeof(BillPanelController).GetField("_legacyPointText", flags).GetValue(controller),
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

        private static void InvokeLifecycle(BillPanelController controller, string methodName)
        {
            var method = typeof(BillPanelController).GetMethod(
                methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            method.Invoke(controller, null);
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
            public int CurrentCycle { get; set; } = 1;
            public int DaysLeft { get; set; }
            public float LoanDailyCut { get; set; }
            public long LoanOwedAmount { get; set; }
            public bool IsLoanUnlocked { get; set; } = true;
            public int LoanCooldownDaysRemaining { get; set; }
            public Bill ActiveBill { get; set; }
            public string[] OfferedPerkIds => Array.Empty<string>();
            public PostPaymentFlowState PaymentFlowState { get; set; }
            public bool IsPrestigeWindowOpen { get; set; }

            /// <summary>true면 TryPay 가 실패한다 — 잔액 부족 캡션 표시를 검증하려고 둔 스위치.</summary>
            public bool ShouldFailPay { get; set; }

            public bool ShouldSucceedLoan { get; set; }
            public int LoanAttemptCount { get; private set; }
            public long LastLoanTakenAmount { get; private set; }

            /// <summary>true면 TryRepayLoan 이 성공한다 — 상환 버튼 검증용 스위치 (이슈 #272).</summary>
            public bool ShouldSucceedRepay { get; set; }
            public int RepayAttemptCount { get; private set; }

            public bool TryPay(Bill bill)
            {
                if (ShouldFailPay)
                {
                    return false;
                }
                if (bill != null)
                {
                    bill.IsPaid = true;
                }
                return true;
            }
            public bool TryTakeLoan(long amount)
            {
                LoanAttemptCount++;
                if (!ShouldSucceedLoan)
                {
                    return false;
                }
                LastLoanTakenAmount = amount;
                LoanDailyCut = float.Epsilon;
                return true;
            }
            public bool TryRepayLoan()
            {
                RepayAttemptCount++;
                if (!ShouldSucceedRepay)
                {
                    return false;
                }
                LoanDailyCut = 0f;
                LoanOwedAmount = 0L;
                return true;
            }
            public bool TryChoosePerk(string perkId) => false;
            public bool TryConfirmPaidFeedback() => true;
            public bool TryEnterInvestmentMenu()
            {
                if (PaymentFlowState != PostPaymentFlowState.NewBillConfirmation)
                {
                    return false;
                }
                PaymentFlowState = PostPaymentFlowState.InvestmentMenu;
                return true;
            }
            public bool TryCompletePostPaymentFlow()
            {
                PaymentFlowState = PostPaymentFlowState.None;
                return true;
            }
            public bool TryCloseDay() => false;
            public void RestoreCycle(int cycle) { CurrentCycle = cycle; }

            /// <summary>자발적 파산 호출 횟수 (계약 #175). 확인창을 거치지 않고 불리면 여기서 드러난다.</summary>
            public int DeclaredBankruptcyCount { get; private set; }

            public void DeclareBankruptcy()
            {
                DeclaredBankruptcyCount++;
            }
        }

        private static int RunCurrencyHudChecks()
        {
            var checkCount = 0;
            var host = BuildHost(out var controller, out var parts);

            try
            {
                var billService = new FakeBillService { DaysLeft = 5 };
                billService.ActiveBill = new Bill { Amount = 45, IssuedDay = 1, DueDay = 5, IsPaid = false };
                var econService = new FakeEconomyAndLegacyService { CurrentCoin = 158, CurrentLegacyPoints = 16 };

                controller.SetServices(billService, econService, econService);

                // 모달 모드 (납부 시 원작 사진처럼 $158 보유액과 16 레거시 포인트 표시)
                controller.ShowAsModal();
                Assert(parts.BalanceText != null && parts.BalanceText.text == "$158",
                    "납부 화면에서 현재 사이클 보유 금액이 $158로 보여야 합니다: " + (parts.BalanceText != null ? parts.BalanceText.text : "null"));
                Assert(parts.LegacyPointText != null && parts.LegacyPointText.text == "16",
                    "납부 화면에서 레거시 포인트가 16으로 보여야 합니다: " + (parts.LegacyPointText != null ? parts.LegacyPointText.text : "null"));
                checkCount += 2;

                // 탭 모드
                controller.ShowAsTab();
                Assert(parts.BalanceText != null && parts.BalanceText.text == "$158",
                    "탭 화면에서도 현재 사이클 보유 금액이 $158로 보여야 합니다.");
                Assert(parts.LegacyPointText != null && parts.LegacyPointText.text == "16",
                    "탭 화면에서도 레거시 포인트가 16으로 보여야 합니다.");
                checkCount += 2;

                // 프레스티지 화면 (파산 후 코인은 몰수되어 숨김, 레거시 포인트는 유지)
                controller.ShowAsPrestige(2);
                Assert(parts.BalanceText != null && string.IsNullOrEmpty(parts.BalanceText.text),
                    "프레스티지 화면에서는 코인 표기가 숨겨져야 합니다.");
                Assert(parts.LegacyPointText != null && parts.LegacyPointText.text == "16",
                    "프레스티지 화면에서도 레거시 포인트는 유지되어야 합니다.");
                checkCount += 2;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }

            return checkCount;
        }

        private sealed class FakeEconomyAndLegacyService : IEconomyService, ILegacyService
        {
            public long CurrentCoin { get; set; } = 158;
            public long RunCoin => 0;
            public long EarnedTotal => 0L;
            public System.Collections.Generic.IReadOnlyList<Data.CoinDrop> RunCoinBreakdown => null;
            public void AddCoin(decimal rawAmount) { }
            public void AddLoanPrincipal(long amount) { }
            public bool TrySpendCoin(long amount) => false;

            public long CurrentLegacyPoints { get; set; } = 16;
            public void AddLegacyPoints(long amount) { }
            public bool TrySpendLegacyPoints(long amount) => false;
        }
    }
}
