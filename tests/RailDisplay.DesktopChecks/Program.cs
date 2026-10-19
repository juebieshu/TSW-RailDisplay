using System;
using System.Linq;
using Avalonia;
using RailDisplay.DesktopChecks;

// 用法：dotnet run --project tests/RailDisplay.DesktopChecks -- <--debug-ui | --shutdown | --audio>
// 每次只运行一项：各检查需要独立初始化 Avalonia 应用。
string? mode = args.FirstOrDefault();
switch (mode)
{
#if DEBUG
    case "--debug-ui":
        AppBuilder.Configure<RailDisplay.App>().UsePlatformDetect().WithInterFont().SetupWithoutStarting();
        DebugUiChecks.Run();
        Console.WriteLine("原生 Debug 菜单、菜单生命周期与热重载重绘检查通过。");
        return 0;
    case "--shutdown":
        ApplicationExitChecks.Run();
        return 0;
#else
    case "--debug-ui" or "--shutdown":
        Console.Error.WriteLine($"{mode} 依赖仅 Debug 配置编译的代码，请使用 -c Debug。");
        return 2;
#endif
    case "--audio":
        await AudioLiveChecks.RunLiveAsync();
        return 0;
    default:
        Console.Error.WriteLine("用法：RailDisplay.DesktopChecks <--debug-ui | --shutdown | --audio>");
        return 2;
}
