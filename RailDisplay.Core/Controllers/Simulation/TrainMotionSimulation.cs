namespace RailDisplay.Core.Controllers.Simulation;

/// <summary>本地手柄速度模拟；速度单位 km/h，加速度单位 m/s²，步长为显式经过时间。</summary>
public static class TrainMotionSimulation
{
    public const double MaximumSpeed = 400;
    private static readonly double[] Boundaries = [0, 40, 80, 120, 160, 200, 250, 300, 350, MaximumSpeed];
    private static readonly double[] PowerFactors = [1, 0.90, 0.75, 0.55, 0.35, 0.20, 0.15, 0.10, 0.05];
    private static readonly double[] BrakeFactors = [1, 1, 0.98, 0.95, 0.90, 0.85, 0.80, 0.75, 0.70];
    private static readonly double[] EmergencyFactors = [1, 1, 1, 1, 0.95, 0.90, 0.85, 0.85, 0.80];
    private static readonly double[] BrakeDecelerations = [0, 0.10, 0.20, 0.30, 0.40, 0.55, 0.70, 0.85, 1.20];

    public static double Acceleration(double speed, int power, int brake)
    {
        Validate(speed, power, brake);
        return AccelerationForSegment(Segment(speed), power, brake);
    }

    public static double Advance(double speed, int power, int brake, TimeSpan elapsed)
    {
        Validate(speed, power, brake);
        if (elapsed < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        double remaining = elapsed.TotalSeconds;
        if (brake == 0 && power == 0)
            return speed;
        while (remaining > 0 && (brake > 0 ? speed > 0 : speed < MaximumSpeed))
        {
            int segment = Segment(speed);
            // 减速从边界进入低速段，不能用边界上方的系数积分整个低速区间。
            if (brake > 0 && segment > 0 && speed == Boundaries[segment])
                segment--;
            double rate = AccelerationForSegment(segment, power, brake) * 3.6;
            double boundary = brake > 0 ? Boundaries[segment] : Boundaries[segment + 1];
            double toBoundary = (boundary - speed) / rate;
            if (remaining < toBoundary)
                return Math.Clamp(speed + rate * remaining, 0, MaximumSpeed);
            speed = boundary;
            remaining -= toBoundary;
        }
        return speed;
    }

    private static double AccelerationForSegment(int segment, int power, int brake) => brake switch
    {
        0 => power * 0.08 * PowerFactors[segment],
        8 => -BrakeDecelerations[brake] * EmergencyFactors[segment],
        _ => -BrakeDecelerations[brake] * BrakeFactors[segment]
    };

    private static int Segment(double speed)
    {
        int segment = 0;
        while (segment < PowerFactors.Length - 1 && speed >= Boundaries[segment + 1])
            segment++;
        return segment;
    }

    private static void Validate(double speed, int power, int brake)
    {
        if (!double.IsFinite(speed) || speed is < 0 or > MaximumSpeed)
            throw new ArgumentOutOfRangeException(nameof(speed));
        if (power is < 0 or > 7)
            throw new ArgumentOutOfRangeException(nameof(power));
        if (brake is < 0 or > 8)
            throw new ArgumentOutOfRangeException(nameof(brake));
    }
}
