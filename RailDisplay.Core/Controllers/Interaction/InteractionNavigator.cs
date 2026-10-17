namespace RailDisplay.Core.Controllers.Interaction;

/// <summary>
/// 维护当前交互与返回栈；TState 必须是不可变快照，内部集合由调用方复制或只读化。
/// 输入清理方式由设备提供，导航器不保存运行模式或界面信息。
/// </summary>
public sealed class InteractionNavigator<TState> where TState : class
{
    private readonly Func<TState, TState> _clearInput;
    public TState Current { get; private set; }
    /// <summary>从最早到最近排列，末项为上一步，不含当前状态。</summary>
    public IReadOnlyList<TState> History { get; private set; } = Array.AsReadOnly(Array.Empty<TState>());

    public InteractionNavigator(TState initial, Func<TState, TState> clearInput)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(clearInput);
        Current = initial;
        _clearInput = clearInput;
    }

    public void Next(TState target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var history = Array.AsReadOnly(History.Append(Current).ToArray());
        Current = target;
        History = history;
    }

    /// <summary>回退 count 步，≤0 按一次处理，超过深度时回到最早状态；默认清空目标输入。</summary>
    public bool Back(int count = 1, bool keepInput = false)
    {
        if (count <= 0)
            count = 1;
        if (History.Count == 0)
            return false;
        int index = History.Count - Math.Min(count, History.Count);
        var previous = keepInput ? History[index] : _clearInput(History[index]);
        ArgumentNullException.ThrowIfNull(previous);
        var history = Array.AsReadOnly(History.Take(index).ToArray());
        Current = previous;
        History = history;
        return true;
    }
}
