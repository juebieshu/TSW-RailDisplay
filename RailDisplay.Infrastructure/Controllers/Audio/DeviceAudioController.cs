using RailDisplay.Core.Controllers.Audio;
using RailDisplay.Core.Models.Audio;
using RailDisplay.Infrastructure.Audio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RailDisplay.Infrastructure.Controllers.Audio;

public class DeviceAudioController : IDeviceAudioController
{
    private sealed class PendingPlayback
    {
        public Task<SoundPlaybackResult> Completion { get; set; } = null!;
        public AudioScheduler.Request? Request { get; set; }
        public bool IsActive => !Completion.IsCompleted && (Request == null || !Request.Completion.IsCompleted);
    }

    private readonly AudioScheduler _scheduler;
    private readonly IAudioOutput _output;
    private readonly ILogger<DeviceAudioController> _logger;
    private readonly TimeProvider _clock;
    private readonly long _started;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _disposeLock = new();
    private readonly object _requestLock = new();
    private readonly Dictionary<string, PendingPlayback> _playTasks = new(StringComparer.Ordinal);
    private Task? _worker;
    private Task? _disposeTask;
    private Task? _playbackTask;
    private long? _playingId;
    private Exception? _failure;
    private float _volume = 1;
    private int _disposed;

    public bool IsSupported => _output.IsSupported;
    public float Volume => Volatile.Read(ref _volume);

    public DeviceAudioController(IEnumerable<SoundDefinition> sounds, IAudioOutput output,
        TimeProvider? clock = null, ILogger<DeviceAudioController>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        _scheduler = new AudioScheduler(sounds);
        foreach (var sound in _scheduler.Sounds)
            NAudioOutput.ResolveResourcePath(AppContext.BaseDirectory, sound.ResourcePath);
        _output = output;
        _logger = logger ?? NullLogger<DeviceAudioController>.Instance;
        _clock = clock ?? TimeProvider.System;
        _started = _clock.GetTimestamp();
    }

    public Task<SoundPlaybackResult> PlayAsync(string name)
    {
        lock (_requestLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            _scheduler.GetSound(name);
            if (!IsSupported)
                throw new PlatformNotSupportedException("当前平台暂不支持音频输出。");
            // 同名调用直接共享任务，避免长循环下重复调用累积异步等待者。
            if (_playTasks.TryGetValue(name, out var existing) && existing.IsActive)
                return existing.Completion;
            var pending = new PendingPlayback();
            pending.Completion = PlayCoreAsync(name, pending);
            _playTasks[name] = pending;
            return pending.Completion;
        }
    }

    private async Task<SoundPlaybackResult> PlayCoreAsync(string name, PendingPlayback pending)
    {
        Task<SoundPlaybackResult>? completion = null;
        await ExecuteAsync(async () =>
        {
            var sound = _scheduler.GetSound(name);
            // 资源加载成功后才提交请求；坏文件不能打断当前有效声音。
            try
            {
                _output.Load(sound);
            }
            catch (Exception error)
            {
                _logger.LogError(error, "音频资源加载失败：{SoundName}，资源 {ResourcePath}", name, sound.ResourcePath);
                throw;
            }
            var request = _scheduler.Play(name);
            lock (_requestLock)
                pending.Request = request;
            completion = request.Completion;
            _worker ??= Task.Run(RunAsync);
            await PumpAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);
        return await completion!.ConfigureAwait(false);
    }

    public Task StopAsync(string name)
    {
        lock (_requestLock)
        {
            _scheduler.GetSound(name);
            _playTasks.Remove(name);
            return ExecuteAsync(async () =>
            {
                _scheduler.Stop(name);
                await PumpAsync().ConfigureAwait(false);
                ThrowIfFailed();
            });
        }
    }

    public Task StopAllAsync()
    {
        lock (_requestLock)
        {
            _playTasks.Clear();
            return ExecuteAsync(async () =>
            {
                _scheduler.StopAll();
                await PumpAsync().ConfigureAwait(false);
                ThrowIfFailed();
            });
        }
    }

    public Task SetVolumeAsync(float volume) => ExecuteAsync(() =>
    {
        if (!float.IsFinite(volume) || volume < 0 || volume > 1)
            throw new ArgumentOutOfRangeException(nameof(volume), "音量必须是 0～1 的有限数。");
        _output.SetVolume(volume);
        Volatile.Write(ref _volume, volume);
        return Task.CompletedTask;
    });

    private async Task ExecuteAsync(Func<Task> action)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _gate.WaitAsync(_shutdown.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            ThrowIfFailed();
            // 先登记串行操作，再转到线程池执行 I/O，保持先播放后停止的调用顺序。
            await Task.Run(action).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ThrowIfFailed()
    {
        if (_failure != null)
            throw new InvalidOperationException("设备音频输出已失败，请释放控制器后重新创建。", _failure);
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10), _clock);
        try
        {
            while (await timer.WaitForNextTickAsync(_shutdown.Token).ConfigureAwait(false))
            {
                await _gate.WaitAsync(_shutdown.Token).ConfigureAwait(false);
                try
                {
                    if (_failure != null)
                        return;
                    await PumpAsync().ConfigureAwait(false);
                    if (_scheduler.ActiveCount == 0)
                    {
                        timer.Dispose();
                        _worker = null;
                        return;
                    }
                }
                finally
                {
                    _gate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
    }

    private async Task PumpAsync()
    {
        try
        {
            var now = _clock.GetElapsedTime(_started);
            if (_playbackTask is { IsCompleted: true })
            {
                await _playbackTask.ConfigureAwait(false);
                await _output.StopAsync().ConfigureAwait(false);
                _scheduler.Complete(_playingId!.Value, now);
                _playingId = null;
                _playbackTask = null;
            }
            _scheduler.Advance(now);
            if (_playingId == _scheduler.Current?.Id)
                return;
            await _output.StopAsync().ConfigureAwait(false);
            _playingId = null;
            _playbackTask = null;
            if (_scheduler.Current is { } playback)
            {
                _playbackTask = _output.StartAsync(playback.Request.Sound.Name, Volume);
                _playingId = playback.Id;
            }
        }
        catch (Exception error)
        {
            try
            {
                await _output.StopAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupError)
            {
                error = new AggregateException(error, cleanupError);
            }
            _failure = error;
            _logger.LogError(error, "设备音频输出失败，停止全部有效请求，当前声音 {SoundName}",
                _scheduler.Current?.Request.Sound.Name);
            _playingId = null;
            _playbackTask = null;
            _scheduler.FailAll(error);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            Interlocked.Exchange(ref _disposed, 1);
            _disposeTask ??= Task.Run(DisposeCoreAsync);
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        _shutdown.Cancel();
        try
        {
            if (_worker != null)
                await _worker.ConfigureAwait(false);
        }
        finally
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                _scheduler.StopAll();
                await _output.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
                _gate.Dispose();
                _shutdown.Dispose();
            }
        }
    }
}
