using System;
using System.Collections.Generic;
using System.Linq;
using RailDisplay.Core.Controllers.CTCS;
using RailDisplay.Core.Controllers.Interaction;
using RailDisplay.Core.Models.CTCS;

namespace RailDisplay.Core.Tests;

public sealed class InteractionTests
{
    [Fact]
    public void Navigator_PushesAndRestoresSnapshots()
    {
        // 独立的测试状态，验证导航器不依赖 CTCS 或显示框架。
        var root = new TestState("root", "");
        var first = new TestState("first", "123");
        var second = new TestState("second", "456");
        var third = new TestState("third", "789");
        var navigator = new InteractionNavigator<TestState>(root, state => state with { Input = "" });
        Assert.True(!navigator.Back() && ReferenceEquals(navigator.Current, root), "An empty return stack must be harmless.");
        navigator.Next(first);
        var publishedHistory = navigator.History;
        navigator.Next(second);
        navigator.Next(third);
        Assert.True(navigator.History.SequenceEqual(new[] { root, first, second }) && publishedHistory.SequenceEqual(new[] { root }),
            "Next must push the entire previous state without changing published history snapshots.");
        Assert.ThrowsAny<NotSupportedException>(() => ((IList<TestState>)navigator.History)[0] = second);
        var current = navigator.Current;
        var history = navigator.History;
        Assert.ThrowsAny<ArgumentNullException>(() => navigator.Next(null!));
        Assert.True(ReferenceEquals(navigator.Current, current) && ReferenceEquals(navigator.History, history),
            "Invalid navigation must leave current state and history unchanged.");
        Assert.True(navigator.Back(count: 2, keepInput: true) && ReferenceEquals(navigator.Current, first) &&
                navigator.History.SequenceEqual(new[] { root }),
            "Back can skip multiple steps and restore the target's saved draft.");
        Assert.True(history.SequenceEqual(new[] { root, first, second }), "Back must not mutate published history.");
        navigator.Next(second);
        Assert.True(navigator.Back() && navigator.Current == (first with { Input = "" }) && first.Input == "123",
            "Back must clear the target input by default without modifying the saved snapshot.");
        Assert.True(navigator.Back(int.MaxValue) && navigator.Current == root && navigator.History.Count == 0 && !navigator.Back(),
            "Back beyond available depth must stop at the earliest state.");
        foreach (int count in new[] { 0, -1, int.MinValue })
        {
            var normalized = new InteractionNavigator<TestState>(root, state => state with { Input = "" });
            normalized.Next(first);
            normalized.Next(second);
            Assert.True(normalized.Back(count, keepInput: true) && ReferenceEquals(normalized.Current, first) && normalized.History.Count == 1,
                "Non-positive Back counts must be normalized to one step.");
        }
        Assert.ThrowsAny<ArgumentNullException>(() => new InteractionNavigator<TestState>(null!, state => state));
        Assert.ThrowsAny<ArgumentNullException>(() => new InteractionNavigator<TestState>(root, null!));
        var invalidClear = new InteractionNavigator<TestState>(root, _ => null!);
        invalidClear.Next(first);
        history = invalidClear.History;
        Assert.ThrowsAny<ArgumentNullException>(() => invalidClear.Back());
        Assert.True(ReferenceEquals(invalidClear.Current, first) && ReferenceEquals(invalidClear.History, history),
            "An invalid input cleaner must not partially change navigation.");
    }

    [Fact]
    public void KeyHandler_RoutesUsingCurrentState()
    {
        // 以下绑定仅测试分发机制，不是已确认的实体键业务规则。
        var bindings = new Dictionary<CtcsButton, Func<CtcsController, bool>>
        {
            [CtcsButton.F1] = controller => controller.Next(new CtcsInteractionState
            {
                Step = controller.Data.ControlMode == CtcsControlMode.Standby
                    ? CtcsInteractionStep.DataEntry : CtcsInteractionStep.TrainNumberEntry,
                Input = "test"
            }),
            [CtcsButton.F2] = controller => controller.CurrentInteraction.Step == CtcsInteractionStep.DataEntry &&
                controller.Next(new CtcsInteractionState { Step = CtcsInteractionStep.DriverNumberEntry, Input = "123" }),
            [CtcsButton.F3] = controller => controller.Back(keepInput: true)
        };
        var handler = new KeyHandler<CtcsButton, CtcsController>(bindings);
        bindings.Clear();
        var controller = new CtcsController(handler);
        var root = controller.CurrentInteraction;
        Assert.True(!controller.KeyHandle(CtcsButton.F2) && ReferenceEquals(controller.CurrentInteraction, root),
            "A handler can reject a key based on the current step without changing state.");
        Assert.True(controller.KeyHandle(CtcsButton.F1) && controller.CurrentInteraction.Step == CtcsInteractionStep.DataEntry &&
                controller.KeyHandle(CtcsButton.F2) && controller.CurrentInteraction.Input == "123",
            "Bindings must survive mutation of the source table and route using the current step.");
        var draft = controller.CurrentInteraction;
        var history = controller.History;
        controller.Update(controller.Data with { ControlMode = CtcsControlMode.FullSupervision });
        Assert.True(ReferenceEquals(controller.CurrentInteraction, draft) && ReferenceEquals(controller.History, history),
            "Updating mode while editing must preserve current state and history.");
        Assert.True(controller.KeyHandle(CtcsButton.F1) && controller.CurrentInteraction.Step == CtcsInteractionStep.TrainNumberEntry,
            "The same initialized handler must read the latest operating mode.");
        Assert.True(controller.KeyHandle(CtcsButton.F3) && ReferenceEquals(controller.CurrentInteraction, draft),
            "A key handler can navigate Back without managing history itself.");
        var current = controller.CurrentInteraction;
        history = controller.History;
        Assert.True(!controller.KeyHandle(CtcsButton.F8), "Unbound keys must report unhandled.");
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => controller.KeyHandle((CtcsButton)0));
        Assert.True(ReferenceEquals(controller.CurrentInteraction, current) && ReferenceEquals(controller.History, history),
            "Unbound and invalid keys must not affect navigation.");
        var another = new CtcsController(handler);
        Assert.True(another.CurrentInteraction.Step is null && another.History.Count == 0,
            "Sharing a binding table must not share navigation state between devices.");
        Assert.ThrowsAny<ArgumentNullException>(() => new CtcsController(null!));
        Assert.ThrowsAny<ArgumentNullException>(() => new KeyHandler<CtcsButton, CtcsController>(null!));
        Assert.ThrowsAny<ArgumentNullException>(() => handler.KeyHandle(CtcsButton.F1, null!));
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => new KeyHandler<CtcsButton, CtcsController>(
            new[] { new KeyValuePair<CtcsButton, Func<CtcsController, bool>>((CtcsButton)0, _ => true) }));
        Assert.ThrowsAny<ArgumentNullException>(() => new KeyHandler<CtcsButton, CtcsController>(
            new[] { new KeyValuePair<CtcsButton, Func<CtcsController, bool>>(CtcsButton.F1, null!) }));
        var duplicate = new KeyValuePair<CtcsButton, Func<CtcsController, bool>>(CtcsButton.F1, _ => true);
        Assert.ThrowsAny<ArgumentException>(() => new KeyHandler<CtcsButton, CtcsController>(new[] { duplicate, duplicate }));
        var throwing = new KeyHandler<CtcsButton, CtcsController>(
            new[] { new KeyValuePair<CtcsButton, Func<CtcsController, bool>>(CtcsButton.F1, _ => throw new InvalidOperationException()) });
        Assert.ThrowsAny<InvalidOperationException>(() => throwing.KeyHandle(CtcsButton.F1, controller));
    }

    [Fact]
    public void CtcsController_NavigationPreservesModeAndHistory()
    {
        foreach (bool keepInput in new[] { false, true })
        {
            ICtcsController controller = new CtcsController();
            Assert.True(controller.CurrentInteractionRoot.ControlMode == CtcsControlMode.Standby,
                "A new device must start at its standby root.");
            Assert.True(!controller.Back(), "Back at the initial state must report unhandled.");
            var first = new CtcsInteractionState { Step = CtcsInteractionStep.DataEntry, Input = "first" };
            controller.Next(first);
            controller.Next(new CtcsInteractionState { Step = CtcsInteractionStep.DriverNumberEntry, Input = "123" });
            controller.Next(new CtcsInteractionState { Step = CtcsInteractionStep.TrainNumberEntry, Input = "456" });
            var draft = controller.CurrentInteraction;
            var history = controller.History;
            foreach (var mode in Enum.GetValues<CtcsControlMode>())
            {
                controller.Update(controller.Data with { ControlMode = mode });
                Assert.True(controller.CurrentInteractionRoot.ControlMode == mode &&
                        ReferenceEquals(controller.CurrentInteraction, draft) && ReferenceEquals(controller.History, history),
                    "All modes must update only the root and preserve editing state and history.");
            }
            var root = controller.CurrentInteractionRoot;
            var data = controller.Data;
            foreach (var invalid in new[]
            {
                new CtcsInteractionState(),
                new CtcsInteractionState { Step = (CtcsInteractionStep)(-1) },
                new CtcsInteractionState { Step = (CtcsInteractionStep)int.MaxValue },
                new CtcsInteractionState { Step = CtcsInteractionStep.DataEntry, Input = null! }
            })
                Assert.ThrowsAny<ArgumentException>(() => controller.Next(invalid));
            Assert.ThrowsAny<ArgumentNullException>(() => controller.Next(null!));
            Assert.ThrowsAny<ArgumentOutOfRangeException>(() => controller.Update(data with { ControlMode = (CtcsControlMode)(-1) }));
            Assert.True(ReferenceEquals(controller.CurrentInteraction, draft) && ReferenceEquals(controller.History, history) &&
                    ReferenceEquals(controller.Data, data) && ReferenceEquals(controller.CurrentInteractionRoot, root),
                "Rejected states and modes must not partially change navigation or business data.");
            Assert.True(controller.Back(count: 2, keepInput: keepInput) && controller.CurrentInteraction.Step == first.Step &&
                    controller.CurrentInteraction.Input == (keepInput ? "first" : "") && controller.History.Count == 1 &&
                    ReferenceEquals(controller.CurrentInteractionRoot, root) && ReferenceEquals(controller.Data, data),
                "Back must preserve the latest mode and apply the input option to the actual target step.");
            Assert.True(history.Count == 3 && history[1] == first && first.Input == "first",
                "Back must not mutate old state or history snapshots.");
            Assert.True(controller.Back(int.MaxValue, keepInput) && controller.CurrentInteraction.Step is null &&
                    controller.History.Count == 0 && ReferenceEquals(controller.CurrentInteractionRoot, root) && !controller.Back(),
                "Returning beyond the available depth must stop at the root using the latest operating mode.");
        }
    }

    [Fact]
    public void KeyHandler_AvailabilityFollowsModeAndStep()
    {
        int executed = 0;
        var handler = new KeyHandler<CtcsButton, CtcsController>(
            new Dictionary<CtcsButton, Func<CtcsController, bool>> { [CtcsButton.F1] = _ => { executed++; return true; } },
            (_, core) => core.Data.ControlMode == CtcsControlMode.FullSupervision &&
                core.CurrentInteraction.Step == CtcsInteractionStep.DriverNumberEntry);
        var core = new CtcsController(handler);
        Assert.True(!core.CanHandle(CtcsButton.F1) && !core.KeyHandle(CtcsButton.F1) && executed == 0,
            "Disabled keys must not run their handler.");
        core.Next(new CtcsInteractionState { Step = CtcsInteractionStep.DriverNumberEntry });
        core.Update(core.Data with { ControlMode = CtcsControlMode.FullSupervision });
        Assert.True(core.CanHandle(CtcsButton.F1) && executed == 0 && !core.CanHandle(CtcsButton.F2) &&
                core.KeyHandle(CtcsButton.F1) && executed == 1,
            "Availability queries must be side-effect free and execution must use the same latest mode and step.");
        core.Back();
        Assert.True(!core.CanHandle(CtcsButton.F1) && !core.KeyHandle(CtcsButton.F1) && executed == 1,
            "Changing steps must immediately disable a no-longer-available binding.");
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => core.CanHandle((CtcsButton)0));
        Assert.ThrowsAny<ArgumentNullException>(() => handler.CanHandle(CtcsButton.F1, null!));
    }

    [Fact]
    public void NavigationKeyMap_UsesCurrentStepBindings()
    {
        var bindings = new List<CtcsNavigationBinding>
        {
            new(null, CtcsButton.F1, CtcsInteractionStep.DataEntry),
            new(CtcsInteractionStep.DataEntry, CtcsButton.F1, CtcsInteractionStep.DriverNumberEntry),
            new(CtcsInteractionStep.DataEntry, CtcsButton.F8, IsBack: true),
            new(CtcsInteractionStep.DriverNumberEntry, CtcsButton.F8, IsBack: true)
        };
        var core = new CtcsController(CtcsNavigationKeys.Create(bindings));
        bindings.Clear();
        var root = core.CurrentInteraction;
        Assert.True(core.CanHandle(CtcsButton.F1) && !core.CanHandle(CtcsButton.F8) && ReferenceEquals(core.CurrentInteraction, root),
            "A navigation map must be copied and querying it must not enter a page.");
        Assert.True(core.KeyHandle(CtcsButton.F1) && core.CurrentInteraction.Step == CtcsInteractionStep.DataEntry &&
                core.CanHandle(CtcsButton.F8) && !core.CanHandle(CtcsButton.F6) && !core.KeyHandle(CtcsButton.F6),
            "Entering a step must refresh availability without enabling unconfigured actions.");
        Assert.True(core.KeyHandle(CtcsButton.F1) && core.CurrentInteraction.Step == CtcsInteractionStep.DriverNumberEntry &&
                !core.CanHandle(CtcsButton.F1), "The same F key must use its current-step binding.");
        var state = core.CurrentInteraction;
        var history = core.History;
        core.Update(core.Data with { ControlMode = CtcsControlMode.Isolation });
        Assert.True(ReferenceEquals(core.CurrentInteraction, state) && ReferenceEquals(core.History, history) &&
                core.CanHandle(CtcsButton.F8) && core.KeyHandle(CtcsButton.F8) && core.KeyHandle(CtcsButton.F8) &&
                core.CurrentInteraction.Step is null && core.CurrentInteractionRoot.ControlMode == CtcsControlMode.Isolation,
            "Menu navigation must retain its path across operating-mode updates and return using the latest root.");
        Assert.True(!core.KeyHandle(CtcsButton.Vigilance), "Navigation configuration must not invent vigilance actions.");
        Assert.ThrowsAny<ArgumentNullException>(() => CtcsNavigationKeys.Create(null!));
        Assert.ThrowsAny<ArgumentNullException>(() => CtcsNavigationKeys.Create(new[] { (CtcsNavigationBinding)null! }));
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => CtcsNavigationKeys.Create(new[] { new CtcsNavigationBinding(null, (CtcsButton)0, CtcsInteractionStep.DataEntry) }));
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => CtcsNavigationKeys.Create(new[] { new CtcsNavigationBinding((CtcsInteractionStep)(-1), CtcsButton.F1, CtcsInteractionStep.DataEntry) }));
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => CtcsNavigationKeys.Create(new[] { new CtcsNavigationBinding(null, CtcsButton.F1, (CtcsInteractionStep)(-1)) }));
        Assert.ThrowsAny<ArgumentException>(() => CtcsNavigationKeys.Create(new[] { new CtcsNavigationBinding(null, CtcsButton.F1) }));
        Assert.ThrowsAny<ArgumentException>(() => CtcsNavigationKeys.Create(new[] { new CtcsNavigationBinding(null, CtcsButton.F1, CtcsInteractionStep.DataEntry, IsBack: true) }));
        var duplicate = new CtcsNavigationBinding(null, CtcsButton.F1, CtcsInteractionStep.DataEntry);
        Assert.ThrowsAny<ArgumentException>(() => CtcsNavigationKeys.Create(new[] { duplicate, duplicate }));
    }

    [Fact]
    public void InteractionTree_ContainsAllSteps()
    {
        Assert.True(CtcsInteractionTree.Roots.Count == Enum.GetValues<CtcsControlMode>().Length + 1 &&
                CtcsInteractionTree.Roots.Single(root => root.ControlMode is null).Children.Count == 0,
            "The tree must contain the off root and every operating mode.");
        foreach (var mode in Enum.GetValues<CtcsControlMode>())
            Assert.True(ReferenceEquals(CtcsInteractionTree.ForMode(mode).Children, CtcsInteractionTree.FunctionSteps),
                "Operating modes must reference one shared function tree.");
        var steps = Flatten(CtcsInteractionTree.FunctionSteps).ToArray();
        Assert.True(steps.Length == 22 && steps.Distinct().Count() == steps.Length &&
                steps.OrderBy(step => step).SequenceEqual(Enum.GetValues<CtcsInteractionStep>().OrderBy(step => step)),
            "The function tree must contain all 22 distinct defined steps.");
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => CtcsInteractionTree.ForMode((CtcsControlMode)(-1)));
        Assert.ThrowsAny<NotSupportedException>(() => ((IList<CtcsInteractionRoot>)CtcsInteractionTree.Roots).Clear());
    }

    private static IEnumerable<CtcsInteractionStep> Flatten(IReadOnlyList<CtcsInteractionNode> nodes)
    {
        Assert.ThrowsAny<NotSupportedException>(() => ((IList<CtcsInteractionNode>)nodes).Clear());
        foreach (var node in nodes)
        {
            yield return node.Step;
            foreach (var step in Flatten(node.Children))
                yield return step;
        }
    }



    private sealed record TestState(string Step, string Input);
}
