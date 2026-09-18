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
    public class SaveManager : MonoBehaviour, ISaveService
    {
        private const string SaveFileName = "save.json";

        public static ISaveService Instance { get; private set; }

        public bool HasSave => File.Exists(SavePath);

        private string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        private void Awake()
        {
            Instance = this;
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
