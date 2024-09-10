namespace Sapling.Engine;

public readonly record struct Position(int Row, int Col)
{
    public int GetIndex()
    {
        return Row * 8 + Col;
    }

    public static Position FromIndex(int index)
    {
        return new Position(index / 8, index % 8);
    }
}