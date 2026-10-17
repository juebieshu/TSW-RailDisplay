using RailDisplay.Core.Controllers.Interaction;
using RailDisplay.Core.Models.CTCS;

namespace RailDisplay.Core.Controllers.CTCS;

/// <summary>把初始化提供的步骤与实体键配置转换成共用导航处理器，不包含显示文字或业务提交操作。</summary>
public static class CtcsNavigationKeys
{
    public static KeyHandler<CtcsButton, CtcsController> Create(IEnumerable<CtcsNavigationBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var table = new Dictionary<(CtcsInteractionStep? Step, CtcsButton Button), CtcsNavigationBinding>();
        foreach (var binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            if (!Enum.IsDefined(binding.Button) ||
                (binding.Step is { } step && !Enum.IsDefined(step)) ||
                (binding.TargetStep is { } target && !Enum.IsDefined(target)))
                throw new ArgumentOutOfRangeException(nameof(bindings), "导航绑定包含未定义的按键或步骤。");
            if (binding.IsBack == binding.TargetStep.HasValue)
                throw new ArgumentException("每条导航绑定必须且只能指定进入目标或返回。", nameof(bindings));
            if (!table.TryAdd((binding.Step, binding.Button), binding))
                throw new ArgumentException("同一步骤的实体键不能重复绑定。", nameof(bindings));
        }

        bool CanHandle(CtcsButton key, CtcsController controller) =>
            table.TryGetValue((controller.CurrentInteraction.Step, key), out var binding) &&
            (!binding.IsBack || controller.History.Count > 0);

        bool Handle(CtcsButton key, CtcsController controller)
        {
            if (!table.TryGetValue((controller.CurrentInteraction.Step, key), out var binding))
                return false;
            return binding.IsBack
                ? controller.Back()
                : controller.Next(new CtcsInteractionState { Step = binding.TargetStep, Input = "" });
        }

        return new KeyHandler<CtcsButton, CtcsController>(table.Keys.Select(row => row.Button).Distinct()
            .Select(key => new KeyValuePair<CtcsButton, Func<CtcsController, bool>>(key, controller => Handle(key, controller))), CanHandle);
    }
}
