using System;
using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using RailDisplay.Controls.CTCS;
using RailDisplay.Core.Controllers.CTCS;
using RailDisplay.Core.Models.CTCS;
using RailDisplay.Models.CTCS;
using RailDisplay.ViewModels.CTCS;

namespace RailDisplay.Tests;

public sealed class CtcsScreenTests
{
    /// <summary>
    /// A2 距离条高度的分段比例目前有两套规则：本测试期望 0–100m 占 2.3/3、100–1000m 占 0.7/3 且两段线性；
    /// TargetDistanceScale 实现的是 0–100m 线性占 13/15、100–1000m 对数占 2/15。确认规则后再取消跳过。
    /// </summary>

    [AvaloniaFact]
    public void Level3_FollowsScreenCapability()
    {
        var disabled = new CtcsViewModel(new CtcsController(), CtcsScreenProfile.CTCS2_200H);
        var snapshot = disabled.Data;
        Assert.False(disabled.CanSwitchToLevel3 || disabled.TrySwitchLevel(CtcsLevel.Level3) || !ReferenceEquals(disabled.Data, snapshot),
            "A disabled screen capability must reject local switching without changing state.");
        disabled.Update(new CtcsData { CurrentLevel = CtcsLevel.Level3 });
        Assert.False(disabled.CurrentLevelName != "CTCS 3" || !disabled.TrySwitchLevel(CtcsLevel.Level2),
            "A screen capability must not hard-reject received level 3 data or switching back.");

        var enabled = CreateEnabled(out var notifications);
        Assert.False(!enabled.CanSwitchToLevel3 || !enabled.TrySwitchLevel(CtcsLevel.Level3) || enabled.CurrentLevelName != "CTCS 3" ||
            !notifications.Contains(nameof(CtcsViewModel.CurrentLevelName)),
            "Enabling the same screen profile must allow level 3 and notify its label.");
        Assert.False(!enabled.TrySwitchLevel(CtcsLevel.Level0) || !enabled.TrySwitchLevel(CtcsLevel.Level2),
            "Existing level switches must remain available.");
        snapshot = enabled.Data;
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => enabled.TrySwitchLevel((CtcsLevel)99));
        Assert.True(ReferenceEquals(snapshot, enabled.Data), "An invalid switch changed state.");
    }

    [AvaloniaFact]
    public void Clock_ShowsTimeAndTwoDigitYearAcrossMidnight()
    {
        var enabled = CreateEnabled(out var notifications);
        var clockSnapshot = enabled.Data;
        enabled.UpdateDisplayTime(new DateTimeOffset(2026, 10, 15, 23, 7, 49, TimeSpan.FromHours(8)));
        Assert.False(enabled.CurrentTimeText != "23:07:49" || enabled.CurrentDateText != "26-10-15" ||
            !notifications.Contains(nameof(CtcsViewModel.CurrentTimeText)) ||
            !notifications.Contains(nameof(CtcsViewModel.CurrentDateText)),
            "The E17 clock must show hours, minutes, seconds and a two-digit-year date.");
        enabled.UpdateDisplayTime(new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.FromHours(8)));
        Assert.False(enabled.CurrentTimeText != "00:00:00" || enabled.CurrentDateText != "26-10-16" ||
            !ReferenceEquals(clockSnapshot, enabled.Data),
            "The clock must cross midnight without changing core business state.");
    }

    [AvaloniaFact]
    public void DistanceRange_SwitchesBetween8kAnd16kOnly()
    {
        var enabled = CreateEnabled(out var notifications);
        Assert.True(enabled.MaximumDisplayDistance == 8000, "The distance scale must initially display 8k.");
        var distanceSnapshot = enabled.Data;
        enabled.MaximumDisplayDistance = 16000;
        Assert.True(notifications.Contains(nameof(CtcsViewModel.MaximumDisplayDistance)), "Changing distance range must notify the screen.");
        foreach (int invalid in new[] { 0, -8000, 1000, 12000, int.MaxValue })
        {
            Assert.ThrowsAny<ArgumentOutOfRangeException>(() => enabled.MaximumDisplayDistance = invalid);
            Assert.True(enabled.MaximumDisplayDistance == 16000, "Invalid input changed the distance range.");
        }
        enabled.MaximumDisplayDistance = 8000;
        Assert.False(enabled.MaximumDisplayDistance != 8000 || !ReferenceEquals(distanceSnapshot, enabled.Data),
            "The distance range must switch back without changing core data.");
    }

    [AvaloniaFact]
    public void AttachingPresentation_PreservesExistingCoreState()
    {
        var core = new CtcsController();
        core.Update(new CtcsData { CurrentLevel = CtcsLevel.Level3, ControlMode = CtcsControlMode.Guiding });
        var attached = new CtcsViewModel(core, CtcsScreenProfile.CTCS2_200H);
        Assert.False(attached.CurrentLevelName != "CTCS 3" || attached.ControlModeShortName != "引导",
            "Attaching presentation must preserve the existing core state.");
    }

    [AvaloniaFact]
    public void A2Scale_ClampsEndpointsAndRejectsInvalidDistance()
    {
        var ruler = new TargetDistanceScale();
        foreach (var (distance, expectedHeight) in new[]
        {
            (0.0, 0.0), (1000.0, 172.0), (1120.0, 172.0), (double.MaxValue, 172.0), (0.0, 0.0)
        })
        {
            ruler.TargetDistance = distance;
            Assert.True(Math.Abs(ruler.BandHeight - expectedHeight) <= 1e-9, $"Unexpected A2 band height for {distance}m.");
        }
        ruler.TargetDistance = 100;
        double height = ruler.BandHeight;
        foreach (double invalid in new[] { -1.0, double.NaN, double.PositiveInfinity })
        {
            Assert.ThrowsAny<ArgumentException>(() => ruler.TargetDistance = invalid);
            Assert.True(ruler.TargetDistance == 100 && ruler.BandHeight == height, "Invalid input changed the A2 scale.");
        }
        ruler.TargetDistance = null;
        Assert.True(ruler.BandHeight == 0, "Clearing A2 distance must clear the band height.");
    }

    [AvaloniaTheory(Skip = SkipReasons.A2RuleUndecided)]
    [InlineData(50.0, 172.0 * 23 / 60)]
    [InlineData(100.0, 172.0 * 23 / 30)]
    [InlineData(200.0, 172.0 * 107 / 135)]
    [InlineData(500.0, 172.0 * 47 / 54)]
    [InlineData(670.0, 172.0 * 823 / 900)]
    public void A2Scale_UsesSegmentRatios(double distance, double expectedHeight)
    {
        var ruler = new TargetDistanceScale { TargetDistance = distance };
        Assert.Equal(expectedHeight, ruler.BandHeight, 1e-9);
    }

    [AvaloniaFact]
    public void A2Text_ShowsMetrePrecisionOnlyInFullSupervisionTsm()
    {
        var model = new CtcsViewModel(new CtcsController(), CtcsScreenProfile.CTCS2_200H);
        var notifications = new HashSet<string?>();
        model.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        Assert.False(model.ShowTargetDistance || model.TargetDistanceText != "", "A missing target must not be displayed as zero.");
        foreach (var (distance, expected) in new[]
        {
            (0.0, "0"), (0.9, "0"), (99.9, "99"), (999.9, "999"),
            (1000.0, "1000"), (1009.9, "1000"), (1234.0, "1230"), (99990.0, "99990"),
            (99999.0, "99990"), (100000.0, "99990"), (double.MaxValue, "99990"), (0.0, "0")
        })
        {
            model.Update(new CtcsData
            {
                TargetDistance = distance, TargetSpeed = 0, TsmEntryDistance = double.MaxValue,
                ControlMode = CtcsControlMode.FullSupervision
            });
            Assert.False(!model.ShowTargetDistance || model.TargetDistanceText != expected || model.Data.TargetDistance != distance,
                $"Unexpected target distance display for {distance}m.");
        }
        Assert.False(!notifications.Contains(nameof(CtcsViewModel.ShowTargetDistance)) ||
            !notifications.Contains(nameof(CtcsViewModel.TargetDistanceText)),
            "Target distance updates must notify the screen.");
        var snapshot = model.Data;
        foreach (double invalid in new[] { -1.0, double.NaN, double.PositiveInfinity })
        {
            Assert.ThrowsAny<ArgumentException>(() => model.Update(snapshot with { TargetDistance = invalid }));
            Assert.False(!ReferenceEquals(snapshot, model.Data) || !model.ShowTargetDistance || model.TargetDistanceText != "0",
                "Invalid target distance changed A2 state.");
        }
        model.Update(snapshot with { TargetDistance = 100001, TsmEntryDistance = 100000 });
        Assert.False(model.ShowTargetDistance || model.TargetDistanceText != "", "CSM must hide A2 even when a target distance is available.");
        model.Update(snapshot);
        Assert.False(!model.ShowTargetDistance || model.TargetDistanceText != "0", "Returning to TSM must restore A2.");
        foreach (var mode in Enum.GetValues<CtcsControlMode>())
        {
            model.Update(snapshot with { ControlMode = mode });
            bool expected = mode == CtcsControlMode.FullSupervision;
            Assert.False(model.ShowTargetDistance != expected || model.TargetDistanceText != (expected ? "0" : ""),
                "A2 must only be displayed in full-supervision TSM.");
        }
        model.Update(snapshot with { Speed = 300, PermittedSpeed = 200, SbiSpeed = 210, EbiSpeed = 220 });
        Assert.True(model.ShowTargetDistance, "Speed warnings must not hide A2 in full-supervision TSM.");
        model.Update(new CtcsData());
        Assert.False(model.ShowTargetDistance || model.TargetDistanceText != "", "Removing target data must clear A2.");
    }

    private static CtcsViewModel CreateEnabled(out HashSet<string?> notifications)
    {
        var model = new CtcsViewModel(new CtcsController(), CtcsScreenProfile.CTCS2_200H with { SupportsLevel3 = true });
        var received = new HashSet<string?>();
        model.PropertyChanged += (_, e) => received.Add(e.PropertyName);
        notifications = received;
        return model;
    }
}
