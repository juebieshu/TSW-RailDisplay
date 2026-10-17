using RailDisplay.Core.Models.CTCS;

namespace RailDisplay.Models.CTCS;

public enum BrakeWarningColor { Gray, Yellow, Orange, Red }

public readonly record struct BrakeWarningDisplay(double Size, BrakeWarningColor Color)
{
    public double Offset => System.Math.Floor((54 - Size) / 2);
    public bool IsYellow => Color == BrakeWarningColor.Yellow;
    public bool IsOrange => Color == BrakeWarningColor.Orange;
    public bool IsRed => Color == BrakeWarningColor.Red;
    public static BrakeWarningDisplay Calculate(CtcsData data, SpeedMonitoringSection section)
    {
        bool allowed = data.ControlMode == CtcsControlMode.FullSupervision ||
                       (data.ControlMode == CtcsControlMode.Guiding && data.HasContinuousControlCurve);
        if (!allowed || data.BrakeWarningTime is not { } tint)
            return default;
        if (section == SpeedMonitoringSection.CSM && tint > 8)
            return default;
        // shortcut: RSM 的 Tint≥8 秒显示条件未明确，按方案隐藏，规则确认后替换。
        if (section == SpeedMonitoringSection.RSM && tint >= 8)
            return default;

        BrakeWarningColor color;
        if (section == SpeedMonitoringSection.RSM)
        {
            if (data.ReleaseSpeed is not { } release)
                return default;
            color = data.Speed >= release ? BrakeWarningColor.Red : BrakeWarningColor.Yellow;
        }
        else
        {
            if (data.BrakeWarningInterventionSpeed is not { } intervention)
                return default;
            color = data.Speed >= intervention ? BrakeWarningColor.Red :
                data.Speed > data.PermittedSpeed ? BrakeWarningColor.Orange :
                section == SpeedMonitoringSection.TSM ? BrakeWarningColor.Yellow : BrakeWarningColor.Gray;
        }
        double size = color == BrakeWarningColor.Red || tint == 0 ? 54 :
            tint >= 8 ? 5 : tint > 4 ? 27 : 41;
        return new BrakeWarningDisplay(size, color);
    }
}
