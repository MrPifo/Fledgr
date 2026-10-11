using System.Threading.Channels;

namespace Fledgr.Audio;

/// <summary>
/// Liest einen Netz-Stream in einem eigenen Hintergrund-Lauf in einen Puffer und gibt die Daten gleichmäßig weiter.
/// Manche Server (z. B. streamonkey bei radio 88.6) liefern Radio in Schüben alle 2 Sekunden. Ohne Vorpuffer läuft der Player
/// zwischen den Schüben leer und der Ton stockt. Der erste Lesevorgang wartet darum, bis <c>prebufferBytes</c> da sind (oder höchstens 8 Sekunden).
/// Nur lesend, ohne Positionen.
/// </summary>
sealed class PumpedBody : Stream
{
    static readonly TimeSpan PrimeTimeout = TimeSpan.FromSeconds(8);

    readonly Stream _src;
    readonly int _prebuffer;
    readonly CancellationTokenSource _cts;
    readonly Channel<ArraySegment<byte>> _chunks = Channel.CreateBounded<ArraySegment<byte>>(new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = true });
    long _buffered;
    volatile bool _pumpDone;
    bool _primed;
    ArraySegment<byte> _current;

    public PumpedBody(Stream source, int prebufferBytes, CancellationToken life)
    {
        _src = source;
        _prebuffer = prebufferBytes;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(life);
        _ = Task.Run(PumpAsync);
    }

    async Task PumpAsync()
    {
        try
        {
            var ct = _cts.Token;
            while (!ct.IsCancellationRequested)
            {
                var buf = new byte[4096];
                var n = await _src.ReadAsync(buf.AsMemory(), ct);
                if (n == 0) break;
                Interlocked.Add(ref _buffered, n);
                await _chunks.Writer.WriteAsync(new ArraySegment<byte>(buf, 0, n), ct);
            }
        }
        catch (Exception)
        {
            // Lesefehler oder Abbruch: für den Leser ist das einfach das Ende des Streams.
        }
        finally
        {
            _pumpDone = true;
            _chunks.Writer.TryComplete();
        }
    }

    async Task PrimeAsync(CancellationToken ct)
    {
        var until = DateTime.UtcNow + PrimeTimeout;
        while (Interlocked.Read(ref _buffered) < _prebuffer && !_pumpDone && DateTime.UtcNow < until)
            await Task.Delay(40, ct);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        if (!_primed)
        {
            _primed = true;
            await PrimeAsync(cancellationToken);
        }
        while (_current.Count == 0)
        {
            if (!await _chunks.Reader.WaitToReadAsync(cancellationToken)) return 0;
            _chunks.Reader.TryRead(out _current);
        }
        var n = Math.Min(destination.Length, _current.Count);
        _current.AsSpan(0, n).CopyTo(destination.Span);
        _current = _current.Slice(n);
        Interlocked.Add(ref _buffered, -n);
        return n;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _cts.Cancel(); } catch (ObjectDisposedException) { }
            _src.Dispose();
        }
        base.Dispose(disposing);
    }
}
