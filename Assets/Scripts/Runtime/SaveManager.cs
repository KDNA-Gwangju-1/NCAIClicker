using System;
using System.IO;
using NCAIClicker.Data;
using NCAIClicker.Interfaces;
using UnityEngine;

namespace NCAIClicker
{
    /// <summary>
    /// ISaveService 구현체. JsonUtility로 SaveData를 persistentDataPath/save.json에 저장·로드한다.
    /// 직렬화 방식·버전 정책은 ARCHITECTURE.md 2절(이슈 1.2.2)이 정본이다.
    /// </summary>
    public class SaveManager : MonoBehaviour, ISaveService, IGamePersistence
    {
        private const string SaveFileName = "save.json";

        public static ISaveService Instance { get; private set; }

        /// <summary>
        /// 수집·분배 통로 (이슈 #203). Instance 와 나눠 둔 이유는 소비처가 다르기 때문이다 —
        /// 메인 메뉴는 HasSave 만, GameManager 는 이쪽만 쓴다.
        /// </summary>
        public static IGamePersistence Persistence { get; private set; }

        // 저장에 담고 되돌릴 대상들. ManagerBootstrap 이 넣어 준다 (이슈 #203).
        //
        // **직접 조회하지 않고 주입받는 이유**: SaveManager 가 EconomyManager 를 찾아 나서면
        // 매니저가 매니저를 뒤지는 경로가 생긴다. ARCHITECTURE 는 "조립하는 지점 한 곳만
        // 구현 클래스를 안다"로 정했고, 그 한 곳이 ManagerBootstrap 이다.
        // 없으면 그 항목만 조용히 건너뛴다 — 저장 자체가 멈추면 안 된다.
        private IWalletPersistence _wallet;
        private IUpgradePersistence _upgrades;
        private ILegacyPersistence _legacy;
        private IEconomyService _economy;
        private ILegacyService _legacyPoints;
        private IStageService _stage;

        public bool HasSave => File.Exists(SavePath);

        private string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        private void Awake()
        {
            Instance = this;
            Persistence = this;
        }

        /// <summary>
        /// 저장 대상 통로를 넣는다 (이슈 #203). ManagerBootstrap 이 생성 직후 한 번 부른다.
        /// 넘기지 않은 항목은 수집·복원에서 건너뛴다.
        /// </summary>
        public void SetPersistenceTargets(IEconomyService economy, IWalletPersistence wallet,
                                          IUpgradePersistence upgrades, ILegacyService legacyPoints,
                                          ILegacyPersistence legacy, IStageService stage)
        {
            _economy = economy;
            _wallet = wallet;
            _upgrades = upgrades;
            _legacyPoints = legacyPoints;
            _legacy = legacy;
            _stage = stage;
        }

        /// <summary>
        /// 지금 매니저들이 들고 있는 값을 모아 저장한다 (이슈 #203).
        ///
        /// **고지서·대출·퍼크 후보는 담지 않는다.** IBillService 에 복원 통로가 없어 담아 봐야
        /// 되돌릴 수 없다 — 쓰기만 하고 읽지 못하는 필드는 "저장된다"는 착각만 만든다
        /// (docs/TECH_NOTES/billing.md 알려진 한계, 별도 계약 이슈가 먼저다).
        /// </summary>
        public void CollectAndSave()
        {
            var data = Load();

            if (_economy != null)
            {
                data.TotalCoin = _economy.CurrentCoin;
            }
            if (_wallet != null)
            {
                data.CoinRemainder = _wallet.CurrentRemainderText;
            }
            if (_upgrades != null)
            {
                data.UpgradeLevels = _upgrades.CurrentUpgradeLevels;
            }
            if (_legacyPoints != null)
            {
                data.LegacyPoints = _legacyPoints.CurrentLegacyPoints;
            }
            if (_legacy != null)
            {
                data.RingLevels = _legacy.CurrentRingLevels;
            }
            if (_stage != null)
            {
                data.StageIndex = _stage.CurrentStageIndex;
            }

            Save(data);
        }

        /// <summary>
        /// 저장을 읽어 매니저들에 되돌린다 (이슈 #203).
        ///
        /// **순서가 ARCHITECTURE 1절 초기화 순서를 따른다** — 저장 로드가 먼저고, 그 결과로
        /// 코인·업그레이드를 복원한 뒤 단계를 되돌린다. 단계가 먼저 오면 단계에 딸린 값을
        /// 읽는 쪽이 아직 복원되지 않은 지갑을 본다.
        /// </summary>
        public void LoadAndDistribute()
        {
            var data = Load();

            if (_wallet != null)
            {
                _wallet.RestoreWallet(data.TotalCoin, data.CoinRemainder);
            }
            if (_upgrades != null)
            {
                _upgrades.RestoreUpgradeLevels(data.UpgradeLevels);
            }
            if (_legacy != null)
            {
                _legacy.RestoreLegacy(data.LegacyPoints, data.RingLevels);
            }
            if (_stage != null)
            {
                _stage.RestoreStage(data.StageIndex);
            }
        }

        /// <summary>
        /// 성장을 지운다 (이슈 #203). 빈 저장을 쓴 뒤 곧바로 분배해 **메모리까지** 비운다.
        ///
        /// 설정 필드는 현재 저장에서 옮겨 담는다 — 성장이 아니므로 새 회차에서도 지우지 않는다
        /// (ARCHITECTURE 2절 SaveData, #202). 여기서 AudioManager 를 조회하지 않는 이유는
        /// 매니저가 매니저를 뒤지지 않기 위해서다. 대신 부르는 쪽이 먼저 반영해 둔다.
        /// </summary>
        public void ResetAndDistribute()
        {
            var current = Load();
            var fresh = new SaveData
            {
                BgmVolume = current.BgmVolume,
                SfxVolume = current.SfxVolume,
                IsFullscreen = current.IsFullscreen,
                IsScreenShakeEnabled = current.IsScreenShakeEnabled
            };

            Save(fresh);
            LoadAndDistribute();
        }

        public SaveData Load()
        {
            if (!File.Exists(SavePath))
            {
                return new SaveData();
            }

            string json;
            try
            {
                json = File.ReadAllText(SavePath);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[SaveManager] 저장 파일을 읽지 못해 초기화한다: {e.Message}");
                return new SaveData();
            }

            try
            {
                var data = JsonUtility.FromJson<SaveData>(json);
                if (data == null)
                {
                    throw new InvalidDataException("역직렬화 결과가 null이다");
                }

                data = ApplyVersionMigrations(data);
                RestoreNullableReferences(data);
                return data;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveManager] 저장 파일이 손상되었거나 지원하지 않는 버전이라 백업 후 초기화한다: {e.Message}");
                BackupCorruptFile();
                return new SaveData();
            }
        }

        public void Save(SaveData data)
        {
            data.HasActiveBill = data.ActiveBill != null;
            data.HasActiveLoan = data.ActiveLoan != null;

            var json = JsonUtility.ToJson(data, true);
            var tempPath = SavePath + ".tmp";

            try
            {
                File.WriteAllText(tempPath, json);

                if (File.Exists(SavePath))
                {
                    File.Replace(tempPath, SavePath, null);
                }
                else
                {
                    File.Move(tempPath, SavePath);
                }
            }
            catch (IOException e)
            {
                Debug.LogError($"[SaveManager] 저장 실패: {e.Message}");
            }
        }

        /// <summary>
        /// 저장된 Version으로 분기해 예전 필드를 오늘 구조로 채워 넣는다.
        /// 마이그레이션 분기는 지우지 않고 버전 수만큼 누적한다 (ARCHITECTURE.md 2절).
        /// </summary>
        private static SaveData ApplyVersionMigrations(SaveData data)
        {
            switch (data.Version)
            {
                case 1:
                    // v1에는 회차 정보(날짜·고지서·대출 등)가 없었다. JsonUtility가 역직렬화 시
                    // SaveData의 필드 이니셜라이저 기본값(CurrentDay=1, BillIndex=1 등)을
                    // 이미 채워 넣으므로 별도 보정 코드가 필요 없다.
                    break;
                case 2:
                    // v2에는 레거시 포인트·반지가 없었다. LegacyPoints는 0으로 오지만
                    // **RingLevels는 null로 온다** — JsonUtility는 없는 배열 필드를 빈 배열이
                    // 아니라 null로 되살린다(OfferedPerkIds와 같다). 복원하는 쪽이 null을
                    // 감당해야 하므로 여기서 억지로 채우지 않는다 (이슈 #175).
                    break;
                case 3:
                    // v3에는 설정 값(볼륨·창모드·화면 흔들림)이 없었다. v1과 같은 이유로
                    // 필드 이니셜라이저 기본값(모두 켬/최대 볼륨)이 이미 채워진다 (이슈 #196).
                    break;
                case SaveData.CurrentVersion:
                    break;
                default:
                    throw new NotSupportedException($"지원하지 않는 저장 버전: {data.Version}");
            }

            data.Version = SaveData.CurrentVersion;
            return data;
        }

        /// <summary>
        /// JsonUtility는 null 참조 필드를 빈 객체로 되살리므로, Has* 플래그를 보고
        /// 실제로 없던 참조는 다시 null로 되돌린다 (이슈 #76).
        /// </summary>
        private static void RestoreNullableReferences(SaveData data)
        {
            if (!data.HasActiveBill)
            {
                data.ActiveBill = null;
            }

            if (!data.HasActiveLoan)
            {
                data.ActiveLoan = null;
            }
        }

        private void BackupCorruptFile()
        {
            if (!File.Exists(SavePath))
            {
                return;
            }

            try
            {
                File.Copy(SavePath, SavePath + ".bak", overwrite: true);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[SaveManager] 손상된 저장 파일 백업 실패: {e.Message}");
            }
        }
    }
}
