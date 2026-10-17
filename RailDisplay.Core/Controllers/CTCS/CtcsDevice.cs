using RailDisplay.Core.Controllers.Interaction;
using RailDisplay.Core.Models.CTCS;

namespace RailDisplay.Core.Controllers.CTCS;

/// <summary>CTCS 实体按键分发、功能步骤校验及交互导航；不持有运行数据或计算状态。</summary>
internal sealed class CtcsDevice
{
    private readonly InteractionNavigator<CtcsInteractionState> _navigation = new(new(), state => state with { Input = "" });
    private readonly KeyHandler<CtcsButton, CtcsController> _keyHandler;

    public CtcsDevice(KeyHandler<CtcsButton, CtcsController> keyHandler)
    {
        ArgumentNullException.ThrowIfNull(keyHandler);
        _keyHandler = keyHandler;
    }

    public CtcsInteractionState CurrentInteraction => _navigation.Current;
    public IReadOnlyList<CtcsInteractionState> History => _navigation.History;

    // 本次上下文由控制器传入，处理器始终读取同一会话的最新模式与步骤。
    public bool KeyHandle(CtcsButton button, CtcsController controller) => _keyHandler.KeyHandle(button, controller);
    public bool CanHandle(CtcsButton button, CtcsController controller) => _keyHandler.CanHandle(button, controller);

    public bool Next(CtcsInteractionState target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Step is not { } step || !Enum.IsDefined(step))
            throw new ArgumentOutOfRangeException(nameof(target), "前进目标必须为已定义的 CTCS 功能步骤。");
        ArgumentNullException.ThrowIfNull(target.Input);
        _navigation.Next(target);
        return true;
    }

    public bool Back(int count = 1, bool keepInput = false) => _navigation.Back(count, keepInput);
}
