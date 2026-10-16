using Microsoft.FlightSimulator.SimConnect;
using OpenSkyAtc.TaxiArrows;
using SC = Microsoft.FlightSimulator.SimConnect.SimConnect;

namespace OpenSkyAtc.SimConnect;

/// <summary>
/// Taxi arrows as sim objects, for one connection: turns <see cref="ArrowPool"/> changes into SimConnect create, move
/// and remove calls. The sim removes a client's objects when it disconnects, so a new connection starts empty.
/// Used only on the bridge thread.
/// </summary>
internal sealed class SimArrows
{
    private const uint FirstRequest = 0x4F530000; // "OS": request ids for arrow creation, clear of the bridge's own

    private readonly SC _sim;
    private readonly string _title;
    private readonly Enum _positionDefinition;
    private readonly Action<string> _warn;
    private readonly ArrowPool _pool = new();
    private readonly Dictionary<int, uint> _objects = new();          // handle -> sim object id
    private readonly Dictionary<int, ArrowPose?> _pending = new();    // created, id not yet known: latest pose (null = remove)
    private bool _unavailable;
    private double _lastGroundFt;

    public SimArrows(SC sim, string title, Enum positionDefinition, Action<string> warn)
    {
        _sim = sim;
        _title = title;
        _positionDefinition = positionDefinition;
        _warn = warn;
        sim.AddToDataDefinition(positionDefinition, "Initial Position", null, SIMCONNECT_DATATYPE.INITPOSITION, 0, SC.SIMCONNECT_UNUSED);
        sim.OnRecvAssignedObjectId += OnAssigned;
    }

    public int Count => _pool.Count;

    /// <param name="groundAltitudeFt">Ground elevation near the aircraft; with OnGround the sim puts each arrow on the ground under it.</param>
    public void Show(IReadOnlyList<ArrowPose> arrows, double groundAltitudeFt)
    {
        if (_unavailable) return;
        foreach (var op in _pool.Update(arrows))
        {
            switch (op)
            {
                case CreateArrow c:
                    _pending[c.Handle] = c.Pose;
                    _sim.AICreateSimulatedObject_EX1(_title, "", Position(c.Pose, groundAltitudeFt), (Request)(FirstRequest + (uint)c.Handle));
                    break;
                case MoveArrow m when _objects.TryGetValue(m.Handle, out uint id):
                    _sim.SetDataOnSimObject(_positionDefinition, id, SIMCONNECT_DATA_SET_FLAG.DEFAULT, Position(m.Pose, groundAltitudeFt));
                    break;
                case MoveArrow m:
                    _pending[m.Handle] = m.Pose;
                    break;
                case RemoveArrow r when _objects.Remove(r.Handle, out uint id):
                    _sim.AIRemoveObject(id, (Request)(FirstRequest - 1));
                    break;
                case RemoveArrow r:
                    _pending[r.Handle] = null;
                    break;
            }
        }
        _lastGroundFt = groundAltitudeFt;
    }

    /// <summary>A create failed: the arrow package is not installed (or its title changed). Stops trying.</summary>
    public void CreateFailed()
    {
        if (_unavailable) return;
        _unavailable = true;
        _pending.Clear();
        _pool.Reset();
        _warn($"Taxi arrows are off: the sim has no object titled '{_title}'. Install the opensky-atc-taxi-arrows package in the Community folder and restart the sim.");
    }

    private void OnAssigned(SC sender, SIMCONNECT_RECV_ASSIGNED_OBJECT_ID data)
    {
        if (data.dwRequestID < FirstRequest) return;
        int handle = (int)(data.dwRequestID - FirstRequest);
        if (!_pending.Remove(handle, out var pose)) pose = default;
        if (pose is null)
        {
            _sim.AIRemoveObject(data.dwObjectID, (Request)(FirstRequest - 1));
            return;
        }
        _objects[handle] = data.dwObjectID;
        _sim.SetDataOnSimObject(_positionDefinition, data.dwObjectID, SIMCONNECT_DATA_SET_FLAG.DEFAULT, Position(pose.Value, _lastGroundFt));
    }

    private static SIMCONNECT_DATA_INITPOSITION Position(ArrowPose p, double groundFt) => new()
    {
        Latitude = p.Position.Latitude,
        Longitude = p.Position.Longitude,
        Altitude = groundFt,
        Heading = p.HeadingTrueDeg,
        Pitch = 0,
        Bank = 0,
        OnGround = 1,
        Airspeed = 0,
    };

    private enum Request : uint;
}
