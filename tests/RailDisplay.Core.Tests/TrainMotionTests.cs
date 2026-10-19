using System;
using RailDisplay.Core.Controllers.Simulation;

namespace RailDisplay.Core.Tests;

public sealed class TrainMotionTests
{
    private static readonly double[] Boundaries = [0, 40, 80, 120, 160, 200, 250, 300, 350, 400];
    private static readonly double[] PowerFactors = [1, .9, .75, .55, .35, .2, .15, .1, .05, .05];
    private static readonly double[] BrakeFactors = [1, 1, .98, .95, .9, .85, .8, .75, .7, .7];
    private static readonly double[] EbFactors = [1, 1, 1, 1, .95, .9, .85, .85, .8, .8];
    private static readonly double[] BrakeTargets = [0, .1, .2, .3, .4, .55, .7, .85];

    [Fact]
    public void Acceleration_UsesNotchFactorsAtEverySpeedBoundary()
    {
        for (int i = 0; i < Boundaries.Length; i++)
        {
            for (int power = 0; power <= 7; power++)
                Near(TrainMotionSimulation.Acceleration(Boundaries[i], power, 0), power * .08 * PowerFactors[i]);
            for (int brake = 1; brake <= 7; brake++)
                Near(TrainMotionSimulation.Acceleration(Boundaries[i], 7, brake), -BrakeTargets[brake] * BrakeFactors[i]);
            Near(TrainMotionSimulation.Acceleration(Boundaries[i], 7, 8), -1.2 * EbFactors[i]);
            if (i > 0)
            {
                Near(TrainMotionSimulation.Acceleration(Boundaries[i] - 1e-6, 7, 0), .56 * PowerFactors[i - 1]);
                Near(TrainMotionSimulation.Acceleration(Boundaries[i] - 1e-6, 7, 7), -.85 * BrakeFactors[i - 1]);
                Near(TrainMotionSimulation.Acceleration(Boundaries[i] - 1e-6, 7, 8), -1.2 * EbFactors[i - 1]);
            }
        }
    }

    [Fact]
    public void Advance_IntegratesAndClampsSpeed()
    {
        Near(TrainMotionSimulation.Advance(0, 7, 0, TimeSpan.FromSeconds(10)), 20.16);
        Near(TrainMotionSimulation.Advance(20, 7, 8, TimeSpan.FromSeconds(1)), 15.68);
        Near(TrainMotionSimulation.Advance(120, 0, 0, TimeSpan.FromHours(1)), 120);
        Near(TrainMotionSimulation.Advance(390, 7, 0, TimeSpan.FromHours(1)), 400);
        Near(TrainMotionSimulation.Advance(10, 7, 8, TimeSpan.FromHours(1)), 0);
        Near(TrainMotionSimulation.Advance(40, 0, 7, TimeSpan.FromSeconds(1)), 40 - .85 * 3.6);
        Near(TrainMotionSimulation.Advance(80, 7, 8, TimeSpan.Zero), 80);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(8)]
    public void Advance_SplitStepsMatchSingleStep(int brake)
    {
        double start = brake == 0 ? 35 : 380;
        double whole = TrainMotionSimulation.Advance(start, 7, brake, TimeSpan.FromSeconds(1000));
        double split = start;
        for (int i = 0; i < 10000; i++)
            split = TrainMotionSimulation.Advance(split, 7, brake, TimeSpan.FromSeconds(.1));
        Near(split, whole);
    }

    [Fact]
    public void Advance_RejectsInvalidInput()
    {
        foreach (double invalid in new[] { -1, 401, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.Throws<ArgumentOutOfRangeException>(() => TrainMotionSimulation.Advance(invalid, 0, 0, TimeSpan.Zero));
        foreach (int invalid in new[] { -1, 8, int.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(() => TrainMotionSimulation.Advance(0, invalid, 0, TimeSpan.Zero));
        foreach (int invalid in new[] { -1, 9, int.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(() => TrainMotionSimulation.Advance(0, 0, invalid, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrainMotionSimulation.Advance(0, 0, 0, TimeSpan.FromTicks(-1)));
    }

    private static void Near(double actual, double expected) => Assert.Equal(expected, actual, 1e-9);
}
