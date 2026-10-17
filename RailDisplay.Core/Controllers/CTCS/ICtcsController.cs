using RailDisplay.Core.Models.CTCS;

namespace RailDisplay.Core.Controllers.CTCS;

public interface ICtcsController
{
    CtcsData Data { get; }
    CtcsInteractionState CurrentInteraction { get; }
    CtcsInteractionRoot CurrentInteractionRoot { get; }
    /// <summary>返回栈，由最早到最近排列，末项是上一步；不包含当前状态。</summary>
    IReadOnlyList<CtcsInteractionState> History { get; }
    CtcsBrakeLevel BrakeLevel { get; }
    SpeedMonitoringSection Section { get; }
    bool IsPermittedSpeedExceeded { get; }
    bool IsInterventionSpeedReached { get; }
    void Update(CtcsData data);
    void AdvanceBrakeWarning(TimeSpan elapsed);
    bool ConfirmTextMessage(Guid id);
    /// <summary>处理实体按键；返回是否已处理，未定义的枚举值抛出异常。</summary>
    bool KeyHandle(CtcsButton button);
    /// <summary>查询当前模式、步骤下是否存在可执行绑定，不触发操作。</summary>
    bool CanHandle(CtcsButton button);
    /// <summary>保存当前步骤并进入目标步骤，目标输入由处理器提供。</summary>
    bool Next(CtcsInteractionState target);
    /// <summary>返回 count 步，≤0 按一次处理，超过历史深度则返回最早状态；默认清空目标步骤输入。</summary>
    bool Back(int count = 1, bool keepInput = false);
}
