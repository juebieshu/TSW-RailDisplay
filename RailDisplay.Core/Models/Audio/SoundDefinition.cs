namespace RailDisplay.Core.Models.Audio;

public enum SoundPlaybackMode
{
    Once,
    Loop,
    Interval
}

public enum SoundPlaybackResult
{
    Completed,
    Stopped,
    Interrupted
}

/// <param name="Name">设备内唯一、大小写敏感的任意音频名称。</param>
/// <param name="ResourcePath">相对于应用目录的 WAV 路径。</param>
/// <param name="RepeatInterval">仅 Interval 使用：每轮结束后等待的时间，必须大于零。</param>
public sealed record SoundDefinition(
    string Name,
    string ResourcePath,
    SoundPlaybackMode Mode = SoundPlaybackMode.Once,
    TimeSpan RepeatInterval = default);
