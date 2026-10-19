using System;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using RailDisplay.Core.Controllers.CTCS;
using RailDisplay.Core.Models.CTCS;
using RailDisplay.Debug.CTCS;
using RailDisplay.Debug.ViewModels;
using RailDisplay.Models.CTCS;
using RailDisplay.ViewModels.CTCS;

namespace RailDisplay.Tests;

public sealed class DebugTests
{
    [AvaloniaFact]
    public void Handles_ClampNotchesAndShareKeyboardAndMouseRules()
    {
        var model = new DebugViewModel();
        Assert.True(model.Power == 0 && model.Brake == 0 && model.Forward, "Debug defaults must be neutral and forward.");
        for (int i = 0; i < 10; i++) model.HandleKey(Key.A);
        Assert.True(model.Power == 7 && model.DirectionWarning && model.EffectivePower == 7, "Power must stop at P7.");
        model.HandleKey(Key.S);
        model.ToggleDirectionCommand.Execute(null);
        Assert.True(model.Forward, "Keyboard and mouse must not reverse under power.");
        model.HandleKey(Key.OemQuotes);
        Assert.True(model.Brake == 1 && model.EffectivePower == 0 && model.Power == 7, "Brake must suppress power without moving its handle.");
        for (int i = 0; i < 10; i++) model.HandleKey(Key.OemQuotes);
        Assert.True(model.Brake == 8 && model.BrakeLabel == "EB", "Brake must stop at EB.");
        for (int i = 0; i < 10; i++) model.HandleKey(Key.OemSemicolon);
        Assert.True(model.Brake == 0 && model.EffectivePower == 7, "Releasing brake must restore effective handle power.");
        for (int i = 0; i < 10; i++) model.HandleKey(Key.D);
        model.HandleKey(Key.S);
        Assert.True(model.Power == 0 && !model.DirectionWarning && !model.Forward, "Neutral must allow backward direction.");
        model.HandleKey(Key.W);
        model.ToggleDirectionCommand.Execute(null);
        Assert.True(!model.Forward && !model.HandleKey(Key.Tab), "Mouse must toggle direction and unrelated keys must pass through.");
        foreach (var invalid in new[] { -1, 8, int.MaxValue })
        {
            Assert.ThrowsAny<ArgumentOutOfRangeException>(() => model.Power = invalid);
            Assert.True(model.Power == 0, "Invalid power changed state.");
        }
        foreach (var invalid in new[] { -1, 9, int.MaxValue })
        {
            Assert.ThrowsAny<ArgumentOutOfRangeException>(() => model.Brake = invalid);
            Assert.True(model.Brake == 0, "Invalid brake changed state.");
        }
        model.Power = 3;
        model.Brake = 4;
        Assert.True(model.EffectivePower == 0 && model.PowerLabel == "P3" && model.BrakeLabel == "B4",
            "Mouse-bound inputs must share keyboard rules.");
    }

    [AvaloniaFact]
    public void Simulation_FeedsSpeedAndPredictionIntoCore()
    {
        var model = new DebugViewModel();
        var ctcs = new CtcsViewModel(new CtcsController(), CtcsScreenProfile.CTCS2_200H);
        ctcs.Update(new CtcsData { Speed = 20, BrakeLevel = CtcsBrakeLevel.ServiceBrake });
        var simulation = new CtcsDebugSimulation(() => ctcs.Data, ctcs.Update, ctcs.AdvanceBrakeWarning, model);
        model.Power = 7;
        model.Brake = 8;
        simulation.Advance(TimeSpan.FromSeconds(1));
        Assert.True(Math.Abs(ctcs.Data.Speed - 15.68) < 1e-9 && ctcs.Data.BrakeLevel == CtcsBrakeLevel.ServiceBrake,
            "Simulation must update core speed without mapping handle brake to CTCS brake state.");
        model.HandleKey(Key.OemSemicolon);
        double before = ctcs.Data.Speed;
        simulation.Advance(TimeSpan.FromSeconds(1));
        Assert.True(Math.Abs(ctcs.Data.Speed - (before - .85 * 3.6)) < 1e-9, "Leaving EB must immediately use B7 deceleration.");
        model.Brake = 0;
        model.Power = 0;
        model.HandleKey(Key.S);
        model.Power = 1;
        before = ctcs.Data.Speed;
        simulation.Advance(TimeSpan.FromSeconds(1));
        Assert.True(Math.Abs(ctcs.Data.Speed - (before + .08 * 3.6)) < 1e-9, "Backward traction must display absolute speed.");
        ctcs.Update(new CtcsData
        {
            ControlMode = CtcsControlMode.FullSupervision, Speed = 0,
            PermittedSpeed = 30, SbiSpeed = 36, EbiSpeed = 72
        });
        model.Power = 7;
        model.Brake = 0;
        simulation.Advance(TimeSpan.FromSeconds(1));
        Assert.True(ctcs.Data.BrakeWarningTime is { } firstTint && Math.Abs(firstTint - (20 - .56) / .56) < 1e-9,
            "The first one-second interval must already produce a bound prediction.");
        for (int i = 0; i < 3; i++) simulation.Advance(TimeSpan.FromSeconds(1));
        Assert.True(ctcs.Data.BrakeWarningTime is { } tint && Math.Abs(tint - (20 - 2.24) / .56) < 1e-9,
            "Local simulation must feed timed FS prediction into the bound core snapshot.");
        model.Power = 0;
        simulation.Advance(TimeSpan.FromSeconds(1));
        Assert.True(ctcs.Data.BrakeWarningTime is { } mixedTint && Math.Abs(mixedTint - (20 - 2.24) / .56) < 1e-9,
            "A zero-acceleration interval must not dilute the remaining nonzero history.");
        for (int i = 0; i < 3; i++) simulation.Advance(TimeSpan.FromSeconds(1));
        Assert.True(ctcs.Data.BrakeWarningTime == double.PositiveInfinity,
            "Prediction must keep sampling even when neutral handles hold speed constant.");
        ctcs.Update(new CtcsData
        {
            ControlMode = CtcsControlMode.FullSupervision, Speed = 20,
            SbiSpeed = 10, EbiSpeed = 15, BrakeLevel = CtcsBrakeLevel.NoBrake
        });
        Assert.True(ctcs.BrakeLevel == CtcsBrakeLevel.EmergencyBrake, "Speed exceeding EBI must display EB.");
        simulation.Advance(TimeSpan.FromSeconds(1));
        Assert.True(ctcs.Data.Speed == 20 && model.Power == 0 && model.Brake == 0,
            "Calculated EB must not brake the local simulation or change its handles.");
        model.Power = 1;
        simulation.Advance(TimeSpan.FromSeconds(1));
        Assert.True(ctcs.Data.Speed > 20 && ctcs.BrakeLevel == CtcsBrakeLevel.EmergencyBrake,
            "Latched EB display must not suppress local traction.");
        var snapshot = ctcs.Data;
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => simulation.Advance(TimeSpan.FromTicks(-1)));
        Assert.True(ReferenceEquals(snapshot, ctcs.Data), "Invalid step must preserve the complete core snapshot.");
        Assert.True(CtcsDebugData.Create(ctcs.Data).Speed == ctcs.Data.Speed, "Debug sample data must preserve simulated speed.");
    }

    [AvaloniaFact]
    public void Settings_ApplyAtomicallyAndReloadCurrentSnapshot()
    {
        var core = new CtcsController();
        core.Update(CtcsDebugData.Create(new CtcsData { Speed = 20 }));
        var settings = new DebugSettingsViewModel(() => core.Data, core.Update);
        settings.TargetSpeed = "60.5";
        settings.PermittedSpeed = "120";
        settings.TargetDistance = "2000";
        core.Update(core.Data with { Speed = 21, CurrentLevel = CtcsLevel.Level3 });
        settings.ApplyCommand.Execute(null);
        Assert.False(settings.Error is not null || core.Data.TargetSpeed != 60.5 || core.Data.PermittedSpeed != 120 ||
            core.Data.TargetDistance != 2000 || core.Data.Speed != 21 || core.Data.CurrentLevel != CtcsLevel.Level3 ||
            core.Data.SbiSpeed != 95 || core.Data.EbiSpeed != 100 || core.Data.TsmEntryDistance != 2000,
            "Settings must submit three fields together using the latest snapshot and preserve supervision inputs.");
        var snapshot = core.Data;
        foreach (string invalid in new[] { "", "bad", "-1", "NaN", "Infinity", "1e309" })
        {
            settings.TargetSpeed = "10";
            settings.PermittedSpeed = invalid;
            settings.TargetDistance = "100";
            settings.ApplyCommand.Execute(null);
            Assert.False(settings.Error is null || !ReferenceEquals(snapshot, core.Data),
                "Invalid settings must reject the complete batch without updating any field.");
        }
        settings.PermittedSpeed = "90";
        foreach (string invalid in new[] { "-1", "NaN", "", "bad" })
        {
            settings.TargetDistance = invalid;
            settings.ApplyCommand.Execute(null);
            Assert.False(settings.Error is null || !ReferenceEquals(snapshot, core.Data),
                "Invalid or missing target distance must not commit a partial target speed.");
        }
        settings.TargetSpeed = settings.PermittedSpeed = settings.TargetDistance = "0";
        settings.ApplyCommand.Execute(null);
        Assert.False(settings.Error is not null || core.Data.TargetSpeed != 0 || core.Data.PermittedSpeed != 0 || core.Data.TargetDistance != 0,
            "Settings must accept the zero boundaries.");
        settings.TargetSpeed = settings.TargetDistance = "";
        settings.ApplyCommand.Execute(null);
        Assert.False(settings.Error is not null || core.Data.TargetSpeed is not null || core.Data.TargetDistance is not null,
            "Optional empty inputs must retain their missing-value semantics.");
        core.Update(core.Data with { TargetSpeed = 30, TargetDistance = 600 });
        settings.Reload();
        Assert.False(settings.TargetSpeed != "30" || settings.TargetDistance != "600" || settings.Error is not null,
            "Reopening settings must read the current snapshot.");
    }

    [AvaloniaFact]
    public void DeviceKeys_DeliverPhysicalButtonsAndRespectAvailability()
    {
        CtcsButton? received = null;
        bool enabled = true;
        var model = new DebugViewModel(key => { received = key; return true; }, _ => enabled);
        foreach (var key in Enum.GetValues<CtcsButton>())
        {
            received = null;
            Assert.True(model.PressButtonCommand.CanExecute(key), "Available physical key command was disabled.");
            model.PressButtonCommand.Execute(key);
            Assert.True(received == key, "A Debug button did not deliver its physical key identifier.");
        }
        foreach (var (key, expected) in new[]
        {
            (Key.F1, CtcsButton.F1), (Key.F8, CtcsButton.F8), (Key.D1, CtcsButton.Digit1),
            (Key.D0, CtcsButton.Digit0), (Key.NumPad9, CtcsButton.Digit9)
        })
            Assert.True(model.HandleKey(key) && received == expected, "Debug keyboard mapping does not match the physical keys.");
        bool notified = false;
        model.PressButtonCommand.CanExecuteChanged += (_, _) => notified = true;
        enabled = false;
        received = null;
        model.RefreshButtons();
        model.PressButtonCommand.Execute(CtcsButton.F1);
        Assert.False(!notified || model.PressButtonCommand.CanExecute(CtcsButton.F1) || received is not null || model.HandleKey(Key.F1),
            "Unavailable Debug keys must refresh and reject both mouse and keyboard activation.");
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => model.HandleButton((CtcsButton)0));
    }
}
