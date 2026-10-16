# Decisions

Log of decisions made while building. Newest last.

## 2026-10-16 - Step 0

- **Solution format:** `OpenSkyATC.slnx` (the .NET 10 default).
- **SDK pin:** `global.json` pins 10.0.301 with `rollForward: latestFeature`.
- **Target frameworks:** libraries, tests and the recorder target `net10.0`; the app targets `net10.0-windows`. SimConnect and Voice may move to `net10.0-windows`/x64 when their native dependencies are added.
- **Build settings:** `Directory.Build.props` turns on nullable, implicit usings and warnings-as-errors for all projects.
- **Design doc:** copied to `docs/ATC-Brain-Design.md` from the file supplied in the repo root.
- **Scaffold before SDK:** the MSFS 2024 SDK is not installed yet. The owner approved building the scaffold anyway; no project needs the SDK until the recorder (step 1).
