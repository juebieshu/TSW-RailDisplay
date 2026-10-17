using RailDisplay.Core.Models.Audio;
using RailDisplay.Infrastructure.Audio;
using Microsoft.Extensions.Logging;

namespace RailDisplay.Infrastructure.Controllers.Audio.CTCS;

public sealed class Ctcs2_200HAudioController : DeviceAudioController
{
    // 按优先级从高到低添加真实音频；名称由设备定义，不限于 S0 / A1。
    // 音频尚未提供，保持为空，不将示例名称或排序当作业务规则。
    private static readonly SoundDefinition[] Sounds =
    [
        // 以下仅为配置写法示例，名称、文件、顺序和 3 秒间隔都不是已确认的设备规则。
        // 使用时先放入真实 WAV，再替换名称/路径并取消对应行的注释；列表越靠前优先级越高。

        // 单次播放：播放一遍后结束请求。名字可以自行更换，不需要新增枚举。
        // new("单次示例", "Assets/Audio/CTCS2_200H/once.wav", SoundPlaybackMode.Once),

        // 持续循环：每轮结束后重新参与调度，直到主动停止或被更高优先级打断。
        // new("循环示例", "Assets/Audio/CTCS2_200H/loop.wav", SoundPlaybackMode.Loop),

        // 间隔重复：每轮播完后等待 3 秒，等待期间允许其他声音播放。
        // new("间隔示例", "Assets/Audio/CTCS2_200H/repeat.wav", SoundPlaybackMode.Interval, TimeSpan.FromSeconds(3)),
    ];

    // 调用示例：使用 App 已经注入的接口，不在控件中重新创建控制器。
    // await viewModel.CtcsAudio.PlayAsync("单次示例");
    // var playback = viewModel.CtcsAudio.PlayAsync("循环示例"); // 也可换成“间隔示例”。
    // await viewModel.CtcsAudio.SetVolumeAsync(0.5f); // 设备总音量 50%，不改变系统音量。
    // await viewModel.CtcsAudio.StopAsync("循环示例"); // 同时取消该名称的排队和重复计时。
    // var result = await playback; // 停止后再等待循环任务；播放失败会在这里抛出异常。
    // await viewModel.CtcsAudio.StopAllAsync(); // 停止并清空本设备全部声音。

    public Ctcs2_200HAudioController(ILogger<Ctcs2_200HAudioController>? logger = null)
        : base(Sounds, new NAudioOutput(), logger: logger)
    {
    }
}
