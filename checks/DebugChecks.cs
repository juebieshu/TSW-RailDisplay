using System;
using Avalonia.Input;
using RailDisplay.Debug.ViewModels;
using RailDisplay.Debug.CTCS;
using RailDisplay.Core.Controllers.CTCS;
using RailDisplay.Core.Models.CTCS;
using RailDisplay.Models.CTCS;
using RailDisplay.ViewModels.CTCS;

internal static class DebugChecks
{
    public static void Run()
    {
        var model = new DebugViewModel();
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
        Check(model.Power == 0 && model.Brake == 0 && model.Forward, "Debug defaults must be neutral and forward.");
        for (int i = 0; i < 10; i++) model.HandleKey(Key.A);
        Check(model.Power == 7 && model.DirectionWarning && model.EffectivePower == 7, "Power must stop at P7.");
        model.HandleKey(Key.S);
        model.ToggleDirectionCommand.Execute(null);
        Check(model.Forward, "Keyboard and mouse must not reverse under power.");
        model.HandleKey(Key.OemQuotes);
        Check(model.Brake == 1 && model.EffectivePower == 0 && model.Power == 7, "Brake must suppress power without moving its handle.");
        for (int i = 0; i < 10; i++) model.HandleKey(Key.OemQuotes);
        Check(model.Brake == 8 && model.BrakeLabel == "EB", "Brake must stop at EB.");
        for (int i = 0; i < 10; i++) model.HandleKey(Key.OemSemicolon);
        Check(model.Brake == 0 && model.EffectivePower == 7, "Releasing brake must restore effective handle power.");
        for (int i = 0; i < 10; i++) model.HandleKey(Key.D);
        model.HandleKey(Key.S);
        Check(model.Power == 0 && !model.DirectionWarning && !model.Forward, "Neutral must allow backward direction.");
        model.HandleKey(Key.W);
        model.ToggleDirectionCommand.Execute(null);
        Check(!model.Forward && !model.HandleKey(Key.Tab), "Mouse must toggle direction and unrelated keys must pass through.");
        foreach (var invalid in new[] { -1, 8, int.MaxValue })
        {
            try { model.Power = invalid; throw new Exception("Invalid power accepted."); }
            catch (ArgumentOutOfRangeException) { Check(model.Power == 0, "Invalid power changed state."); }
        }
        foreach (var invalid in new[] { -1, 9, int.MaxValue })
        {
            try { model.Brake = invalid; throw new Exception("Invalid brake accepted."); }
            catch (ArgumentOutOfRangeException) { Check(model.Brake == 0, "Invalid brake changed state."); }
        }
        model.Power = 3;
        model.Brake = 4;
        Check(model.EffectivePower == 0 && model.PowerLabel == "P3" && model.BrakeLabel == "B4", "Mouse-bound inputs must share keyboard rules.");
        var ctcs = new CtcsViewModel(new CtcsController(), CtcsScreenProfile.CTCS2_200H);
        ctcs.Update(new CtcsData { Speed = 20, BrakeLevel = CtcsBrakeLevel.ServiceBrake });
        var simulation = new CtcsDebugSimulation(() => ctcs.Data, ctcs.Update, ctcs.AdvanceBrakeWarning, model);
        model.Power = 7;
        model.Brake = 8;
        simulation.Advance(TimeSpan.FromSeconds(1));
        Check(Math.Abs(ctcs.Data.Speed - 15.68) < 1e-9 && ctcs.Data.BrakeLevel == CtcsBrakeLevel.ServiceBrake,
            "Simulation must update core speed without mapping handle brake to CTCS brake state.");
        model.HandleKey(Key.OemSemicolon);
        double before = ctcs.Data.Speed;
        simulation.Advance(TimeSpan.FromSeconds(1));
        Check(Math.Abs(ctcs.Data.Speed - (before - .85 * 3.6)) < 1e-9, "Leaving EB must immediately use B7 deceleration.");
        model.Brake = 0;
        model.Power = 0;
        model.HandleKey(Key.S);
        model.Power = 1;
        before = ctcs.Data.Speed;
        simulation.Advance(TimeSpan.FromSeconds(1));
        Check(Math.Abs(ctcs.Data.Speed - (before + .08 * 3.6)) < 1e-9, "Backward traction must display absolute speed.");
        ctcs.Update(new CtcsData
        {
            ControlMode = CtcsControlMode.FullSupervision, Speed = 0,
            PermittedSpeed = 30, SbiSpeed = 36, EbiSpeed = 72
        });
        model.Power = 7;
        model.Brake = 0;
        simulation.Advance(TimeSpan.FromSeconds(1));
        Check(ctcs.Data.BrakeWarningTime is { } firstTint && Math.Abs(firstTint - (20 - .56) / .56) < 1e-9,
            "The first one-second interval must already produce a bound prediction.");
        for (int i = 0; i < 3; i++) simulation.Advance(TimeSpan.FromSeconds(1));
        Check(ctcs.Data.BrakeWarningTime is { } tint && Math.Abs(tint - (20 - 2.24) / .56) < 1e-9,
            "Local simulation must feed timed FS prediction into the bound core snapshot.");
        model.Power = 0;
        simulation.Advance(TimeSpan.FromSeconds(1));
        Check(ctcs.Data.BrakeWarningTime is { } mixedTint && Math.Abs(mixedTint - (20 - 2.24) / .56) < 1e-9,
            "A zero-acceleration interval must not dilute the remaining nonzero history.");
        for (int i = 0; i < 3; i++) simulation.Advance(TimeSpan.FromSeconds(1));
        Check(ctcs.Data.BrakeWarningTime == double.PositiveInfinity,
            "Prediction must keep sampling even when neutral handles hold speed constant.");
        ctcs.Update(new CtcsData
        {
            ControlMode = CtcsControlMode.FullSupervision, Speed = 20,
            SbiSpeed = 10, EbiSpeed = 15, BrakeLevel = CtcsBrakeLevel.NoBrake
        });
        Check(ctcs.BrakeLevel == CtcsBrakeLevel.EmergencyBrake, "Speed exceeding EBI must display EB.");
        simulation.Advance(TimeSpan.FromSeconds(1));
        Check(ctcs.Data.Speed == 20 && model.Power == 0 && model.Brake == 0,
            "Calculated EB must not brake the local simulation or change its handles.");
        model.Power = 1;
        simulation.Advance(TimeSpan.FromSeconds(1));
        Check(ctcs.Data.Speed > 20 && ctcs.BrakeLevel == CtcsBrakeLevel.EmergencyBrake,
            "Latched EB display must not suppress local traction.");
        var snapshot = ctcs.Data;
        try { simulation.Advance(TimeSpan.FromTicks(-1)); throw new Exception("Negative simulation time accepted."); }
        catch (ArgumentOutOfRangeException) { Check(ReferenceEquals(snapshot, ctcs.Data), "Invalid step must preserve the complete core snapshot."); }
        Check(CtcsDebugData.Create(ctcs.Data).Speed == ctcs.Data.Speed, "Debug sample data must preserve simulated speed.");
        CheckSettings();
        CheckDeviceKeys();
        Console.WriteLine("Debug handle checks passed.");
    }

    private static void CheckSettings()
    {
        var core = new CtcsController();
        core.Update(CtcsDebugData.Create(new CtcsData { Speed = 20 }));
        var settings = new DebugSettingsViewModel(() => core.Data, core.Update);
        settings.TargetSpeed = "60.5";
        settings.PermittedSpeed = "120";
        settings.TargetDistance = "2000";
        core.Update(core.Data with { Speed = 21, CurrentLevel = CtcsLevel.Level3 });
        settings.ApplyCommand.Execute(null);
        if (settings.Error is not null || core.Data.TargetSpeed != 60.5 || core.Data.PermittedSpeed != 120 ||
            core.Data.TargetDistance != 2000 || core.Data.Speed != 21 || core.Data.CurrentLevel != CtcsLevel.Level3 ||
            core.Data.SbiSpeed != 95 || core.Data.EbiSpeed != 100 || core.Data.TsmEntryDistance != 2000)
            throw new Exception("Settings must submit three fields together using the latest snapshot and preserve supervision inputs.");
        var snapshot = core.Data;
        foreach (string invalid in new[] { "", "bad", "-1", "NaN", "Infinity", "1e309" })
        {
            settings.TargetSpeed = "10";
            settings.PermittedSpeed = invalid;
            settings.TargetDistance = "100";
            settings.ApplyCommand.Execute(null);
            if (settings.Error is null || !ReferenceEquals(snapshot, core.Data))
                throw new Exception("Invalid settings must reject the complete batch without updating any field.");
        }
        settings.PermittedSpeed = "90";
        foreach (string invalid in new[] { "-1", "NaN", "", "bad" })
        {
            settings.TargetDistance = invalid;
            settings.ApplyCommand.Execute(null);
            if (settings.Error is null || !ReferenceEquals(snapshot, core.Data))
                throw new Exception("Invalid or missing target distance must not commit a partial target speed.");
        }
        settings.TargetSpeed = settings.PermittedSpeed = settings.TargetDistance = "0";
        settings.ApplyCommand.Execute(null);
        if (settings.Error is not null || core.Data.TargetSpeed != 0 || core.Data.PermittedSpeed != 0 || core.Data.TargetDistance != 0)
            throw new Exception("Settings must accept the zero boundaries.");
        settings.TargetSpeed = settings.TargetDistance = "";
        settings.ApplyCommand.Execute(null);
        if (settings.Error is not null || core.Data.TargetSpeed is not null || core.Data.TargetDistance is not null)
            throw new Exception("Optional empty inputs must retain their missing-value semantics.");
        core.Update(core.Data with { TargetSpeed = 30, TargetDistance = 600 });
        settings.Reload();
        if (settings.TargetSpeed != "30" || settings.TargetDistance != "600" || settings.Error is not null)
            throw new Exception("Reopening settings must read the current snapshot.");
        Console.WriteLine("Debug settings validation, atomic updates, boundaries and reload checks passed.");
    }

    private static void CheckDeviceKeys()
    {
        CtcsButton? received = null;
        bool enabled = true;
        var model = new DebugViewModel(key => { received = key; return true; }, _ => enabled);
        foreach (var key in Enum.GetValues<CtcsButton>())
        {
            received = null;
            if (!model.PressButtonCommand.CanExecute(key)) throw new Exception("Available physical key command was disabled.");
            model.PressButtonCommand.Execute(key);
            if (received != key) throw new Exception("A Debug button did not deliver its physical key identifier.");
        }
        foreach (var (key, expected) in new[]
        {
            (Key.F1, CtcsButton.F1), (Key.F8, CtcsButton.F8), (Key.D1, CtcsButton.Digit1),
            (Key.D0, CtcsButton.Digit0), (Key.NumPad9, CtcsButton.Digit9)
        })
            if (!model.HandleKey(key) || received != expected) throw new Exception("Debug keyboard mapping does not match the physical keys.");
        bool notified = false;
        model.PressButtonCommand.CanExecuteChanged += (_, _) => notified = true;
        enabled = false;
        received = null;
        model.RefreshButtons();
        model.PressButtonCommand.Execute(CtcsButton.F1);
        if (!notified || model.PressButtonCommand.CanExecute(CtcsButton.F1) || received is not null || model.HandleKey(Key.F1))
            throw new Exception("Unavailable Debug keys must refresh and reject both mouse and keyboard activation.");
        try { model.HandleButton((CtcsButton)0); throw new Exception("Debug accepted an undefined physical key."); }
        catch (ArgumentOutOfRangeException) { }
    }
}
