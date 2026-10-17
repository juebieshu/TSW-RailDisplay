namespace RailDisplay.Models.CTCS;

public sealed record CtcsScreenProfile(bool SupportsLevel3)
{
    // 屏型能力仅限制本地切换入口，不限制核心接收合法的等级数据。
    public static CtcsScreenProfile CTCS2_200H { get; } = new(SupportsLevel3: false);
}
