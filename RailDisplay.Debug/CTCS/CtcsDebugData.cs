using System.Linq;
using RailDisplay.Core.Models.CTCS;

namespace RailDisplay.Debug.CTCS;

public static class CtcsDebugData
{
    public static CtcsData Create(CtcsData current) => new()
    {
        Speed = current.Speed,
        TargetSpeed =  40,
        TargetDistance = 1122,
        TsmEntryDistance = 2000,
        PermittedSpeed = 90,
        SbiSpeed = 95,
        EbiSpeed = 100,
        BrakeWarningTime = 0,
        BrakeLevel = CtcsBrakeLevel.NoBrake,
        ControlMode = CtcsControlMode.FullSupervision,
        TextMessages = current.TextMessages.Append(new CtcsTextMessage
        {
            Content = "进入待机模式"
        }).ToArray()
    };
}
