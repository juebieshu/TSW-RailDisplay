using System;
using System.Collections.Generic;
using RailDisplay.Core.Controllers.Audio;
using RailDisplay.Core.Models.Audio;

internal static class AudioCoreChecks
{
    public static void Run()
    {
        var definitions = new[]
        {
            new SoundDefinition("警报 A1", "high.wav"),
            new SoundDefinition("Repeat", "repeat.wav", SoundPlaybackMode.Interval, TimeSpan.FromSeconds(3)),
            new SoundDefinition("Loop", "loop.wav", SoundPlaybackMode.Loop),
            new SoundDefinition("low", "low.wav")
        };
        var scheduler = new AudioScheduler(definitions);
        definitions[0] = definitions[0] with { Name = "changed" };
        Require(scheduler.GetSound("警报 A1").Name == "警报 A1", "配置必须复制且允许任意名称。");
        Throws<KeyNotFoundException>(() => scheduler.Play("LOW"));
        Require(scheduler.ActiveCount == 0, "未知名称不能留下请求。");

        var low = scheduler.Play("low");
        scheduler.Advance(TimeSpan.Zero);
        long oldId = scheduler.Current!.Id;
        Require(ReferenceEquals(low, scheduler.Play("low")), "有效同名请求必须去重。");
        var high = scheduler.Play("警报 A1");
        scheduler.Advance(TimeSpan.Zero);
        Require(low.Completion.Result == SoundPlaybackResult.Interrupted &&
            scheduler.Current!.Request == high, "高优先级必须立即打断。");
        scheduler.Complete(oldId, TimeSpan.Zero);
        Require(scheduler.Current!.Request == high, "旧播放完成通知不能结束新声音。");
        var queued = scheduler.Play("low");
        scheduler.Advance(TimeSpan.Zero);
        Require(!queued.Completion.IsCompleted, "低优先级必须等待。");
        scheduler.Complete(scheduler.Current!.Id, TimeSpan.FromSeconds(1));
        scheduler.Advance(TimeSpan.FromSeconds(1));
        Require(high.Completion.Result == SoundPlaybackResult.Completed &&
            scheduler.Current!.Request == queued, "单次完成后必须调度等待声音。");
        scheduler.Stop("low");
        Require(queued.Completion.Result == SoundPlaybackResult.Stopped, "停止必须结束播放请求。");

        var repeat = scheduler.Play("Repeat");
        scheduler.Advance(TimeSpan.FromSeconds(1));
        scheduler.Complete(scheduler.Current!.Id, TimeSpan.FromSeconds(2));
        scheduler.Advance(TimeSpan.FromSeconds(2));
        Require(scheduler.Current == null && !repeat.Completion.IsCompleted, "重复间隔必须让出播放位置。");
        var waitingLow = scheduler.Play("low");
        scheduler.Advance(TimeSpan.FromSeconds(2));
        scheduler.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromTicks(1));
        Require(scheduler.Current!.Request == waitingLow, "重复间隔不得提前到期。");
        scheduler.Advance(TimeSpan.FromSeconds(5));
        Require(scheduler.Current!.Request == repeat &&
            waitingLow.Completion.Result == SoundPlaybackResult.Interrupted, "重复到期必须重新比较优先级。");
        scheduler.Complete(scheduler.Current!.Id, TimeSpan.FromSeconds(6));
        scheduler.Stop("Repeat");
        scheduler.Advance(TimeSpan.FromSeconds(20));
        Require(repeat.Completion.Result == SoundPlaybackResult.Stopped && scheduler.Current == null,
            "停止必须取消重复计时。");

        var loop = scheduler.Play("Loop");
        scheduler.Advance(TimeSpan.FromSeconds(20));
        long loopId = scheduler.Current!.Id;
        scheduler.Complete(loopId, TimeSpan.FromSeconds(21));
        Require(scheduler.Current == null && !loop.Completion.IsCompleted, "循环每轮必须重新参与调度。");
        scheduler.Advance(TimeSpan.FromSeconds(21));
        Require(scheduler.Current!.Id != loopId && scheduler.Current.Request == loop, "循环必须从新一轮播放。");
        var higher = scheduler.Play("警报 A1");
        scheduler.Advance(TimeSpan.FromSeconds(21));
        Require(loop.Completion.Result == SoundPlaybackResult.Interrupted, "暂定被打断循环彻底结束。");
        var stoppedQueue = scheduler.Play("low");
        scheduler.Stop("low");
        scheduler.StopAll();
        scheduler.Advance(TimeSpan.FromSeconds(22));
        Require(higher.Completion.Result == SoundPlaybackResult.Stopped &&
            stoppedQueue.Completion.Result == SoundPlaybackResult.Stopped && scheduler.ActiveCount == 0,
            "清空必须结束播放和排队请求。");

        var current = scheduler.Play("low");
        scheduler.Advance(TimeSpan.FromSeconds(22));
        var snapshot = scheduler.Current;
        Throws<ArgumentOutOfRangeException>(() => scheduler.Advance(TimeSpan.FromSeconds(21)));
        Require(scheduler.Current == snapshot && scheduler.ActiveCount == 1, "非法时间不能半更新状态。");
        scheduler.FailAll(new InvalidOperationException("output lost"));
        Throws<InvalidOperationException>(() => current.Completion.GetAwaiter().GetResult());
        Require(scheduler.ActiveCount == 0 && scheduler.Current == null, "输出失败必须清空请求。");

        var isolated = new AudioScheduler(scheduler.Sounds);
        var other = isolated.Play("low");
        isolated.Advance(TimeSpan.Zero);
        scheduler.StopAll();
        Require(!other.Completion.IsCompleted, "设备状态必须隔离。");
        isolated.StopAll();
        for (int i = 0; i < 1000; i++)
        {
            var request = scheduler.Play("low");
            scheduler.Stop("low");
            Require(request.Completion.Result == SoundPlaybackResult.Stopped, "反复取消不能遗漏任务。");
        }
        scheduler.Advance(TimeSpan.FromSeconds(22));
        Require(scheduler.Current == null && scheduler.ActiveCount == 0, "取消队列不能残留声音。");

        Throws<ArgumentException>(() => new AudioScheduler(new[] { scheduler.Sounds[0], scheduler.Sounds[0] }));
        foreach (var invalid in new[]
        {
            new SoundDefinition(" ", "a.wav"), new SoundDefinition("a", ""),
            new SoundDefinition("a", "a.wav", (SoundPlaybackMode)99),
            new SoundDefinition("a", "a.wav", SoundPlaybackMode.Interval),
            new SoundDefinition("a", "a.wav", SoundPlaybackMode.Interval, TimeSpan.FromSeconds(-1)),
            new SoundDefinition("a", "a.wav", SoundPlaybackMode.Once, TimeSpan.FromSeconds(1))
        })
            Throws<ArgumentException>(() => new AudioScheduler(new[] { invalid }));
        var overflow = new AudioScheduler(new[] { new SoundDefinition("r", "r.wav", SoundPlaybackMode.Interval, TimeSpan.MaxValue) });
        var overflowRequest = overflow.Play("r");
        overflow.Advance(TimeSpan.Zero);
        var overflowSnapshot = overflow.Current;
        Throws<ArgumentOutOfRangeException>(() => overflow.Complete(overflowSnapshot!.Id, TimeSpan.FromTicks(1)));
        Require(overflow.Current == overflowSnapshot && !overflowRequest.Completion.IsCompleted,
            "时间溢出必须保留上一状态。");
        overflow.StopAll();
        Console.WriteLine("音频核心配置、抢占、重复、停止、异常和设备隔离检查通过。");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
