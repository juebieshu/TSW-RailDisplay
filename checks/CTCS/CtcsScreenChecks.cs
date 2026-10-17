using System;
using System.Collections.Generic;
using RailDisplay.Core.Controllers.CTCS;
using RailDisplay.Core.Models.CTCS;
using RailDisplay.Models.CTCS;
using RailDisplay.ViewModels.CTCS;
using RailDisplay.Controls.CTCS;

internal static class CtcsScreenChecks
{
    public static void Run()
    {
        CheckTargetDistance();
        var disabled = new CtcsViewModel(new CtcsController(), CtcsScreenProfile.CTCS2_200H);
        var snapshot = disabled.Data;
        if (disabled.CanSwitchToLevel3 || disabled.TrySwitchLevel(CtcsLevel.Level3) || !ReferenceEquals(disabled.Data, snapshot))
            throw new Exception("A disabled screen capability must reject local switching without changing state.");

        disabled.Update(new CtcsData { CurrentLevel = CtcsLevel.Level3 });
        if (disabled.CurrentLevelName != "CTCS 3" || !disabled.TrySwitchLevel(CtcsLevel.Level2))
            throw new Exception("A screen capability must not hard-reject received level 3 data or switching back.");

        var enabled = new CtcsViewModel(new CtcsController(), CtcsScreenProfile.CTCS2_200H with { SupportsLevel3 = true });
        var notifications = new HashSet<string?>();
        enabled.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        var clockSnapshot = enabled.Data;
        enabled.UpdateDisplayTime(new DateTimeOffset(2026, 10, 15, 23, 7, 49, TimeSpan.FromHours(8)));
        if (enabled.CurrentTimeText != "23:07:49" || enabled.CurrentDateText != "26-10-15" ||
            !notifications.Contains(nameof(CtcsViewModel.CurrentTimeText)) ||
            !notifications.Contains(nameof(CtcsViewModel.CurrentDateText)))
            throw new Exception("The E17 clock must show hours, minutes, seconds and a two-digit-year date.");
        enabled.UpdateDisplayTime(new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.FromHours(8)));
        if (enabled.CurrentTimeText != "00:00:00" || enabled.CurrentDateText != "26-10-16" ||
            !ReferenceEquals(clockSnapshot, enabled.Data))
            throw new Exception("The clock must cross midnight without changing core business state.");
        if (enabled.MaximumDisplayDistance != 8000)
            throw new Exception("The distance scale must initially display 8k.");
        var distanceSnapshot = enabled.Data;
        enabled.MaximumDisplayDistance = 16000;
        if (!notifications.Contains(nameof(CtcsViewModel.MaximumDisplayDistance)))
            throw new Exception("Changing distance range must notify the screen.");
        foreach (int invalid in new[] { 0, -8000, 1000, 12000, int.MaxValue })
        {
            try
            {
                enabled.MaximumDisplayDistance = invalid;
                throw new Exception("An invalid distance range was accepted.");
            }
            catch (ArgumentOutOfRangeException)
            {
                if (enabled.MaximumDisplayDistance != 16000)
                    throw new Exception("Invalid input changed the distance range.");
            }
        }
        enabled.MaximumDisplayDistance = 8000;
        if (enabled.MaximumDisplayDistance != 8000 || !ReferenceEquals(distanceSnapshot, enabled.Data))
            throw new Exception("The distance range must switch back without changing core data.");
        if (!enabled.CanSwitchToLevel3 || !enabled.TrySwitchLevel(CtcsLevel.Level3) || enabled.CurrentLevelName != "CTCS 3" ||
            !notifications.Contains(nameof(CtcsViewModel.CurrentLevelName)))
            throw new Exception("Enabling the same screen profile must allow level 3 and notify its label.");
        if (!enabled.TrySwitchLevel(CtcsLevel.Level0) || !enabled.TrySwitchLevel(CtcsLevel.Level2))
            throw new Exception("Existing level switches must remain available.");

        snapshot = enabled.Data;
        try
        {
            enabled.TrySwitchLevel((CtcsLevel)99);
            throw new Exception("An undefined level was accepted.");
        }
        catch (ArgumentOutOfRangeException)
        {
            if (!ReferenceEquals(snapshot, enabled.Data))
                throw new Exception("An invalid switch changed state.");
        }

        var core = new CtcsController();
        core.Update(new CtcsData { CurrentLevel = CtcsLevel.Level3, ControlMode = CtcsControlMode.Guiding });
        var attached = new CtcsViewModel(core, CtcsScreenProfile.CTCS2_200H);
        if (attached.CurrentLevelName != "CTCS 3" || attached.ControlModeShortName != "引导")
            throw new Exception("Attaching presentation must preserve the existing core state.");
        Console.WriteLine("CTCS screen capability and level-switch checks passed.");
    }

    private static void CheckTargetDistance()
    {
        var ruler = new TargetDistanceScale();
        foreach (var (distance, expectedHeight) in new[]
        {
            (0.0, 0.0), (50.0, 172.0 * 23 / 60), (100.0, 172.0 * 23 / 30),
            (200.0, 172.0 * 107 / 135), (500.0, 172.0 * 47 / 54),
            (670.0, 172.0 * 823 / 900),
            (1000.0, 172.0), (1120.0, 172.0), (double.MaxValue, 172.0), (0.0, 0.0)
        })
        {
            ruler.TargetDistance = distance;
            if (Math.Abs(ruler.BandHeight - expectedHeight) > 1e-9)
                throw new Exception($"Unexpected A2 band height for {distance}m.");
        }
        ruler.TargetDistance = 100;
        foreach (double invalid in new[] { -1.0, double.NaN, double.PositiveInfinity })
        {
            try
            {
                ruler.TargetDistance = invalid;
                throw new Exception("An invalid distance reached the A2 scale.");
            }
            catch (ArgumentException)
            {
                if (ruler.TargetDistance != 100 || Math.Abs(ruler.BandHeight - 172.0 * 23 / 30) > 1e-9)
                    throw new Exception("Invalid input changed the A2 scale.");
            }
        }
        ruler.TargetDistance = null;
        if (ruler.BandHeight != 0)
            throw new Exception("Clearing A2 distance must clear the band height.");
        var model = new CtcsViewModel(new CtcsController(), CtcsScreenProfile.CTCS2_200H);
        var notifications = new HashSet<string?>();
        model.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        if (model.ShowTargetDistance || model.TargetDistanceText != "")
            throw new Exception("A missing target must not be displayed as zero.");
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
            if (!model.ShowTargetDistance || model.TargetDistanceText != expected || model.Data.TargetDistance != distance)
                throw new Exception($"Unexpected target distance display for {distance}m.");
        }
        if (!notifications.Contains(nameof(CtcsViewModel.ShowTargetDistance)) ||
            !notifications.Contains(nameof(CtcsViewModel.TargetDistanceText)))
            throw new Exception("Target distance updates must notify the screen.");
        var snapshot = model.Data;
        foreach (double invalid in new[] { -1.0, double.NaN, double.PositiveInfinity })
        {
            try
            {
                model.Update(snapshot with { TargetDistance = invalid });
                throw new Exception("Invalid target distance was accepted.");
            }
            catch (ArgumentException)
            {
                if (!ReferenceEquals(snapshot, model.Data) || !model.ShowTargetDistance || model.TargetDistanceText != "0")
                    throw new Exception("Invalid target distance changed A2 state.");
            }
        }
        model.Update(snapshot with { TargetDistance = 100001, TsmEntryDistance = 100000 });
        if (model.ShowTargetDistance || model.TargetDistanceText != "")
            throw new Exception("CSM must hide A2 even when a target distance is available.");
        model.Update(snapshot);
        if (!model.ShowTargetDistance || model.TargetDistanceText != "0")
            throw new Exception("Returning to TSM must restore A2.");
        foreach (var mode in Enum.GetValues<CtcsControlMode>())
        {
            model.Update(snapshot with { ControlMode = mode });
            bool expected = mode == CtcsControlMode.FullSupervision;
            if (model.ShowTargetDistance != expected || model.TargetDistanceText != (expected ? "0" : ""))
                throw new Exception("A2 must only be displayed in full-supervision TSM.");
        }
        model.Update(snapshot with { Speed = 300, PermittedSpeed = 200, SbiSpeed = 210, EbiSpeed = 220 });
        if (!model.ShowTargetDistance)
            throw new Exception("Speed warnings must not hide A2 in full-supervision TSM.");
        model.Update(new CtcsData());
        if (model.ShowTargetDistance || model.TargetDistanceText != "")
            throw new Exception("Removing target data must clear A2.");
        Console.WriteLine("A2 TSM visibility, metre precision, boundaries and invalid-input checks passed.");
    }
}
