using RailDisplay.Core.Models.Audio;

namespace RailDisplay.Infrastructure.Audio;

/// <summary>播放适配边界；实例由一个设备控制器独占，所有操作串行调用。</summary>
public interface IAudioOutput : IAsyncDisposable
{
    bool IsSupported { get; }
    void Load(SoundDefinition sound);
    Task StartAsync(string name, float volume);
    Task StopAsync();
    void SetVolume(float volume);
}
