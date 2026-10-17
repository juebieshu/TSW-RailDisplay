using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RailDisplay.Core.Models.Audio;
using RailDisplay.Infrastructure.Audio;
using RailDisplay.Infrastructure.Controllers.Audio;
using RailDisplay.Infrastructure.Controllers.Audio.CTCS;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

internal static class AudioOutputChecks
{
    private static readonly SoundDefinition[] Sounds =
    [
        new("alarm", "alarm.wav"),
        new("loop", "loop.wav", SoundPlaybackMode.Loop),
        new("提示 A2", "low.wav"),
        new("broken", "broken.wav")
    ];

    public static async Task RunAsync()
    {
        var logs = new RecordingSink();
        using var logFactory = new SerilogLoggerFactory(new LoggerConfiguration().WriteTo.Sink(logs).CreateLogger(), dispose: true);
        var output = new FakeOutput();
        var otherOutput = new FakeOutput();
        await using var device = new DeviceAudioController(Sounds, output,
            logger: logFactory.CreateLogger<DeviceAudioController>());
        await using var otherDevice = new DeviceAudioController(Sounds, otherOutput);
        var low = device.PlayAsync("提示 A2");
        var other = otherDevice.PlayAsync("提示 A2");
        await UntilAsync(() => output.Started.Count == 1 && otherOutput.Started.Count == 1);
        var duplicate = device.PlayAsync("提示 A2");
        Require(ReferenceEquals(low, duplicate), "重复调用必须返回同一任务，不能累积异步等待者。");
        await device.SetVolumeAsync(0.25f);
        Require(device.Volume == 0.25f && output.Volume == 0.25f && otherDevice.Volume == 1,
            "设备音量必须立即更新且互不影响。");
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, -0.01f, 1.01f })
            await ThrowsAsync<ArgumentOutOfRangeException>(() => device.SetVolumeAsync(invalid));
        Require(device.Volume == 0.25f && !low.IsCompleted, "非法音量不能改变状态。");
        await device.SetVolumeAsync(0);
        Require(!low.IsCompleted, "静音不结束播放请求。");
        await device.SetVolumeAsync(1);

        output.LoadFailureName = "broken";
        await ThrowsAsync<InvalidDataException>(() => device.PlayAsync("broken"));
        Require(logs.Events.Any(e => e.Level == LogEventLevel.Error && e.Exception is InvalidDataException &&
            e.Properties["SoundName"] is ScalarValue { Value: "broken" }), "资源错误日志必须保留声音名称和原始异常。");
        Require(!low.IsCompleted && output.Started.Count == 1, "坏文件不能打断有效声音。");
        await ThrowsAsync<System.Collections.Generic.KeyNotFoundException>(() => device.PlayAsync("missing"));
        var high = device.PlayAsync("alarm");
        await UntilAsync(() => output.Started.Count == 2);
        Require(await low == SoundPlaybackResult.Interrupted && await duplicate == SoundPlaybackResult.Interrupted,
            "重复请求必须共享打断结果。");
        Require(!other.IsCompleted && otherOutput.Started.Count == 1, "抢占不能影响其他设备。");
        var queued = device.PlayAsync("提示 A2");
        await device.StopAsync("提示 A2");
        Require(await queued == SoundPlaybackResult.Stopped && !high.IsCompleted, "取消排队不能停止当前声音。");
        output.Complete();
        Require(await high.WaitAsync(TimeSpan.FromSeconds(3)) == SoundPlaybackResult.Completed,
            "实际完成通知必须结束单次请求。");

        var loop = device.PlayAsync("loop");
        await UntilAsync(() => output.Started.Count == 3);
        output.Complete();
        await UntilAsync(() => output.Started.Count == 4);
        Require(!loop.IsCompleted, "每轮结束不能结束循环请求。");
        await device.StopAllAsync();
        Require(await loop == SoundPlaybackResult.Stopped, "清空设备必须停止循环。");
        for (int i = 0; i < 20; i++)
        {
            int starts = output.Started.Count;
            var immediateStop = device.PlayAsync("loop");
            await device.StopAllAsync();
            Require(await immediateStop.WaitAsync(TimeSpan.FromSeconds(3)) == SoundPlaybackResult.Stopped,
                "先播放后立即停止必须保持调用顺序，不能重新开始声音。");
            Require(output.Started.Count == starts + 1, "停止后重新触发必须创建新请求，不能返回上一轮任务。");
        }
        var disposedRequest = otherDevice.PlayAsync("alarm");
        await UntilAsync(() => otherOutput.Started.Count == 2);
        Require(await other == SoundPlaybackResult.Interrupted, "另一设备也必须独立抢占。");
        await otherDevice.DisposeAsync();
        await otherDevice.DisposeAsync();
        Require(await disposedRequest == SoundPlaybackResult.Stopped && otherOutput.Disposed,
            "释放必须停止请求并支持重复释放。");
        await ThrowsAsync<ObjectDisposedException>(() => otherDevice.PlayAsync("alarm"));

        var failedOutput = new FakeOutput();
        await using var failed = new DeviceAudioController(Sounds, failedOutput,
            logger: logFactory.CreateLogger<DeviceAudioController>());
        var failing = failed.PlayAsync("alarm");
        await UntilAsync(() => failedOutput.Started.Count == 1);
        var pending = failed.PlayAsync("提示 A2");
        await failed.SetVolumeAsync(0.5f);
        failedOutput.Fail();
        await ThrowsAsync<IOException>(() => failing.WaitAsync(TimeSpan.FromSeconds(3)));
        await ThrowsAsync<IOException>(() => pending.WaitAsync(TimeSpan.FromSeconds(3)));
        await ThrowsAsync<InvalidOperationException>(() => failed.PlayAsync("alarm"));

        var startOutput = new FakeOutput { StartFailure = true };
        await using var startFailure = new DeviceAudioController(Sounds, startOutput,
            logger: logFactory.CreateLogger<DeviceAudioController>());
        await ThrowsAsync<IOException>(() => startFailure.PlayAsync("alarm"));
        Require(logs.Events.Count(e => e.Level == LogEventLevel.Error && e.Exception is IOException) == 2,
            "播放启动及播放期间故障必须各记录一次，并继续向调用方传播异常。");
        await using var unavailable = new DeviceAudioController(Sounds, new FakeOutput(false));
        Require(!unavailable.IsSupported, "不支持平台必须可识别。");
        await ThrowsAsync<PlatformNotSupportedException>(() => unavailable.PlayAsync("alarm"));
        await using var ctcs = new Ctcs2_200HAudioController();
        await ThrowsAsync<System.Collections.Generic.KeyNotFoundException>(() => ctcs.PlayAsync("S0"));

        await CheckWavAsync();
        Console.WriteLine("音频播放适配、音量、异常、释放与 WAV 加载检查通过（使用可控替身，无需声卡）。");
    }

    private sealed class RecordingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }

    private static async Task CheckWavAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "RailDisplayAudioChecks-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            WriteTestWav(Path.Combine(root, "test.wav"));
            await using (var output = new NAudioOutput(root))
            {
                output.Load(new SoundDefinition("测试音", "test.wav"));
                output.Load(new SoundDefinition("测试音", "test.wav"));
                await ThrowsAsync<FileNotFoundException>(() => Task.Run(() => output.Load(new("missing", "missing.wav"))));
                File.WriteAllText(Path.Combine(root, "bad.wav"), "invalid wav");
                await ThrowsAsync<FormatException>(() => Task.Run(() => output.Load(new("bad", "bad.wav"))));
                foreach (var path in new[] { "../outside.wav", "test.mp3", Path.Combine(root, "test.wav") })
                    await ThrowsAsync<ArgumentException>(() => Task.Run(() => NAudioOutput.ResolveResourcePath(root, path)));
            }
            File.Delete(Path.Combine(root, "test.wav"));
            Require(!File.Exists(Path.Combine(root, "test.wav")), "释放后必须关闭 WAV 文件。");
        }
        finally { Directory.Delete(root, true); }
    }

    public static async Task RunLiveAsync()
    {
        await using var output = new NAudioOutput();
        if (!output.IsSupported)
        {
            Console.WriteLine("当前平台暂不支持 NAudio 音频输出，未执行真实播放检查。");
            return;
        }
        string root = Path.Combine(Path.GetTempPath(), "RailDisplayAudioLive-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            WriteTestWav(Path.Combine(root, "test.wav"));
            var sounds = new[]
            {
                new SoundDefinition("测试音", "test.wav"),
                new SoundDefinition("测试循环", "test.wav", SoundPlaybackMode.Loop)
            };
            await using var first = new DeviceAudioController(sounds, new NAudioOutput(root));
            await using var second = new DeviceAudioController(sounds, new NAudioOutput(root));
            await first.SetVolumeAsync(0.2f);
            await second.SetVolumeAsync(0.2f);
            var results = await Task.WhenAll(first.PlayAsync("测试音"), second.PlayAsync("测试音"))
                .WaitAsync(TimeSpan.FromSeconds(10));
            Require(results.All(result => result == SoundPlaybackResult.Completed), "多设备真实播放必须完成。");
            var replay = first.PlayAsync("测试循环");
            await first.SetVolumeAsync(0);
            await first.StopAllAsync();
            Require(await replay.WaitAsync(TimeSpan.FromSeconds(10)) == SoundPlaybackResult.Stopped,
                "真实循环播放必须可停止。");
            Console.WriteLine("NAudio 真实 WAV 播放、多设备输出、音量和释放检查通过；请另行确认听感。");
        }
        finally { Directory.Delete(root, true); }
    }

    // 测试专用：生成 0.25 秒、440Hz、低振幅 PCM WAV，不是设备业务音频。
    private static void WriteTestWav(string path)
    {
        using var writer = new BinaryWriter(File.Create(path));
        int samples = 12000;
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + samples * 2);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(48000); writer.Write(96000); writer.Write((short)2); writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
        for (int i = 0; i < samples; i++)
            writer.Write((short)(2000 * Math.Sin(i * 2 * Math.PI * 440 / 48000)));
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(3)) throw new TimeoutException("音频适配检查超时。");
            await Task.Delay(5);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }

    private sealed class FakeOutput(bool supported = true) : IAudioOutput
    {
        private TaskCompletionSource? _completion;
        public bool IsSupported => supported;
        public ConcurrentQueue<string> Started { get; } = new();
        public float Volume { get; private set; } = 1;
        public bool Disposed { get; private set; }
        public string? LoadFailureName { get; set; }
        public bool StartFailure { get; init; }
        public void Load(SoundDefinition sound)
        {
            if (sound.Name == LoadFailureName) throw new InvalidDataException("test: bad WAV");
        }
        public Task StartAsync(string name, float volume)
        {
            if (StartFailure) throw new IOException("test: output initialization failed");
            Volume = volume;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _completion, completion);
            Started.Enqueue(name);
            return completion.Task;
        }
        public Task StopAsync()
        {
            Interlocked.Exchange(ref _completion, null)?.TrySetResult();
            return Task.CompletedTask;
        }
        public void SetVolume(float volume) => Volume = volume;
        public void Complete() => Volatile.Read(ref _completion)!.TrySetResult();
        public void Fail() => Volatile.Read(ref _completion)!.TrySetException(new IOException("test: output lost"));
        public async ValueTask DisposeAsync() { await StopAsync(); Disposed = true; }
    }
}
