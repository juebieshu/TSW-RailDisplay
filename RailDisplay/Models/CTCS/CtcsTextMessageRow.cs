using System.Globalization;
using RailDisplay.Core.Models.CTCS;

namespace RailDisplay.Models.CTCS;

public sealed record CtcsTextMessageRow(CtcsTextMessage Message, bool IsNew, bool BlinkVisible)
{
    public string TimeText => Message.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    public string Content => Message.Content;
    public bool IsWhite => IsNew && !Message.IsConfirmed;
    public bool ShowConfirmationBorder => Message.NeedsConfirmation && BlinkVisible;
}
