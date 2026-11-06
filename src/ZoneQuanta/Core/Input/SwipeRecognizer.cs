using System;

namespace ZoneQuanta.Core.Input;

public enum SwipeDirection { None, Up, Down, Left, Right }

// Multi-finger swipe from contact frames (finger count + centroid, 0..1 of the surface). Tracking starts when exactly
// `fingers` are down and restarts whenever that count is reached again; a swipe fires once per touch when the centroid has
// travelled `travel` of the surface along one axis, `dominance` times more than across it (in physical units, via the
// surface's width / height), within `windowMs`. More fingers than asked cancel until every finger lifts.
public sealed class SwipeRecognizer
{
    private readonly int _fingers;
    private readonly double _travel, _dominance;
    private readonly long _windowMs;
    private bool _tracking, _done, _blocked;
    private long _t0;
    private double _x0, _y0;

    public SwipeRecognizer(int fingers = 3, double travel = 0.15, double dominance = 1.4, long windowMs = 1500)
    {
        _fingers = fingers;
        _travel = travel;
        _dominance = dominance;
        _windowMs = windowMs;
    }

    // Fingers down in the last frame (any count), for callers that care whether a gesture is in progress.
    public int Down { get; private set; }

    // aspect = surface width / height in physical units (1 when unknown).
    public SwipeDirection Feed(long ms, int down, double x, double y, double aspect)
    {
        Down = down;
        if (down == 0)
        {
            _tracking = _done = _blocked = false;
            return SwipeDirection.None;
        }
        if (down > _fingers) _blocked = true;
        if (_blocked || down != _fingers)
        {
            _tracking = false;
            return SwipeDirection.None;
        }
        if (!_tracking)
        {
            (_tracking, _t0, _x0, _y0) = (true, ms, x, y);
            return SwipeDirection.None;
        }
        if (_done || ms - _t0 > _windowMs) return SwipeDirection.None;
        double dx = x - _x0, dy = y - _y0;
        double a = aspect > 0 && double.IsFinite(aspect) ? aspect : 1;
        // Across-axis drift in units of the travel axis: a 1.6:1 touchpad's width fraction counts 1.6 times.
        if (Math.Abs(dy) >= _travel && Math.Abs(dy) >= _dominance * Math.Abs(dx) * a)
        {
            _done = true;
            return dy > 0 ? SwipeDirection.Down : SwipeDirection.Up;
        }
        if (Math.Abs(dx) >= _travel && Math.Abs(dx) * a >= _dominance * Math.Abs(dy))
        {
            _done = true;
            return dx > 0 ? SwipeDirection.Right : SwipeDirection.Left;
        }
        return SwipeDirection.None;
    }
}
