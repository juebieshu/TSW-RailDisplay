using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using RailDisplay.Core.Controllers.Simulation;
using RailDisplay.Core.Models.CTCS;
using RailDisplay.Debug.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RailDisplay.Debug.CTCS;

public sealed class CtcsDebugSimulation(
    Func<CtcsData> readData, Action<CtcsData> updateData,
    Action<TimeSpan> advanceBrakeWarning, DebugViewModel handles, ILogger<CtcsDebugSimulation>? logger = null)
{
    public void Advance(TimeSpan elapsed)
    {
        var current = readData();
        double speed = TrainMotionSimulation.Advance(current.Speed, handles.Power, handles.Brake, elapsed);
        if (speed != current.Speed)
            updateData(current with { Speed = speed });
        advanceBrakeWarning(elapsed);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        long previous = Stopwatch.GetTimestamp();
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;
                    long now = Stopwatch.GetTimestamp();
                    TimeSpan elapsed = Stopwatch.GetElapsedTime(previous, now);
                    Advance(elapsed);
                    previous = now;
                });
            }
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            (logger ?? NullLogger<CtcsDebugSimulation>.Instance).LogError(error, "本地速度模拟异常停止");
            throw;
        }
    }
}
