namespace Hyperion.Core.Torrent;

/// <summary>
/// Fixed-capacity ring holding a contiguous window of a file: [StartOffset, EndOffset).
/// Not thread-safe; callers lock.
/// </summary>
public sealed class StreamRingBuffer
{
    readonly byte[] _buf;
    int _head;

    public StreamRingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _buf = GC.AllocateUninitializedArray<byte>(capacity);
    }

    public int Capacity => _buf.Length;
    public int Count { get; private set; }
    public int Free => Capacity - Count;
    public long StartOffset { get; private set; }
    public long EndOffset => StartOffset + Count;

    public void Reset(long fileOffset)
    {
        _head = 0;
        Count = 0;
        StartOffset = fileOffset;
    }

    /// <summary>Appends as much of <paramref name="src"/> as fits; returns bytes written.</summary>
    public int Write(ReadOnlySpan<byte> src)
    {
        var n = Math.Min(src.Length, Free);
        var tail = (_head + Count) % Capacity;
        var first = Math.Min(n, Capacity - tail);
        src[..first].CopyTo(_buf.AsSpan(tail, first));
        src[first..n].CopyTo(_buf.AsSpan(0, n - first));
        Count += n;
        return n;
    }

    /// <summary>Copies buffered bytes starting at <paramref name="fileOffset"/> without consuming; 0 if not buffered.</summary>
    public int Read(long fileOffset, Span<byte> dst)
    {
        if (fileOffset < StartOffset || fileOffset >= EndOffset) return 0;
        var rel = (int)(fileOffset - StartOffset);
        var n = Math.Min(dst.Length, Count - rel);
        var idx = (_head + rel) % Capacity;
        var first = Math.Min(n, Capacity - idx);
        _buf.AsSpan(idx, first).CopyTo(dst);
        _buf.AsSpan(0, n - first).CopyTo(dst[first..]);
        return n;
    }

    /// <summary>Frees space by dropping everything before <paramref name="fileOffset"/>.</summary>
    public void TrimBefore(long fileOffset)
    {
        if (fileOffset <= StartOffset) return;
        var drop = (int)Math.Min(fileOffset - StartOffset, Count);
        _head = (_head + drop) % Capacity;
        Count -= drop;
        StartOffset += drop;
    }
}
