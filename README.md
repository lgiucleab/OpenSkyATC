# OpenSkyATC

A free, open-source (GPL-3.0) replacement ATC add-on for **Microsoft Flight Simulator 2024**.

OpenSkyATC is a Windows desktop app that connects to the sim through SimConnect and replaces the default ATC for one IFR flight, gate to gate. It uses ICAO/European phraseology and will have:

- a local voice (Piper TTS) with a radio filter,
- local voice input (Whisper) and click-to-respond buttons,
- conformance monitoring (altitude, heading, speed, squawk, runway safety),
- an in-sim toolbar panel and taxi-route arrows,
- an end-of-flight debrief.

Everything runs locally. No accounts, no cloud services, no telemetry.

## Status

Early development. Only the solution skeleton exists (build step 0). Nothing is usable yet.

The ATC logic is specified in [docs/ATC-Brain-Design.md](docs/ATC-Brain-Design.md).

## Building

Requirements: Windows 10/11 x64, the .NET 10 SDK, and the Microsoft Flight Simulator 2024 SDK (for the SimConnect libraries; the build finds them through the `MSFS2024_SDK` environment variable).

```
dotnet build
dotnet test
```

## Licence

GPL-3.0, with an additional permission to link with Microsoft's SimConnect libraries. See [LICENSE](LICENSE), [LICENSE-EXCEPTION.md](LICENSE-EXCEPTION.md) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

OpenSkyATC does not ship the SimConnect DLLs. You need them from your own MSFS 2024 SDK installation.

OpenSkyATC is not affiliated with Microsoft, Asobo Studio or BeyondATC.
