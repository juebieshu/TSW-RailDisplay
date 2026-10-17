using System;
using System.Collections.Generic;

namespace RailDisplay.Core.Models.CTCS;

public enum SpeedMonitoringSection
{
    CSM,
    TSM,
    RSM
}

// -1 表示无制动；0 表示允许缓解，正值随制动力从弱到强递增。
public enum CtcsBrakeLevel
{
    NoBrake = -1,
    ReleaseAllowed = 0,
    WeakServiceBrake = 1,
    MediumServiceBrake = 2,
    ServiceBrake = 3,
    EmergencyBrake = 4
}

public enum CtcsLevel
{
    Level0 = 0,
    Level2 = 2,
    Level3 = 3
}

// CTCS 共用模式；等级专属规则由控制器根据 CurrentLevel 处理。
public enum CtcsControlMode
{
    FullSupervision,     // 完全监控模式 FS
    PartialSupervision,  // 部分监控模式 PS
    Shunting,            // 调车模式    SH
    OnSight,             // 目视行车模式 OS
    Standby,             // 待机模式    SB
    Guiding,             // 引导模式    CO
    Isolation,           // 隔离模式    IS
    CabSignalling        // 机车信号工作状态 LKJ
}

public sealed record CtcsData
{
    public double Speed { get; init; }
    public double? TargetSpeed { get; init; }
    public double PermittedSpeed { get; init; }
    public double? SbiSpeed { get; init; }
    public double? EbiSpeed { get; init; }
    /// <summary>制动预警时间，秒；非负数，正无穷表示本轮预测不会冲线，null 表示数据不足。不由显示层倒计时。</summary>
    public double? BrakeWarningTime { get; init; }
    /// <summary>释放速度，km/h，非负有限数；null 表示尚未提供。</summary>
    public double? ReleaseSpeed { get; init; }
    /// <summary>同批监督结果；null 时沿用现有临时距离区段判定。</summary>
    public SpeedMonitoringSection? MonitoringSection { get; init; }
    public bool HasContinuousControlCurve { get; init; }

    public double? InterventionSpeed => SbiSpeed is { } sbi && Speed < sbi ? sbi : EbiSpeed;
    // shortcut: A1 的 Vint 按用户要求暂取当前 EBI，正式规则确认后替换。
    public double? BrakeWarningInterventionSpeed => EbiSpeed;
    public double? TargetDistance { get; init; }
    public double? TsmEntryDistance { get; init; }
    public IReadOnlyList<CtcsTextMessage> TextMessages { get; init; } = Array.Empty<CtcsTextMessage>();

    public CtcsLevel CurrentLevel { get; init; } = CtcsLevel.Level2;
    public CtcsBrakeLevel BrakeLevel { get; init; } = CtcsBrakeLevel.ReleaseAllowed;
    public CtcsControlMode ControlMode { get; init; } = CtcsControlMode.Standby;
}
