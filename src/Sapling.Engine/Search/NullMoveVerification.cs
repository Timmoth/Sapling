namespace Sapling.Engine.Search;

internal static class NullMoveVerification
{
    public static bool ShouldVerify(int depth, int minimumDepth) => depth >= minimumDepth;
}
