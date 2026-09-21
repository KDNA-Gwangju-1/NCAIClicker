namespace NCAIClicker.Interfaces
{
    /// <summary>
    /// 볼륨·화면 흔들림 설정 조회 및 적용 계약 (이슈 #196, 공용 계약 변경 #202).
    /// 값은 AudioManager 안에서만 적용한다 — 호출측은 가공 전 원시값만 넘긴다 (AGENTS.md).
    /// </summary>
    public interface IAudioService
    {
        float BgmVolume { get; }
        float SfxVolume { get; }
        bool IsScreenShakeEnabled { get; }

        void SetBgmVolume(float linear01);
        void SetSfxVolume(float linear01);
        void SetScreenShakeEnabled(bool enabled);
    }
}
