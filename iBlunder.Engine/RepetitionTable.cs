namespace iBlunder.Engine;

public sealed class RepetitionTable
{
    private readonly ulong[] _hashes;
    private readonly int[] _startIndices;
    private int _count;

    public RepetitionTable()
    {
        _hashes = new ulong[256];
        _startIndices = new int[_hashes.Length + 1];
    }

    public void Init(Stack<ulong> history)
    {
        var initialHashes = history.Reverse().ToArray();
        _count = initialHashes.Length;

        for (var i = 0; i < initialHashes.Length; i++)
        {
            _hashes[i] = initialHashes[i];
            _startIndices[i] = 0;
        }

        _startIndices[_count] = 0;
    }

    public void Push(ulong hash, bool reset)
    {
        // Check bounds just in case
        if (_count < _hashes.Length)
        {
            _hashes[_count] = hash;
            _startIndices[_count + 1] = reset ? _count : _startIndices[_count];
        }

        _count++;
    }

    public void TryPop()
    {
        _count = Math.Max(0, _count - 1);
    }

    public bool DetectThreeFoldRepetition(ulong h)
    {
        var c = 0;
        // up to count-1 so that curr position is not counted
        for (var i = _startIndices[_count]; i < _count; i++)
        {
            if (_hashes[i] == h && ++c >= 2)
            {
                return true;
            }
        }

        return false;
    }
}