using System;
using Avalonia.Headless.XUnit;
using RailDisplay.Core.Controllers.CTCS;
using RailDisplay.Core.Models.CTCS;
using RailDisplay.Models.CTCS;
using RailDisplay.ViewModels.CTCS;

namespace RailDisplay.Tests;

public sealed class BrakeWarningTests
{
    [AvaloniaFact]
    public void A1Warning_FollowsTimeSpeedSectionAndMode()
    {
        var model = new CtcsViewModel(new CtcsController(), CtcsScreenProfile.CTCS2_200H);
        var data = new CtcsData
        {
            Speed = 150, PermittedSpeed = 160, SbiSpeed = 165, EbiSpeed = 175,
            MonitoringSection = SpeedMonitoringSection.TSM, ControlMode = CtcsControlMode.FullSupervision,
            BrakeWarningTime = 10
        };
        foreach (var (time, size) in new[] { (10d, 5d), (8d, 5d), (7.999, 27d), (4.001, 27d), (4d, 41d), (0.001, 41d), (0d, 54d) })
            Check(data with { BrakeWarningTime = time }, size, BrakeWarningColor.Yellow);
        Check(data with { MonitoringSection = SpeedMonitoringSection.CSM }, 0);
        Check(data with { MonitoringSection = SpeedMonitoringSection.CSM, BrakeWarningTime = 8 }, 5, BrakeWarningColor.Gray);
        foreach (var speed in new[] { 160.001, 164.999, 165, 168, 174.999 })
            Check(data with { Speed = speed, BrakeWarningTime = 3 }, 41, BrakeWarningColor.Orange);
        Check(data with { Speed = 160, BrakeWarningTime = 3 }, 41, BrakeWarningColor.Yellow);
        Check(data with { Speed = 175, BrakeWarningTime = 3 }, 54, BrakeWarningColor.Red);
        Check(data with { Speed = 176, BrakeWarningTime = 3 }, 54, BrakeWarningColor.Red);
        var rsm = data with { MonitoringSection = SpeedMonitoringSection.RSM, ReleaseSpeed = 40, BrakeWarningTime = 3 };
        Check(rsm with { Speed = 39.999 }, 41, BrakeWarningColor.Yellow);
        Check(rsm with { Speed = 40 }, 54, BrakeWarningColor.Red);
        Check(rsm with { Speed = 41 }, 54, BrakeWarningColor.Red);
        Check(rsm with { BrakeWarningTime = 8 }, 0);
        Check(rsm with { BrakeWarningTime = 10 }, 0);
        Check(data with { BrakeWarningTime = null }, 0);
        Check(data with { BrakeWarningTime = double.PositiveInfinity }, 5, BrakeWarningColor.Yellow);
        Check(data with { BrakeWarningTime = double.PositiveInfinity, MonitoringSection = SpeedMonitoringSection.CSM }, 0);
        Check(data with { SbiSpeed = null, EbiSpeed = null }, 0);
        Check(data with { EbiSpeed = null }, 0);
        Check(data with { SbiSpeed = null }, 5, BrakeWarningColor.Yellow);
        foreach (var mode in Enum.GetValues<CtcsControlMode>())
            foreach (bool curve in new[] { false, true })
                Check(data with { ControlMode = mode, HasContinuousControlCurve = curve },
                    mode == CtcsControlMode.FullSupervision || mode == CtcsControlMode.Guiding && curve ? 5 : 0);
        Check(data, 5, BrakeWarningColor.Yellow);
        Check(data with { BrakeWarningTime = null }, 0);

        void Check(CtcsData input, double size, BrakeWarningColor? color = null)
        {
            model.Update(input);
            Assert.False(model.BrakeWarning.Size != size || color is { } expected && model.BrakeWarning.Color != expected,
                $"Unexpected A1 warning for {input}: {model.BrakeWarning}.");
        }
    }
}
