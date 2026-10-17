using System;
using System.Collections.Generic;
using System.Linq;
using RailDisplay.Core.Controllers.CTCS;
using RailDisplay.Core.Controllers.Interaction;
using RailDisplay.ViewModels.CTCS;
using RailDisplay.Models.CTCS;
using RailDisplay.Core.Models.CTCS;

internal static class CtcsDisplayChecks
{
    public static void Run()
    {
        var controller = new CtcsViewModel(new CtcsController(), CtcsScreenProfile.CTCS2_200H);
        if (controller.Data.CurrentLevel != CtcsLevel.Level2 || controller.CurrentLevelName != "CTCS 2")
            throw new Exception("The default CTCS level must be 2.");
        var data = new CtcsData
        {
            Speed = 150, PermittedSpeed = 200, TargetSpeed = 80,
            TargetDistance = 1001, TsmEntryDistance = 1000,
            SbiSpeed = 210, EbiSpeed = 220
        };
        Check(data, SpeedMonitoringSection.CSM);
        Check(data with { TargetDistance = 1000 }, SpeedMonitoringSection.TSM);
        Check(data with { TargetDistance = 999 }, SpeedMonitoringSection.TSM);
        Check(data with { TargetSpeed = 0, TargetDistance = 0 }, SpeedMonitoringSection.TSM);
        Check(data with { TargetSpeed = null, TargetDistance = null }, SpeedMonitoringSection.CSM);
        Check(data, SpeedMonitoringSection.CSM);

        var validSnapshot = controller.Data;
        foreach (var invalid in new[]
        {
            data with { TargetDistance = null },
            data with { TsmEntryDistance = null },
            data with { Speed = double.NaN },
            data with { EbiSpeed = -1 },
            data with { CurrentLevel = (CtcsLevel)1 },
            data with { BrakeLevel = (CtcsBrakeLevel)5 },
            data with { TextMessages = null! },
            data with { TextMessages = new[] { (CtcsTextMessage)null! } }
        })
        {
            try
            {
                controller.Update(invalid);
                throw new Exception("Invalid input was accepted.");
            }
            catch (ArgumentException)
            {
                if (!ReferenceEquals(controller.Data, validSnapshot))
                    throw new Exception("Invalid input changed the current state.");
            }
        }
        bool bandVisibilityNotified = false;
        controller.PropertyChanged += (_, e) =>
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
                controller.Update(data with { ControlMode = mode, TargetSpeed = target });
                if (controller.ShowLightBand || !bandVisibilityNotified)
                    throw new Exception($"Light bands were not hidden/notified for {mode}.");
                if (controller.Data.PermittedSpeed != data.PermittedSpeed)
                    throw new Exception("Hiding light bands changed the end marker speed.");

                controller.Update(data with { ControlMode = CtcsControlMode.FullSupervision });
                if (!controller.ShowLightBand)
                    throw new Exception("Returning to full supervision did not restore light bands.");
            }
        }
        controller.Update(data with { ControlMode = CtcsControlMode.Guiding });
        if (controller.ControlModeShortName != "引导")
            throw new Exception("The guiding mode label is missing.");
        bool csgVisibilityNotified = false;
        controller.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CtcsViewModel.ShowCsg))
                csgVisibilityNotified = true;
        };
        foreach (var mode in Enum.GetValues<CtcsControlMode>())
        {
            foreach (double distance in new[] { 1001.0, 800.0 })
            {
                csgVisibilityNotified = false;
                controller.Update(data with { ControlMode = mode, TargetDistance = distance });
                if (controller.ShowCsg != (mode == CtcsControlMode.FullSupervision) || !csgVisibilityNotified)
                    throw new Exception($"Only full supervision must display the complete CSG, including in {controller.Section}.");
                controller.Update(data with { ControlMode = CtcsControlMode.FullSupervision, TargetDistance = distance });
                if (!controller.ShowCsg)
                    throw new Exception("Returning to full supervision must restore the CSG.");
            }
        }
        var tsmData = data with { TargetDistance = 800, ControlMode = CtcsControlMode.FullSupervision };
        CheckDisplay(tsmData with { Speed = 0 }, SpeedDisplayState.TargetMonitoring, null);
        CheckDisplay(tsmData with { Speed = 80 }, SpeedDisplayState.TargetMonitoring, null);
        CheckDisplay(tsmData with { Speed = 80.01 }, SpeedDisplayState.TargetMonitoring, null);
        CheckDisplay(tsmData with { Speed = 200 }, SpeedDisplayState.TargetMonitoring, null);
        CheckDisplay(tsmData with { Speed = 200.01 }, SpeedDisplayState.PermittedExceeded, 210);
        CheckDisplay(tsmData with { Speed = 210 }, SpeedDisplayState.SbiExceeded, 220);
        CheckDisplay(tsmData with { Speed = 210.01 }, SpeedDisplayState.SbiExceeded, 220);
        CheckDisplay(tsmData with { Speed = 220 }, SpeedDisplayState.SbiExceeded, 220);
        CheckDisplay(tsmData with { Speed = 205 }, SpeedDisplayState.PermittedExceeded, 210);
        CheckDisplay(tsmData with { Speed = 150 }, SpeedDisplayState.TargetMonitoring, null);
        CheckDisplay(tsmData with { Speed = 80 }, SpeedDisplayState.TargetMonitoring, null);
        CheckDisplay(data with { Speed = 150 }, SpeedDisplayState.Normal, null);
        CheckDisplay(data with { Speed = 205, TargetSpeed = null }, SpeedDisplayState.PermittedExceeded, 210);
        CheckDisplay(data with { Speed = 205, SbiSpeed = null }, SpeedDisplayState.PermittedExceeded, 220);
        CheckDisplay(data with { Speed = 220, SbiSpeed = null }, SpeedDisplayState.SbiExceeded, 220);
        CheckDisplay(data with { Speed = 215, EbiSpeed = null }, SpeedDisplayState.SbiExceeded, null);
        foreach (var mode in new[]
        {
            CtcsControlMode.PartialSupervision, CtcsControlMode.OnSight,
            CtcsControlMode.Shunting, CtcsControlMode.Guiding, CtcsControlMode.Standby
        })
        {
            CheckDisplay(tsmData with { Speed = 215, ControlMode = mode }, SpeedDisplayState.SbiExceeded, 220);
            if (controller.ShowLightBand)
                throw new Exception($"Overspeed unexpectedly enabled hidden bands in {mode}.");
            CheckDisplay(tsmData with { Speed = 150, ControlMode = mode }, SpeedDisplayState.Normal, null);
        }
        controller.Update(tsmData with { Speed = 210 });
        if (controller.InterventionSpeed != 220)
            throw new Exception("The display plan selects EBI at exactly SBI.");
        controller.Update(tsmData with { Speed = 210.01 });
        if (controller.InterventionSpeed != 220 || !controller.IsSbiExceeded)
            throw new Exception("Passing SBI must extend Vint to EBI without reverting to orange.");
        controller.Update(tsmData with { Speed = 205, TargetDistance = 2000 });
        if (!controller.IsPermittedBandYellow)
            throw new Exception("CSM overspeed must turn the permitted band yellow.");
        controller.Update(tsmData with { Speed = 200, TargetDistance = 2000 });
        if (controller.IsPermittedBandYellow)
            throw new Exception("CSM normal speed must restore the grey permitted band.");
        CheckTextScrolling();
        CheckFunctionKeyMenu();
        CheckD6PageTitles();
        CheckButtonEntry();
        CheckBoundButtonNotifications();
        CheckFunctionKeyRefresh();
        CheckCancelNavigation(FunctionKeyMenu.Items, Array.Empty<CtcsButton>());
        Console.WriteLine("CTCS and function key relationship checks passed.");

        void CheckD6PageTitles()
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
                if (step is { } target && !core.Next(new CtcsInteractionState { Step = target }))
                    throw new Exception($"Could not set interaction step {target} for the D6 display check.");
                core.Update(new CtcsData { ControlMode = CtcsControlMode.FullSupervision });
                var pageModel = new CtcsViewModel(core, CtcsScreenProfile.CTCS2_200H);
                if (pageModel.D6PageTitle != title || pageModel.ShowD6PageTitle != (title.Length > 0) ||
                    pageModel.ShowDistanceScale != (step is null))
                    throw new Exception($"Unexpected D6 title or plan visibility for {step}.");
            }
        }

        void CheckButtonEntry()
        {
            var core = new CtcsController();
            var viewModel = new CtcsViewModel(core, CtcsScreenProfile.CTCS2_200H);
            var snapshot = viewModel.Data;
            foreach (var button in Enum.GetValues<CtcsButton>())
                if (viewModel.KeyHandle(button) || !ReferenceEquals(viewModel.Data, snapshot) ||
                    !ReferenceEquals(viewModel.CurrentInteraction, core.CurrentInteraction))
                    throw new Exception("The DMI must read core interaction state without consuming unimplemented keys.");
            try
            {
                viewModel.KeyHandle((CtcsButton)(-1));
                throw new Exception("The view model accepted an invalid button.");
            }
            catch (ArgumentOutOfRangeException)
            {
                if (!ReferenceEquals(viewModel.Data, snapshot))
                    throw new Exception("An invalid button changed the displayed snapshot.");
            }
        }

        void CheckBoundButtonNotifications()
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
            if (model.ControlModeShortName != "待机" || !model.KeyHandle(CtcsButton.F1) ||
                !ReferenceEquals(model.CurrentInteraction, core.CurrentInteraction) || model.CurrentInteraction.Input != "12345" ||
                model.History.Count != 1 || !notifications.Contains(nameof(CtcsViewModel.CurrentInteraction)) ||
                !notifications.Contains(nameof(CtcsViewModel.History)))
                throw new Exception("Handled keys must refresh core interaction state and history through the view model.");
            var draft = model.CurrentInteraction;
            var history = model.History;
            notifications.Clear();
            model.Update(model.Data with { ControlMode = CtcsControlMode.FullSupervision });
            if (model.CurrentInteractionRoot.ControlMode != CtcsControlMode.FullSupervision ||
                !ReferenceEquals(model.CurrentInteraction, draft) || !ReferenceEquals(model.History, history) ||
                !notifications.Contains(nameof(CtcsViewModel.CurrentInteractionRoot)))
                throw new Exception("Mode changes must notify the root while preserving editing state and history.");
            notifications.Clear();
            if (model.KeyHandle(CtcsButton.F8) || notifications.Count != 0)
                throw new Exception("Unbound keys must not refresh or change presentation state.");
            if (!model.KeyHandle(CtcsButton.F2) || model.CurrentInteraction.Step is not null || model.History.Count != 0 ||
                model.CurrentInteractionRoot.ControlMode != CtcsControlMode.FullSupervision)
                throw new Exception("A handled Back must refresh the root using the latest operating mode.");
        }

        void CheckTextScrolling()
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
                if (textController.CanScrollTextUp || textController.CanScrollTextDown)
                    throw new Exception("Both arrows must remain grey with at most four messages.");
                if (count < 4 && textController.TextLine4 is not null)
                    throw new Exception("Unused message rows must be empty.");
            }

            textController.Update(new CtcsData { TextMessages = messages });
            if (textController.CanScrollTextUp || !textController.CanScrollTextDown || textController.TextLine4?.Content != "3")
                throw new Exception("Only the down arrow should be enabled at the start.");
            textController.ScrollTextDown();
            if (!textController.CanScrollTextUp || !textController.CanScrollTextDown ||
                textController.TextLine1?.Content != "5" || textController.TextLine4?.Content != "2")
                throw new Exception("Both directions should be enabled between the ends.");
            textController.ScrollTextDown();
            textController.ScrollTextDown();
            if (!textController.CanScrollTextUp || textController.CanScrollTextDown ||
                textController.TextLine1?.Content != "4" || textController.TextLine4?.Content != "1")
                throw new Exception("Scrolling must stop at the last four messages.");
            textController.ScrollTextUp();
            textController.ScrollTextUp();
            textController.ScrollTextUp();
            if (textController.CanScrollTextUp || textController.TextLine1?.Content != "6")
                throw new Exception("Scrolling must stop at the first message.");
            textController.ScrollTextDown();
            textController.ScrollTextDown();
            textController.Update(new CtcsData { TextMessages = new[] { new CtcsTextMessage { Content = "replacement" } } });
            if (textController.CanScrollTextUp || textController.CanScrollTextDown ||
                textController.TextLine1?.Content != "replacement" || textController.TextLine2 is not null)
                throw new Exception("Reducing the message count must clamp the scroll position.");

            var retained = new CtcsTextMessage
            {
                Content = "confirm", Type = CtcsTextMessageType.RetainedConfirmation,
                Timestamp = DateTimeOffset.Now.AddMinutes(1)
            };
            textController.Update(new CtcsData { TextMessages = new[] { retained, messages[0] } });
            if (textController.TextLine1?.IsWhite != true || textController.TextLine2?.IsWhite != false ||
                textController.TextLine1?.ShowConfirmationBorder != true)
                throw new Exception("Newest messages must be white and pending confirmations must show a border.");
            textController.ToggleTextConfirmationBlink();
            if (textController.TextLine1?.ShowConfirmationBorder != false)
                throw new Exception("The confirmation border must alternate visibility.");
            textController.ConfirmTextMessage(retained.Id);
            if (textController.TextLine1?.IsWhite != false || textController.TextLine1?.Message.IsConfirmed != true ||
                textController.HasPendingTextConfirmation)
                throw new Exception("Retained confirmed messages must become grey and stop blinking.");
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
            if (textController.TextLine1?.Message.Id != retained.Id || textController.TextLine1?.IsWhite != false)
                throw new Exception("Transient confirmations must disappear without making old messages white.");
        }

        void CheckFunctionKeyRefresh()
        {
            var core = new CtcsController(CtcsNavigationKeys.Create(FunctionKeyMenu.NavigationBindings));
            var model = new CtcsViewModel(core, CtcsScreenProfile.CTCS2_200H);
            if (model.FunctionKeys.Count != 8 || model.FunctionKeys[0].Label != "数据" ||
                !model.FunctionKeys[0].IsEnabled || model.FunctionKeys[7].IsEnabled || model.FunctionKeys[7].IsEmpty)
                throw new Exception("The root menu must show configured navigation and disable the unconfigured vigilance action.");
            var original = model.FunctionKeys;
            if (!model.KeyHandle(CtcsButton.F5) || model.FunctionKeys[0].Label != "音量" ||
                !model.FunctionKeys[3].IsEmpty || model.FunctionKeys[3].IsEnabled || !model.FunctionKeys[7].IsEnabled ||
                original[0].Label != "数据")
                throw new Exception("Changing steps must publish fresh labels and disable empty slots without changing old snapshots.");
            if (!model.KeyHandle(CtcsButton.F1) || model.FunctionKeys[0].Label != "大" ||
                model.FunctionKeys[0].IsEnabled || model.FunctionKeys[0].IsEmpty || !model.FunctionKeys[2].IsEmpty || model.FunctionKeys[2].IsEnabled)
                throw new Exception("Unimplemented operations and absent entries must remain disabled.");
            var state = model.CurrentInteraction;
            if (model.KeyHandle(CtcsButton.F1) || !ReferenceEquals(model.CurrentInteraction, state))
                throw new Exception("A disabled operation must not change the interaction state.");
            if (!model.KeyHandle(CtcsButton.F8) || model.FunctionKeys[0].Label != "音量" ||
                !model.KeyHandle(CtcsButton.F8) || model.FunctionKeys[0].Label != "数据" || model.FunctionKeys[7].IsEnabled)
                throw new Exception("Back must restore the correct menu and current availability.");
            try { FunctionKeyMenu.GetItems((CtcsInteractionStep)(-1)); throw new Exception("Unknown menu step accepted."); }
            catch (ArgumentOutOfRangeException) { }
        }

        void CheckCancelNavigation(IReadOnlyList<FunctionKeyMenuItem> items, IReadOnlyList<CtcsButton> path)
        {
            foreach (var cancel in items.Where(item => item.Label == "取消"))
            {
                var core = new CtcsController(CtcsNavigationKeys.Create(FunctionKeyMenu.NavigationBindings));
                var model = new CtcsViewModel(core, CtcsScreenProfile.CTCS2_200H);
                foreach (var key in path)
                    if (!model.KeyHandle(key))
                        throw new Exception("Every cancellation check must reach its menu through configured keys.");
                var data = model.Data;
                var history = model.History;
                var previous = history[^1];
                if (!model.FunctionKeys.Single(key => key.Key == cancel.Key).IsEnabled ||
                    !model.KeyHandle(cancel.Key) || model.CurrentInteraction != (previous with { Input = "" }) ||
                    model.History.Count != history.Count - 1 || !ReferenceEquals(model.Data, data))
                    throw new Exception("Cancel must be enabled and return exactly one step without submitting business data.");
            }
            foreach (var item in items.Where(item => item.TargetStep is not null))
                CheckCancelNavigation(item.Children, path.Append(item.Key).ToArray());
        }

        void CheckFunctionKeyMenu()
        {
            if (FunctionKeyMenu.Items.Count != 8)
                throw new Exception("The primary function key menu must contain F1-F8.");

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
            if (vigilance.Label != "警惕" || vigilance.Children.Count != 0)
                throw new Exception("The F8 vigilance/text confirmation relationship is incorrect.");
        }

        void CheckUniqueKeys(IReadOnlyList<FunctionKeyMenuItem> items, string path)
        {
            if (items.Select(item => item.Key).Distinct().Count() != items.Count)
                throw new Exception($"Duplicate function keys under {path}.");

            foreach (var item in items)
                CheckUniqueKeys(item.Children, $"{path}/{item.Label}");
        }

        void CheckPath(CtcsButton first, CtcsButton second, CtcsButton? third, string expectedLabel)
        {
            var item = FunctionKeyMenu.Items.Single(node => node.Key == first)
                .Children.Single(node => node.Key == second);
            if (third is not null)
                item = item.Children.Single(node => node.Key == third);
            if (item.Label != expectedLabel)
                throw new Exception($"Unexpected function key relationship for {first}/{second}/{third}.");
        }

        void CheckDisplay(CtcsData input, SpeedDisplayState expected, double? endSpeed)
        {
            controller.Update(input);
            if (controller.DisplayState != expected || controller.WarningBandEndSpeed != endSpeed)
                throw new Exception($"Incorrect display priority/range at speed {input.Speed}: {controller.DisplayState}.");
            if (controller.IsTargetMonitoring != (expected == SpeedDisplayState.TargetMonitoring) ||
                controller.IsPermittedExceeded != (expected == SpeedDisplayState.PermittedExceeded) ||
                controller.IsSbiExceeded != (expected == SpeedDisplayState.SbiExceeded))
                throw new Exception("Pointer colour flags do not match the selected state.");
        }

        void Check(CtcsData input, SpeedMonitoringSection expected)
        {
            controller.Update(input);
            if (controller.Section != expected || controller.IsTsm != (expected == SpeedMonitoringSection.TSM))
                throw new Exception($"Unexpected section for {input}.");
            if (controller.Data with { TextMessages = input.TextMessages } != input ||
                !controller.Data.TextMessages.SequenceEqual(input.TextMessages))
                throw new Exception("Speed data was not preserved.");
        }
    }
}
