using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Core.Sim;

namespace OpenSkyAtc.Host;

/// <summary>The tug through the live bridge.</summary>
public sealed class BridgeTug(OpenSkyAtc.SimConnect.SimBridge bridge) : IPushbackTug
{
    public void Toggle() => bridge.TogglePushback();
    public void Steer(double headingTrueDeg) => bridge.SteerTug(headingTrueDeg);
}

/// <summary>The sim's pushback tug (SimBridge in the app; a fake in tests).</summary>
public interface IPushbackTug
{
    /// <summary>Starts or stops the pushback (the sim's toggle).</summary>
    void Toggle();

    /// <summary>The heading the tug turns the aircraft to while pushing.</summary>
    void Steer(double headingTrueDeg);
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
/// brake is released, pushes straight back by the planned distance, turns until the nose is on the planned heading
/// ("facing south"), then stops. Gives up (and stops the tug) after 3 minutes, when the parking brake is set during
/// the push, or when the sim ends the pushback itself.
/// </summary>
public sealed class PushbackDriver(IPushbackTug tug)
{
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Resteer = TimeSpan.FromSeconds(1);
    private const double TurnLeadM = 3, AlignedDeg = 4;

    private Core.Planning.PushPlan? _plan;
    private TimeSpan _startedAt, _steeredAt;
    private bool _tugRunning;

    public PushbackStage Stage { get; private set; } = PushbackStage.Done;

    public void Update(Clearance clearance, FlightPhase phase, SimState s)
    {
        if (clearance.Pushback is { } plan && !ReferenceEquals(plan, _plan))
        {
            _plan = plan;
            Stage = PushbackStage.Waiting;
        }
        if (_plan is null || Stage == PushbackStage.Done) return;
        if (!clearance.PushbackReadBack || phase is not (FlightPhase.Cleared or FlightPhase.Pushback)) return;
        var now = s.Time;
        var position = new GeoPoint(s.Latitude, s.Longitude);

        switch (Stage)
        {
            case PushbackStage.Waiting:
                if (s.ParkingBrakeSet || !s.OnGround) return;
                tug.Toggle();
                _tugRunning = true;
                _startedAt = now;
                Steer(_plan.StartHeadingTrueDeg, now);
                Stage = PushbackStage.Straight;
                return;

            case PushbackStage.Straight or PushbackStage.Turning when s.ParkingBrakeSet || now - _startedAt > MaxDuration
                                                                  || (now - _startedAt > TimeSpan.FromSeconds(8) && !s.PushbackActive):
                Finish();
                return;

            case PushbackStage.Straight:
                if (GeoMath.DistanceM(_plan.Start, position) >= _plan.StraightBackM - TurnLeadM)
                {
                    Stage = PushbackStage.Turning;
                    Steer(_plan.FinalHeadingTrueDeg, now);
                }
                else if (now - _steeredAt >= Resteer) Steer(_plan.StartHeadingTrueDeg, now);
                return;

            case PushbackStage.Turning:
                if (Math.Abs(GeoMath.AngleDiffDeg(s.HeadingTrueDeg, _plan.FinalHeadingTrueDeg)) <= AlignedDeg) Finish();
                else if (now - _steeredAt >= Resteer) Steer(_plan.FinalHeadingTrueDeg, now);
                return;
        }
    }

    /// <summary>Forgets the push without touching the tug (the sim connection is gone, and with it the pushback).</summary>
    public void Abandon()
    {
        _tugRunning = false;
        Stage = PushbackStage.Done;
    }

    /// <summary>Stops the tug if it is running (end of push, flight ended).</summary>
    public void Finish()
    {
        if (_tugRunning) tug.Toggle();
        _tugRunning = false;
        Stage = PushbackStage.Done;
    }

    private void Steer(double heading, TimeSpan now)
    {
        tug.Steer(heading);
        _steeredAt = now;
    }
}
