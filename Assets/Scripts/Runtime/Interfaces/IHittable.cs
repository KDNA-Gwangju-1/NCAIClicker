using NCAIClicker.Data;

namespace NCAIClicker.Interfaces
{
    /// <summary>
    /// 타격 대상 계약
    /// </summary>
    public interface IHittable
    {
        void OnHit(HitInfo info);
        bool IsAlive { get; }
    }
}
