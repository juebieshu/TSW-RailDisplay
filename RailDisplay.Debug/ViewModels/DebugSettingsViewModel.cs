using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RailDisplay.Core.Models.CTCS;

namespace RailDisplay.Debug.ViewModels;

public partial class DebugSettingsViewModel : ObservableObject
{
    private readonly Func<CtcsData> _readData;
    private readonly Action<CtcsData> _updateData;
    [ObservableProperty] private string _targetSpeed = "";
    [ObservableProperty] private string _permittedSpeed = "";
    [ObservableProperty] private string _targetDistance = "";
    [ObservableProperty] private string? _error;

    public DebugSettingsViewModel(Func<CtcsData> readData, Action<CtcsData> updateData)
    {
        ArgumentNullException.ThrowIfNull(readData);
        ArgumentNullException.ThrowIfNull(updateData);
        _readData = readData;
        _updateData = updateData;
        Reload();
    }

    public void Reload()
    {
        var data = _readData();
        TargetSpeed = data.TargetSpeed?.ToString("R", CultureInfo.InvariantCulture) ?? "";
        PermittedSpeed = data.PermittedSpeed.ToString("R", CultureInfo.InvariantCulture);
        TargetDistance = data.TargetDistance?.ToString("R", CultureInfo.InvariantCulture) ?? "";
        Error = null;
    }

    [RelayCommand]
    private void Apply()
    {
        try
        {
            double? target = Parse(TargetSpeed, "Target speed", optional: true);
            double permitted = Parse(PermittedSpeed, "Permitted speed", optional: false)!.Value;
            double? distance = Parse(TargetDistance, "Target distance", optional: true);
            // shortcut: SBI/EBI/TSM 暂保留现值，用户确认自动计算规则后替换。
            _updateData(_readData() with
            {
                TargetSpeed = target, PermittedSpeed = permitted, TargetDistance = distance
            });
            Error = null;
        }
        catch (ArgumentException error)
        {
            Error = error.Message;
        }
    }

    private static double? Parse(string text, string name, bool optional)
    {
        if (optional && string.IsNullOrWhiteSpace(text))
            return null;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            throw new ArgumentException($"{name} must be a number.");
        return value;
    }
}
