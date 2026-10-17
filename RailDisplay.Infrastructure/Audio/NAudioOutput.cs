using NAudio.Wave;
using NAudio.Wave.Alsa;
using NAudio.Wave.SampleProviders;
using RailDisplay.Core.Models.Audio;

namespace RailDisplay.Infrastructure.Audio;

public sealed class NAudioOutput : IAudioOutput
{
    private sealed record Clip(WaveFileReader Reader, VolumeSampleProvider Samples);

    private readonly string _resourceRoot;
    private readonly Dictionary<string, Clip> _clips = new(StringComparer.Ordinal);
    private IWavePlayer? _player;
    private TaskCompletionSource? _ended;
    private EventHandler<StoppedEventArgs>? _stoppedHandler;
    private Clip? _current;
    private bool _disposed;

    // TODO shortcut: NAudio 当前没有正式 macOS 播放后端，未来开发完成后加入 macOS 适配。
    public bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    public NAudioOutput(string? resourceRoot = null)
    {
        _resourceRoot = Path.GetFullPath(resourceRoot ?? AppContext.BaseDirectory);
    }

    public static string ResolveResourcePath(string resourceRoot, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath) ||
            !string.Equals(Path.GetExtension(relativePath), ".wav", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("音频必须使用相对于应用目录的 WAV 路径。", nameof(relativePath));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(resourceRoot));
        var path = Path.GetFullPath(relativePath, root);
        if (!path.StartsWith(root + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("音频路径不能离开应用目录。", nameof(relativePath));
        return path;
    }

    public void Load(SoundDefinition sound)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_clips.ContainsKey(sound.Name))
            return;
        var path = ResolveResourcePath(_resourceRoot, sound.ResourcePath);
        var reader = new WaveFileReader(path);
        try
        {
            // 首版只接入无需系统解码器的 PCM / IEEE float WAV。
            var samples = new VolumeSampleProvider(reader.ToSampleProvider());
            if (reader.Length == 0)
                throw new InvalidDataException($"音频没有采样数据：{path}");
            _clips.Add(sound.Name, new Clip(reader, samples));
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    public Task StartAsync(string name, float volume)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsSupported)
            throw new PlatformNotSupportedException("当前平台暂不支持音频输出。");
        if (_player != null)
            throw new InvalidOperationException("开始下一轮前必须停止并释放上一轮播放。");
        _current = _clips[name];
        _current.Reader.Position = 0;
        _current.Samples.Volume = volume;
        _player = CreatePlayer();
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ended = ended;
        _stoppedHandler = (_, args) =>
        {
            if (args.Exception is { } error)
                ended.TrySetException(error);
            else
                ended.TrySetResult();
        };
        _player.PlaybackStopped += _stoppedHandler;
        try
        {
            _player.Init(_current.Samples.ToWaveProvider16());
            _player.Play();
            return ended.Task;
        }
        catch
        {
            ReleasePlayer();
            throw;
        }
    }

    private static IWavePlayer CreatePlayer()
    {
        if (OperatingSystem.IsWindows())
            return new WasapiPlayerBuilder().Build();
        if (OperatingSystem.IsLinux())
            return new AlsaOut();
        throw new PlatformNotSupportedException("当前平台暂不支持音频输出。");
    }

    public async Task StopAsync()
    {
        if (_player == null)
            return;
        try
        {
            if (_player.PlaybackState != PlaybackState.Stopped)
                _player.Stop();
            if (_ended != null)
                await _ended.Task.ConfigureAwait(false);
        }
        finally
        {
            ReleasePlayer();
        }
    }

    public void SetVolume(float volume)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_current != null)
            _current.Samples.Volume = volume;
    }

    private void ReleasePlayer()
    {
        var player = _player;
        var handler = _stoppedHandler;
        _player = null;
        _ended = null;
        _stoppedHandler = null;
        _current = null;
        if (player != null)
        {
            player.PlaybackStopped -= handler;
            player.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            foreach (var clip in _clips.Values)
                clip.Reader.Dispose();
            _clips.Clear();
        }
    }
}
