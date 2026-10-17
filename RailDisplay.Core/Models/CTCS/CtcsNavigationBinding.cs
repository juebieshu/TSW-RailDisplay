namespace RailDisplay.Core.Models.CTCS;

/// <summary>Step 为 null 时属于模式默认菜单；每条绑定明确指定进入目标或返回。</summary>
public sealed record CtcsNavigationBinding(
    CtcsInteractionStep? Step,
    CtcsButton Button,
    CtcsInteractionStep? TargetStep = null,
    bool IsBack = false);
