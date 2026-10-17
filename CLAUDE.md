# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this is

OpenSkyATC: a GPL-3.0 replacement ATC add-on for MSFS 2024 (Windows, .NET 10, WPF).
- Build prompt and step plan: `OpenSkyATC-ClaudeCode-Prompt.md`
- ATC behaviour spec (source of truth): `docs/ATC-Brain-Design.md`
- Decision log: `docs/decisions.md`

## Commands

```
dotnet build            # whole solution (OpenSkyATC.slnx)
dotnet test             # all test projects
```

SDK is pinned in `global.json` (10.0.301, rollForward latestFeature).

`OpenSkyAtc.SimConnect`, the recorder and the app need the MSFS 2024 SDK. The build and `SimConnectLibrary` look for it in the shell's `MSFS2024_SDK`, then the machine/user value the SDK installer wrote (so a shell opened before the install still works), then `C:\MSFS 2024 SDK\`; the build fails with a clear error if the managed SimConnect DLL is not there.

Record a flight (the sim must be running or the recorder waits for it):

```
dotnet run --project src/OpenSkyAtc.Recorder -- --out flight.jsonl
```

Export an airport's facility data from the running sim, or survey CENTER frequencies in a region:

```
dotnet run --project src/OpenSkyAtc.Recorder -- export LEMD --out LEMD.facility.json
dotnet run --project src/OpenSkyAtc.Recorder -- survey-center LE --max 400
dotnet run --project src/OpenSkyAtc.Recorder -- taxi LEMD
```

Run the brain against the live sim in a console (step 9 checklist: `docs/step-09-checklist.md`):

```
dotnet run --project src/OpenSkyAtc.Recorder -- live --from LEMD --to LEBL --callsign IBE1234 --type A320 --cruise 330 --ptt "VK_LCONTROL+T"
```

`taxi ICAO` loads the airport live through `SimConnectAirportProvider` (the same path the app uses) and prints its units and a routed taxi from a gate to every runway end.

Facility dumps (`*.facility.json`) are git-ignored: they are the sim's data, not ours to publish. At runtime every airport comes from the player's own sim, so routing works anywhere they have scenery. Tests on real dumps in `tests/fixtures/airports/` run only when the files exist; committed taxi tests use the synthetic airports in `tests/OpenSkyAtc.Core.Tests/Planning/SyntheticAirports.cs` (also linked into Replay.Tests).

Replay scenarios (`tests/OpenSkyAtc.Replay.Tests/DepartureScenarioTests.cs`) run the synthetic departure with an intent script; the runner can auto-read back every instruction. Replay transcripts are approval-tested: `tests/fixtures/transcripts/<name>.approved.txt`. On a change the test writes `<name>.received.txt` (git-ignored); review it and rename to `.approved.txt` to accept. Real recordings go in `tests/fixtures/recordings/*.jsonl` and are replayed automatically.

## Layout

- `src/OpenSkyAtc.Core` - the brain (phases, events, clearance, conformance, airspace, planning, taxi routing). No project or package references.
- `src/OpenSkyAtc.Phraseology` - ICAO renderer (display + speech text). References Core.
- `src/OpenSkyAtc.SimConnect` - sim bridge: `SimBridge` (SimState at 4 Hz, push-to-talk, reconnect every 5 s; its `SimState.Time` only advances while connected), `SimConnectAirportProvider` (airports from the sim, reopens its connection after the sim restarts), `FacilityExporter`. References Core.
- `src/OpenSkyAtc.Voice` - Piper TTS, radio filter, Whisper STT, intent parser, push-to-talk. References Core.
- `src/OpenSkyAtc.SimBrief` - flight plan import. References Core.
- `src/OpenSkyAtc.TaxiArrows` - taxi arrow logic (app layer, pure): `TaxiArrowController` (which arrows where, from `FlightSession`) and `ArrowPool` (sim-object changes). References Core.
- `src/OpenSkyAtc.App` - WPF app (net10.0-windows). References all libraries.
- `src/OpenSkyAtc.Recorder` - console tool, records SimState to JSON-lines.
- `tests/*` - xUnit test projects; `tests/fixtures` holds airport JSON and recorded flights.
- `toolbar/` - MSFS 2024 Community-folder packages (toolbar panel, taxi arrow model). `toolbar/opensky-atc-taxi-arrows`: `python toolbar/opensky-atc-taxi-arrows/tools/assemble_package.py [--install <Community folder>]` writes the package (no compiling needed: no textures); the model comes from its `tools/generate_arrow.py`. `fspackagetool.exe` hangs with the Microsoft Store sim.

## Conventions

- `OpenSkyAtc.Core` must stay pure: no SimConnect, audio, WPF or I/O. It takes `SimState` + `PilotIntent` and emits `AtcMessage`.
- Namespaces are `OpenSkyAtc.*` (the design doc's `Atc.*` names are renamed).
- SimConnect DLLs are never committed or shipped (GPL linking exception, `LICENSE-EXCEPTION.md`). They are referenced with `Private=false` and loaded at runtime by `SimConnectLibrary.EnsureLoaded`, which must run before any SimConnect type is touched.
- Projects that touch SimConnect target `net10.0-windows` with `PlatformTarget` x64.
- The brain's randomness goes through `IRandom` / `SeededRandom` (SplitMix64), never `System.Random`.
- Brain flow: `AtcBrain` ticks the `EventEngine`, the current `IPhase` and the `ConversationManager`, then releases due transmissions from the `TransmissionQueue`. Phases return transitions; the brain applies and logs them. Phases send ATC messages only through `ctx.Conversation`, with a `ReadbackExpectation` when a readback is required, and update the `Clearance` in the `onSent` callback.
- Airspace: `AirspaceModel` builds terminal units for both airports and CTR sectors along the route from CENTER frequencies passed in `AtcBrainOptions.CenterFrequencies` (never invented). `FlightPlanning` holds step climbs, top of descent and arrival levels.
- Runways: `RunwaySelector` (headwind, 5 kt tailwind for preferred, 25 kt crosswind limit) and `RunwayLock`; preferred runways from `data/airport-sops.json`. Vectors: `Planning/Vectoring` (runway frame, legs ToDownwind/Downwind/Base/StraightIn/Cleared) driven by `ApproachPhase`.
- Simulated pilot: `tests/OpenSkyAtc.Replay.Tests/Harness/SimulatedPilot.cs` flies `SyntheticRoute` gate to gate against the brain; use it to catch stuck phases.
- Conformance: `Conformance/ConformanceMonitor` runs after the phase each tick. Each check is a read/warn pair with grace and escalation handled centrally; thresholds live in `ConformanceOptions`. Synthetic-state tests use `tests/OpenSkyAtc.Core.Tests/Conformance/MonitorRig.cs`.
- Text comes from `OpenSkyAtc.Phraseology.IcaoRenderer` (display + speech, one instance per flight, render messages in order); number rules live in `Spoken`. Core never builds message strings. Wording variants are marked European and can be turned off; every part and variant is in `tests/fixtures/snapshots/phraseology-parts.approved.txt`.
- Phraseology reference: design doc first, then UK CAA CAP 413 edition 24; never invent wording without a source.
- Phases never read raw `SimState` numbers; they react to `EventEngine` events. Thresholds live in `EventEngineOptions`.
- Owner-filled data files (`data/*.json`) are parsed by `Core/Config/DataFiles` from text the host reads; CENTER frequencies for a flight are the sim's (searched along `CenterSearch.RoutePoints`) merged with `data/ctr-frequencies.json`.
- Taxi arrows: the brain never knows about them. It exposes the route (`Clearance.Taxi`), `TaxiReadBack` and `IsCrossReadBack`; `TaxiArrowController` reads those and `SimBridge.ShowArrows` places them. Checklist: `docs/step-10-checklist.md`.
- Recordings: JSON-lines, header line then one `SimState` per line (`RecordingFormat`). Core converts lines only; callers do file I/O.
- Shared build settings live in `Directory.Build.props`: nullable on, implicit usings, warnings are errors.
- Every dependency, model, voice or dataset must be GPL-3.0-compatible and listed in `THIRD_PARTY_NOTICES.md` before it is added.
- No cloud APIs, telemetry or accounts.
- Work one build step at a time; build + test + commit (`step NN: name`) after each, then stop for review.
- Ask instead of assuming; record answers in `docs/decisions.md`.
