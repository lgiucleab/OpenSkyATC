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

Requirements: Windows 10/11 x64 and the .NET 10 SDK.

```
dotnet build
dotnet test
```

## Licence

GPL-3.0. See [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

OpenSkyATC is not affiliated with Microsoft, Asobo Studio or BeyondATC.
