using System.Collections.ObjectModel;
using RailDisplay.Core.Models.Audio;

namespace RailDisplay.Core.Controllers.Audio;

/// <summary>单设备调度；由调用方串行使用，时间为从会话开始累计的单调时间。</summary>
public sealed class AudioScheduler
{
    internal enum RequestState { Ready, Waiting, Playing }
    public sealed class Request
    {
        internal Request(SoundDefinition sound, int priority)
        {
            Sound = sound;
            Priority = priority;
        }

        public SoundDefinition Sound { get; }
        public Task<SoundPlaybackResult> Completion => Source.Task;
        internal int Priority { get; }
        internal RequestState State { get; set; }
        internal TaskCompletionSource<SoundPlaybackResult> Source { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed record Playback(long Id, Request Request);

    private readonly Dictionary<string, int> _priorities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Request> _requests = new(StringComparer.Ordinal);
    private readonly PriorityQueue<Request, int> _ready = new();
    private readonly PriorityQueue<Request, TimeSpan> _waiting = new();
    private TimeSpan _time;
    private long _playbackId;

    public IReadOnlyList<SoundDefinition> Sounds { get; }
    public Playback? Current { get; private set; }
    public int ActiveCount => _requests.Count;

    public AudioScheduler(IEnumerable<SoundDefinition> sounds)
    {
        ArgumentNullException.ThrowIfNull(sounds);
        var copy = sounds.ToArray();
        for (int i = 0; i < copy.Length; i++)
        {
            var sound = copy[i];
            ArgumentNullException.ThrowIfNull(sound);
            ArgumentException.ThrowIfNullOrWhiteSpace(sound.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(sound.ResourcePath);
            if (!Enum.IsDefined(sound.Mode))
                throw new ArgumentOutOfRangeException(nameof(sounds), "播放方式无效。");
            if (sound.Mode == SoundPlaybackMode.Interval
                ? sound.RepeatInterval <= TimeSpan.Zero
                : sound.RepeatInterval != TimeSpan.Zero)
                throw new ArgumentException("只有间隔重复使用正数 RepeatInterval。", nameof(sounds));
            if (!_priorities.TryAdd(sound.Name, i))
                throw new ArgumentException($"设备内音频名称重复：{sound.Name}", nameof(sounds));
        }
        Sounds = new ReadOnlyCollection<SoundDefinition>(copy);
    }

    public SoundDefinition GetSound(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_priorities.TryGetValue(name, out var index))
            throw new KeyNotFoundException($"设备未配置音频：{name}");
        return Sounds[index];
    }

    public Request Play(string name)
    {
        var sound = GetSound(name);
        // TODO shortcut: 同名有效请求暂时忽略重复触发，用户确认重复请求规则后替换。
        if (_requests.TryGetValue(name, out var existing))
            return existing;
        var request = new Request(sound, _priorities[name]);
        _requests.Add(name, request);
        _ready.Enqueue(request, request.Priority);
        return request;
    }

    public void Advance(TimeSpan now)
    {
        ValidateTime(now);
        _time = now;
        while (_waiting.TryPeek(out var request, out var due) && due <= now)
        {
            _waiting.Dequeue();
            request.State = RequestState.Ready;
            _ready.Enqueue(request, request.Priority);
        }
        if (!_ready.TryPeek(out _, out var priority))
            return;
        if (Current is { } current)
        {
            if (priority >= current.Request.Priority)
                return;
            // TODO shortcut: 被打断请求（含循环和间隔重复）暂时全部结束，用户确认恢复规则后替换。
            Finish(current.Request, SoundPlaybackResult.Interrupted);
        }
        var selected = _ready.Dequeue();
        selected.State = RequestState.Playing;
        Current = new Playback(++_playbackId, selected);
    }

    public void Complete(long playbackId, TimeSpan now)
    {
        ValidateTime(now);
        if (Current is not { } current || current.Id != playbackId)
            return;
        var request = current.Request;
        if (request.Sound.Mode == SoundPlaybackMode.Interval &&
            now > TimeSpan.MaxValue - request.Sound.RepeatInterval)
            throw new ArgumentOutOfRangeException(nameof(now), "重复截止时间超出范围。");
        _time = now;
        Current = null;
        switch (request.Sound.Mode)
        {
            case SoundPlaybackMode.Once:
                Finish(request, SoundPlaybackResult.Completed);
                break;
            case SoundPlaybackMode.Loop:
                request.State = RequestState.Ready;
                _ready.Enqueue(request, request.Priority);
                break;
            case SoundPlaybackMode.Interval:
                request.State = RequestState.Waiting;
                _waiting.Enqueue(request, now + request.Sound.RepeatInterval);
                break;
        }
    }

    public void Stop(string name)
    {
        GetSound(name);
        if (_requests.TryGetValue(name, out var request))
            Finish(request, SoundPlaybackResult.Stopped);
    }

    public void StopAll()
    {
        foreach (var request in _requests.Values)
            request.Source.TrySetResult(SoundPlaybackResult.Stopped);
        Clear();
    }

    public void FailAll(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        foreach (var request in _requests.Values)
            request.Source.TrySetException(exception);
        Clear();
    }

    private void Finish(Request request, SoundPlaybackResult result)
    {
        _requests.Remove(request.Sound.Name);
        // 实际移除，避免反复停止/触发累积失效队列条目；停止的移除操作为 O(n)。
        if (request.State == RequestState.Ready)
            _ready.Remove(request, out _, out _);
        else if (request.State == RequestState.Waiting)
            _waiting.Remove(request, out _, out _);
        if (Current?.Request == request)
            Current = null;
        request.Source.TrySetResult(result);
    }

    private void Clear()
    {
        Current = null;
        _requests.Clear();
        _ready.Clear();
        _waiting.Clear();
    }

    private void ValidateTime(TimeSpan now)
    {
        if (now < _time)
            throw new ArgumentOutOfRangeException(nameof(now), "调度时间不能倒退。");
    }
}
