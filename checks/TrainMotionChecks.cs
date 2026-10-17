using System;
using RailDisplay.Core.Controllers.Simulation;

internal static class TrainMotionChecks
{
    public static void Run()
    {
        static void Near(double actual, double expected)
        {
            if (Math.Abs(actual - expected) > 1e-9)
                throw new Exception($"Expected {expected}, received {actual}.");
        }
        double[] boundaries = [0, 40, 80, 120, 160, 200, 250, 300, 350, 400];
        double[] powerFactors = [1, .9, .75, .55, .35, .2, .15, .1, .05, .05];
        double[] brakeFactors = [1, 1, .98, .95, .9, .85, .8, .75, .7, .7];
        double[] ebFactors = [1, 1, 1, 1, .95, .9, .85, .85, .8, .8];
        double[] brakeTargets = [0, .1, .2, .3, .4, .55, .7, .85];
        for (int i = 0; i < boundaries.Length; i++)
        {
            for (int power = 0; power <= 7; power++)
                Near(TrainMotionSimulation.Acceleration(boundaries[i], power, 0), power * .08 * powerFactors[i]);
            for (int brake = 1; brake <= 7; brake++)
                Near(TrainMotionSimulation.Acceleration(boundaries[i], 7, brake), -brakeTargets[brake] * brakeFactors[i]);
            Near(TrainMotionSimulation.Acceleration(boundaries[i], 7, 8), -1.2 * ebFactors[i]);
            if (i > 0)
            {
                Near(TrainMotionSimulation.Acceleration(boundaries[i] - 1e-6, 7, 0), .56 * powerFactors[i - 1]);
                Near(TrainMotionSimulation.Acceleration(boundaries[i] - 1e-6, 7, 7), -.85 * brakeFactors[i - 1]);
                Near(TrainMotionSimulation.Acceleration(boundaries[i] - 1e-6, 7, 8), -1.2 * ebFactors[i - 1]);
            }
        }
        Near(TrainMotionSimulation.Advance(0, 7, 0, TimeSpan.FromSeconds(10)), 20.16);
        Near(TrainMotionSimulation.Advance(20, 7, 8, TimeSpan.FromSeconds(1)), 15.68);
        Near(TrainMotionSimulation.Advance(120, 0, 0, TimeSpan.FromHours(1)), 120);
        Near(TrainMotionSimulation.Advance(390, 7, 0, TimeSpan.FromHours(1)), 400);
        Near(TrainMotionSimulation.Advance(10, 7, 8, TimeSpan.FromHours(1)), 0);
        Near(TrainMotionSimulation.Advance(40, 0, 7, TimeSpan.FromSeconds(1)), 40 - .85 * 3.6);
        foreach (int brake in new[] { 0, 7, 8 })
        {
            double start = brake == 0 ? 35 : 380;
            double whole = TrainMotionSimulation.Advance(start, 7, brake, TimeSpan.FromSeconds(1000));
            double split = start;
            for (int i = 0; i < 10000; i++)
                split = TrainMotionSimulation.Advance(split, 7, brake, TimeSpan.FromSeconds(.1));
            Near(split, whole);
        }
        foreach (double invalid in new[] { -1, 401, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Reject(() => TrainMotionSimulation.Advance(invalid, 0, 0, TimeSpan.Zero));
        foreach (int invalid in new[] { -1, 8, int.MaxValue })
            Reject(() => TrainMotionSimulation.Advance(0, invalid, 0, TimeSpan.Zero));
        foreach (int invalid in new[] { -1, 9, int.MaxValue })
            Reject(() => TrainMotionSimulation.Advance(0, 0, invalid, TimeSpan.Zero));
        Reject(() => TrainMotionSimulation.Advance(0, 0, 0, TimeSpan.FromTicks(-1)));
        Near(TrainMotionSimulation.Advance(80, 7, 8, TimeSpan.Zero), 80);
        Console.WriteLine("本地速度模拟档位、速度边界、分段积分、限速及非法输入检查通过。");
    }

    private static void Reject(Action action)
    {
        try { action(); throw new Exception("Invalid motion input accepted."); }
        catch (ArgumentOutOfRangeException) { }
    }
}
