using System;

namespace RailDisplay.Core.Models.CTCS;

public enum CtcsTextMessageType
{
    Information,
    RetainedConfirmation,
    TransientConfirmation
}

public sealed record CtcsTextMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public string Content { get; init; } = string.Empty;
    public CtcsTextMessageType Type { get; init; }
    public bool IsConfirmed { get; init; }
    public bool NeedsConfirmation => Type != CtcsTextMessageType.Information && !IsConfirmed;
}

