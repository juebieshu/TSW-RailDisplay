using System;
using System.Collections.Generic;
using System.Linq;
using RailDisplay.Core.Models.CTCS;

namespace RailDisplay.Models.CTCS;

public sealed record FunctionKeyMenuItem(
    string Label,
    CtcsButton Key,
    IReadOnlyList<FunctionKeyMenuItem> Children,
    CtcsInteractionStep? TargetStep = null,
    bool IsBack = false);

// 显示文字与导航配置共用此表；具体业务提交动作逐项确认后另接处理器。
public static class FunctionKeyMenu
{
    public static IReadOnlyList<FunctionKeyMenuItem> GetItems(CtcsInteractionStep? step)
    {
        if (step is null)
            return Items;
        return Find(Items, step.Value)?.Children
            ?? throw new ArgumentOutOfRangeException(nameof(step), "未定义该步骤的功能键菜单。");
    }

    private static FunctionKeyMenuItem? Find(IReadOnlyList<FunctionKeyMenuItem> items, CtcsInteractionStep step)
    {
        foreach (var item in items)
        {
            if (item.TargetStep == step)
                return item;
            if (Find(item.Children, step) is { } found)
                return found;
        }
        return null;
    }

    public static IReadOnlyList<FunctionKeyMenuItem> Items { get; } = Array.AsReadOnly<FunctionKeyMenuItem>(
    [
        Item("数据", CtcsButton.F1, CtcsInteractionStep.DataEntry,
            Item("司机号", CtcsButton.F1, CtcsInteractionStep.DriverNumberEntry, ConfirmDeleteCancel()),
            Item("车次号", CtcsButton.F2, CtcsInteractionStep.TrainNumberEntry, ConfirmDeleteCancel()),
            Item("列车数据", CtcsButton.F3, CtcsInteractionStep.TrainDataEntry,
                Item("8辆", CtcsButton.F1),
                Item("16辆", CtcsButton.F2),
                Item("确定", CtcsButton.F6),
                BackItem("取消")),
            Item("向上", CtcsButton.F4),
            Item("向下", CtcsButton.F5),
            Item("日期时间", CtcsButton.F6, CtcsInteractionStep.DateTimeSetting,
                Item("日期", CtcsButton.F1, CtcsInteractionStep.DateSetting),
                Item("时间", CtcsButton.F2, CtcsInteractionStep.TimeSetting),
                Item("确定", CtcsButton.F6),
                Item("删除", CtcsButton.F7),
                BackItem("取消")),
            BackItem()),

        Item("模式", CtcsButton.F2, CtcsInteractionStep.ModeSelection,
            Item("调车", CtcsButton.F1, CtcsInteractionStep.ShuntingConfirmation, ConfirmCancel()),
            Item("目视", CtcsButton.F2, CtcsInteractionStep.OnSightConfirmation, ConfirmCancel()),
            BackItem()),

        Item("载频", CtcsButton.F3, CtcsInteractionStep.CarrierFrequencySelection,
            Item("上行", CtcsButton.F1, CtcsInteractionStep.UpCarrierFrequencyConfirmation, ConfirmCancel()),
            Item("下行", CtcsButton.F2, CtcsInteractionStep.DownCarrierFrequencyConfirmation, ConfirmCancel()),
            BackItem()),

        Item("等级", CtcsButton.F4, CtcsInteractionStep.LevelSelection,
            Item("CTCS2", CtcsButton.F1, CtcsInteractionStep.Level2Confirmation, ConfirmCancel()),
            Item("CTCS0", CtcsButton.F2, CtcsInteractionStep.Level0Confirmation, ConfirmCancel()),
            BackItem()),

        Item("其他", CtcsButton.F5, CtcsInteractionStep.OtherSettings,
            Item("音量", CtcsButton.F1, CtcsInteractionStep.VolumeSetting,
                Item("大", CtcsButton.F1),
                Item("小", CtcsButton.F2),
                BackItem()),
            Item("亮度", CtcsButton.F2, CtcsInteractionStep.BrightnessSetting,
                Item("亮", CtcsButton.F1),
                Item("暗", CtcsButton.F2),
                BackItem()),
            Item("软件版本", CtcsButton.F3, CtcsInteractionStep.SoftwareVersion,
                BackItem()),
            BackItem()),

        Item("启动", CtcsButton.F6, CtcsInteractionStep.StartConfirmation, ConfirmCancel()),
        Item("缓解", CtcsButton.F7, CtcsInteractionStep.BrakeReleaseConfirmation, ConfirmCancel()),
        Item("警惕", CtcsButton.F8)
    ]);

    public static IReadOnlyList<CtcsNavigationBinding> NavigationBindings { get; } =
        Array.AsReadOnly(BuildBindings(Items, null).ToArray());

    private static IEnumerable<CtcsNavigationBinding> BuildBindings(
        IReadOnlyList<FunctionKeyMenuItem> items, CtcsInteractionStep? step)
    {
        foreach (var item in items)
        {
            if (item.TargetStep is { } target)
            {
                yield return new CtcsNavigationBinding(step, item.Key, target);
                foreach (var binding in BuildBindings(item.Children, target))
                    yield return binding;
            }
            else if (item.IsBack)
                yield return new CtcsNavigationBinding(step, item.Key, IsBack: true);
        }
    }

    private static FunctionKeyMenuItem BackItem(string label = "返回") =>
        new(label, CtcsButton.F8, Array.Empty<FunctionKeyMenuItem>(), IsBack: true);

    private static FunctionKeyMenuItem[] ConfirmCancel() =>
    [
        Item("确定", CtcsButton.F6),
        BackItem("取消")
    ];

    private static FunctionKeyMenuItem[] ConfirmDeleteCancel() =>
    [
        Item("确定", CtcsButton.F6),
        Item("删除", CtcsButton.F7),
        BackItem("取消")
    ];

    private static FunctionKeyMenuItem Item(
        string label,
        CtcsButton key,
        params FunctionKeyMenuItem[] children) =>
        new(label, key, Array.AsReadOnly(children));

    private static FunctionKeyMenuItem Item(
        string label,
        CtcsButton key,
        CtcsInteractionStep targetStep,
        params FunctionKeyMenuItem[] children) =>
        new(label, key, Array.AsReadOnly(children), targetStep);
}
