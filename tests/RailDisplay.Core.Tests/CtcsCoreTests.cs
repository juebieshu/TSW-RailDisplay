using System;
using System.Collections.Generic;
using System.Linq;
using RailDisplay.Core.Controllers.CTCS;
using RailDisplay.Core.Controllers.Interaction;
using RailDisplay.Core.Models.CTCS;

namespace RailDisplay.Core.Tests;

public sealed class CtcsCoreTests
{
    private static readonly CtcsData Data = new()
    {
        Speed = 150, PermittedSpeed = 200, TargetSpeed = 80,
        TargetDistance = 1001, TsmEntryDistance = 1000,
        SbiSpeed = 210, EbiSpeed = 220
    };

    private static readonly CtcsTextMessage Retained = new()
    {
        Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Timestamp = DateTimeOffset.UnixEpoch,
        Content = "retain", Type = CtcsTextMessageType.RetainedConfirmation
    };

    private static readonly CtcsTextMessage Transient = Retained with
    {
        Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), Type = CtcsTextMessageType.TransientConfirmation
    };

    [Fact]
    public void NewController_StartsAtStandbyRootWithReadOnlyHistory()
    {
        ICtcsController controller = new CtcsController();
        var initialInteraction = controller.CurrentInteraction;
        var initialHistory = controller.History;
        Assert.True(initialInteraction.Step is null && initialInteraction.Input == "" && initialHistory.Count == 0 &&
                controller.CurrentInteractionRoot.ControlMode == CtcsControlMode.Standby,
            "A new controller must be at its standby root with empty input and history.");
        Assert.True((initialInteraction with { Input = "draft" }).Input == "draft" && initialInteraction.Input == "",
            "An interaction snapshot must not be mutable by its consumer.");
        Assert.ThrowsAny<NotSupportedException>(() => ((IList<CtcsInteractionState>)initialHistory).Add(initialInteraction));
    }

    [Fact]
    public void Update_AcceptsAllLevelsAndKeepsSectionBoundary()
    {
        ICtcsController controller = new CtcsController();
        foreach (var level in new[] { CtcsLevel.Level0, CtcsLevel.Level2, CtcsLevel.Level3 })
        {
            controller.Update(Data with { CurrentLevel = level });
            Assert.True(controller.Data.CurrentLevel == level, "The shared core must accept levels 0, 2 and 3.");
            foreach (var distance in new[] { 1001d, 1000d, 999d })
            {
                controller.Update(Data with { CurrentLevel = level, TargetDistance = distance });
                Assert.True(controller.Section == (distance <= 1000 ? SpeedMonitoringSection.TSM : SpeedMonitoringSection.CSM),
                    "The existing section boundary must survive migration for both CTCS levels.");
            }
        }
    }

    [Fact]
    public void Update_HandlesTargetsAndSpeedBoundaries()
    {
        ICtcsController controller = new CtcsController();
        controller.Update(Data with { TargetSpeed = 0, TargetDistance = 0 });
        Assert.True(controller.Section == SpeedMonitoringSection.TSM, "Zero target speed is a valid stopping target.");
        controller.Update(Data with { TargetSpeed = null, TargetDistance = null });
        Assert.True(controller.Section == SpeedMonitoringSection.CSM, "A missing target must retain CSM.");
        foreach (var speed in new[] { 200d, 200.01, 210, 210.01 })
        {
            controller.Update(Data with { Speed = speed });
            Assert.True(controller.IsPermittedSpeedExceeded == (speed > 200) &&
                    controller.IsInterventionSpeedReached == (speed >= 210), "Speed boundary comparisons changed.");
        }
    }

    [Fact]
    public void SpeedBrake_RespectsThresholdsModesLevelsAndLatching()
    {
        foreach (var mode in Enum.GetValues<CtcsControlMode>())
        foreach (var level in Enum.GetValues<CtcsLevel>())
        {
            ICtcsController brakes = new CtcsController();
            var brakeInput = Data with { ControlMode = mode, CurrentLevel = level, BrakeLevel = CtcsBrakeLevel.NoBrake };
            bool enabled = mode is not (CtcsControlMode.CabSignalling or CtcsControlMode.Isolation) && level != CtcsLevel.Level0;
            foreach (var (speed, expected) in new[]
            {
                (210d, CtcsBrakeLevel.NoBrake), (210.001, CtcsBrakeLevel.ServiceBrake),
                (220d, CtcsBrakeLevel.ServiceBrake), (220.001, CtcsBrakeLevel.EmergencyBrake),
                (215d, CtcsBrakeLevel.EmergencyBrake), (150d, CtcsBrakeLevel.EmergencyBrake)
            })
            {
                brakes.Update(brakeInput with { Speed = speed });
                Assert.True(brakes.BrakeLevel == (enabled ? expected : CtcsBrakeLevel.NoBrake),
                    "Speed brake must respect strict thresholds, EB priority, mode/level exclusions and latching.");
                Assert.True(brakes.Data.BrakeLevel == CtcsBrakeLevel.NoBrake && brakes.Data.Speed == speed,
                    "Calculated brake must not overwrite supplied brake or simulated speed.");
            }
            var beforeInvalid = brakes.Data;
            var brakeBeforeInvalid = brakes.BrakeLevel;
            Assert.ThrowsAny<ArgumentException>(() => brakes.Update(brakeInput with { Speed = double.NaN }));
            Assert.True(ReferenceEquals(beforeInvalid, brakes.Data) && brakes.BrakeLevel == brakeBeforeInvalid,
                "Invalid input must preserve the brake latch as well as the snapshot.");
        }
    }

    [Fact]
    public void SpeedBrake_ExcludedModesRetainLatchAndMissingThresholdsAreUnknown()
    {
        var latchedBrakes = new CtcsController();
        var latchedInput = Data with { Speed = 211, ControlMode = CtcsControlMode.FullSupervision, BrakeLevel = CtcsBrakeLevel.NoBrake };
        latchedBrakes.Update(latchedInput);
        foreach (var excluded in new[]
        {
            latchedInput with { CurrentLevel = CtcsLevel.Level0 },
            latchedInput with { ControlMode = CtcsControlMode.CabSignalling },
            latchedInput with { ControlMode = CtcsControlMode.Isolation }
        })
        {
            latchedBrakes.Update(excluded with { Speed = 230 });
            Assert.True(latchedBrakes.BrakeLevel == CtcsBrakeLevel.ServiceBrake,
                "Excluded modes/levels must retain the latch without upgrading it to EB.");
        }
        Assert.True(!latchedBrakes.IsPermittedSpeedExceeded && !latchedBrakes.IsInterventionSpeedReached,
            "Isolation must disable speed monitoring alerts.");
        latchedBrakes.AdvanceBrakeWarning(TimeSpan.FromSeconds(1));
        Assert.True(latchedBrakes.Data.BrakeWarningTime is null, "Isolation must not advance Tint prediction.");
        latchedBrakes.Update(latchedInput with { Speed = 230 });
        Assert.True(latchedBrakes.BrakeLevel == CtcsBrakeLevel.EmergencyBrake && latchedBrakes.IsInterventionSpeedReached,
            "Returning to supervision must resume speed monitoring and upgrade the latch.");
        latchedBrakes.Update(latchedInput with { Speed = 150, SbiSpeed = null, EbiSpeed = null });
        Assert.True(latchedBrakes.BrakeLevel == CtcsBrakeLevel.EmergencyBrake,
            "Missing thresholds must not clear a latched brake.");

        var missingBrakes = new CtcsController();
        missingBrakes.Update(latchedInput with { Speed = 230, SbiSpeed = null, EbiSpeed = null });
        Assert.True(missingBrakes.BrakeLevel == CtcsBrakeLevel.NoBrake, "Missing thresholds must not be treated as zero.");
        missingBrakes.Update(latchedInput with { Speed = 230, SbiSpeed = null });
        Assert.True(missingBrakes.BrakeLevel == CtcsBrakeLevel.EmergencyBrake, "EBI alone must still trigger EB.");
    }

    [Fact]
    public void BrakeLevel_RoundTripsSuppliedValues()
    {
        ICtcsController controller = new CtcsController();
        Assert.True((int)CtcsBrakeLevel.NoBrake == -1, "No brake must use the explicit value -1.");
        foreach (var brake in new[] { CtcsBrakeLevel.ServiceBrake, CtcsBrakeLevel.NoBrake, CtcsBrakeLevel.ReleaseAllowed })
        {
            controller.Update(Data with { BrakeLevel = brake });
            Assert.True(controller.Data.BrakeLevel == brake, "Brake state round-trip must preserve no-brake and release-allowed values.");
        }
    }

    [Fact]
    public void Update_RejectsInvalidInputWithoutChangingState()
    {
        ICtcsController controller = new CtcsController();
        controller.Update(Data with { BrakeLevel = CtcsBrakeLevel.ReleaseAllowed });
        var validSnapshot = controller.Data;
        var validSection = controller.Section;
        foreach (var invalid in new CtcsData?[]
        {
            null, Data with { Speed = double.NaN }, Data with { TargetDistance = double.PositiveInfinity },
            Data with { EbiSpeed = -1 }, Data with { TargetDistance = null }, Data with { TsmEntryDistance = null },
            Data with { BrakeWarningTime = double.NaN }, Data with { BrakeWarningTime = -1 },
            Data with { BrakeWarningTime = double.NegativeInfinity }, Data with { ReleaseSpeed = -1 },
            Data with { ReleaseSpeed = double.NaN }, Data with { ReleaseSpeed = double.PositiveInfinity },
            Data with { MonitoringSection = (SpeedMonitoringSection)99 },
            Data with { MonitoringSection = SpeedMonitoringSection.RSM }, Data with { SbiSpeed = 221 },
            Data with { CurrentLevel = (CtcsLevel)1 }, Data with { ControlMode = (CtcsControlMode)99 },
            Data with { BrakeLevel = (CtcsBrakeLevel)99 }, Data with { BrakeLevel = (CtcsBrakeLevel)(-2) },
            Data with { TextMessages = null! },
            Data with { TextMessages = new[] { (CtcsTextMessage)null! } },
            Data with { TextMessages = new[] { new CtcsTextMessage { Content = null! } } },
            Data with { TextMessages = new[] { new CtcsTextMessage { Id = Guid.Empty } } },
            Data with { TextMessages = new[] { new CtcsTextMessage { Type = (CtcsTextMessageType)99 } } }
        })
        {
            Assert.ThrowsAny<ArgumentException>(() => controller.Update(invalid!));
            Assert.True(ReferenceEquals(controller.Data, validSnapshot) && controller.Section == validSection,
                "Rejected input must preserve the entire previous state.");
        }
    }

    [Fact]
    public void Update_RoundTripsSupervisionInputsAndSelectsVint()
    {
        ICtcsController controller = new CtcsController();
        foreach (var section in Enum.GetValues<SpeedMonitoringSection>())
        {
            controller.Update(Data with { MonitoringSection = section, ReleaseSpeed = 40, BrakeWarningTime = 4 });
            Assert.True(controller.Section == section && controller.Data.BrakeWarningTime == 4,
                "Explicit supervision and warning inputs must survive a round trip.");
        }
        controller.Update(Data);
        Assert.True(controller.Section == SpeedMonitoringSection.CSM && controller.Data.BrakeWarningTime is null,
            "Removing supervision inputs must restore the existing fallback and clear warning time.");
        foreach (var (speed, intervention) in new[] { (209.999, 210d), (210d, 220d), (215d, 220d), (220d, 220d) })
        {
            controller.Update(Data with { Speed = speed });
            Assert.True(controller.Data.InterventionSpeed == intervention, "Vint must switch from SBI to EBI at the selected boundary.");
        }
    }

    [Fact]
    public void Update_CopiesTextMessagesIntoReadOnlySnapshot()
    {
        ICtcsController controller = new CtcsController();
        var messages = new[] { Retained, Transient };
        controller.Update(Data with { TextMessages = messages });
        messages[0] = Retained with { Content = "changed outside" };
        Assert.True(controller.Data.TextMessages[0] == Retained, "Input collections must not remain writable aliases.");
        Assert.ThrowsAny<NotSupportedException>(() => ((IList<CtcsTextMessage>)controller.Data.TextMessages)[0] = messages[0]);
    }

    [Fact]
    public void KeyHandle_UnboundAndInvalidButtonsPreserveAllState()
    {
        ICtcsController controller = new CtcsController();
        var initialInteraction = controller.CurrentInteraction;
        var initialHistory = controller.History;
        controller.Update(Data with { TextMessages = new[] { Retained, Transient } });
        var buttons = Enum.GetValues<CtcsButton>();
        Assert.True(buttons.Length == 19 && buttons.Distinct().Count() == 19,
            "The CTCS button list must contain 19 distinct physical keys.");
        var beforeButton = controller.Data;
        var sectionBeforeButton = controller.Section;
        var brakeBeforeButton = controller.BrakeLevel;
        bool Unchanged() => ReferenceEquals(controller.Data, beforeButton) && controller.Section == sectionBeforeButton &&
            controller.BrakeLevel == brakeBeforeButton && ReferenceEquals(controller.CurrentInteraction, initialInteraction) &&
            ReferenceEquals(controller.History, initialHistory);
        foreach (var button in buttons)
        {
            Assert.True(!controller.KeyHandle(button), "Buttons without confirmed rules must report unhandled.");
            Assert.True(Unchanged(), "Updates and unhandled buttons must preserve interaction state, history and business state.");
        }
        foreach (var invalid in new[] { (CtcsButton)(-1), (CtcsButton)0, (CtcsButton)20, (CtcsButton)int.MaxValue })
        {
            Assert.ThrowsAny<ArgumentOutOfRangeException>(() => controller.KeyHandle(invalid));
            Assert.True(Unchanged(), "Invalid buttons must preserve interaction state, history and business state.");
        }
    }

    [Fact]
    public void ConfirmTextMessage_PublishesNewSnapshot()
    {
        ICtcsController controller = new CtcsController();
        controller.Update(Data with { TextMessages = new[] { Retained, Transient } });
        var beforeConfirmation = controller.Data;
        Assert.True(controller.ConfirmTextMessage(Retained.Id), "A pending message must be confirmable.");
        Assert.True(!beforeConfirmation.TextMessages[0].IsConfirmed && controller.Data.TextMessages[0].IsConfirmed,
            "Confirmation must publish a new snapshot without modifying the previous one.");
        Assert.True(!controller.ConfirmTextMessage(Retained.Id) && !controller.ConfirmTextMessage(Guid.Empty),
            "Repeated or unknown confirmations must be harmless.");
        Assert.True(controller.ConfirmTextMessage(Transient.Id) && controller.Data.TextMessages.All(message => message.IsConfirmed),
            "The core must retain confirmation state; presentation decides which rows remain visible.");
    }

    [Fact]
    public void DeviceActionsAndRunningUpdates_ShareOneSchedule()
    {
        // 仅验证统一调度，不定义正式功能键规则。
        var message = new CtcsTextMessage { Content = "confirm", Type = CtcsTextMessageType.RetainedConfirmation };
        var keys = new KeyHandler<CtcsButton, CtcsController>(new Dictionary<CtcsButton, Func<CtcsController, bool>>
        {
            [CtcsButton.F1] = core => core.Next(new CtcsInteractionState { Step = CtcsInteractionStep.DriverNumberEntry, Input = "123" }),
            [CtcsButton.F2] = core => core.ConfirmTextMessage(message.Id),
            [CtcsButton.F3] = core => core.Back(count: 0, keepInput: true)
        });
        var controller = new CtcsController(keys);
        controller.Update(new CtcsData
        {
            ControlMode = CtcsControlMode.FullSupervision, Speed = 100, PermittedSpeed = 180,
            SbiSpeed = 190, EbiSpeed = 200, TextMessages = new[] { message }
        });
        controller.AdvanceBrakeWarning(TimeSpan.FromSeconds(1));
        Assert.True(controller.KeyHandle(CtcsButton.F1), "The controller must route device actions to navigation.");
        var interaction = controller.CurrentInteraction;
        var history = controller.History;
        controller.Update(controller.Data with { Speed = 110 });
        controller.AdvanceBrakeWarning(TimeSpan.FromSeconds(1));
        Assert.True(controller.Data.BrakeWarningTime is { } time && Math.Abs(time - 9) < 1e-9 &&
                ReferenceEquals(controller.CurrentInteraction, interaction) && ReferenceEquals(controller.History, history),
            "Device navigation must not restart prediction; running updates must preserve device state.");
        var snapshot = controller.Data;
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => controller.Update(snapshot with { Speed = double.NaN }));
        Assert.True(ReferenceEquals(controller.Data, snapshot) && ReferenceEquals(controller.CurrentInteraction, interaction) &&
                ReferenceEquals(controller.History, history), "Rejected running input must preserve both sides of the controller.");
        Assert.True(controller.KeyHandle(CtcsButton.F2) && controller.Data.TextMessages[0].IsConfirmed &&
                !snapshot.TextMessages[0].IsConfirmed && ReferenceEquals(controller.CurrentInteraction, interaction) &&
                ReferenceEquals(controller.History, history),
            "A device handler must confirm the same running snapshot without modifying interaction history.");
        controller.Update(controller.Data with { Speed = 201 });
        Assert.True(controller.BrakeLevel == CtcsBrakeLevel.EmergencyBrake && controller.KeyHandle(CtcsButton.F3) &&
                controller.CurrentInteraction.Step is null && controller.History.Count == 0 &&
                controller.BrakeLevel == CtcsBrakeLevel.EmergencyBrake && controller.Data.Speed == 201,
            "Back must normalize a non-positive count without changing running data or latched braking.");
        controller.Update(controller.Data with { Speed = 0, ControlMode = CtcsControlMode.Isolation });
        controller.AdvanceBrakeWarning(TimeSpan.FromSeconds(1));
        Assert.True(controller.CurrentInteractionRoot.ControlMode == CtcsControlMode.Isolation &&
                controller.Data.BrakeWarningTime is null && controller.BrakeLevel == CtcsBrakeLevel.EmergencyBrake,
            "The controller must publish the latest running mode, prediction lifecycle and retained brake latch.");
        var separate = new CtcsController(keys);
        Assert.True(separate.Data.Speed == 0 && separate.Data.ControlMode == CtcsControlMode.Standby &&
                separate.BrakeLevel == CtcsBrakeLevel.ReleaseAllowed &&
                separate.CurrentInteraction.Step is null && separate.History.Count == 0,
            "Sharing key bindings must not share running or device state between controller sessions.");
    }
}
