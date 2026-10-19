using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using RailDisplay.Core.Controllers.CTCS;
using RailDisplay.Core.Controllers.Interaction;
using RailDisplay.Core.Models.CTCS;
using RailDisplay.Models.CTCS;
using RailDisplay.ViewModels.CTCS;

namespace RailDisplay.Tests;

public sealed class CtcsDisplayTests
{
    private static readonly CtcsData Data = new()
    {
        Speed = 150, PermittedSpeed = 200, TargetSpeed = 80,
        TargetDistance = 1001, TsmEntryDistance = 1000,
        SbiSpeed = 210, EbiSpeed = 220
    };

    private static readonly CtcsData TsmData = Data with { TargetDistance = 800, ControlMode = CtcsControlMode.FullSupervision };

    private CtcsViewModel? _model;

    private CtcsViewModel Controller => _model ??= new CtcsViewModel(new CtcsController(), CtcsScreenProfile.CTCS2_200H);

    [AvaloniaFact]
    public void Section_DefaultsToLevel2AndSwitchesAtTsmEntry()
    {
        Assert.False(Controller.Data.CurrentLevel != CtcsLevel.Level2 || Controller.CurrentLevelName != "CTCS 2", "The default CTCS level must be 2.");
        Check(Data, SpeedMonitoringSection.CSM);
        Check(Data with { TargetDistance = 1000 }, SpeedMonitoringSection.TSM);
        Check(Data with { TargetDistance = 999 }, SpeedMonitoringSection.TSM);
        Check(Data with { TargetSpeed = 0, TargetDistance = 0 }, SpeedMonitoringSection.TSM);
        Check(Data with { TargetSpeed = null, TargetDistance = null }, SpeedMonitoringSection.CSM);
        Check(Data, SpeedMonitoringSection.CSM);
    }

    [AvaloniaFact]
    public void Update_RejectsInvalidInputWithoutChangingState()
    {
        Controller.Update(Data);
        var validSnapshot = Controller.Data;
        foreach (var invalid in new[]
        {
            Data with { TargetDistance = null },
            Data with { TsmEntryDistance = null },
            Data with { Speed = double.NaN },
            Data with { EbiSpeed = -1 },
            Data with { CurrentLevel = (CtcsLevel)1 },
            Data with { BrakeLevel = (CtcsBrakeLevel)5 },
            Data with { TextMessages = null! },
            Data with { TextMessages = new[] { (CtcsTextMessage)null! } }
        })
        {
            Assert.ThrowsAny<ArgumentException>(() => Controller.Update(invalid));
            Assert.True(ReferenceEquals(Controller.Data, validSnapshot), "Invalid input changed the current state.");
        }
    }

    [AvaloniaFact]
    public void LightBands_AreHiddenOutsideFullSupervision()
    {
        bool bandVisibilityNotified = false;
        Controller.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CtcsViewModel.ShowLightBand))
                bandVisibilityNotified = true;
        };
        foreach (var mode in new[]
        {
            CtcsControlMode.PartialSupervision, CtcsControlMode.OnSight,
            CtcsControlMode.Shunting, CtcsControlMode.Guiding, CtcsControlMode.Standby
        })
        {
            foreach (double? target in new double?[] { null, 0, 80 })
            {
                bandVisibilityNotified = false;
                Controller.Update(Data with { ControlMode = mode, TargetSpeed = target });
                Assert.False(Controller.ShowLightBand || !bandVisibilityNotified, $"Light bands were not hidden/notified for {mode}.");
                Assert.False(Controller.Data.PermittedSpeed != Data.PermittedSpeed, "Hiding light bands changed the end marker speed.");

                Controller.Update(Data with { ControlMode = CtcsControlMode.FullSupervision });
                Assert.False(!Controller.ShowLightBand, "Returning to full supervision did not restore light bands.");
            }
        }
        Controller.Update(Data with { ControlMode = CtcsControlMode.Guiding });
        Assert.False(Controller.ControlModeShortName != "引导", "The guiding mode label is missing.");
    }

    [AvaloniaFact]
    public void Csg_IsShownOnlyInFullSupervision()
    {
        bool csgVisibilityNotified = false;
        Controller.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CtcsViewModel.ShowCsg))
                csgVisibilityNotified = true;
        };
        foreach (var mode in Enum.GetValues<CtcsControlMode>())
        {
            foreach (double distance in new[] { 1001.0, 800.0 })
            {
                csgVisibilityNotified = false;
                Controller.Update(Data with { ControlMode = mode, TargetDistance = distance });
                Assert.False(Controller.ShowCsg != (mode == CtcsControlMode.FullSupervision) || !csgVisibilityNotified, $"Only full supervision must display the complete CSG, including in {Controller.Section}.");
                Controller.Update(Data with { ControlMode = CtcsControlMode.FullSupervision, TargetDistance = distance });
                Assert.False(!Controller.ShowCsg, "Returning to full supervision must restore the CSG.");
            }
        }
    }

    [AvaloniaFact]
    public void SpeedDisplay_SelectsPriorityStateAndWarningRange()
    {
        CheckDisplay(TsmData with { Speed = 0 }, SpeedDisplayState.TargetMonitoring, null);
        CheckDisplay(TsmData with { Speed = 80 }, SpeedDisplayState.TargetMonitoring, null);
        CheckDisplay(TsmData with { Speed = 80.01 }, SpeedDisplayState.TargetMonitoring, null);
        CheckDisplay(TsmData with { Speed = 200 }, SpeedDisplayState.TargetMonitoring, null);
        CheckDisplay(TsmData with { Speed = 200.01 }, SpeedDisplayState.PermittedExceeded, 210);
        CheckDisplay(TsmData with { Speed = 210 }, SpeedDisplayState.SbiExceeded, 220);
        CheckDisplay(TsmData with { Speed = 210.01 }, SpeedDisplayState.SbiExceeded, 220);
        CheckDisplay(TsmData with { Speed = 220 }, SpeedDisplayState.SbiExceeded, 220);
        CheckDisplay(TsmData with { Speed = 205 }, SpeedDisplayState.PermittedExceeded, 210);
        CheckDisplay(TsmData with { Speed = 150 }, SpeedDisplayState.TargetMonitoring, null);
        CheckDisplay(TsmData with { Speed = 80 }, SpeedDisplayState.TargetMonitoring, null);
        CheckDisplay(Data with { Speed = 150 }, SpeedDisplayState.Normal, null);
        CheckDisplay(Data with { Speed = 205, TargetSpeed = null }, SpeedDisplayState.PermittedExceeded, 210);
        CheckDisplay(Data with { Speed = 205, SbiSpeed = null }, SpeedDisplayState.PermittedExceeded, 220);
        CheckDisplay(Data with { Speed = 220, SbiSpeed = null }, SpeedDisplayState.SbiExceeded, 220);
        CheckDisplay(Data with { Speed = 215, EbiSpeed = null }, SpeedDisplayState.SbiExceeded, null);
        foreach (var mode in new[]
        {
            CtcsControlMode.PartialSupervision, CtcsControlMode.OnSight,
            CtcsControlMode.Shunting, CtcsControlMode.Guiding, CtcsControlMode.Standby
        })
        {
            CheckDisplay(TsmData with { Speed = 215, ControlMode = mode }, SpeedDisplayState.SbiExceeded, 220);
            Assert.False(Controller.ShowLightBand, $"Overspeed unexpectedly enabled hidden bands in {mode}.");
            CheckDisplay(TsmData with { Speed = 150, ControlMode = mode }, SpeedDisplayState.Normal, null);
        }
    }

    [AvaloniaFact]
    public void Vint_ExtendsToEbiAndPermittedBandTurnsYellowInCsm()
    {
        Controller.Update(TsmData with { Speed = 210 });
        Assert.False(Controller.InterventionSpeed != 220, "The display plan selects EBI at exactly SBI.");
        Controller.Update(TsmData with { Speed = 210.01 });
        Assert.False(Controller.InterventionSpeed != 220 || !Controller.IsSbiExceeded, "Passing SBI must extend Vint to EBI without reverting to orange.");
        Controller.Update(TsmData with { Speed = 205, TargetDistance = 2000 });
        Assert.False(!Controller.IsPermittedBandYellow, "CSM overspeed must turn the permitted band yellow.");
        Controller.Update(TsmData with { Speed = 200, TargetDistance = 2000 });
        Assert.False(Controller.IsPermittedBandYellow, "CSM normal speed must restore the grey permitted band.");
    }

    [AvaloniaFact]
    public void D6_ShowsPageTitleForEachStep()
    {
        var expectedTitles = new (CtcsInteractionStep? Step, string Title)[]
        {
            (null, ""),
            (CtcsInteractionStep.DataEntry, ""),
            (CtcsInteractionStep.DriverNumberEntry, "司机号输入"),
            (CtcsInteractionStep.TrainNumberEntry, "车次号输入"),
            (CtcsInteractionStep.TrainDataEntry, "列车数据输入"),
            (CtcsInteractionStep.DateTimeSetting, ""),
            (CtcsInteractionStep.DateSetting, ""),
            (CtcsInteractionStep.TimeSetting, ""),
            (CtcsInteractionStep.ModeSelection, "模式功能选择"),
            (CtcsInteractionStep.ShuntingConfirmation, "调车模式确认"),
            (CtcsInteractionStep.OnSightConfirmation, "目视行车确认"),
            (CtcsInteractionStep.CarrierFrequencySelection, "载频方式选择"),
            (CtcsInteractionStep.UpCarrierFrequencyConfirmation, "上/下行载频确认"),
            (CtcsInteractionStep.DownCarrierFrequencyConfirmation, "上/下行载频确认"),
            (CtcsInteractionStep.LevelSelection, "CTCS等级选择"),
            (CtcsInteractionStep.Level2Confirmation, "转换到CTCS 2级确认"),
            (CtcsInteractionStep.Level0Confirmation, "转换到CTCS 0级确认"),
            (CtcsInteractionStep.OtherSettings, "其他选项"),
            (CtcsInteractionStep.VolumeSetting, "音量设置"),
            (CtcsInteractionStep.BrightnessSetting, "亮度设置"),
            (CtcsInteractionStep.SoftwareVersion, "软件版本"),
            (CtcsInteractionStep.StartConfirmation, "启动确认"),
            (CtcsInteractionStep.BrakeReleaseConfirmation, "缓解确认")
        };

        foreach (var (step, title) in expectedTitles)
        {
            var core = new CtcsController();
            if (step is { } target)
                Assert.True(core.Next(new CtcsInteractionState { Step = target }), $"Could not set interaction step {target} for the D6 display check.");
            core.Update(new CtcsData { ControlMode = CtcsControlMode.FullSupervision });
            var pageModel = new CtcsViewModel(core, CtcsScreenProfile.CTCS2_200H);
            Assert.False(pageModel.D6PageTitle != title || pageModel.ShowD6PageTitle != (title.Length > 0) ||
                pageModel.ShowDistanceScale != (step is null),
                $"Unexpected D6 title or plan visibility for {step}.");
        }
    }

    [AvaloniaFact]
    public void Buttons_WithoutRulesAreNotConsumed()
    {
        var core = new CtcsController();
        var viewModel = new CtcsViewModel(core, CtcsScreenProfile.CTCS2_200H);
        var snapshot = viewModel.Data;
        foreach (var button in Enum.GetValues<CtcsButton>())
            Assert.False(viewModel.KeyHandle(button) || !ReferenceEquals(viewModel.Data, snapshot) ||
                !ReferenceEquals(viewModel.CurrentInteraction, core.CurrentInteraction),
                "The DMI must read core interaction state without consuming unimplemented keys.");
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => viewModel.KeyHandle((CtcsButton)(-1)));
        Assert.False(!ReferenceEquals(viewModel.Data, snapshot), "An invalid button changed the displayed snapshot.");
    }

    [AvaloniaFact]
    public void BoundButtons_RefreshInteractionAndNotify()
    {
        // 仅用于检查统一分发与绑定通知，不定义正式的功能键行为。
        var keys = new KeyHandler<CtcsButton, CtcsController>(new Dictionary<CtcsButton, Func<CtcsController, bool>>
        {
            [CtcsButton.F1] = core => core.Next(new CtcsInteractionState
            {
                Step = CtcsInteractionStep.DriverNumberEntry, Input = "12345"
            }),
            [CtcsButton.F2] = core => core.Back()
        });
        var core = new CtcsController(keys);
        var model = new CtcsViewModel(core, CtcsScreenProfile.CTCS2_200H);
        var notifications = new HashSet<string?>();
        model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        Assert.False(model.ControlModeShortName != "待机" || !model.KeyHandle(CtcsButton.F1) ||
            !ReferenceEquals(model.CurrentInteraction, core.CurrentInteraction) || model.CurrentInteraction.Input != "12345" ||
            model.History.Count != 1 || !notifications.Contains(nameof(CtcsViewModel.CurrentInteraction)) ||
            !notifications.Contains(nameof(CtcsViewModel.History)),
            "Handled keys must refresh core interaction state and history through the view model.");
        var draft = model.CurrentInteraction;
        var history = model.History;
        notifications.Clear();
        model.Update(model.Data with { ControlMode = CtcsControlMode.FullSupervision });
        Assert.False(model.CurrentInteractionRoot.ControlMode != CtcsControlMode.FullSupervision ||
            !ReferenceEquals(model.CurrentInteraction, draft) || !ReferenceEquals(model.History, history) ||
            !notifications.Contains(nameof(CtcsViewModel.CurrentInteractionRoot)),
            "Mode changes must notify the root while preserving editing state and history.");
        notifications.Clear();
        Assert.False(model.KeyHandle(CtcsButton.F8) || notifications.Count != 0, "Unbound keys must not refresh or change presentation state.");
        Assert.False(!model.KeyHandle(CtcsButton.F2) || model.CurrentInteraction.Step is not null || model.History.Count != 0 ||
            model.CurrentInteractionRoot.ControlMode != CtcsControlMode.FullSupervision,
            "A handled Back must refresh the root using the latest operating mode.");
    }

    [AvaloniaFact]
    public void TextMessages_ScrollHighlightAndConfirm()
    {
        var messages = Enumerable.Range(1, 6).Select(index => new CtcsTextMessage
        {
            Content = index.ToString(), Timestamp = DateTimeOffset.Now.AddSeconds(index)
        }).ToArray();
        var textController = new CtcsViewModel(new CtcsController(), CtcsScreenProfile.CTCS2_200H);
        for (int count = 0; count <= 4; count++)
        {
            textController.Update(new CtcsData { TextMessages = messages.Take(count).ToArray() });
            textController.ScrollTextUp();
            textController.ScrollTextDown();
            Assert.False(textController.CanScrollTextUp || textController.CanScrollTextDown, "Both arrows must remain grey with at most four messages.");
            Assert.False(count < 4 && textController.TextLine4 is not null, "Unused message rows must be empty.");
        }

        textController.Update(new CtcsData { TextMessages = messages });
        Assert.False(textController.CanScrollTextUp || !textController.CanScrollTextDown || textController.TextLine4?.Content != "3", "Only the down arrow should be enabled at the start.");
        textController.ScrollTextDown();
        Assert.False(!textController.CanScrollTextUp || !textController.CanScrollTextDown ||
            textController.TextLine1?.Content != "5" || textController.TextLine4?.Content != "2",
            "Both directions should be enabled between the ends.");
        textController.ScrollTextDown();
        textController.ScrollTextDown();
        Assert.False(!textController.CanScrollTextUp || textController.CanScrollTextDown ||
            textController.TextLine1?.Content != "4" || textController.TextLine4?.Content != "1",
            "Scrolling must stop at the last four messages.");
        textController.ScrollTextUp();
        textController.ScrollTextUp();
        textController.ScrollTextUp();
        Assert.False(textController.CanScrollTextUp || textController.TextLine1?.Content != "6", "Scrolling must stop at the first message.");
        textController.ScrollTextDown();
        textController.ScrollTextDown();
        textController.Update(new CtcsData { TextMessages = new[] { new CtcsTextMessage { Content = "replacement" } } });
        Assert.False(textController.CanScrollTextUp || textController.CanScrollTextDown ||
            textController.TextLine1?.Content != "replacement" || textController.TextLine2 is not null,
            "Reducing the message count must clamp the scroll position.");

        var retained = new CtcsTextMessage
        {
            Content = "confirm", Type = CtcsTextMessageType.RetainedConfirmation,
            Timestamp = DateTimeOffset.Now.AddMinutes(1)
        };
        textController.Update(new CtcsData { TextMessages = new[] { retained, messages[0] } });
        Assert.False(textController.TextLine1?.IsWhite != true || textController.TextLine2?.IsWhite != false ||
            textController.TextLine1?.ShowConfirmationBorder != true,
            "Newest messages must be white and pending confirmations must show a border.");
        textController.ToggleTextConfirmationBlink();
        Assert.False(textController.TextLine1?.ShowConfirmationBorder != false, "The confirmation border must alternate visibility.");
        textController.ConfirmTextMessage(retained.Id);
        Assert.False(textController.TextLine1?.IsWhite != false || textController.TextLine1?.Message.IsConfirmed != true ||
            textController.HasPendingTextConfirmation,
            "Retained confirmed messages must become grey and stop blinking.");
        var transient = retained with
        {
            Id = Guid.NewGuid(), Timestamp = retained.Timestamp.AddSeconds(1),
            Type = CtcsTextMessageType.TransientConfirmation
        };
        textController.Update(textController.Data with
        {
            TextMessages = textController.Data.TextMessages.Append(transient).ToArray()
        });
        textController.ConfirmTextMessage(transient.Id);
        textController.Update(textController.Data);
        Assert.False(textController.TextLine1?.Message.Id != retained.Id || textController.TextLine1?.IsWhite != false, "Transient confirmations must disappear without making old messages white.");
    }

    [AvaloniaFact]
    public void FunctionKeys_RefreshLabelsAndAvailability()
    {
        var core = new CtcsController(CtcsNavigationKeys.Create(FunctionKeyMenu.NavigationBindings));
        var model = new CtcsViewModel(core, CtcsScreenProfile.CTCS2_200H);
        Assert.False(model.FunctionKeys.Count != 8 || model.FunctionKeys[0].Label != "数据" ||
            !model.FunctionKeys[0].IsEnabled || model.FunctionKeys[7].IsEnabled || model.FunctionKeys[7].IsEmpty,
            "The root menu must show configured navigation and disable the unconfigured vigilance action.");
        var original = model.FunctionKeys;
        Assert.False(!model.KeyHandle(CtcsButton.F5) || model.FunctionKeys[0].Label != "音量" ||
            !model.FunctionKeys[3].IsEmpty || model.FunctionKeys[3].IsEnabled || !model.FunctionKeys[7].IsEnabled ||
            original[0].Label != "数据",
            "Changing steps must publish fresh labels and disable empty slots without changing old snapshots.");
        Assert.False(!model.KeyHandle(CtcsButton.F1) || model.FunctionKeys[0].Label != "大" ||
            model.FunctionKeys[0].IsEnabled || model.FunctionKeys[0].IsEmpty || !model.FunctionKeys[2].IsEmpty || model.FunctionKeys[2].IsEnabled,
            "Unimplemented operations and absent entries must remain disabled.");
        var state = model.CurrentInteraction;
        Assert.False(model.KeyHandle(CtcsButton.F1) || !ReferenceEquals(model.CurrentInteraction, state), "A disabled operation must not change the interaction state.");
        Assert.False(!model.KeyHandle(CtcsButton.F8) || model.FunctionKeys[0].Label != "音量" ||
            !model.KeyHandle(CtcsButton.F8) || model.FunctionKeys[0].Label != "数据" || model.FunctionKeys[7].IsEnabled,
            "Back must restore the correct menu and current availability.");
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => FunctionKeyMenu.GetItems((CtcsInteractionStep)(-1)));
    }

    [AvaloniaFact]
    public void FunctionKeyMenu_HasExpectedStructure()
    {
        Assert.False(FunctionKeyMenu.Items.Count != 8, "The primary function key menu must contain F1-F8.");

        CheckUniqueKeys(FunctionKeyMenu.Items, "根菜单");

        CheckPath(CtcsButton.F1, CtcsButton.F1, CtcsButton.F7, "删除");
        CheckPath(CtcsButton.F1, CtcsButton.F3, CtcsButton.F2, "16辆");
        CheckPath(CtcsButton.F1, CtcsButton.F6, CtcsButton.F2, "时间");
        CheckPath(CtcsButton.F2, CtcsButton.F2, CtcsButton.F6, "确定");
        CheckPath(CtcsButton.F3, CtcsButton.F2, CtcsButton.F8, "取消");
        CheckPath(CtcsButton.F4, CtcsButton.F2, CtcsButton.F6, "确定");
        CheckPath(CtcsButton.F5, CtcsButton.F1, CtcsButton.F1, "大");
        CheckPath(CtcsButton.F6, CtcsButton.F8, null, "取消");
        CheckPath(CtcsButton.F7, CtcsButton.F6, null, "确定");

        var vigilance = FunctionKeyMenu.Items.Single(item => item.Key == CtcsButton.F8);
        Assert.False(vigilance.Label != "警惕" || vigilance.Children.Count != 0, "The F8 vigilance/text confirmation relationship is incorrect.");
    }

    [AvaloniaFact]
    public void CancelKeys_ReturnExactlyOneStep() => CheckCancelNavigation(FunctionKeyMenu.Items, Array.Empty<CtcsButton>());

    private static void CheckCancelNavigation(IReadOnlyList<FunctionKeyMenuItem> items, IReadOnlyList<CtcsButton> path)
    {
        foreach (var cancel in items.Where(item => item.Label == "取消"))
        {
            var core = new CtcsController(CtcsNavigationKeys.Create(FunctionKeyMenu.NavigationBindings));
            var model = new CtcsViewModel(core, CtcsScreenProfile.CTCS2_200H);
            foreach (var key in path)
                Assert.False(!model.KeyHandle(key), "Every cancellation check must reach its menu through configured keys.");
            var data = model.Data;
            var history = model.History;
            var previous = history[^1];
            Assert.False(!model.FunctionKeys.Single(key => key.Key == cancel.Key).IsEnabled ||
                !model.KeyHandle(cancel.Key) || model.CurrentInteraction != (previous with { Input = "" }) ||
                model.History.Count != history.Count - 1 || !ReferenceEquals(model.Data, data),
                "Cancel must be enabled and return exactly one step without submitting business data.");
        }
        foreach (var item in items.Where(item => item.TargetStep is not null))
            CheckCancelNavigation(item.Children, path.Append(item.Key).ToArray());
    }

    private static void CheckUniqueKeys(IReadOnlyList<FunctionKeyMenuItem> items, string path)
    {
        Assert.False(items.Select(item => item.Key).Distinct().Count() != items.Count, $"Duplicate function keys under {path}.");

        foreach (var item in items)
            CheckUniqueKeys(item.Children, $"{path}/{item.Label}");
    }

    private static void CheckPath(CtcsButton first, CtcsButton second, CtcsButton? third, string expectedLabel)
    {
        var item = FunctionKeyMenu.Items.Single(node => node.Key == first)
            .Children.Single(node => node.Key == second);
        if (third is not null)
            item = item.Children.Single(node => node.Key == third);
        Assert.False(item.Label != expectedLabel, $"Unexpected function key relationship for {first}/{second}/{third}.");
    }

    private void CheckDisplay(CtcsData input, SpeedDisplayState expected, double? endSpeed)
    {
        Controller.Update(input);
        Assert.False(Controller.DisplayState != expected || Controller.WarningBandEndSpeed != endSpeed, $"Incorrect display priority/range at speed {input.Speed}: {Controller.DisplayState}.");
        Assert.False(Controller.IsTargetMonitoring != (expected == SpeedDisplayState.TargetMonitoring) ||
            Controller.IsPermittedExceeded != (expected == SpeedDisplayState.PermittedExceeded) ||
            Controller.IsSbiExceeded != (expected == SpeedDisplayState.SbiExceeded),
            "Pointer colour flags do not match the selected state.");
    }

    private void Check(CtcsData input, SpeedMonitoringSection expected)
    {
        Controller.Update(input);
        Assert.False(Controller.Section != expected || Controller.IsTsm != (expected == SpeedMonitoringSection.TSM), $"Unexpected section for {input}.");
        Assert.False(Controller.Data with { TextMessages = input.TextMessages } != input ||
            !Controller.Data.TextMessages.SequenceEqual(input.TextMessages),
            "Speed data was not preserved.");
    }
}
