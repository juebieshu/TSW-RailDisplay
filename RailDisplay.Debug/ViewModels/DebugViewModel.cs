using System;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RailDisplay.Core.Models.CTCS;

namespace RailDisplay.Debug.ViewModels;

public partial class DebugViewModel : ObservableObject
{
    private readonly Func<CtcsButton, bool> _keyHandle;
    private readonly Func<CtcsButton, bool> _canHandle;

    public DebugViewModel() : this(_ => false, _ => false) { }

    public DebugViewModel(Func<CtcsButton, bool> keyHandle, Func<CtcsButton, bool> canHandle)
    {
        ArgumentNullException.ThrowIfNull(keyHandle);
        ArgumentNullException.ThrowIfNull(canHandle);
        _keyHandle = keyHandle;
        _canHandle = canHandle;
    }

    private int _power;
    private int _brake;
    private bool _forward = true;

    public int Power
    {
        get => _power;
        set
        {
            if (value is < 0 or > 7)
                throw new ArgumentOutOfRangeException(nameof(value));
            if (SetProperty(ref _power, value))
            {
                OnPropertyChanged(nameof(PowerLabel));
                OnPropertyChanged(nameof(EffectivePower));
                OnPropertyChanged(nameof(EffectivePowerLabel));
                OnPropertyChanged(nameof(DirectionWarning));
            }
        }
    }

    public int Brake
    {
        get => _brake;
        set
        {
            if (value is < 0 or > 8)
                throw new ArgumentOutOfRangeException(nameof(value));
            if (SetProperty(ref _brake, value))
            {
                OnPropertyChanged(nameof(BrakeLabel));
                OnPropertyChanged(nameof(EffectivePower));
                OnPropertyChanged(nameof(EffectivePowerLabel));
            }
        }
    }

    public bool Forward
    {
        get => _forward;
        private set
        {
            if (SetProperty(ref _forward, value))
                OnPropertyChanged(nameof(DirectionArrow));
        }
    }

    public int EffectivePower => Brake > 0 ? 0 : Power;
    public string PowerLabel => Power == 0 ? "0" : $"P{Power}";
    public string BrakeLabel => Brake switch { 0 => "0", 8 => "EB", _ => $"B{Brake}" };
    public string EffectivePowerLabel => $"有效功率：{(EffectivePower == 0 ? "0" : $"P{EffectivePower}")}";
    public string DirectionArrow => Forward ? "↑" : "↓";
    public bool DirectionWarning => Power != 0;

    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.A: Power = Math.Min(7, Power + 1); break;
            case Key.D: Power = Math.Max(0, Power - 1); break;
            case Key.W: SetDirection(true); break;
            case Key.S: SetDirection(false); break;
            case Key.OemSemicolon: Brake = Math.Max(0, Brake - 1); break;
            case Key.OemQuotes: Brake = Math.Min(8, Brake + 1); break;
            default: return HandleDeviceKey(key);
        }
        return true;
    }

    private bool HandleDeviceKey(Key key)
    {
        CtcsButton? button = key switch
        {
            Key.F1 => CtcsButton.F1,
            Key.F2 => CtcsButton.F2,
            Key.F3 => CtcsButton.F3,
            Key.F4 => CtcsButton.F4,
            Key.F5 => CtcsButton.F5,
            Key.F6 => CtcsButton.F6,
            Key.F7 => CtcsButton.F7,
            Key.F8 => CtcsButton.F8,
            Key.D0 or Key.NumPad0 => CtcsButton.Digit0,
            Key.D1 or Key.NumPad1 => CtcsButton.Digit1,
            Key.D2 or Key.NumPad2 => CtcsButton.Digit2,
            Key.D3 or Key.NumPad3 => CtcsButton.Digit3,
            Key.D4 or Key.NumPad4 => CtcsButton.Digit4,
            Key.D5 or Key.NumPad5 => CtcsButton.Digit5,
            Key.D6 or Key.NumPad6 => CtcsButton.Digit6,
            Key.D7 or Key.NumPad7 => CtcsButton.Digit7,
            Key.D8 or Key.NumPad8 => CtcsButton.Digit8,
            Key.D9 or Key.NumPad9 => CtcsButton.Digit9,
            _ => null
        };
        return button is { } physical && HandleButton(physical);
    }

    public bool CanHandleButton(CtcsButton button)
    {
        if (!Enum.IsDefined(button))
            throw new ArgumentOutOfRangeException(nameof(button));
        return _canHandle(button);
    }

    public bool HandleButton(CtcsButton button) => CanHandleButton(button) && _keyHandle(button);

    public void RefreshButtons() => PressButtonCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanHandleButton))]
    private void PressButton(CtcsButton button) => HandleButton(button);

    private void SetDirection(bool forward)
    {
        if (Power == 0)
            Forward = forward;
    }

    [RelayCommand]
    private void ToggleDirection() => SetDirection(!Forward);
}
