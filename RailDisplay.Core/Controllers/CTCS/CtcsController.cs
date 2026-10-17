using RailDisplay.Core.Controllers.Interaction;
using RailDisplay.Core.Models.CTCS;

namespace RailDisplay.Core.Controllers.CTCS;

/// <summary>CTCS 共用协调入口；统一调度运行逻辑与设备逻辑，每个会话使用独立实例。</summary>
public sealed class CtcsController : ICtcsController
{
    private readonly CtcsCore _running = new();
    private readonly CtcsDevice _device;

    public CtcsController() : this(new KeyHandler<CtcsButton, CtcsController>([])) { }

    public CtcsController(KeyHandler<CtcsButton, CtcsController> keyHandler)
    {
        _device = new CtcsDevice(keyHandler);
    }

    public CtcsData Data => _running.Data;
    public CtcsBrakeLevel BrakeLevel => _running.BrakeLevel;
    public SpeedMonitoringSection Section => _running.Section;
    public bool IsPermittedSpeedExceeded => _running.IsPermittedSpeedExceeded;
    public bool IsInterventionSpeedReached => _running.IsInterventionSpeedReached;
    public CtcsInteractionState CurrentInteraction => _device.CurrentInteraction;
    public IReadOnlyList<CtcsInteractionState> History => _device.History;
    // 以运行侧最新模式解释设备根状态；交互历史不复制运行数据。
    public CtcsInteractionRoot CurrentInteractionRoot => CtcsInteractionTree.ForMode(Data.ControlMode);

    public void Update(CtcsData data) => _running.Update(data);
    public void AdvanceBrakeWarning(TimeSpan elapsed) => _running.AdvanceBrakeWarning(elapsed);
    public bool ConfirmTextMessage(Guid id) => _running.ConfirmTextMessage(id);

    public bool KeyHandle(CtcsButton button) => _device.KeyHandle(button, this);
    public bool CanHandle(CtcsButton button) => _device.CanHandle(button, this);
    public bool Next(CtcsInteractionState target) => _device.Next(target);
    public bool Back(int count = 1, bool keepInput = false) => _device.Back(count, keepInput);
}
