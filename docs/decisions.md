# Decisions

Log of decisions made while building. Newest last.

## 2026-10-16 - Step 0

- **Solution format:** `OpenSkyATC.slnx` (the .NET 10 default).
- **SDK pin:** `global.json` pins 10.0.301 with `rollForward: latestFeature`.
- **Target frameworks:** libraries, tests and the recorder target `net10.0`; the app targets `net10.0-windows`. SimConnect and Voice may move to `net10.0-windows`/x64 when their native dependencies are added.
- **Build settings:** `Directory.Build.props` turns on nullable, implicit usings and warnings-as-errors for all projects.
- **Design doc:** copied to `docs/ATC-Brain-Design.md` from the file supplied in the repo root.
- **Scaffold before SDK:** the MSFS 2024 SDK is not installed yet. The owner approved building the scaffold anyway; no project needs the SDK until the recorder (step 1).

## 2026-10-16 - Answers before step 1

- **MSFS 2024 SDK:** installed at `C:\MSFS 2024 SDK\` (`MSFS2024_SDK` env var). Managed SimConnect DLL found at `SimConnect SDK\lib\managed`.
- **SimConnect licensing:** settled before step 1 (the recorder needs SimConnect), not before step 9.
- **`SimState` in step 1:** defined in step 1 so the recorder can write it; the other records stay in step 2.
- **Stub brain:** step 1 ships a stub `IAtcBrain` so the replay harness can be built before the real brain.
- **Extra `SimState` fields:** add sim-paused and slew flags, and weather at the airport (visibility, cloud layers, temperature, dewpoint, QNH) for ATIS.
- **Transmission queue split:** `Core` owns the logical queue (priority, due time, response delays, drop on frequency change). `Voice` handles playback, preemption and not talking over push-to-talk.
- **Synthetic recordings:** replay tests may use generated recordings until real flights are recorded.
- **Test projects:** four test projects as in the build prompt (not the design doc's single `Atc.Tests`).
- **Simulated-pilot bot:** lives in `OpenSkyAtc.Replay.Tests`.
- **Airline telephony table:** research an openly licensed source.
- **Squawk ranges, transition altitudes, SOPs:** shipped as data files with blank or placeholder entries for the owner to fill in.
- **New requirement - local .pln import:** besides SimBrief, the user can load a flight plan from a local MSFS `.pln` file. Planned for step 11 next to the SimBrief import.
- **CTR frequencies:** use real data. The source and its licence are still to be found (before step 6).
- **`Transmission` vs `AtcMessage`:** same thing; the type is `AtcMessage`. The design doc's "paid product" remark does not apply (the project is free).
- **SimConnect licensing (option B):** the GPL-3.0 licence gets a section 7 additional permission for linking with the SimConnect libraries (`LICENSE-EXCEPTION.md`). The SimConnect DLLs are never committed or shipped; users supply them from their own MSFS 2024 SDK. The SDK EULA only allows redistribution of unnamed "distributable code", and no official statement says the SimConnect DLLs are distributable. Builds reference the managed DLL with `Private=false`, and the app loads it at runtime from the SDK folder.

## 2026-10-16 - Step 1

- **SimVar names** were checked against the MSFS 2024 SDK docs (SimVars, Environment Variables, Pause_EX1). MSFS has no SimVar for dewpoint, cloud layers or airport METAR, so `SimState` holds ambient weather at the aircraft (wind, visibility, temperature, sea-level pressure). Where ATIS gets dewpoint and clouds is still open (needed before ATIS is built).
- **Units in `SimState`:** frequencies as integer kHz; squawk as its four digits (BCO16 decoded); time as `TimeSpan` since recording start, which keeps running during pause (pause is a separate flag so the brain can freeze its own timers).
- **Recorder sampling:** polls with `SIMCONNECT_PERIOD.ONCE` every 250 ms on a stopwatch, because SimConnect has no fixed 4 Hz period.
- **Replay speed:** the harness feeds samples as fast as possible with no wall-clock waits; simulated time comes only from the recording.
- **Stub brain:** `StubAtcBrain` and the minimal `FlightPlan`/`AirportData`/`PilotIntent`/`AtcMessage`/`FlightSession` in `Brain/Placeholders.cs` exist only so the harness has something to drive. Steps 2-4 replace them.
- **Approval files:** a small built-in helper (`Fixtures.VerifyTranscript`) instead of a library such as Verify, to avoid a dependency.

## 2026-10-16 - Step 2

- **Hold times and hysteresis not given by the design doc** were picked as starting values. All of them live in `EventEngineOptions` (doc values are marked) and are meant to be tuned with real recordings:
  - Taxi started re-arms after ground speed < 1 kt for 5 s. Touchdown re-arms after 5 s airborne (mirrors Airborne).
  - Runway polygon: 5 m exit margin. Hold-short line: +/-2 m either side, 30 m half-width.
  - Takeoff roll fires at once above 40 kt on a runway and re-arms after < 30 kt for 5 s, so a landing roll never fires it.
  - Altitude crossings use a 50 ft band, distances 0.1 nm. Level-off re-arms after |VS| > 500 fpm for 5 s.
  - Frequency changes need 1 s stable (so spinning the knob reports only the final value).
  - Established: +/-1 degree, closing, held 2 s; re-arms after > 2 degrees for 5 s.
- **Baseline rule:** the first value any detector sees is its baseline and never fires (engines already running, starting in the air, starting on a runway).
- **Crossing vs state registrations:** altitude and distance registrations fire only on a crossing; an Established registration is a state and fires if the aircraft is already established when registered.
- **Runway and hold-short events are ground-only.** Overflights do not fire them; `EventEngine.CurrentRunway` tells which runway the aircraft is on.
- **Engine events fire per engine** ("any engine running flag changes") and carry the running count, so phases can tell first start from last stop.
- **Pause:** hold timers run on an active clock that stops while paused; changes during a pause are not events. `SimPaused`/`SimResumed` are raised for the queue.
- **Teleport and slew:** a jump over 2 nm in one tick, or the end of slew, raises `Teleported`; no other events fire until `PositionSettled` 10 s later, then every detector restarts from a fresh baseline. Slew counts as a teleport because slewed movement can be under 2 nm per tick.
- **Naming:** the design doc's `EventBus` is `EventEngine` (it derives events rather than routing them). `DistanceTo(point) < d` is `DistanceCrossed` with an `Inbound` flag.
- **Clearance and FlightSession** expose public getters and internal setters: only Core (the brain) writes them.
