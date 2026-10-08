using System;
using System.IO;

namespace ZoneQuanta.Core.Monitor;

public sealed class TotalsRecorder
{
    private readonly string _path = Path.Combine(TrafficFile.DefaultDir, "totals.tsv");
    private readonly TrafficFile _file;
    private long _flushedAt = Environment.TickCount64;

    public TotalsRecorder()
    {
        _file = TrafficFile.Load(_path);
        _file.Compact();
    }

    public TrafficFile File => _file;

    public void Add(Metrics m)
    {
        _file.AddNow(TrafficFile.TotalKey, m.UpBytes, m.DownBytes);
        if (Environment.TickCount64 - _flushedAt >= 120_000) Flush();
    }

    public void Flush()
    {
        _flushedAt = Environment.TickCount64;
        _file.Save(_path);
    }
}
