using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RailDisplay.Controls;
using RailDisplay.Controls.CTCS;
using RailDisplay.Core.Controllers.CTCS;
using RailDisplay.Core.Models.CTCS;
using RailDisplay.Debug;
using RailDisplay.Debug.ViewModels;
using RailDisplay.Models.CTCS;
using RailDisplay.ViewModels.CTCS;
using RailDisplay.Views;
using Avalonia.Headless.XUnit;

namespace RailDisplay.Tests.Rendering;

public sealed class CtcsKeyUiTests
{
    [AvaloniaFact(Skip = SkipReasons.FunctionKeyBorderRuleUndecided)]
    public void FunctionKeyBordersAre2px()
    {
        var view = new CTCS2_200H { DataContext = new CtcsViewModel(new CtcsController(), CtcsScreenProfile.CTCS2_200H) };
        var window = new Window { Content = view, Width = 640, Height = 480 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            foreach (int index in Enumerable.Range(1, 8))
                Assert.Equal(new Thickness(0, 0, 2, 2), view.FindControl<Border>($"F{index}")!.BorderThickness);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DebugKeysDriveDisplayOnlyDmi()
    {
        var core = new CtcsController(CtcsNavigationKeys.Create(FunctionKeyMenu.NavigationBindings));
        var model = new CtcsViewModel(core, CtcsScreenProfile.CTCS2_200H);
        model.Update(model.Data with { ControlMode = CtcsControlMode.FullSupervision });
        var view = new CTCS2_200H { DataContext = model };
        var display = (PixelDisplay)view.Content!;
        var owner = new Window { Content = view, Width = 640, Height = 480 };
        var plugin = new DebugPluginLoader(owner, model);
        try
        {
            owner.Show();
            Dispatcher.UIThread.RunJobs();
            CheckD6Title("");
            foreach (bool pixelMode in new[] { false, true })
            foreach (var size in new[] { new Size(320, 240), new Size(640, 480), new Size(1280, 960) })
            {
                display.UsePixelRendering = pixelMode;
                owner.Width = size.Width;
                owner.Height = size.Height;
                view.Measure(size);
                view.Arrange(new Rect(size));
                display.ForceRedraw();
                CheckDisplayOnly();
            }

            Wait(plugin.ShowAsync());
            var debugWindow = owner.OwnedWindows.Single(child => child.DataContext is DebugViewModel);
            var handles = (DebugViewModel)debugWindow.DataContext!;
            var buttons = debugWindow.GetVisualDescendants().OfType<Button>()
                .Where(button => ReferenceEquals(button.Command, handles.PressButtonCommand)).ToArray();
            Assert.False(buttons.Length != 19 || buttons.Select(button => button.CommandParameter).Distinct().Count() != 19, "The loaded Debug plugin must bind every physical key exactly once.");
            Button DebugKey(CtcsButton key) => buttons.Single(button => Equals(button.CommandParameter, key));
            Assert.False(DebugKey(CtcsButton.F8).IsEffectivelyEnabled || DebugKey(CtcsButton.Digit1).IsEffectivelyEnabled, "The Debug window must disable unconfigured keys.");
            DebugKey(CtcsButton.F5).Command!.Execute(CtcsButton.F5);
            Dispatcher.UIThread.RunJobs();
            Assert.False(Text("F1") != "音量" || !DebugKey(CtcsButton.F8).IsEffectivelyEnabled, "Debug actions must navigate DMI and refresh Debug availability.");
            CheckD6Title("其他选项");
            DebugKey(CtcsButton.F1).Command!.Execute(CtcsButton.F1);
            Dispatcher.UIThread.RunJobs();
            Assert.False(Text("F1") != "大" || Text("F3") != "" || Text("F8") != "返回" ||
                DebugKey(CtcsButton.F1).IsEffectivelyEnabled || DebugKey(CtcsButton.F2).IsEffectivelyEnabled,
                "Debug navigation must refresh DMI labels and disable unsupported operations.");
            CheckD6Title("音量设置");
            CheckDisplayOnly();
            DebugKey(CtcsButton.F8).Command!.Execute(CtcsButton.F8);
            DebugKey(CtcsButton.F8).Command!.Execute(CtcsButton.F8);
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.CurrentInteraction.Step is not null || DebugKey(CtcsButton.F8).IsEffectivelyEnabled, "Debug Back must refresh the DMI root labels and command availability.");
            CheckD6Title("");
            DebugKey(CtcsButton.F1).Command!.Execute(CtcsButton.F1);
            DebugKey(CtcsButton.F1).Command!.Execute(CtcsButton.F1);
            Dispatcher.UIThread.RunJobs();
            Assert.False(Text("F8") != "取消" || !DebugKey(CtcsButton.F8).IsEffectivelyEnabled, "Debug Cancel must be enabled on an input step.");
            CheckD6Title("司机号输入");
            DebugKey(CtcsButton.F8).Command!.Execute(CtcsButton.F8);
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.CurrentInteraction.Step != CtcsInteractionStep.DataEntry || Text("F8") != "返回", "Debug Cancel must return one step and refresh the DMI menu.");
            Wait(plugin.DisposeAsync().AsTask());
            model.Update(model.Data with { ControlMode = CtcsControlMode.Isolation });
            Assert.False(owner.OwnedWindows.Count != 0, "Disposing the plugin must close its window and detach key refresh subscriptions.");
        }
        finally
        {
            Wait(plugin.DisposeAsync().AsTask());
            owner.Close();
        }

        string Text(string name) => ((TextBlock)view.FindControl<Border>(name)!.Child!).Text ?? "";

        void CheckD6Title(string expected)
        {
            var d6 = view.FindControl<Border>("D6")!;
            bool hasTitleBackground = d6.Background is SolidColorBrush brush && brush.Color == Color.Parse("#555555");
            bool showPlan = model.CurrentInteraction.Step is null &&
                (model.Data.ControlMode is CtcsControlMode.FullSupervision or CtcsControlMode.PartialSupervision);
            Assert.False(Text("D6") != expected || hasTitleBackground != (expected.Length > 0) ||
                view.FindControl<DistanceScale>("DistanceRuler")!.IsVisible != showPlan ||
                view.FindControl<Border>("D7")!.IsVisible != showPlan,
                $"D6 must show '{expected}' and the plan only in the interaction root.");
        }

        void CheckDisplayOnly()
        {
            Assert.False(view.IsHitTestVisible || view.Focusable, "The DMI must be display-only.");
            var state = model.CurrentInteraction;
            var history = model.History;
            foreach (string name in Enumerable.Range(1, 8).Select(index => $"F{index}").Concat(new[] { "E24", "E25" }))
            {
                var region = view.FindControl<Border>(name)!;
                if (name.StartsWith('F'))
                {
                    var label = (TextBlock)region.Child!;
                    bool empty = string.IsNullOrEmpty(label.Text);
                    Assert.False(empty
                        ? region.Background is not SolidColorBrush brush || brush.Color != Color.Parse("#555555")
                        : region.Background is not null,
                        "Only function-key regions without content may use the dark grey background.");
                    Assert.True(label.FontWeight == FontWeight.Bold, "Function-key labels must be bold.");
                }
                var point = region.TranslatePoint(new Point(10, 10), view)!.Value;
                var hit = owner.InputHitTest(region.TranslatePoint(new Point(10, 10), owner)!.Value) as Visual;
                Assert.False(hit is not null && (ReferenceEquals(hit, view) || hit.GetVisualAncestors().Contains(view)), "DMI regions must not receive pointer hit tests at any scale.");
                foreach (var type in new[] { PointerType.Mouse, PointerType.Touch })
                {
                    var args = new PointerPressedEventArgs(region, new Pointer(42, type, true), view, point, 0,
                        new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1);
                    region.RaiseEvent(args);
                    Assert.False(args.Handled || !ReferenceEquals(model.CurrentInteraction, state) || !ReferenceEquals(model.History, history), "DMI mouse and touch events must not change navigation.");
                }
            }
        }
    }

    private static void Wait(Task task)
    {
        var timeout = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(5))
                throw new TimeoutException("Debug key connection check timed out.");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
    }
}
