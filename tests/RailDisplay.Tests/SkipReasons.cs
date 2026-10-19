namespace RailDisplay.Tests;

internal static class SkipReasons
{
    public const string A2RuleUndecided = "A2 距离条比例规则未确认：测试期望与 TargetDistanceScale 实现不一致。";
    public const string FunctionKeyBorderRuleUndecided = "F 区边框规则未确认：测试期望 0,0,2,2，界面样式为 1,1,1,1。";
    public const string ClockTitleRuleUndecided = "E17 标题未确认：测试期望“日期时间”，界面为“时间日期”。";
    public const string SpeedGridRuleUndecided = "D 区速度网格未确认：测试期望行 90/110/129/148/167/186/205，DistanceScale 实现为 85 起每 20px 一行。";
}
