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
- Shared build settings live in `Directory.Build.props`: nullable on, implicit usings, warnings are errors.
- Every dependency, model, voice or dataset must be GPL-3.0-compatible and listed in `THIRD_PARTY_NOTICES.md` before it is added.
- No cloud APIs, telemetry or accounts.
- Work one build step at a time; build + test + commit (`step NN: name`) after each, then stop for review.
- Ask instead of assuming; record answers in `docs/decisions.md`.
