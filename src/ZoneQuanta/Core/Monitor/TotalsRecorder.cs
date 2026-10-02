using System.IO;

namespace ZoneQuanta.Core.Monitor;

public sealed class TotalsRecorder
{
    private readonly string _path = Path.Combine(TrafficFile.DefaultDir, "totals.tsv");
    private readonly TrafficFile _file;
    private long _pendingUp, _pendingDown;
    private int _seconds;

    public TotalsRecorder()
    {
        _file = TrafficFile.Load(_path);
        _file.Compact();
    }

    public TrafficFile File => _file;

    public void Add(Metrics m)
    {
        _pendingUp += m.UpBytes;
        _pendingDown += m.DownBytes;
        if (++_seconds % 10 == 0) Commit();
        if (_seconds % 120 == 0) Flush();
    }

    public void Flush()
    {
        Commit();
        _file.Save(_path);
    }

    private void Commit()
    {
        if (_pendingUp == 0 && _pendingDown == 0) return;
        _file.AddNow(TrafficFile.TotalKey, _pendingUp, _pendingDown);
        _pendingUp = _pendingDown = 0;
    }
}
