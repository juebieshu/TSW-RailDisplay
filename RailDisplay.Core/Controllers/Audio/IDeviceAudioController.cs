using RailDisplay.Core.Models.Audio;

namespace RailDisplay.Core.Controllers.Audio;

public interface IDeviceAudioController : IAsyncDisposable
{
    bool IsSupported { get; }
    /// <summary>设备总音量，范围 0～1；静音不暂停播放进度。</summary>
    float Volume { get; }
    /// <summary>持续到整个请求结束；循环/间隔请求需要停止或被打断。失败通过任务异常反馈。</summary>
    Task<SoundPlaybackResult> PlayAsync(string name);
    Task StopAsync(string name);
    Task StopAllAsync();
    Task SetVolumeAsync(float volume);
}
