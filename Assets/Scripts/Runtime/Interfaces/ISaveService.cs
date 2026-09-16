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
    }
}
