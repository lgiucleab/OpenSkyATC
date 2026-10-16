using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.TaxiArrows;

public sealed record TaxiArrowOptions
{
    /// <summary>Settings toggle (app settings and toolbar panel); off hides every arrow at once.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Arrows are shown this far along the route ahead of the aircraft (owner's default, a setting).</summary>
    public double LookAheadM { get; init; } = 300;

    /// <summary>Distance between arrows along the route (owner's default, a setting).</summary>
    public double SpacingM { get; init; } = 10;

    /// <summary>No arrow closer than this ahead of the aircraft's reference point (it would sit under the nose).</summary>
    public double MinAheadM { get; init; } = 8;

    /// <summary>Within this distance of the route's end (holding point or stand) the arrows are removed.</summary>
    public double EndReachedM { get; init; } = 30;

    /// <summary>How far back along the route the aircraft may be found again (a small reverse or GPS jitter).</summary>
    public double BacktrackM { get; init; } = 15;
}

/// <summary>One arrow: a stable key (the same arrow keeps its key while it stays in view), position and true heading.</summary>
public readonly record struct ArrowPose(long Key, GeoPoint Position, double HeadingTrueDeg);

/// <summary>
/// Decides which taxi arrows to show (design: step 10). Arrows sit at fixed points every <see cref="TaxiArrowOptions.SpacingM"/>
/// along the cleared route, so they stay put while the aircraft moves; only the next
/// <see cref="TaxiArrowOptions.LookAheadM"/> are shown. Nothing is shown until the route is read back correctly; at a
/// runway hold-short the arrows stop until that crossing is read back. A new route replaces the old arrows; at the end
/// of the route, outside the taxi phases, or when turned off, none are shown. Reads only <see cref="FlightSession"/>.
/// </summary>
public sealed class TaxiArrowController(TaxiArrowOptions? options = null)
{
    private TaxiClearance? _route;
    private TaxiPath? _path;
    private long _version;
    private double _progress;
    private bool _finished;

    public TaxiArrowOptions Options { get; set; } = options ?? new TaxiArrowOptions();

    /// <summary>Metres along the active route the aircraft has reached (for tests and diagnostics).</summary>
    public double ProgressM => _progress;

    public IReadOnlyList<ArrowPose> Update(FlightSession session, SimState state)
    {
        var o = Options;
        var clearance = session.Clearance;
        if (clearance.Taxi is not { } taxi || taxi.Path.Count < 2) return Forget();

        if (!ReferenceEquals(taxi, _route))
        {
            _route = taxi;
            _path = new TaxiPath(taxi.Path);
            _version++;
            _progress = 0;
            _finished = false;
        }
        var path = _path!;
        var aircraft = new GeoPoint(state.Latitude, state.Longitude);
        _progress = path.Along(aircraft, Math.Max(_progress - o.BacktrackM, 0));
        if (path.LengthM - _progress <= o.EndReachedM) _finished = true;

        bool taxiing = session.Phase is FlightPhase.TaxiOut or FlightPhase.TaxiIn && !session.Ended;
        if (!o.Enabled || !taxiing || !clearance.TaxiReadBack || _finished || o.SpacingM <= 0) return [];

        // Arrows end at the first hold-short ahead whose crossing has not been read back.
        double stop = path.LengthM;
        foreach (var (runway, holdShort) in taxi.Crossings)
        {
            if (clearance.IsCrossReadBack(runway)) continue;
            double s = path.Along(holdShort, Math.Max(_progress - o.BacktrackM, 0));
            if (s >= _progress - o.BacktrackM) stop = Math.Min(stop, s);
        }

        double first = _progress + o.MinAheadM, last = Math.Min(_progress + o.LookAheadM, stop);
        var arrows = new List<ArrowPose>();
        for (long k = (long)Math.Ceiling(first / o.SpacingM); k * o.SpacingM <= last + 1e-6; k++)
        {
            var (p, track) = path.At(k * o.SpacingM);
            arrows.Add(new ArrowPose(_version * 1_000_000 + k, p, track));
        }
        return arrows;
    }

    private IReadOnlyList<ArrowPose> Forget()
    {
        _route = null;
        _path = null;
        return [];
    }
}

/// <summary>A change to the sim objects: create a new arrow, move an existing one (reused), or remove one.</summary>
public abstract record ArrowOp(int Handle);
public sealed record CreateArrow(int Handle, ArrowPose Pose) : ArrowOp(Handle);
public sealed record MoveArrow(int Handle, ArrowPose Pose) : ArrowOp(Handle);
public sealed record RemoveArrow(int Handle) : ArrowOp(Handle);

/// <summary>
/// Turns the wanted arrows into as few sim-object changes as possible: arrows that stay keep their object, arrows that
/// leave the window are moved to the new positions ahead (cheaper than removing and creating), and only the surplus is
/// removed. Handles are this pool's ids; the renderer maps them to sim object ids.
/// </summary>
public sealed class ArrowPool
{
    private readonly Dictionary<long, (int Handle, ArrowPose Pose)> _shown = new();
    private int _nextHandle = 1;

    public int Count => _shown.Count;

    public IReadOnlyList<ArrowOp> Update(IReadOnlyList<ArrowPose> wanted)
    {
        var ops = new List<ArrowOp>();
        var wantedKeys = wanted.Select(w => w.Key).ToHashSet();
        var free = new Queue<int>();
        foreach (var key in _shown.Keys.Where(k => !wantedKeys.Contains(k)).ToList())
        {
            free.Enqueue(_shown[key].Handle);
            _shown.Remove(key);
        }

        foreach (var pose in wanted)
        {
            if (_shown.TryGetValue(pose.Key, out var have))
            {
                if (GeoMath.DistanceM(have.Pose.Position, pose.Position) > 0.05 || Math.Abs(GeoMath.AngleDiffDeg(have.Pose.HeadingTrueDeg, pose.HeadingTrueDeg)) > 0.5)
                {
                    ops.Add(new MoveArrow(have.Handle, pose));
                    _shown[pose.Key] = (have.Handle, pose);
                }
                continue;
            }
            if (free.TryDequeue(out int handle)) ops.Add(new MoveArrow(handle, pose));
            else ops.Add(new CreateArrow(handle = _nextHandle++, pose));
            _shown[pose.Key] = (handle, pose);
        }

        while (free.TryDequeue(out int surplus)) ops.Add(new RemoveArrow(surplus));
        return ops;
    }

    /// <summary>Removes every arrow (route finished, turned off, app closing).</summary>
    public IReadOnlyList<ArrowOp> Clear() => Update([]);

    /// <summary>Forgets every arrow without removing it: after a disconnect the sim has already removed them.</summary>
    public void Reset() => _shown.Clear();
}
