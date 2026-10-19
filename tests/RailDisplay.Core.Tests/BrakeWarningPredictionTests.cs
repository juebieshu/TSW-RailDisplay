using System;
using RailDisplay.Core.Controllers.CTCS;
using RailDisplay.Core.Models.CTCS;

namespace RailDisplay.Core.Tests;

public sealed class BrakeWarningPredictionTests
{
    private static readonly CtcsData Input = new()
    {
        ControlMode = CtcsControlMode.FullSupervision,
        Speed = 0, PermittedSpeed = 30, SbiSpeed = 36, EbiSpeed = 36,
        MonitoringSection = SpeedMonitoringSection.CSM
    };

    private readonly CtcsController _controller = new();

    public BrakeWarningPredictionTests() => _controller.Update(Input);

    [Fact]
    public void Prediction_AveragesAvailableHistoryAndUsesLatestEbi()
    {
        _controller.AdvanceBrakeWarning(TimeSpan.Zero);
        Assert.True(_controller.Data.BrakeWarningTime is null, "One observation cannot provide acceleration.");
        Step(3.6, 1);
        Near(9); // 第 1 秒使用已有一段加速度，不等待历史凑满。
        Step(10.8, 1);
        Near(7 / 1.5);
        Step(18, 1);
        Near(3); // 历史未满时使用已有三段平均加速度 5/3。
        Step(25.2, 1);
        Near(3 / 1.75); // 当前及前四次为 0、1、3、5、7，四段平均加速度 1.75。
        _controller.Update(_controller.Data with { MonitoringSection = SpeedMonitoringSection.TSM, SbiSpeed = 32.4 });
        _controller.AdvanceBrakeWarning(TimeSpan.Zero);
        Near(3 / 1.75); // 改变 SBI 不改变 A1 的 EBI 阈值。
        _controller.Update(_controller.Data with { EbiSpeed = 72 });
        Step(32.4, 1);
        Near(5.5); // 使用最新 EBI=20m/s，历史保持滚动。
        _controller.Update(_controller.Data with { SbiSpeed = 54 });
        _controller.AdvanceBrakeWarning(TimeSpan.Zero);
        Near(5.5); // SBI 再次高于当前速度，也不改变 A1 阈值。

        var snapshot = _controller.Data;
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => _controller.AdvanceBrakeWarning(TimeSpan.FromSeconds(-1)));
        Assert.True(ReferenceEquals(snapshot, _controller.Data), "Invalid time changed state.");
        Assert.ThrowsAny<ArgumentException>(() => _controller.Update(_controller.Data with { Speed = double.NaN }));
        Assert.True(ReferenceEquals(snapshot, _controller.Data), "Invalid input changed state.");
        Step(39.6, 1);
        Near(4.5); // 非法输入不得破坏采样历史。
    }

    [Fact]
    public void Prediction_ReportsNoCrossingForZeroOrNegativeAverage()
    {
        Reset(36);
        Step(32.4, 1); Step(28.8, 1); Step(25.2, 1); Step(21.6, 1);
        Assert.True(_controller.Data.BrakeWarningTime == double.PositiveInfinity, "Deceleration below a fixed Vint must not predict a crossing.");
        Reset(18);
        for (int i = 0; i < 4; i++) Step(18, 1);
        Assert.True(_controller.Data.BrakeWarningTime == double.PositiveInfinity, "Constant speed below Vint has no predicted crossing.");
        Reset(0);
        Step(0, 1);
        Assert.True(_controller.Data.BrakeWarningTime == double.PositiveInfinity, "All available zero accelerations must average to zero.");
        Step(3.6, 1);
        Near(9);
        Step(3.6, 1);
        Near(9);
        Step(10.8, 1);
        Near(7 / 1.5); // 0、1、0、2 m/s²，只平均非零的 1、2。
        Step(10.8, 1); Step(10.8, 1); Step(10.8, 1);
        Near(3.5); // 仍有一段 2m/s²，其余零段不稀释平均值。
        Step(10.8, 1);
        Assert.True(_controller.Data.BrakeWarningTime == double.PositiveInfinity, "A full zero-acceleration window must average to zero.");
        Reset(0);
        Step(0, 1); Step(3.6, 1); Step(0, 1); Step(0, 1);
        Assert.True(_controller.Data.BrakeWarningTime == double.PositiveInfinity,
            "Nonzero positive and negative accelerations must both participate and may cancel out.");
    }

    [Fact]
    public void Prediction_RequiresEbiAndReachesZeroAtThreshold()
    {
        _controller.Update(_controller.Data with { EbiSpeed = null });
        Step(3.6, 1);
        Assert.True(_controller.Data.BrakeWarningTime is null, "Missing EBI below SBI must not fall back to SBI.");
        Reset(18);
        _controller.Update(_controller.Data with { EbiSpeed = 18, SbiSpeed = 18 });
        _controller.AdvanceBrakeWarning(TimeSpan.Zero);
        Near(0);
    }

    [Fact]
    public void Prediction_SamplesOncePerSecondUsingRealElapsedTime()
    {
        Step(1.8, .5);
        Assert.True(_controller.Data.BrakeWarningTime is null, "Subsecond updates must not become extra samples.");
        Step(3.6, .5);
        Near(9);
        Step(7.2, 1); Step(10.8, 1); Step(14.4, 1);
        Near(6);

        Reset(0);
        Step(5.4, 1.5); Step(19.8, 2);
        Step(23.4, 1); Step(30.6, 1);
        Near(1); // 四段加速度 1、2、1、2，算术平均 1.5；使用真实时间差。
        Reset(0);
        _controller.Update(_controller.Data with { EbiSpeed = 72 });
        Step(36, 10);
        Near(10); // 0→10m/s 用实际 10 秒计算一段加速度，不伪造遗漏的采样。
        Step(39.6, 1); Step(43.2, 1); Step(46.8, 1);
        Near(7); // 超过 SBI 后仍然使用 EBI=20m/s。
        _controller.Update(_controller.Data with { EbiSpeed = null });
        _controller.AdvanceBrakeWarning(TimeSpan.Zero);
        Assert.True(_controller.Data.BrakeWarningTime is null, "Missing EBI is unknown, not zero.");
    }

    [Fact]
    public void Prediction_RunsOnlyInFullSupervisionAndRestartsOnReentry()
    {
        Step(3.6, 1); Step(7.2, 1); Step(10.8, 1);
        foreach (var mode in Enum.GetValues<CtcsControlMode>())
        {
            if (mode == CtcsControlMode.FullSupervision) continue;
            _controller.Update(Input with { ControlMode = mode });
            _controller.AdvanceBrakeWarning(TimeSpan.FromSeconds(5));
            Assert.True(_controller.Data.BrakeWarningTime is null, "Only FS may advance local prediction.");
        }
        _controller.Update(Input);
        Step(3.6, 1);
        Near(9); // 重入后仅用新的第一段，不沿用旧历史。
        Step(7.2, 1);
        Step(10.8, 1);
        Near(7);
        Step(14.4, 1);
        Near(6);
    }

    private void Reset(double speed)
    {
        _controller.Update(Input with { ControlMode = CtcsControlMode.Standby });
        _controller.Update(Input with { Speed = speed });
    }

    private void Step(double speed, double seconds)
    {
        _controller.Update(_controller.Data with { Speed = speed });
        _controller.AdvanceBrakeWarning(TimeSpan.FromSeconds(seconds));
    }

    private void Near(double expected) =>
        Assert.True(_controller.Data.BrakeWarningTime is { } actual && Math.Abs(actual - expected) < 1e-9,
            $"Expected Tint {expected}, got {_controller.Data.BrakeWarningTime}.");
}
