using RailDisplay.Core.Models.CTCS;

namespace RailDisplay.Models.CTCS;

public sealed record CtcsFunctionKeyState(CtcsButton Key, string Label, bool IsEnabled)
{
    public bool IsEmpty => string.IsNullOrEmpty(Label);
}
