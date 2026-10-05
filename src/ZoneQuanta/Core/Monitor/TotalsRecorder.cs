using System.IO;

namespace ZoneQuanta.Core.Monitor;

public sealed class TotalsRecorder
{
    private readonly string _path = Path.Combine(TrafficFile.DefaultDir, "totals.tsv");
    private readonly TrafficFile _file;
    private int _seconds;

    public TotalsRecorder()
    {
        _file = TrafficFile.Load(_path);
        _file.Compact();
    }

    public TrafficFile File => _file;

    public void Add(Metrics m)
    {
        _file.AddNow(TrafficFile.TotalKey, m.UpBytes, m.DownBytes);
        if (++_seconds % 120 == 0) Flush();
    }

    public void Flush() => _file.Save(_path);
}
