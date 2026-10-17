using RailDisplay.Core.Models.CTCS;

namespace RailDisplay.Core.Controllers.CTCS;

internal sealed class CtcsBrakeWarningPredictor
{
    private const int SampleCount = 5;
    private readonly List<(double Speed, double Time)> _samples = new(SampleCount);
    private double _time;

    public void Reset(double? speed = null)
    {
        _samples.Clear();
        _time = 0;
        if (speed is { } initial)
            _samples.Add((initial / 3.6, 0));
    }

    public double? Advance(CtcsData data, TimeSpan elapsed)
    {
        _time += elapsed.TotalSeconds;
        if (_samples.Count == 0 || _time - _samples[^1].Time >= 1)
        {
            if (_samples.Count == SampleCount)
                _samples.RemoveAt(0);
            // 调度延迟时只记录实际观测，不用同一个速度伪造遗漏的采样。
            _samples.Add((data.Speed / 3.6, _time));
        }

        if (data.BrakeWarningInterventionSpeed is not { } intervention)
            return null;
        if (data.Speed >= intervention)
            return 0;
        if (_samples.Count < 2)
            return null;

        double acceleration = 0;
        int nonZeroCount = 0;
        for (int i = 1; i < _samples.Count; i++)
        {
            var previous = _samples[i - 1];
            var current = _samples[i];
            double segmentAcceleration = (current.Speed - previous.Speed) / (current.Time - previous.Time);
            if (segmentAcceleration != 0)
            {
                acceleration += segmentAcceleration;
                nonZeroCount++;
            }
        }
        if (nonZeroCount > 0)
            acceleration /= nonZeroCount;
        // 当前方式：FS 内至少每秒采样一次，保留当前及前 4 次速度/实际时间；已有相邻区间仅平均非零加速度，全零时取 0，不等待窗口凑满。
        // A1 的 Vint 暂取最新 EBI：已达 EBI 时 Tint=0；缺少 EBI 或不足两次采样时为 null；低于 EBI 时，a>0 则 Tint=(EBI-v)/a，否则为正无穷。速度统一为 m/s，a 为 m/s²。
        // TODO: 预测需考虑 EBI 随行进降低，覆盖车速不变仍会接近干预线的情况；变化率或前方曲线算法确认后实现。
        // shortcut: 按用户规则保持平均加速度并固定本次 Vint，每轮使用最新阈值，正式预测算法提供后替换。
        return acceleration > 0
            ? ((intervention - data.Speed) / 3.6) / acceleration
            : double.PositiveInfinity;
    }
}
