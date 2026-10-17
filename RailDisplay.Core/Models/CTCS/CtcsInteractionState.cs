namespace RailDisplay.Core.Models.CTCS;

/// <summary>CTCS 业务交互步骤；层级见 CtcsInteractionTree，DMI 在展示层映射内容。</summary>
public enum CtcsInteractionStep
{
    DataEntry,                         // 数据
    DriverNumberEntry,                 // 司机号
    TrainNumberEntry,                  // 车次号
    TrainDataEntry,                    // 列车数据
    DateTimeSetting,                   // 日期时间
    DateSetting,                       // 日期
    TimeSetting,                       // 时间

    ModeSelection,                     // 模式
    ShuntingConfirmation,              // 调车确认
    OnSightConfirmation,               // 目视确认

    CarrierFrequencySelection,         // 载频
    UpCarrierFrequencyConfirmation,    // 上行确认
    DownCarrierFrequencyConfirmation,  // 下行确认

    LevelSelection,                    // 等级
    Level2Confirmation,                // CTCS2 确认
    Level0Confirmation,                // CTCS0 确认

    OtherSettings,                     // 其他
    VolumeSetting,                     // 音量
    BrightnessSetting,                 // 亮度
    SoftwareVersion,                   // 软件版本

    StartConfirmation,                 // 启动确认
    BrakeReleaseConfirmation           // 缓解确认
}

/// <summary>一个交互步骤的只读快照；Input 为尚未提交的输入草稿。</summary>
public sealed record CtcsInteractionState
{
    /// <summary>null 表示当前运行模式的根状态；模式从最新 ControlMode 读取，不进入返回栈。</summary>
    public CtcsInteractionStep? Step { get; init; }
    public string Input { get; init; } = "";
}
