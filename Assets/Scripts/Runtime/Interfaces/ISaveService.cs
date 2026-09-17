using NCAIClicker.Data;

namespace NCAIClicker.Interfaces
{
    /// <summary>
    /// 저장 및 로드 계약
    /// </summary>
    public interface ISaveService
    {
        SaveData Load();
        void Save(SaveData data);

        /// <summary>저장 파일이 실제로 존재하는지 여부. Load()는 없어도 항상 기본값을 반환해 구분이 안 된다 (이슈 #139).</summary>
        bool HasSave { get; }
    }
}
