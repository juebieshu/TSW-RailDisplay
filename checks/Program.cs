using System.Linq;
using Avalonia;

#if DEBUG
if (args.Contains("--shutdown"))
{
    ApplicationExitChecks.Run();
    return;
}

if (args.Contains("--debug-ui"))
{
    Avalonia.AppBuilder.Configure<RailDisplay.App>().UsePlatformDetect().WithInterFont().SetupWithoutStarting();
    DebugUiChecks.Run();
    return;
}
#endif

if (args.Contains("--pixels"))
{
    PixelDisplayChecks.Run();
    return;
}

if (args.Contains("--audio"))
{
    await AudioOutputChecks.RunLiveAsync();
    return;
}

CtcsCoreChecks.Run();
LoggingChecks.Run();
InteractionChecks.Run();
BrakeWarningPredictionChecks.Run();
AudioCoreChecks.Run();
CtcsDisplayChecks.Run();
BrakeWarningChecks.Run();
DebugChecks.Run();
CtcsScreenChecks.Run();
TrainMotionChecks.Run();
await AudioOutputChecks.RunAsync();
