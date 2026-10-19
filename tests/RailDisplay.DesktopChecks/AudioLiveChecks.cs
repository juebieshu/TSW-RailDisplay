using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RailDisplay.Core.Models.Audio;
using RailDisplay.Infrastructure.Audio;
using RailDisplay.Infrastructure.Controllers.Audio;

namespace RailDisplay.DesktopChecks;

internal static class AudioLiveChecks
{
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
            Assert.True(results.All(result => result == SoundPlaybackResult.Completed), "多设备真实播放必须完成。");
            var replay = first.PlayAsync("测试循环");
            await first.SetVolumeAsync(0);
            await first.StopAllAsync();
            Assert.True(await replay.WaitAsync(TimeSpan.FromSeconds(10)) == SoundPlaybackResult.Stopped,
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
}
