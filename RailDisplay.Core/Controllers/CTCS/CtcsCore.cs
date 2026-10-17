using RailDisplay.Core.Models.CTCS;

namespace RailDisplay.Core.Controllers.CTCS;

/// <summary>CTCS 运行数据、监视区、速度告警、制动锁存、预警预测及运行消息状态。</summary>
internal sealed class CtcsCore
{
    private readonly CtcsBrakeWarningPredictor _brakeWarningPredictor = new();
    // 只记录速度触发的制动，避免覆盖输入状态；当前没有缓解入口，随控制器实例重建清空。
    private CtcsBrakeLevel _speedBrakeLevel = CtcsBrakeLevel.NoBrake;
    /// <summary>最后一批通过校验的数据；TextMessages 经复制后只读发布。</summary>
    public CtcsData Data { get; private set; } = new();
    /// <summary>C9 使用的制动结果：输入与速度锁存取较强者，不参与本地速度积分。</summary>
    public CtcsBrakeLevel BrakeLevel => (CtcsBrakeLevel)Math.Max((int)Data.BrakeLevel, (int)_speedBrakeLevel);
    /// <summary>优先采用输入的显式监视区，否则使用下方标注的临时距离判定。</summary>
    public SpeedMonitoringSection Section { get; private set; }
    // 速度表告警在隔离模式关闭；允许速度用严格大于，干预速度用大于等于。
    public bool IsPermittedSpeedExceeded => Data.ControlMode != CtcsControlMode.Isolation && Data.Speed > Data.PermittedSpeed;
    // 速度表保留 SBI 优先、缺失时取 EBI 的规则；不同于 A1 暂用 EBI 和 C9 严格超过阈值的判定。
    public bool IsInterventionSpeedReached =>
        Data.ControlMode != CtcsControlMode.Isolation &&
        (Data.SbiSpeed ?? Data.EbiSpeed) is { } intervention && Data.Speed >= intervention;

    /// <summary>提交同一批输入。全部校验成功后才更新快照、预测历史和制动锁存；失败保留旧状态。</summary>
    public void Update(CtcsData data)
    {
        // 先复制并校验消息，调用方后续修改原集合不会影响已发布快照。
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(data.TextMessages);
        var messages = data.TextMessages.ToArray();
        foreach (var message in messages)
            if (message is null || message.Content is null || message.Id == Guid.Empty ||
                !Enum.IsDefined(message.Type))
                throw new ArgumentException("文本信息必须有内容、有效类型和标识。", nameof(data));
        // 速度使用 km/h、距离使用 m；缺失可选值用 null，不把未知值补成零。
        Validate(data.Speed, nameof(data.Speed));
        Validate(data.TargetSpeed, nameof(data.TargetSpeed));
        Validate(data.PermittedSpeed, nameof(data.PermittedSpeed));
        Validate(data.SbiSpeed, nameof(data.SbiSpeed));
        Validate(data.EbiSpeed, nameof(data.EbiSpeed));
        // 仅 Tint（秒）允许正无穷，表示本轮预测不冲线；其余数值仍须非负且有限。
        if (data.BrakeWarningTime != double.PositiveInfinity)
            Validate(data.BrakeWarningTime, nameof(data.BrakeWarningTime));
        Validate(data.ReleaseSpeed, nameof(data.ReleaseSpeed));
        if (data.MonitoringSection is { } suppliedSection && !Enum.IsDefined(suppliedSection))
            throw new ArgumentOutOfRangeException(nameof(data.MonitoringSection));
        if (data.SbiSpeed > data.EbiSpeed)
            throw new ArgumentException("SBI 速度不得高于 EBI 速度。", nameof(data));
        if (data.MonitoringSection == SpeedMonitoringSection.RSM && data.ReleaseSpeed is null)
            throw new ArgumentException("RSM 需要提供释放速度。", nameof(data));
        Validate(data.TargetDistance, nameof(data.TargetDistance));
        Validate(data.TsmEntryDistance, nameof(data.TsmEntryDistance));

        if (!Enum.IsDefined(data.CurrentLevel))
            throw new ArgumentOutOfRangeException(nameof(data.CurrentLevel), "CTCS 等级必须为有效枚举值。");

        if (!Enum.IsDefined(data.ControlMode))
            throw new ArgumentOutOfRangeException(nameof(data.ControlMode), "控制模式必须为有效枚举值。");

        if (!Enum.IsDefined(data.BrakeLevel))
            throw new ArgumentOutOfRangeException(nameof(data.BrakeLevel), "制动等级必须是已定义的有效值。");

        // 目标速度必须与目标距离、TSM 入口距离成组提供，避免不完整数据参与区段判断。
        if (data.TargetSpeed.HasValue &&
            (!data.TargetDistance.HasValue || !data.TsmEntryDistance.HasValue))
            throw new ArgumentException("有目标速度时，需要提供目标距离和 TSM 进入距离。", nameof(data));

        // shortcut: 模拟区段使用配置的距离边界，接入列控数据后替换为监督曲线判定。
        var section = data.MonitoringSection ?? (data.TargetSpeed.HasValue &&
                      data.TargetDistance <= data.TsmEntryDistance
            ? SpeedMonitoringSection.TSM
            : SpeedMonitoringSection.CSM);

        // 此处起才提交状态。进入 FS 以当前速度建立首个样本，退出 FS 清空；FS 内区段切换保留历史。
        var snapshot = data with { TextMessages = Array.AsReadOnly(messages) };
        if (data.ControlMode != Data.ControlMode)
            _brakeWarningPredictor.Reset(data.ControlMode == CtcsControlMode.FullSupervision ? data.Speed : null);
        Data = snapshot;
        Section = section;
        ApplySpeedBrake();
    }

    // C9 速度制动只升级不自动回落；排除模式/等级停止新增判定，已有锁存仍保留。
    private void ApplySpeedBrake()
    {
        if (Data.CurrentLevel == CtcsLevel.Level0 ||
            Data.ControlMode is CtcsControlMode.CabSignalling or CtcsControlMode.Isolation)
            return;
        // shortcut: 速度触发只锁存显示状态，不控制本地模拟；缓解规则及入口接入后补充解锁。
        // 先判断 EBI 保证 EB 优先；等于阈值不触发对应升级，缺失阈值不参与判断。
        if (Data.EbiSpeed is { } ebi && Data.Speed > ebi)
            _speedBrakeLevel = CtcsBrakeLevel.EmergencyBrake;
        else if (Data.SbiSpeed is { } sbi && Data.Speed > sbi && _speedBrakeLevel < CtcsBrakeLevel.ServiceBrake)
            _speedBrakeLevel = CtcsBrakeLevel.ServiceBrake;
    }

    /// <summary>
    /// 由协调入口在提交本批速度后传入实际经过时间；仅 FS 推进预测，其余模式清除本地 Tint。
    /// 采样与计算交给预测器，控制器不依赖界面刷新或自行读取系统时钟。
    /// </summary>
    public void AdvanceBrakeWarning(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        double? time = Data.ControlMode == CtcsControlMode.FullSupervision
            ? _brakeWarningPredictor.Advance(Data, elapsed)
            : null;
        Data = Data with { BrakeWarningTime = time };
    }

    /// <summary>仅确认存在且仍待确认的消息；成功时发布新快照，消息的保留或隐藏由展示层处理。</summary>
    public bool ConfirmTextMessage(Guid id)
    {
        if (!Data.TextMessages.Any(message => message.Id == id && message.NeedsConfirmation))
            return false;

        var messages = Data.TextMessages.Select(message =>
            message.Id == id ? message with { IsConfirmed = true } : message).ToArray();
        Data = Data with { TextMessages = Array.AsReadOnly(messages) };
        return true;
    }

    // 可选值缺失合法；NaN、无穷和负值拒绝，Tint 的正无穷特例由调用方单独处理。
    private static void Validate(double? value, string name)
    {
        if (value is { } number && (!double.IsFinite(number) || number < 0))
            throw new ArgumentOutOfRangeException(name, "数值必须是非负有限数，缺失值使用 null。");
    }
}
