using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RailDisplay.Core.Controllers.CTCS;
using RailDisplay.Core.Models.CTCS;
using RailDisplay.Models.CTCS;

namespace RailDisplay.ViewModels.CTCS;

public sealed class CtcsViewModel : ViewModelBase
{
    private readonly ICtcsController _controller;
    public CtcsScreenProfile Screen { get; }
    public bool CanSwitchToLevel3 => Screen.SupportsLevel3;

    private DateTimeOffset _displayTime = DateTimeOffset.Now;
    public string CurrentTimeText => _displayTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    public string CurrentDateText => _displayTime.ToString("yy-MM-dd", CultureInfo.InvariantCulture);

    public void UpdateDisplayTime(DateTimeOffset time)
    {
        _displayTime = time;
        OnPropertyChanged(nameof(CurrentTimeText));
        OnPropertyChanged(nameof(CurrentDateText));
    }

    private int _maximumDisplayDistance = 8000;
    /// <summary>D 区显示量程，单位为米，仅接受 8000 或 16000。</summary>
    public int MaximumDisplayDistance
    {
        get => _maximumDisplayDistance;
        set
        {
            if (value is not (8000 or 16000))
                throw new ArgumentOutOfRangeException(nameof(value));
            SetProperty(ref _maximumDisplayDistance, value);
        }
    }

    public CtcsViewModel(ICtcsController controller, CtcsScreenProfile screen)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(screen);
        _controller = controller;
        Screen = screen;
        Refresh();
    }

    public bool TrySwitchLevel(CtcsLevel level)
    {
        if (!Enum.IsDefined(level))
            throw new ArgumentOutOfRangeException(nameof(level));
        if (level == CtcsLevel.Level3 && !CanSwitchToLevel3)
            return false;
        Update(Data with { CurrentLevel = level });
        return true;
    }

    private int _firstVisibleMessage;
    private List<CtcsTextMessage> _messages = new();
    private Guid? _latestMessageId;
    private bool _messageBlinkVisible = true;
    public CtcsData Data => _controller.Data;
    public CtcsInteractionState CurrentInteraction => _controller.CurrentInteraction;
    public CtcsInteractionRoot CurrentInteractionRoot => _controller.CurrentInteractionRoot;
    public IReadOnlyList<CtcsInteractionState> History => _controller.History;
    public IReadOnlyList<CtcsFunctionKeyState> FunctionKeys { get; private set; } = Array.Empty<CtcsFunctionKeyState>();
    public string D6PageTitle => CurrentInteraction.Step switch
    {
        CtcsInteractionStep.DriverNumberEntry => "司机号输入",
        CtcsInteractionStep.TrainNumberEntry => "车次号输入",
        CtcsInteractionStep.TrainDataEntry => "列车数据输入",
        CtcsInteractionStep.ModeSelection => "模式功能选择",
        CtcsInteractionStep.ShuntingConfirmation => "调车模式确认",
        CtcsInteractionStep.OnSightConfirmation => "目视行车确认",
        CtcsInteractionStep.CarrierFrequencySelection => "载频方式选择",
        CtcsInteractionStep.UpCarrierFrequencyConfirmation or CtcsInteractionStep.DownCarrierFrequencyConfirmation => "上/下行载频确认",
        CtcsInteractionStep.LevelSelection => "CTCS等级选择",
        CtcsInteractionStep.Level0Confirmation => "转换到CTCS 0级确认",
        CtcsInteractionStep.Level2Confirmation => "转换到CTCS 2级确认",
        CtcsInteractionStep.OtherSettings => "其他选项",
        CtcsInteractionStep.VolumeSetting => "音量设置",
        CtcsInteractionStep.BrightnessSetting => "亮度设置",
        CtcsInteractionStep.SoftwareVersion => "软件版本",
        CtcsInteractionStep.StartConfirmation => "启动确认",
        CtcsInteractionStep.BrakeReleaseConfirmation => "缓解确认",
        _ => ""
    };
    public bool ShowD6PageTitle => D6PageTitle.Length > 0;
    public bool CanHandle(CtcsButton button) => _controller.CanHandle(button);
    public CtcsBrakeLevel BrakeLevel => _controller.BrakeLevel;
    public bool ShowTargetDistance => Data.ControlMode == CtcsControlMode.FullSupervision && IsTsm && Data.TargetDistance.HasValue;
    public string TargetDistanceText
    {
        get
        {
            if (!ShowTargetDistance)
                return "";
            double distance = Math.Min(Data.TargetDistance!.Value, 99990);
            int precision = distance < 1000 ? 1 : 10;
            return (Math.Floor(distance / precision) * precision).ToString("0", CultureInfo.InvariantCulture);
        }
    }
    public bool CanScrollTextUp => _firstVisibleMessage > 0;
    public bool CanScrollTextDown => _firstVisibleMessage + 4 < _messages.Count;
    public bool HasPendingTextConfirmation => _messages.Any(message => message.NeedsConfirmation);
    public CtcsTextMessageRow? TextLine1 => GetTextLine(0);
    public CtcsTextMessageRow? TextLine2 => GetTextLine(1);
    public CtcsTextMessageRow? TextLine3 => GetTextLine(2);
    public CtcsTextMessageRow? TextLine4 => GetTextLine(3);
    public SpeedMonitoringSection Section => _controller.Section;
    public bool IsTsm => Section == SpeedMonitoringSection.TSM;
    public bool ShowCsg => Data.ControlMode == CtcsControlMode.FullSupervision;
    public bool ShowLightBand => Data.ControlMode == CtcsControlMode.FullSupervision;
    public bool ShowDistanceScale => CurrentInteraction.Step is null &&
        (Data.ControlMode is CtcsControlMode.FullSupervision or CtcsControlMode.PartialSupervision);

    // Vint 是本次显示的干预速度终点；超过 SBI 后向 EBI 延伸。
    public double? InterventionSpeed => Data.InterventionSpeed;
    public BrakeWarningDisplay BrakeWarning => BrakeWarningDisplay.Calculate(Data, Section);

    public SpeedDisplayState DisplayState =>
        _controller.IsInterventionSpeedReached ? SpeedDisplayState.SbiExceeded :
        _controller.IsPermittedSpeedExceeded ? SpeedDisplayState.PermittedExceeded :
        ShowLightBand && IsTsm ? SpeedDisplayState.TargetMonitoring :
        SpeedDisplayState.Normal;

    public bool IsTargetMonitoring => DisplayState == SpeedDisplayState.TargetMonitoring;
    public bool IsPermittedExceeded => DisplayState == SpeedDisplayState.PermittedExceeded;
    public bool IsSbiExceeded => DisplayState == SpeedDisplayState.SbiExceeded;
    public bool IsPermittedBandYellow => IsTsm || IsPermittedExceeded || IsSbiExceeded;

    public double? WarningBandEndSpeed => DisplayState switch
    {
        SpeedDisplayState.SbiExceeded or SpeedDisplayState.PermittedExceeded => InterventionSpeed,
        _ => null
    };

    // 在界面线程一次提交一组数据，避免界面混用不同批次的速度和区段。
    public void Update(CtcsData data)
    {
        _controller.Update(data);
        Refresh();
    }

    public void AdvanceBrakeWarning(TimeSpan elapsed)
    {
        _controller.AdvanceBrakeWarning(elapsed);
        OnPropertyChanged(nameof(Data));
        OnPropertyChanged(nameof(BrakeWarning));
    }

    public bool KeyHandle(CtcsButton button)
    {
        if (!_controller.KeyHandle(button))
            return false;
        Refresh();
        return true;
    }

    private void Refresh()
    {
        var menu = FunctionKeyMenu.GetItems(CurrentInteraction.Step);
        FunctionKeys = Array.AsReadOnly(new[]
        {
            CtcsButton.F1, CtcsButton.F2, CtcsButton.F3, CtcsButton.F4,
            CtcsButton.F5, CtcsButton.F6, CtcsButton.F7, CtcsButton.F8
        }.Select(key =>
        {
            var item = menu.SingleOrDefault(candidate => candidate.Key == key);
            return new CtcsFunctionKeyState(key, item?.Label ?? "", item is not null && CanHandle(key));
        }).ToArray());
        var orderedMessages = Data.TextMessages.Select((message, index) => (message, index))
            .OrderByDescending(item => item.message.Timestamp)
            .ThenByDescending(item => item.index)
            .Select(item => item.message).ToList();
        _messages = orderedMessages.Where(message =>
            message.Type != CtcsTextMessageType.TransientConfirmation || !message.IsConfirmed).ToList();
        var latestId = orderedMessages.FirstOrDefault()?.Id;
        if (latestId != _latestMessageId)
        {
            _latestMessageId = latestId;
            _firstVisibleMessage = 0;
            _messageBlinkVisible = true;
        }
        ClampTextScrollPosition();
        OnPropertyChanged(nameof(Data));
        OnPropertyChanged(nameof(CurrentInteraction));
        OnPropertyChanged(nameof(CurrentInteractionRoot));
        OnPropertyChanged(nameof(History));
        OnPropertyChanged(nameof(FunctionKeys));
        OnPropertyChanged(nameof(D6PageTitle));
        OnPropertyChanged(nameof(ShowD6PageTitle));
        OnPropertyChanged(nameof(BrakeWarning));
        OnPropertyChanged(nameof(ShowTargetDistance));
        OnPropertyChanged(nameof(BrakeLevel));
        OnPropertyChanged(nameof(TargetDistanceText));
        OnPropertyChanged(nameof(CurrentLevelName));
        OnPropertyChanged(nameof(ControlModeShortName));
        OnPropertyChanged(nameof(Section));
        OnPropertyChanged(nameof(IsTsm));
        OnPropertyChanged(nameof(ShowCsg));
        OnPropertyChanged(nameof(ShowLightBand));
        OnPropertyChanged(nameof(ShowDistanceScale));
        OnPropertyChanged(nameof(DisplayState));
        OnPropertyChanged(nameof(IsTargetMonitoring));
        OnPropertyChanged(nameof(IsPermittedExceeded));
        OnPropertyChanged(nameof(IsSbiExceeded));
        OnPropertyChanged(nameof(InterventionSpeed));
        OnPropertyChanged(nameof(IsPermittedBandYellow));
        OnPropertyChanged(nameof(WarningBandEndSpeed));
        NotifyTextMessages();
    }

    public void ScrollTextUp()
    {
        if (!CanScrollTextUp)
            return;
        _firstVisibleMessage--;
        NotifyTextMessages();
    }

    public void ScrollTextDown()
    {
        if (!CanScrollTextDown)
            return;
        _firstVisibleMessage++;
        NotifyTextMessages();
    }

    public void ToggleTextConfirmationBlink()
    {
        if (!HasPendingTextConfirmation)
            return;
        _messageBlinkVisible = !_messageBlinkVisible;
        NotifyTextMessages();
    }

    public void ConfirmTextMessage(Guid id)
    {
        if (_controller.ConfirmTextMessage(id))
            Refresh();
    }

    private void ClampTextScrollPosition() =>
        _firstVisibleMessage = Math.Min(_firstVisibleMessage, Math.Max(0, _messages.Count - 4));

    private CtcsTextMessageRow? GetTextLine(int row)
    {
        int index = _firstVisibleMessage + row;
        if (index >= _messages.Count)
            return null;
        var message = _messages[index];
        return new CtcsTextMessageRow(message, message.Id == _latestMessageId, _messageBlinkVisible);
    }

    private void NotifyTextMessages()
    {
        OnPropertyChanged(nameof(CanScrollTextUp));
        OnPropertyChanged(nameof(CanScrollTextDown));
        OnPropertyChanged(nameof(HasPendingTextConfirmation));
        OnPropertyChanged(nameof(TextLine1));
        OnPropertyChanged(nameof(TextLine2));
        OnPropertyChanged(nameof(TextLine3));
        OnPropertyChanged(nameof(TextLine4));
    }

    public string CurrentLevelName => $"CTCS {(int)Data.CurrentLevel}";

    // 对应 B7 的两字（或 LKJ）显示。由 ControlMode 自动计算，无需单独更新。
    public string ControlModeShortName => Data.ControlMode switch
    {
        CtcsControlMode.FullSupervision => "完全",
        CtcsControlMode.PartialSupervision => "部分",
        CtcsControlMode.Shunting => "调车",
        CtcsControlMode.OnSight => "目视",
        CtcsControlMode.Standby => "待机",
        CtcsControlMode.Isolation => "隔离",
        CtcsControlMode.CabSignalling => "LKJ",
        CtcsControlMode.Guiding => "引导",
        _ => ""
    };

}
