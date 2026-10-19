using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Host;

/// <summary>Which way the sim's tug pushes: straight, or turning (MSFS 2024 has three fixed states, no free heading).</summary>
public enum TugSteer
{
    // Values are the sim's PUSHBACK STATE while pushing.
    Straight = 0,

    /// <summary>The sim's "steer left": the tail goes left, the nose turns clockwise (heading increases).</summary>
    Left = 1,

    /// <summary>The sim's "steer right": the nose turns anticlockwise (heading decreases).</summary>
    Right = 2,
}

/// <summary>The sim's pushback tug (SimBridge in the app; a fake in tests).</summary>
public interface IPushbackTug
{
    void Start();
    void Steer(TugSteer steer);
    void Stop();
}

/// <summary>
/// The tug through the live bridge: ground-service messages request, steer and stop it. (TOGGLE_PUSHBACK starts the
/// sim's own push, which follows its own path and ignores steering, checked twice in the owner's sim.)
/// </summary>
public sealed class BridgeTug(OpenSkyAtc.SimConnect.SimBridge bridge) : IPushbackTug
{
    public void Start() => bridge.GroundService(OpenSkyAtc.SimConnect.GroundServiceMessage.RequestPushback);
    public void Stop() => bridge.GroundService(OpenSkyAtc.SimConnect.GroundServiceMessage.StopPushback);

    public void Steer(TugSteer steer) => bridge.GroundService(steer switch
    {
        TugSteer.Left => OpenSkyAtc.SimConnect.GroundServiceMessage.PushbackSteerLeft,
        TugSteer.Right => OpenSkyAtc.SimConnect.GroundServiceMessage.PushbackSteerRight,
        _ => OpenSkyAtc.SimConnect.GroundServiceMessage.PushbackStraighten,
    });
}

public enum PushbackStage
{
    /// <summary>Approved and read back; waiting for the parking brake to be released.</summary>
    Waiting,
    Straight,
    Turning,
    Done,
}

/// <summary>
/// Drives the sim's tug for an approved push (owner's request): once the approval is read back and the parking
/// brake is released, pushes straight back by the planned distance, then turns (left or right, whichever brings the
/// nose round the short way) until the nose is near the planned heading ("facing south"), then stops. MSFS 2024
/// steers the tug with three fixed states (straight, left, right), checked in the owner's sim; "left" turns the nose
/// clockwise. The tug takes up to half a minute to attach and the sim ignores steering before that (and starts its
/// own turn otherwise), so the wanted state is sent again every 2 s until PUSHBACK STATE shows it. Gives up (and stops the tug) after 3 minutes, when the parking brake is set during the push, or when
/// the sim ends the pushback itself. Asking for taxi before the push has started does not cancel it (owner's choice,
/// after the 1.0.0 Alpha flight): the push still runs once the brake is released, unless the aircraft has already
/// left the stand under its own power.
/// </summary>
public sealed class PushbackDriver(IPushbackTug tug)
{
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(3);

    /// <summary>The tug keeps turning a little after the stop (about 1.5 degrees in the owner's sim): stop this early.</summary>
    public const double StopEarlyDeg = 2;

    private const double TurnLeadM = 3;

    /// <summary>Taxiing this far from the stand before the tug started means the pilot left without the push.</summary>
    private const double LeftStandM = 15;
    private static readonly TimeSpan AttachGrace = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ResendAfter = TimeSpan.FromSeconds(2);

    private Core.Planning.PushPlan? _plan;
    private TimeSpan _startedAt;
    private bool _tugRunning;
    private TugSteer? _steer;
    private TimeSpan _steerSentAt;

    public PushbackStage Stage { get; private set; } = PushbackStage.Done;

    public void Update(Clearance clearance, FlightPhase phase, SimState s)
    {
        if (clearance.Pushback is { } plan && !ReferenceEquals(plan, _plan))
        {
            _plan = plan;
            Stage = PushbackStage.Waiting;
        }
        if (_plan is null || Stage == PushbackStage.Done) return;
        if (!clearance.PushbackReadBack || !PushPhase(phase)) return;
        var now = s.Time;

        switch (Stage)
        {
            case PushbackStage.Waiting:
                if (phase == FlightPhase.TaxiOut && GeoMath.DistanceM(_plan.Start, new GeoPoint(s.Latitude, s.Longitude)) > LeftStandM)
                {
                    Stage = PushbackStage.Done;
                    return;
                }
                if (s.ParkingBrakeSet || !s.OnGround) return;
                tug.Start();
                _tugRunning = true;
                _startedAt = now;
                _steer = null;
                Steer(TugSteer.Straight, s);
                Stage = PushbackStage.Straight;
                return;

            // The tug takes a few seconds to attach; after that, "no pushback" means the sim (or the pilot) ended it.
            case PushbackStage.Straight or PushbackStage.Turning when s.ParkingBrakeSet || now - _startedAt > MaxDuration
                                                                  || (now - _startedAt > AttachGrace && !s.PushbackActive):
                Finish();
                return;

            case PushbackStage.Straight:
                if (GeoMath.DistanceM(_plan.Start, new GeoPoint(s.Latitude, s.Longitude)) >= _plan.StraightBackM - TurnLeadM)
                {
                    Stage = PushbackStage.Turning;
                    TurnTowards(s);
                }
                else Steer(TugSteer.Straight, s);
                return;

            case PushbackStage.Turning:
                if (Math.Abs(GeoMath.AngleDiffDeg(_plan.FinalHeadingTrueDeg, s.HeadingTrueDeg)) <= StopEarlyDeg) Finish();
                else TurnTowards(s);
                return;
        }
    }

    /// <summary>The push is approved and read back, and only the parking brake holds it (the app says so).</summary>
    public bool WaitingForBrake(Clearance clearance, FlightPhase phase, SimState s) =>
        Stage == PushbackStage.Waiting && clearance.PushbackReadBack && PushPhase(phase) && s.ParkingBrakeSet && s.OnGround;

    private static bool PushPhase(FlightPhase phase) => phase is FlightPhase.Cleared or FlightPhase.Pushback or FlightPhase.TaxiOut;

    /// <summary>Forgets the push without touching the tug (the sim connection is gone, and with it the pushback).</summary>
    public void Abandon()
    {
        _tugRunning = false;
        Stage = PushbackStage.Done;
    }

    /// <summary>Stops the tug if it is running (end of push, flight ended).</summary>
    public void Finish()
    {
        if (_tugRunning)
        {
            tug.Stop();
            tug.Steer(TugSteer.Straight); // leave the tug straight for the next push
        }
        _tugRunning = false;
        Stage = PushbackStage.Done;
    }

    /// <summary>Nose clockwise to the target (heading must increase): the sim's "left"; anticlockwise: "right".</summary>
    private void TurnTowards(SimState s) =>
        Steer(GeoMath.AngleDiffDeg(_plan!.FinalHeadingTrueDeg, s.HeadingTrueDeg) > 0 ? TugSteer.Left : TugSteer.Right, s);

    /// <summary>Sends a steer when it changes, and again every 2 s while the sim shows another state.</summary>
    private void Steer(TugSteer steer, SimState s)
    {
        if (_steer == steer && (s.PushbackState == (int)steer || s.Time - _steerSentAt < ResendAfter)) return;
        tug.Steer(steer);
        _steer = steer;
        _steerSentAt = s.Time;
    }
}
