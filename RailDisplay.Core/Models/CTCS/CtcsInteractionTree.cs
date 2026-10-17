namespace RailDisplay.Core.Models.CTCS;

public sealed record CtcsInteractionNode(
    CtcsInteractionStep Step,
    IReadOnlyList<CtcsInteractionNode> Children);

/// <summary>模式根；ControlMode 为 null 时专指关机，电源转换规则尚未接入。</summary>
public sealed record CtcsInteractionRoot(
    CtcsControlMode? ControlMode,
    IReadOnlyList<CtcsInteractionNode> Children);

/// <summary>业务交互状态的只读层级；不表示已实现按键转换条件或开关机流程。</summary>
public static class CtcsInteractionTree
{
    public static IReadOnlyList<CtcsInteractionNode> FunctionSteps { get; } = Array.AsReadOnly<CtcsInteractionNode>(
    [
        Node(CtcsInteractionStep.DataEntry,
            Node(CtcsInteractionStep.DriverNumberEntry),
            Node(CtcsInteractionStep.TrainNumberEntry),
            Node(CtcsInteractionStep.TrainDataEntry),
            Node(CtcsInteractionStep.DateTimeSetting,
                Node(CtcsInteractionStep.DateSetting),
                Node(CtcsInteractionStep.TimeSetting))),
        Node(CtcsInteractionStep.ModeSelection,
            Node(CtcsInteractionStep.ShuntingConfirmation),
            Node(CtcsInteractionStep.OnSightConfirmation)),
        Node(CtcsInteractionStep.CarrierFrequencySelection,
            Node(CtcsInteractionStep.UpCarrierFrequencyConfirmation),
            Node(CtcsInteractionStep.DownCarrierFrequencyConfirmation)),
        Node(CtcsInteractionStep.LevelSelection,
            Node(CtcsInteractionStep.Level2Confirmation),
            Node(CtcsInteractionStep.Level0Confirmation)),
        Node(CtcsInteractionStep.OtherSettings,
            Node(CtcsInteractionStep.VolumeSetting),
            Node(CtcsInteractionStep.BrightnessSetting),
            Node(CtcsInteractionStep.SoftwareVersion)),
        Node(CtcsInteractionStep.StartConfirmation),
        Node(CtcsInteractionStep.BrakeReleaseConfirmation)
    ]);

    public static IReadOnlyList<CtcsInteractionRoot> Roots { get; } = Array.AsReadOnly<CtcsInteractionRoot>(
    [
        new(null, Array.AsReadOnly(Array.Empty<CtcsInteractionNode>())),
        .. Enum.GetValues<CtcsControlMode>().Select(mode => new CtcsInteractionRoot(mode, FunctionSteps))
    ]);

    public static CtcsInteractionRoot ForMode(CtcsControlMode mode)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        return Roots.Single(root => root.ControlMode == mode);
    }

    private static CtcsInteractionNode Node(CtcsInteractionStep step, params CtcsInteractionNode[] children) =>
        new(step, Array.AsReadOnly(children));
}
