# OpenSkyATC — Build Prompt for Claude Code

You are building **OpenSkyATC**, a free, open-source (GPL-3.0) replacement ATC add-on for **Microsoft Flight Simulator 2024**, in the style of BeyondATC. It is a Windows desktop app that connects to the sim via SimConnect and replaces the default ATC. It has a voice (Piper TTS), voice input (local Whisper), click-to-respond buttons, an in-sim toolbar panel, and an end-of-flight debrief.

The ATC logic ("the brain") is fully specified in **`docs/ATC-Brain-Design.md`**. That document is the source of truth for phases, events, conformance, airspace, planning, taxi routing, phraseology, timing, interfaces and edge cases. Read it completely before writing any code, and re-read the relevant section at the start of each step.

---

## 1. Working rules (follow these strictly)

1. **Never assume. Ask.** If anything is ambiguous, missing from this prompt or the design doc, or conflicts between them, STOP. Ask me a clear question with 2–4 concrete options and your recommendation. Do not invent requirements, data, frequencies, procedures or UI behaviour.
2. **One step at a time.** Work through the steps in section 5 in order. After each step:
   - run the build and all tests,
   - commit with a message like `step 03: event engine`,
   - give me a short summary: what was built, test results, open questions, and anything you deviated from,
   - **wait for my OK** before starting the next step.
3. **Tests are mandatory.** Every step ships with xUnit tests. The brain also gets replay tests (see the design doc, "Build order and testing"). A step is not done until its tests pass.
4. **The brain stays pure.** `OpenSkyAtc.Core` must not reference SimConnect, audio, WPF or any I/O. It receives `SimState` snapshots and pilot intents, and emits `AtcMessage` objects.
5. **Licences.** Before adding any dependency, model, voice or dataset, check its licence is compatible with GPL-3.0 and with free redistribution. Record each in `THIRD_PARTY_NOTICES.md`. If unsure, ask me.
6. **No secrets, no paid services.** Everything must run locally and for free. Do not add cloud APIs, telemetry or accounts.
7. **Keep `CLAUDE.md` up to date** with build commands, project layout, conventions and decisions we make along the way.

---

## 2. Locked decisions

| Area | Decision |
| --- | --- |
| Simulator | MSFS 2024 only |
| Platform | Windows 10/11 x64 |
| Runtime | .NET 10 (LTS), C# |
| Desktop UI | WPF |
| App UI languages | English and Spanish (resource files; English is the default) |
| ATC radio language | English, ICAO/European phraseology only |
| Text-to-speech | Piper (local), one fixed voice per controller unit, radio filter in the audio layer |
| Speech recognition | Local Whisper via whisper.cpp (for example the Whisper.net bindings — verify the current package and licence) |
| Push-to-talk | All three, user-configurable: keyboard hotkey (global), joystick/yoke button, and an MSFS key binding read through SimConnect |
| Flight plan | SimBrief import by the user's SimBrief pilot ID (set in settings) |
| Airport data | SimConnect facility data requests (runways, frequencies, taxi points/paths/names, parking) |
| In-sim panel | MSFS 2024 toolbar panel package (Community folder) that mirrors the app; the app must be running |
| In-sim taxi arrows | When a taxi clearance is read back correctly, show the cleared route in the sim as arrows that look like the default MSFS blue taxi-ribbon arrows. Show only the next part of the route ahead of the aircraft, removing arrows behind it. Stop at each runway hold-short until the crossing clearance is given. Clear the arrows at the holding point or stand. On by default, with a toggle in settings and in the toolbar panel |
| Debrief | End-of-flight summary of deviations and readback errors, viewable in the app |
| Tests | xUnit, plus recorded-flight replay tests and a simulated-pilot bot |
| Repo | New public GitHub repo named `OpenSkyATC`; initialise git locally, and I will create the remote |
| Licence | GPL-3.0 |
| Workflow | Stop after every step for my review |

**Out of scope for v1** (do not build, but keep the interfaces open for them): AI traffic, FAA phraseology, MSFS career-mode integration, VFR flight following, holding patterns, full emergency handling, MSFS 2020.

---

## 3. Solution layout

```
OpenSkyATC/
  CLAUDE.md
  README.md
  LICENSE                      (GPL-3.0)
  THIRD_PARTY_NOTICES.md
  docs/
    ATC-Brain-Design.md        (provided by me)
    decisions.md               (log of decisions made while building)
  src/
    OpenSkyAtc.Core/           brain: phases, events, clearance, conformance, airspace, planning, taxi routing
    OpenSkyAtc.Phraseology/    ICAO renderer: display text + speech text, number rules, callsign table
    OpenSkyAtc.SimConnect/     bridge: SimState at 4 Hz, airport facility data, key events
    OpenSkyAtc.Voice/          Piper TTS, radio audio filter, Whisper STT, intent parser, push-to-talk
    OpenSkyAtc.SimBrief/       SimBrief flight plan import
    OpenSkyAtc.App/            WPF app: UI, settings, debrief, local server for the toolbar panel
    OpenSkyAtc.Recorder/       console tool: records SimState to JSON-lines
  toolbar/
    opensky-atc-toolbar/       MSFS 2024 Community-folder package for the in-sim panel
    opensky-atc-taxi-arrows/   MSFS 2024 Community-folder package with the taxi arrow SimObject model(s)
  tests/
    OpenSkyAtc.Core.Tests/
    OpenSkyAtc.Phraseology.Tests/
    OpenSkyAtc.Voice.Tests/
    OpenSkyAtc.Replay.Tests/   recorded flights + expected transcripts
    fixtures/                  airport JSON fixtures, recorded flights
```

Use the interfaces from the design doc ("Core interfaces (C#)"): `IAtcBrain`, `IPhase`, `PhaseContext`, `IAirportDataProvider`. Rename the `Atc.*` namespaces in the doc to `OpenSkyAtc.*`.

---

## 4. Known facts and open items to verify (do not guess these)

- **MSFS 2024 SDK:** I am not sure it is installed. In step 0, check for it (look for the SDK environment variable and the SimConnect SDK folder). If it is missing, stop and tell me exactly what to install and from where.
- **GPL-3.0 and SimConnect:** the managed SimConnect DLL comes from Microsoft's SDK and is not GPL. Before step 9, research whether it can be referenced and redistributed with a GPL-3.0 app. Propose an option (for example, a GPL linking exception for the SimConnect DLL, or requiring users to supply it), and ask me to choose.
- **Piper voices:** each voice model has its own licence. Shortlist 4–6 English voices whose licences allow redistribution in a GPL project, and ask me to pick.
- **Whisper model size:** benchmark at least two model sizes for accuracy on ATC phrases and CPU/GPU load while MSFS is running. Then propose a default and make it a setting.
- **SimBrief:** confirm the current public endpoint for fetching the latest OFP by pilot ID, and its usage terms, before building the import.
- **Toolbar panel ↔ app communication:** research what an MSFS 2024 toolbar panel can use to talk to an external app (for example a localhost WebSocket, or SimConnect client data). Present the options and ask me.
- **In-sim taxi arrows:** the native MSFS taxi ribbon only follows routes from the default ATC, and there is no public API to feed it a route from a third-party app. The expected approach is to ship our own arrow model as a SimObject in a Community package, and spawn, move and remove instances along the route through SimConnect (AI/simulated-object creation). Before step 10, verify this works in MSFS 2024. Check the SDK docs and samples, object count limits, placement on sloped ground, and performance. Look for any newer official API for taxi guidance. Report back with options and ask me before building. The arrow model must be original work that only resembles the default style; do not extract or copy Asobo/Microsoft assets.
- **Airline telephony table** (IBE → "Iberia", etc.): find an openly licensed source, or ask me. Do not hand-type a large list from memory.
- **Squawk code ranges, transition altitudes and per-airport SOPs:** make them configurable data files, and ask me where values should come from. Never hard-code invented values.
- **Frequencies for generated CTR sectors:** the design doc says these are generated in v1. Ask me how to generate them before implementing (for example, a fixed pool or real data).

---

## 5. Build steps

Each step lists what to build and when it is done. Follow the design doc for behaviour details.

### Step 0 — Environment and repo
- Check Windows version, .NET 10 SDK, Visual Studio/Build Tools, git, and the MSFS 2024 SDK. Report what is present or missing, and stop if anything is missing.
- Create the solution and empty projects from section 3. Add GPL-3.0 `LICENSE`, `README.md` (what it is, status, build instructions), `CLAUDE.md`, `THIRD_PARTY_NOTICES.md` and `docs/decisions.md`.
- Ask me to place `docs/ATC-Brain-Design.md` if it is not there yet.
- **Done when:** the solution builds, an empty test project runs, and the first commit is made.

### Step 1 — Recorder and replay harness
- `OpenSkyAtc.Recorder`: connects to MSFS via SimConnect and writes `SimState` at 4 Hz to JSON-lines, with all fields from the design doc's data model.
- Replay harness in tests: feeds a recording into `IAtcBrain` at accelerated speed, applies a script of pilot intents at given sim times, and outputs a transcript for approval testing.
- Ask me to record 2–3 flights (one with a go-around) and tell me exactly how to run the recorder.
- **Done when:** a recording replays deterministically, and the same seed gives an identical transcript.

### Step 2 — Data model and event engine
- `SimState`, `FlightPlan`, `Clearance`, `FlightSession`, `ControllerUnit`, `AltitudeTarget`, and the event log, as in the design doc.
- Event engine with registration, edge detection, hysteresis and hold times. Include every event in the doc's table, plus pause freeze and teleport detection.
- **Done when:** each event has unit tests, including noise-at-threshold and hysteresis cases.

### Step 3 — Phases 1–7 (gate to departure)
- Phase infrastructure (`IPhase`, transitions returned and logged centrally), conversation model (exchanges, intents, readback verification), and phases Preflight → Initial climb.
- Display text only; no audio yet.
- **Done when:** replay tests cover clearance, push/start, taxi, line-up, takeoff and handoff to departure, including wrong and missing readbacks.

### Step 4 — Phraseology renderer
- `AtcMessage` parts and the ICAO renderer for display text and speech text, with all number rules from the doc, callsign handling and wording variety via the seeded RNG.
- **Done when:** snapshot tests pass for every message part and number rule.

### Step 5 — Conformance monitor
- All checks from the doc's conformance table, with tolerances, grace times, three escalation levels, suppression windows, and logging of deviations for the debrief.
- **Done when:** synthetic-state tests pass for every check, including reset after returning inside tolerance.

### Step 6 — Phases 8–15 and go-around
- Departure, En route, Descent, Approach, Final, Landing roll, Taxi in, Parked, and the go-around branch.
- Airspace model: generated sectors and handoffs per the doc (ask me about CTR frequencies first; see section 4).
- **Done when:** a full recorded flight replays gate to gate, and the go-around recording re-enters Approach.

### Step 7 — Planning
- Runway selection by wind, departure clearance contents, step climbs, top of descent, STAR/approach clearance, vectoring to final and speed control.
- **Done when:** unit tests pass for runway scoring and TOD, and vectoring is verified against a recorded arrival.

### Step 8 — Taxi routing
- Taxiway graph from airport data, A* with the doc's cost rules, instruction compression, hold-shorts, the bad-data fallback, and taxi-in to a stand.
- Test with airport JSON fixtures for 5–10 airports. Ask me which airports to use, including at least one known-bad default airport.
- **Done when:** routing tests pass on all fixtures, and the fallback triggers on the bad airport.

### Step 9 — Live SimConnect bridge
- Resolve the GPL/SimConnect licensing question with me first (section 4).
- `OpenSkyAtc.SimConnect`: live `SimState` at 4 Hz, facility data provider implementing `IAirportDataProvider`, MSFS key event for push-to-talk, and reconnect handling.
- **Done when:** the bridge passes an integration checklist I run in the sim. Give me the checklist.

### Step 10 — In-sim taxi arrows
- Resolve the taxi-arrow approach with me first (section 4).
- Arrow model package (`toolbar/opensky-atc-taxi-arrows`): an original arrow model styled like the default blue taxi-ribbon arrows.
- `TaxiArrowController` in the app layer (not in `OpenSkyAtc.Core`): takes the cleared taxi route from the brain and places arrows along the route centreline at a fixed spacing, oriented along the direction of travel and following the ground elevation.
- Behaviour:
    - Arrows appear only after the taxi clearance is read back correctly.
    - Only the next part of the route ahead of the aircraft is shown; arrows behind the aircraft are removed as it moves. Ask me for the default look-ahead distance and arrow spacing, and make both settings.
    - At each runway hold-short, the arrows end at the hold-short line. The route beyond appears only after the crossing clearance is read back.
    - On a re-route, the old arrows are replaced. All arrows are removed at the holding point, at the stand, or when the flight ends or the app disconnects.
    - The toggle in settings and in the toolbar panel works instantly.
- The brain exposes the active cleared taxi route and hold-short state through `FlightSession`/`Clearance`; it must not know about arrows.
- **Done when:** unit tests pass for arrow placement and windowing logic using fixture routes, no arrows are left behind after a disconnect, and the arrows work in the sim per a checklist you give me (departure taxi with a runway crossing, arrival taxi to a stand, a re-route).

### Step 11 — SimBrief import
- Fetch the latest OFP by pilot ID and map it to `FlightPlan`, with clear errors for a missing ID, no plan, or network failure.
- **Done when:** tests pass against saved sample responses.

### Step 12 — Voice out
- Piper integration (I pick the voices; see section 4), one voice per controller unit per session, radio band-pass and noise filter, and the transmission queue rules from the doc (priority, gaps, preemption, no talking over push-to-talk).
- **Done when:** a replay can be played as audio, and queue rules have unit tests.

### Step 13 — WPF app
- Main window: connection status, flight info, current frequency/unit, transcript, and the available-intent buttons (from `AvailableIntents`), plus a "hard mode" readback option.
- Settings: SimBrief ID, voices, volume, push-to-talk bindings, Whisper model, UI language (English/Spanish), taxi arrows on/off, arrow look-ahead distance and spacing.
- Ask me for layout preferences before building the UI. Show me a rough mockup description first.
- **Done when:** a full live flight can be flown with buttons and audio.

### Step 14 — Voice in
- Push-to-talk from all three sources (keyboard global hotkey, joystick/yoke button, MSFS binding), Whisper transcription, and an intent parser that maps transcripts to `PilotIntent` with slots (altitudes, headings, frequencies, squawks, runways, callsign).
- Low-confidence results → ATC says "say again" instead of guessing.
- **Done when:** parser tests pass on a set of ATC phrases (ask me to record some samples), and voice works in a live flight.

### Step 15 — In-sim toolbar panel
- MSFS 2024 Community package that mirrors status, transcript and intent buttons, includes the taxi arrows on/off toggle, and shows "Not connected" when the app is not running. Use the communication method I chose (section 4).
- **Done when:** the panel works in-sim and in VR per a checklist you give me.

### Step 16 — Debrief
- End-of-flight summary: deviations by severity, readback errors, missed handoffs and timings, saved locally and viewable in the app.
- **Done when:** a replayed flight with known errors produces the expected debrief.

### Step 17 — Release preparation
- Ask me before doing anything here: installer type, versioning, GitHub release process, and README screenshots.

---

## 6. Start now

Begin with **Step 0**. Before writing any code, read `docs/ATC-Brain-Design.md` (or ask me to provide it). Then list every question you have about this prompt or the design doc, grouped by step, and wait for my answers.
