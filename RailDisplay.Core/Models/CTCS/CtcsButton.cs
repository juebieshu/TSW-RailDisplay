namespace RailDisplay.Core.Models.CTCS;

/// <summary>CTCS 实体按键标识，共 19 个；枚举值不代表通信协议编号。</summary>
public enum CtcsButton
{
    // 功能键；具体作用由当前菜单决定。
    F1 = 1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,

    Digit0,    // 0
    Digit1,    // 调车/1
    Digit2,    // 目视/2 ABC
    Digit3,    // 启动/3 DEF
    Digit4,    // 缓解/4 GHI
    Digit5,    // 上行/5 JKL
    Digit6,    // 下行/6 MNO
    Digit7,    // 确定/7 PQRS
    Digit8,    // 8 TLV
    Digit9,    // 9 WXYZ
    Vigilance  // 警惕/字母
}
