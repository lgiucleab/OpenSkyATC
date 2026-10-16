using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;
using OpenSkyAtc.Core.Geo;
using OpenSkyAtc.Core.Model;
using SC = Microsoft.FlightSimulator.SimConnect.SimConnect;

namespace OpenSkyAtc.SimConnect;

/// <summary>
/// Reads an airport's facility data from the running sim (SimConnect_RequestFacilityData) into a
/// <see cref="FacilityDump"/>. Field names and layouts follow the SDK's SimConnect_AddToFacilityDefinition page.
/// Call <see cref="SimConnectLibrary.EnsureLoaded"/> first.
/// </summary>
public sealed class FacilityExporter : IDisposable
{
    private enum Def { Airport, Frequencies }
    private enum Req { Airport, Frequencies, AirportList }

    // ---- Structs: one per facility data type, fields in the order they are added below.

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    private struct AirportData
    {
        public double Latitude, Longitude, Altitude;
        public float MagVar;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)] public string Icao;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)] public string Region;
        public float TransitionAltitude, TransitionLevel;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string CityState;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    private struct RunwayData
    {
        public double Latitude, Longitude, Altitude;
        public float Heading, Length, Width;
        public int PrimaryNumber, PrimaryDesignator, SecondaryNumber, SecondaryDesignator;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)] public string PrimaryIls;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)] public string SecondaryIls;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    private struct FrequencyData
    {
        public int Type, Frequency;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Name;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TaxiPointData
    {
        public int Type, Orientation;
        public float BiasX, BiasZ;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TaxiParkingData
    {
        public int Type, TaxiPointType, Name, Suffix;
        public uint Number;
        public int Orientation;
        public float Heading, Radius, BiasX, BiasZ;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TaxiPathData
    {
        public int Type;
        public float Width;
        public int RunwayNumber, RunwayDesignator, Start, End;
        public uint NameIndex;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    private struct TaxiNameData
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    private struct ApproachData
    {
        public int Type, Suffix, RunwayNumber, RunwayDesignator;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)] public string FafIcao;
        public float FafAltitude, MissedAltitude;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    private struct ProcedureNameData
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)] public string Name;
    }

    private readonly AutoResetEvent _signal = new(false);
    private readonly SC _sim;
    private bool _quit;

    // State of the request in flight.
    private AirportData? _airport;
    private readonly List<FacilityRunway> _runways = [];
    private readonly List<FacilityFrequency> _frequencies = [];
    private readonly List<(int Index, TaxiPointData P)> _points = [];
    private readonly List<(int Index, TaxiParkingData P)> _parkings = [];
    private readonly List<FacilityTaxiPath> _paths = [];
    private readonly List<string> _names = [];
    private readonly List<FacilityApproach> _approaches = [];
    private readonly List<string> _departures = [];
    private readonly List<string> _arrivals = [];
    private bool _done;
    private readonly List<(string Icao, string Region, double Lat, double Lon)> _airportList = [];
    private bool _listDone;

    public FacilityExporter(string clientName = "OpenSkyATC facility exporter")
    {
        _sim = new SC(clientName, IntPtr.Zero, 0, _signal, 0);
        _sim.OnRecvQuit += (_, _) => _quit = true;
        _sim.OnRecvFacilityData += OnData;
        _sim.OnRecvFacilityDataEnd += (_, _) => _done = true;
        _sim.OnRecvAirportList += OnAirportList;
        _sim.OnRecvException += (_, e) => Console.Error.WriteLine($"SimConnect exception {(SIMCONNECT_EXCEPTION)e.dwException} (send {e.dwSendID}, index {e.dwIndex})");

        DefineAirport();
        DefineFrequenciesOnly();
        foreach (var (type, register) in new (SIMCONNECT_FACILITY_DATA_TYPE, Action)[]
                 {
                     (SIMCONNECT_FACILITY_DATA_TYPE.AIRPORT, () => _sim.RegisterFacilityDataDefineStruct<AirportData>(SIMCONNECT_FACILITY_DATA_TYPE.AIRPORT)),
                     (SIMCONNECT_FACILITY_DATA_TYPE.RUNWAY, () => _sim.RegisterFacilityDataDefineStruct<RunwayData>(SIMCONNECT_FACILITY_DATA_TYPE.RUNWAY)),
                     (SIMCONNECT_FACILITY_DATA_TYPE.FREQUENCY, () => _sim.RegisterFacilityDataDefineStruct<FrequencyData>(SIMCONNECT_FACILITY_DATA_TYPE.FREQUENCY)),
                     (SIMCONNECT_FACILITY_DATA_TYPE.TAXI_POINT, () => _sim.RegisterFacilityDataDefineStruct<TaxiPointData>(SIMCONNECT_FACILITY_DATA_TYPE.TAXI_POINT)),
                     (SIMCONNECT_FACILITY_DATA_TYPE.TAXI_PARKING, () => _sim.RegisterFacilityDataDefineStruct<TaxiParkingData>(SIMCONNECT_FACILITY_DATA_TYPE.TAXI_PARKING)),
                     (SIMCONNECT_FACILITY_DATA_TYPE.TAXI_PATH, () => _sim.RegisterFacilityDataDefineStruct<TaxiPathData>(SIMCONNECT_FACILITY_DATA_TYPE.TAXI_PATH)),
                     (SIMCONNECT_FACILITY_DATA_TYPE.TAXI_NAME, () => _sim.RegisterFacilityDataDefineStruct<TaxiNameData>(SIMCONNECT_FACILITY_DATA_TYPE.TAXI_NAME)),
                     (SIMCONNECT_FACILITY_DATA_TYPE.APPROACH, () => _sim.RegisterFacilityDataDefineStruct<ApproachData>(SIMCONNECT_FACILITY_DATA_TYPE.APPROACH)),
                     (SIMCONNECT_FACILITY_DATA_TYPE.DEPARTURE, () => _sim.RegisterFacilityDataDefineStruct<ProcedureNameData>(SIMCONNECT_FACILITY_DATA_TYPE.DEPARTURE)),
                     (SIMCONNECT_FACILITY_DATA_TYPE.ARRIVAL, () => _sim.RegisterFacilityDataDefineStruct<ProcedureNameData>(SIMCONNECT_FACILITY_DATA_TYPE.ARRIVAL)),
                 })
        {
            register();
        }
    }

    private void Add(Def def, string field) => _sim.AddToFacilityDefinition(def, field);

    private void DefineAirport()
    {
        var d = Def.Airport;
        Add(d, "OPEN AIRPORT");
        foreach (var f in new[] { "LATITUDE", "LONGITUDE", "ALTITUDE", "MAGVAR", "NAME", "ICAO", "REGION", "TRANSITION_ALTITUDE", "TRANSITION_LEVEL", "CITY_STATE" }) Add(d, f);

        Add(d, "OPEN RUNWAY");
        foreach (var f in new[] { "LATITUDE", "LONGITUDE", "ALTITUDE", "HEADING", "LENGTH", "WIDTH", "PRIMARY_NUMBER", "PRIMARY_DESIGNATOR", "SECONDARY_NUMBER", "SECONDARY_DESIGNATOR", "PRIMARY_ILS_ICAO", "SECONDARY_ILS_ICAO" }) Add(d, f);
        Add(d, "CLOSE RUNWAY");

        Add(d, "OPEN FREQUENCY");
        foreach (var f in new[] { "TYPE", "FREQUENCY", "NAME" }) Add(d, f);
        Add(d, "CLOSE FREQUENCY");

        Add(d, "OPEN TAXI_POINT");
        foreach (var f in new[] { "TYPE", "ORIENTATION", "BIAS_X", "BIAS_Z" }) Add(d, f);
        Add(d, "CLOSE TAXI_POINT");

        Add(d, "OPEN TAXI_PARKING");
        foreach (var f in new[] { "TYPE", "TAXI_POINT_TYPE", "NAME", "SUFFIX", "NUMBER", "ORIENTATION", "HEADING", "RADIUS", "BIAS_X", "BIAS_Z" }) Add(d, f);
        Add(d, "CLOSE TAXI_PARKING");

        Add(d, "OPEN TAXI_PATH");
        foreach (var f in new[] { "TYPE", "WIDTH", "RUNWAY_NUMBER", "RUNWAY_DESIGNATOR", "START", "END", "NAME_INDEX" }) Add(d, f);
        Add(d, "CLOSE TAXI_PATH");

        Add(d, "OPEN TAXI_NAME");
        Add(d, "NAME");
        Add(d, "CLOSE TAXI_NAME");

        Add(d, "OPEN APPROACH");
        foreach (var f in new[] { "TYPE", "SUFFIX", "RUNWAY_NUMBER", "RUNWAY_DESIGNATOR", "FAF_ICAO", "FAF_ALTITUDE", "MISSED_ALTITUDE" }) Add(d, f);
        Add(d, "CLOSE APPROACH");

        Add(d, "OPEN DEPARTURE");
        Add(d, "NAME");
        Add(d, "CLOSE DEPARTURE");

        Add(d, "OPEN ARRIVAL");
        Add(d, "NAME");
        Add(d, "CLOSE ARRIVAL");

        Add(d, "CLOSE AIRPORT");
    }

    /// <summary>A light definition for surveys: just the frequencies (needs the airport fields first).</summary>
    private void DefineFrequenciesOnly()
    {
        var d = Def.Frequencies;
        Add(d, "OPEN AIRPORT");
        foreach (var f in new[] { "LATITUDE", "LONGITUDE", "ALTITUDE", "MAGVAR", "NAME", "ICAO", "REGION", "TRANSITION_ALTITUDE", "TRANSITION_LEVEL", "CITY_STATE" }) Add(d, f);
        Add(d, "OPEN FREQUENCY");
        foreach (var f in new[] { "TYPE", "FREQUENCY", "NAME" }) Add(d, f);
        Add(d, "CLOSE FREQUENCY");
        Add(d, "CLOSE AIRPORT");
    }

    /// <summary>Exports one airport. Returns null when the sim does not answer within the timeout.</summary>
    public FacilityDump? Export(string icao, TimeSpan timeout, bool frequenciesOnly = false)
    {
        Reset();
        _sim.RequestFacilityData(frequenciesOnly ? Def.Frequencies : Def.Airport, Req.Airport, icao.ToUpperInvariant(), "");
        if (!Pump(() => _done, timeout) || _airport is not { } a) return null;

        var origin = new GeoPoint(a.Latitude, a.Longitude);
        GeoPoint Biased(float x, float z) => GeoMath.Destination(GeoMath.Destination(origin, 90, x), 0, z);

        return new FacilityDump
        {
            ExportedUtc = DateTime.UtcNow,
            Icao = a.Icao,
            Region = a.Region,
            Name = a.Name,
            City = a.CityState,
            Latitude = a.Latitude,
            Longitude = a.Longitude,
            ElevationM = a.Altitude,
            MagVarDeg = a.MagVar,
            TransitionAltitudeM = a.TransitionAltitude,
            TransitionLevelM = a.TransitionLevel,
            Runways = [.. _runways],
            Frequencies = [.. _frequencies],
            TaxiPoints = _points.Select(p => { var g = Biased(p.P.BiasX, p.P.BiasZ); return new FacilityTaxiPoint(p.Index, p.P.Type, p.P.Orientation, g.Latitude, g.Longitude); }).ToList(),
            Parkings = _parkings.Select(p => { var g = Biased(p.P.BiasX, p.P.BiasZ); return new FacilityParking(p.Index, p.P.Type, p.P.Name, p.P.Suffix, p.P.Number, p.P.Heading, p.P.Radius, g.Latitude, g.Longitude); }).ToList(),
            TaxiPaths = [.. _paths],
            TaxiNames = [.. _names],
            Approaches = [.. _approaches],
            Departures = [.. _departures],
            Arrivals = [.. _arrivals],
        };
    }

    /// <summary>Every airport the sim knows: ICAO, region and position (SimConnect_RequestAllFacilities).</summary>
    public IReadOnlyList<(string Icao, string Region, double Lat, double Lon)> AllAirports(TimeSpan timeout)
    {
        _airportList.Clear();
        _listDone = false;
        _sim.RequestAllFacilities(SIMCONNECT_FACILITY_LIST_TYPE.AIRPORT, Req.AirportList);
        Pump(() => _listDone, timeout);
        return [.. _airportList];
    }

    private void OnAirportList(SC sender, SIMCONNECT_RECV_AIRPORT_LIST data)
    {
        foreach (var o in data.rgData)
        {
            if (o is SIMCONNECT_DATA_FACILITY_AIRPORT a) _airportList.Add((a.Ident, a.Region, a.Latitude, a.Longitude));
        }
        if (data.dwEntryNumber + 1 >= data.dwOutOf) _listDone = true;
    }

    private void OnData(SC sender, SIMCONNECT_RECV_FACILITY_DATA data)
    {
        object item = data.Data[0];
        int index = (int)data.ItemIndex;
        switch ((SIMCONNECT_FACILITY_DATA_TYPE)data.Type)
        {
            case SIMCONNECT_FACILITY_DATA_TYPE.AIRPORT:
                _airport = (AirportData)item;
                break;
            case SIMCONNECT_FACILITY_DATA_TYPE.RUNWAY:
                var r = (RunwayData)item;
                _runways.Add(new FacilityRunway(r.Latitude, r.Longitude, r.Altitude, r.Heading, r.Length, r.Width,
                    r.PrimaryNumber, r.PrimaryDesignator, r.SecondaryNumber, r.SecondaryDesignator, Blank(r.PrimaryIls), Blank(r.SecondaryIls)));
                break;
            case SIMCONNECT_FACILITY_DATA_TYPE.FREQUENCY:
                var f = (FrequencyData)item;
                _frequencies.Add(new FacilityFrequency(f.Type, f.Frequency, Blank(f.Name)));
                break;
            case SIMCONNECT_FACILITY_DATA_TYPE.TAXI_POINT:
                _points.Add((index, (TaxiPointData)item));
                break;
            case SIMCONNECT_FACILITY_DATA_TYPE.TAXI_PARKING:
                _parkings.Add((index, (TaxiParkingData)item));
                break;
            case SIMCONNECT_FACILITY_DATA_TYPE.TAXI_PATH:
                var p = (TaxiPathData)item;
                _paths.Add(new FacilityTaxiPath(p.Type, p.Width, p.RunwayNumber, p.RunwayDesignator, p.Start, p.End, p.NameIndex));
                break;
            case SIMCONNECT_FACILITY_DATA_TYPE.TAXI_NAME:
                _names.Add(((TaxiNameData)item).Name ?? "");
                break;
            case SIMCONNECT_FACILITY_DATA_TYPE.APPROACH:
                var ap = (ApproachData)item;
                _approaches.Add(new FacilityApproach(ap.Type, ap.Suffix, ap.RunwayNumber, ap.RunwayDesignator, Blank(ap.FafIcao), ap.FafAltitude, ap.MissedAltitude));
                break;
            case SIMCONNECT_FACILITY_DATA_TYPE.DEPARTURE:
                _departures.Add(((ProcedureNameData)item).Name ?? "");
                break;
            case SIMCONNECT_FACILITY_DATA_TYPE.ARRIVAL:
                _arrivals.Add(((ProcedureNameData)item).Name ?? "");
                break;
        }
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void Reset()
    {
        _airport = null;
        _runways.Clear(); _frequencies.Clear(); _points.Clear(); _parkings.Clear(); _paths.Clear();
        _names.Clear(); _approaches.Clear(); _departures.Clear(); _arrivals.Clear();
        _done = false;
    }

    private bool Pump(Func<bool> until, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!until() && !_quit && DateTime.UtcNow < deadline)
        {
            _signal.WaitOne(TimeSpan.FromMilliseconds(100));
            _sim.ReceiveMessage();
        }
        return until();
    }

    public void Dispose()
    {
        _sim.Dispose();
        _signal.Dispose();
    }
}
