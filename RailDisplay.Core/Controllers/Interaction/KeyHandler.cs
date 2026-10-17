namespace RailDisplay.Core.Controllers.Interaction;

/// <summary>初始化时复制按键表；处理器从本次 context 读取最新模式与步骤，不管理返回栈。</summary>
public sealed class KeyHandler<TKey, TContext>
    where TKey : struct, Enum
    where TContext : class
{
    private readonly Dictionary<TKey, Func<TContext, bool>> _bindings = new();
    private readonly Func<TKey, TContext, bool>? _canHandle;

    public KeyHandler(IEnumerable<KeyValuePair<TKey, Func<TContext, bool>>> bindings,
        Func<TKey, TContext, bool>? canHandle = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _canHandle = canHandle;
        foreach (var (key, handler) in bindings)
        {
            ValidateKey(key);
            ArgumentNullException.ThrowIfNull(handler);
            if (!_bindings.TryAdd(key, handler))
                throw new ArgumentException($"按键 {key} 重复绑定。", nameof(bindings));
        }
    }

    /// <summary>只查询可用性，不执行处理器；查询和执行共用同一条件。</summary>
    public bool CanHandle(TKey key, TContext context)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(context);
        return _bindings.ContainsKey(key) && (_canHandle?.Invoke(key, context) ?? true);
    }

    public bool KeyHandle(TKey key, TContext context)
    {
        return CanHandle(key, context) && _bindings[key](context);
    }

    private static void ValidateKey(TKey key)
    {
        if (!Enum.IsDefined(key))
            throw new ArgumentOutOfRangeException(nameof(key), "按键必须为已定义的有效值。");
    }
}
