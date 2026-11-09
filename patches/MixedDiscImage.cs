using RecompOne.Runtime.Cdrom;

namespace Kf3;

/// <summary>
/// The US disc with the Japanese layer over it, as the one image the game reads.
///
/// A read is checked in this order: a sector inside a stream range comes from the
/// Japanese disc at the same place in its video (past that video's end it is zeros);
/// a 2048-byte data read of an overridden sector returns the override; anything else
/// is the US disc's. Tracks, the leadout and the data sector count are the US disc's.
///
/// The game thread and the CD thread both read it. The two maps are filled once, in
/// the constructor, and never written again, so they need no lock. The US disc is
/// opened more than once, so several of these can share one Japanese image; its reads
/// lock on that image itself. The US image is owned: disposing this disposes it. The
/// Japanese image is not, and outlives it.
/// </summary>
internal sealed class MixedDiscImage : IDiscImage
{
    const int SectorBytes = 2048;
    const int RawSectorBytes = 2352;

    readonly IDiscImage _us;
    readonly IDiscImage _jp;
    readonly IReadOnlyDictionary<int, byte[]> _overrides;
    readonly IReadOnlyList<StreamRange> _streams;
    readonly ServedCount? _served;

    /// <param name="served">The KF3_JP_PROBE count, or null when the probe is off: then a
    /// read pays one null check and nothing else.</param>
    public MixedDiscImage(IDiscImage us, IDiscImage jp, IReadOnlyDictionary<int, byte[]> overrides, IReadOnlyList<StreamRange> streams, ServedCount? served)
    {
        _us = us;
        _jp = jp;
        _overrides = overrides;
        _streams = streams;
        _served = served;
    }

    /// <summary>
    /// The two totals KF3_JP_PROBE=1 counts: override sectors and video sectors served.
    /// The read path only increments them, with Interlocked. A timer, every 5 s, logs
    /// the pair when either total has moved since the last line; nothing is logged on
    /// a read.
    /// </summary>
    internal sealed class ServedCount
    {
        const int ReportMs = 5000;

        readonly object _report = new();
        readonly Timer _timer;
        long _overrides;
        long _streams;
        long _reportedOverrides;
        long _reportedStreams;

        public ServedCount()
        {
            _timer = new Timer(_ => Report(), null, ReportMs, ReportMs);
        }

        public void CountOverride() => Interlocked.Increment(ref _overrides);

        public void CountStream() => Interlocked.Increment(ref _streams);

        void Report()
        {
            lock (_report)
            {
                long overrides = Interlocked.Read(ref _overrides);
                long streams = Interlocked.Read(ref _streams);
                if (overrides == _reportedOverrides && streams == _reportedStreams) return;
                _reportedOverrides = overrides;
                _reportedStreams = streams;
                Console.WriteLine($"[KF3] japanese: served {overrides} override sector(s), {streams} video sector(s)");
            }
        }
    }

    public string Format => _us.Format;

    public int FirstTrack => _us.FirstTrack;

    public int LastTrack => _us.LastTrack;

    public bool HasTracks => _us.HasTracks;

    public int LeadoutLba => _us.LeadoutLba;

    public int DataSectors => _us.DataSectors;

    public IReadOnlyList<DiscTrack> Tracks => _us.Tracks;

    public bool TrackStartLba(int track, out int lba)
    {
        return _us.TrackStartLba(track, out lba);
    }

    public byte[] ReadSectorData(int lba, int size)
    {
        if (StreamAt(lba) is { } stream)
        {
            _served?.CountStream();
            return StreamSectorData(stream, lba, size);
        }
        if (size == SectorBytes && _overrides.TryGetValue(lba, out var replacement))
        {
            _served?.CountOverride();
            return replacement.AsSpan().ToArray();
        }
        return _us.ReadSectorData(lba, size);
    }

    public byte[] ReadRawSector(int lba)
    {
        if (StreamAt(lba) is { } stream)
        {
            _served?.CountStream();
            return StreamRawSector(stream, lba);
        }
        return _us.ReadRawSector(lba);
    }

    public void Dispose()
    {
        _us.Dispose();
    }

    /// <summary>The video that covers this US sector, if any.</summary>
    StreamRange? StreamAt(int lba)
    {
        foreach (var stream in _streams)
            if (lba >= stream.UsLba && lba - stream.UsLba < stream.Sectors)
                return stream;
        return null;
    }

    byte[] StreamSectorData(StreamRange stream, int lba, int size)
    {
        int index = stream.JpLba + (lba - stream.UsLba);
        if (lba - stream.UsLba >= stream.JpSectors)
            return new byte[size];
        lock (_jp)
        {
            return _jp.ReadSectorData(index, size);
        }
    }

    byte[] StreamRawSector(StreamRange stream, int lba)
    {
        int index = stream.JpLba + (lba - stream.UsLba);
        if (lba - stream.UsLba >= stream.JpSectors)
            return new byte[RawSectorBytes];
        lock (_jp)
        {
            return _jp.ReadRawSector(index);
        }
    }
}
