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

Run the app (step 13; checklist `docs/step-13-checklist.md`):

```
dotnet run --project src/OpenSkyAtc.App
```

`OpenSkyATC.exe --screenshot FILE.png [main|compact|settings[:TAB]|readback|debrief|past] [en|es]` renders a window with sample content to a PNG and exits (developer check of the layout, uses a throwaway settings file); `--debrief-html FILE [en|es]` writes the sample debrief's HTML export.

Debrief (step 16, checklist `docs/step-16-checklist.md`): built from the session log by `Core/Debrief/DebriefBuilder` when a flight ends, saved by `Host/DebriefStore` to `%LOCALAPPDATA%\OpenSkyATC\debriefs\*.json`, shown by the app's debrief window (Gear > Past flights for older ones). The done-when test is `tests/OpenSkyAtc.Replay.Tests/DebriefTests.cs` (simulated pilot with fault options).

Installer (step 17): `python installer/build.py [--version 0.1.0]` publishes the app self-contained, assembles both MSFS packages and compiles `installer/OpenSkyATC.iss` with Inno Setup 6 into `artifacts/installer/OpenSkyATC-Setup-<version>.exe` (git-ignored). Silent test install: `Setup.exe /VERYSILENT /DIR=... /COMMUNITY=... /TASKS=packages` (run it from PowerShell: Git Bash rewrites `/X` switches as paths). Releases: GitHub `lgiucleab/OpenSkyATC`, pre-releases while 0.x.

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

Voices: `voices list | install [ids|all] | say "text" [--voice vctk:12] [--out f.wav] | audition MODEL | play FILE.wav`. `live` speaks every ATC call (downloads missing voices first; `--no-voice`, `--volume`). The replay-to-audio demo is `ReplayAudioTests` (writes `tests/fixtures/audio/departure-normal.wav` where voices are installed).

With a flight plan: `live --simbrief PILOT_ID` (latest SimBrief OFP) or `live --pln FILE` (MSFS .pln; callsign from the sim aircraft, waypoint positions from the sim's navdata).

Speech (step 14, checklist `docs/step-14-checklist.md`): `stt list | install [models] | synth | record | bench [--models a,b] [--set piper|owner|both] | listen`. The phrase list is `tests/fixtures/speech/phrases.txt` (expectations in `tests/OpenSkyAtc.Voice.Tests/SpeechSamples.cs`); Piper renderings in `tests/fixtures/speech/piper/` are committed, the owner's recordings in `owner/` are not. The Whisper tests run only where a model is installed.

`taxi ICAO` loads the airport live through `SimConnectAirportProvider` (the same path the app uses) and prints its units and a routed taxi from a gate to every runway end.

Facility dumps (`*.facility.json`) are git-ignored: they are the sim's data, not ours to publish. At runtime every airport comes from the player's own sim, so routing works anywhere they have scenery. Tests on real dumps in `tests/fixtures/airports/` run only when the files exist; committed taxi tests use the synthetic airports in `tests/OpenSkyAtc.Core.Tests/Planning/SyntheticAirports.cs` (also linked into Replay.Tests).

Replay scenarios (`tests/OpenSkyAtc.Replay.Tests/DepartureScenarioTests.cs`) run the synthetic departure with an intent script; the runner can auto-read back every instruction. Replay transcripts are approval-tested: `tests/fixtures/transcripts/<name>.approved.txt`. On a change the test writes `<name>.received.txt` (git-ignored); review it and rename to `.approved.txt` to accept. Real recordings go in `tests/fixtures/recordings/*.jsonl` and are replayed automatically.

## Layout

- `src/OpenSkyAtc.Core` - the brain (phases, events, clearance, conformance, airspace, planning, taxi routing, end-of-flight debrief in `Debrief/`). No project or package references.
- `src/OpenSkyAtc.Phraseology` - ICAO renderer (display + speech text). References Core.
- `src/OpenSkyAtc.SimConnect` - sim bridge: `SimBridge` (SimState at 4 Hz, push-to-talk, reconnect every 5 s; its `SimState.Time` only advances while connected), `SimConnectAirportProvider` (airports from the sim, reopens its connection after the sim restarts), `FacilityExporter`. References Core.
- `src/OpenSkyAtc.Voice` - voice out (net10.0-windows, x64): `VoiceCatalog`/`VoiceAssigner` (a fixed voice per unit per session), `VoiceStore` (download on first use to %LOCALAPPDATA%\OpenSkyATC\voices), `PiperSynthesizer` (sherpa-onnx), `RadioFilter`, `RadioChannel` (queue rules, pure), `AtcRadio` (glue + NAudio playback). Voice in (`Input/`, step 14): `SpokenText` + `IntentParser` (transcript to `PilotIntent`, pure), `SpeechRecognizer` (Whisper.net), `WhisperModels`/`WhisperModelStore` (%LOCALAPPDATA%\OpenSkyATC\whisper), `Microphone`, `SpeechPrompt`, `SpeechPhrases`. References Core and Phraseology.
- `src/OpenSkyAtc.SimBrief` - flight plan import: `SimBriefClient` + `OfpParser` (SimBrief json=v2), `PlnParser` (MSFS .pln), errors as `FlightPlanImportException`. References Core. Tests: `tests/OpenSkyAtc.SimBrief.Tests` with hand-written samples in `tests/fixtures/simbrief` (real OFPs `*.ofp.json` and `*.local.pln` are git-ignored).
- `src/OpenSkyAtc.TaxiArrows` - taxi arrow logic (app layer, pure): `TaxiArrowController` (which arrows where, from `FlightSession`) and `ArrowPool` (sim-object changes). References Core.
- `src/OpenSkyAtc.Host` - runs a flight for the app and the console (`FlightHost`: plan import, waiting for the sim, airports, CENTER search, brain, voices, arrows, bridge thread; thread-safe snapshot and events), `AppSettings`/`SettingsStore` (%APPDATA%\OpenSkyATC\settings.json), `TranscriptWriter`, `PilotPhrase`, `DataBundle` (data/ copied next to the app), `PushbackDriver` (drives the sim's tug with ground-service messages), `VoiceInput` + `KeyboardPushToTalk` (step 14).
- `src/OpenSkyAtc.App` - WPF app (net10.0-windows, x64, Fluent theme following Windows): `MainViewModel` (CommunityToolkit.Mvvm) on a `FlightHost`, main window, compact always-on-top strip, settings window, hard-mode readback form. UI strings in English and Spanish: `Resources/Strings*.resx`, generated by `tools/app-strings/generate.py` (edit the table there), bound through `Loc`.
- `src/OpenSkyAtc.Recorder` - console tool, records SimState to JSON-lines.
- `tests/*` - xUnit test projects; `tests/fixtures` holds airport JSON and recorded flights.
- `toolbar/` - MSFS 2024 Community-folder packages (toolbar panel, taxi arrow model). `toolbar/opensky-atc-taxi-arrows`: `python toolbar/opensky-atc-taxi-arrows/tools/assemble_package.py [--install <Community folder>]` writes the package (no compiling needed: no textures); the model comes from its `tools/generate_arrow.py`. `fspackagetool.exe` starts MSFS 2024 to build and needs `-nopause` (without it, it waits for a key press at the end, which looked like a hang); with the Store sim its exit code is 0x87E50043 even after a good build. `toolbar/opensky-atc-toolbar` (step 15, checklist `docs/step-15-checklist.md`): the in-sim panel, talking to the app over the MSFS 2024 CommBus (`SimBridge.SendToPanel`/`PanelCommand`, `Host/ToolbarPanel.cs`); `python toolbar/opensky-atc-toolbar/tools/assemble_package.py [--build-spb] [--install <Community folder>]` (the committed `InGamePanels/*.spb` is compiled by fspackagetool through the sim, only when the panel XML changes); panel view tests: `node toolbar/opensky-atc-toolbar/tools/test_panel.js`. Toolbar icon: `tools/generate_icon.py` (MSFS 2024 reads `html_ui/icons/toolbar/`, 2020 `html_ui/Textures/Menu/toolbar/`). Unhandled app errors go to `%LOCALAPPDATA%\OpenSkyATC\logs`.

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
- Voice timing: the brain releases a transmission when due; `RadioChannel` decides when it can be heard (push-to-talk, 1 s gap, urgent cuts, drop when off frequency), and `IAtcBrain.TransmissionHeard` restarts the readback timer when the audio ends.
- Recordings: JSON-lines, header line then one `SimState` per line (`RecordingFormat`). Core converts lines only; callers do file I/O.
- Shared build settings live in `Directory.Build.props`: nullable on, implicit usings, warnings are errors.
- Every dependency, model, voice or dataset must be GPL-3.0-compatible and listed in `THIRD_PARTY_NOTICES.md` before it is added.
- No cloud APIs, telemetry or accounts.
- Work one build step at a time; build + test + commit (`step NN: name`) after each, then stop for review.
- Ask instead of assuming; record answers in `docs/decisions.md`.
