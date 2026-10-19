using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.Core.Conversation;

/// <summary>
/// Which exchange answers a pilot request, for asking again something already given (owner's choice: ATC repeats
/// the clearance as given, standard wording, instead of "say again"). Only while that clearance still stands: the
/// IFR clearance until takeoff, push and start until taxiing, the taxi route while taxiing, line-up or takeoff at
/// the holding point.
/// </summary>
internal static class GrantedRequests
{
    private static readonly string[] Clearance = ["ifr-clearance"];
    private static readonly string[] PushStart = ["push-start"];
    private static readonly string[] Taxi = ["taxi"];
    private static readonly string[] TaxiIn = ["taxi-in"];
    private static readonly string[] Departure = ["takeoff", "line-up"];

    public static IReadOnlyCollection<string>? Tags(PilotIntent intent, FlightPhase phase) => intent switch
    {
        RequestClearance when phase is FlightPhase.Preflight or FlightPhase.Cleared or FlightPhase.Pushback or FlightPhase.TaxiOut or FlightPhase.Holding => Clearance,
        RequestPushStart when phase is FlightPhase.Cleared or FlightPhase.Pushback or FlightPhase.TaxiOut => PushStart,
        RequestTaxi when phase is FlightPhase.TaxiOut => Taxi,
        RequestTaxi when phase is FlightPhase.TaxiIn => TaxiIn,
        ReadyForDeparture when phase is FlightPhase.Holding => Departure,
        _ => null,
    };
}
