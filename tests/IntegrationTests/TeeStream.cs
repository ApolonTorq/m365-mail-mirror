namespace M365MailMirror.IntegrationTests;

/// <summary>
/// A Stream decorator that forwards all writes to multiple underlying streams.
/// The primary stream is used for reading; all streams receive writes.
/// </summary>
public class TeeStream : Stream
{
    private readonly Stream _primary;
    private readonly Stream[] _secondaries;

    /// <summary>
    /// Creates a TeeStream that writes to multiple streams.
    /// </summary>
    /// <param name="primary">The primary stream (used for reads and position).</param>
    /// <param name="secondaries">Additional streams that receive all writes.</param>
    public TeeStream(Stream primary, params Stream[] secondaries)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _secondaries = secondaries ?? [];
    }

    public override bool CanRead => _primary.CanRead;
    public override bool CanSeek => _primary.CanSeek;
    public override bool CanWrite => _primary.CanWrite;
    public override long Length => _primary.Length;

    public override long Position
    {
        get => _primary.Position;
        set => _primary.Position = value;
    }

    public override void Flush()
    {
        _primary.Flush();
        foreach (var s in _secondaries)
            s.Flush();
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _primary.FlushAsync(cancellationToken);
        foreach (var s in _secondaries)
            await s.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        _primary.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) =>
        _primary.Seek(offset, origin);

    public override void SetLength(long value) =>
        _primary.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        _primary.Write(buffer, offset, count);
        foreach (var s in _secondaries)
            s.Write(buffer, offset, count);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var memory = buffer.AsMemory(offset, count);
        await _primary.WriteAsync(memory, cancellationToken);
        foreach (var s in _secondaries)
            await s.WriteAsync(memory, cancellationToken);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _primary.WriteAsync(buffer, cancellationToken);
        foreach (var s in _secondaries)
            await s.WriteAsync(buffer, cancellationToken);
    }

    public override void WriteByte(byte value)
    {
        _primary.WriteByte(value);
        foreach (var s in _secondaries)
            s.WriteByte(value);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _primary.Dispose();
            foreach (var s in _secondaries)
                s.Dispose();
        }
        base.Dispose(disposing);
    }
}
