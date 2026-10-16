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

`OpenSkyAtc.SimConnect`, the recorder and the app need the MSFS 2024 SDK: the build reads the managed SimConnect DLL from `$(MSFS2024_SDK)SimConnect SDK\lib\managed` and fails with a clear error if it is missing. A shell opened before the SDK was installed will not have the variable yet.

Record a flight (the sim must be running or the recorder waits for it):

```
dotnet run --project src/OpenSkyAtc.Recorder -- --out flight.jsonl
```

Replay transcripts are approval-tested: `tests/fixtures/transcripts/<name>.approved.txt`. On a change the test writes `<name>.received.txt` (git-ignored); review it and rename to `.approved.txt` to accept. Real recordings go in `tests/fixtures/recordings/*.jsonl` and are replayed automatically.

## Layout

- `src/OpenSkyAtc.Core` - the brain (phases, events, clearance, conformance, airspace, planning, taxi routing). No project or package references.
- `src/OpenSkyAtc.Phraseology` - ICAO renderer (display + speech text). References Core.
- `src/OpenSkyAtc.SimConnect` - sim bridge (SimState, facility data, key events). References Core.
- `src/OpenSkyAtc.Voice` - Piper TTS, radio filter, Whisper STT, intent parser, push-to-talk. References Core.
- `src/OpenSkyAtc.SimBrief` - flight plan import. References Core.
- `src/OpenSkyAtc.App` - WPF app (net10.0-windows). References all libraries.
- `src/OpenSkyAtc.Recorder` - console tool, records SimState to JSON-lines.
- `tests/*` - xUnit test projects; `tests/fixtures` holds airport JSON and recorded flights.
- `toolbar/` - MSFS 2024 Community-folder packages (toolbar panel, taxi arrow model).

## Conventions

- `OpenSkyAtc.Core` must stay pure: no SimConnect, audio, WPF or I/O. It takes `SimState` + `PilotIntent` and emits `AtcMessage`.
- Namespaces are `OpenSkyAtc.*` (the design doc's `Atc.*` names are renamed).
- SimConnect DLLs are never committed or shipped (GPL linking exception, `LICENSE-EXCEPTION.md`). They are referenced with `Private=false` and loaded at runtime by `SimConnectLibrary.EnsureLoaded`, which must run before any SimConnect type is touched.
- Projects that touch SimConnect target `net10.0-windows` with `PlatformTarget` x64.
- The brain's randomness goes through `IRandom` / `SeededRandom` (SplitMix64), never `System.Random`.
- Phases never read raw `SimState` numbers; they react to `EventEngine` events. Thresholds live in `EventEngineOptions`.
- Recordings: JSON-lines, header line then one `SimState` per line (`RecordingFormat`). Core converts lines only; callers do file I/O.
- Shared build settings live in `Directory.Build.props`: nullable on, implicit usings, warnings are errors.
- Every dependency, model, voice or dataset must be GPL-3.0-compatible and listed in `THIRD_PARTY_NOTICES.md` before it is added.
- No cloud APIs, telemetry or accounts.
- Work one build step at a time; build + test + commit (`step NN: name`) after each, then stop for review.
- Ask instead of assuming; record answers in `docs/decisions.md`.
