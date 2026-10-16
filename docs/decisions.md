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

## 2026-10-16 - Answers before step 3

1. **Departure runway before step 7:** use the flight plan's runway hint if it exists at the airport, otherwise the runway end with the highest headwind (doc formula). Tailwind/SOP/crosswind rules come in step 7.
2. **Taxi before step 8:** the doc's bad-data fallback ("taxi to holding point runway 24L", no route), behind an `ITaxiPlanner` interface. Holding points come from airport data (fixtures now, taxi graph in step 8).
3. **Message parts in step 3:** typed parts with plain display text; step 4 adds speech text, number rules and variety.
4. **ATIS deferred:** the broadcast and the "confirm information" check wait for the dewpoint/cloud decision. `RequestClearance` carries an optional ATIS letter that is not checked yet.
5. **Missing units:** no DEL -> GND gives the clearance; no GND -> TWR. No TWR -> uncontrolled (doc).
6. **DEL -> GND handoff:** after a correct clearance readback: "readback correct, when ready for push and start contact Ground 121.700"; the pilot reads back the frequency.
7. **Line-up flow:** seeded choice: about 30 % "line up and wait" then the takeoff clearance 20-60 s after the line-up readback; otherwise a direct takeoff clearance. The split is a setting.
8. **Push and start:** no "facing ..." until stand geometry exists (step 8).
9. **No-push stands:** `RequestTaxi` is accepted straight from the Cleared phase.
10. **Squawk fallback:** with no range for the region, any valid octal code except the doc's reserved codes; per-region ranges keyed by ICAO prefix in `data/squawk-ranges.json`.
11. **Transmit radio:** `SimState` records COM1/COM2 transmit flags (`COM TRANSMIT`); ATC answers on the radio the pilot transmits on.

## 2026-10-16 - Step 3

- **Interfaces beyond the design doc:** `IPhase` also has `Unit(ctx)` and `AvailableIntents(ctx)`, and `OnPilotIntent` may return `PhaseResult.Unhandled` so the brain answers "unable, contact [unit]" (wrong unit) or "say again" (right unit, wrong request). `PhaseContext` adds the tick's new events, the conversation manager, plan, airports, options, the active-clock `Now` and the called unit. `PhaseResult` can redispatch the intent to the next phase (a check-in that starts it).
- **Readback corrections:** all wrong items are corrected in one transmission ("negative, squawk 4521"); only when nothing is wrong are missing items requested ("read back the initial climb"). The exchange then expects only those items.
- **No readback:** repeated once after 10 s, marked missed 10 s later (logged for the debrief) and never repeated again. A late readback is still accepted so the flight can continue. Further escalation is the conformance monitor (step 5).
- **Wilco** on an instruction that needs a readback counts as a readback with every item missing.
- **One open exchange per unit:** a request while that unit has an exchange queued or awaiting readback gets "stand by" and is handled when the exchange closes.
- **Drop rule:** a queued transmission is dropped (and logged) when neither COM radio is tuned to the unit at send time.
- **Taxi-out exit** reads the doc's numbers directly (within 150 m of the holding point and below 5 kt) instead of a distance event, because the crossing band would not arm when taxi starts close to the holding point.
- **Holding point without data:** the runway end position is used as the taxi target until step 8.
- **Reject takeoff:** added `RejectTakeoff` intent (it is in the phase table but not the intent catalog). Tower answers "roger", cancels the takeoff clearance and the flight returns to Holding.
- **Wind in clearances:** magnetic, using the variation from the aircraft's true and magnetic heading; rounded to 10 degrees (north is 360); under 1 kt is "wind calm".
- **Initial climb:** "FL070 above a high transition altitude" is read as: if the airport's transition altitude is known and is 5,000 ft or lower, clear FL070; otherwise 5,000 ft.
- **Reserved squawks:** besides the doc's 7500, 7600, 7700, 2000 and 1000, the allocator never assigns 7000 (European VFR) or 0000.
- **Departure phase (8)** is a placeholder that accepts the check-in; step 6 builds it.
- **Sessions that start in the air or on the runway** always start in Preflight for now; phase inference is step 6.
- **Simulated pilot (first part):** the replay runner can read back every instruction correctly after 4 s (`ReplayOptions.AutoReadback`); scripts override single items (`Readback squawk=1234 -initialclimb`).

## 2026-10-16 - Step 4

Owner's choices: ICAO wording plus European variants, destination spoken by name (ICAO code shown), courtesy phrases on every handoff, "niner/tree" off by default.

- **Source check:** the variants were checked against UK CAA CAP 413 edition 24 (Radiotelephony Manual, effective 1 July 2026). ICAO Doc 4444 itself is not freely available, so where the design doc gives a wording it stays the default ("doc") wording. CAP 413 is used as a reference only; nothing from it is redistributed.
- **Variants dropped because no source supported them:** "climb initially", "SID + flight planned route", "taxi via K, M to holding point", "line up and wait runway 24L", "start-up and pushback approved", "for clearance contact Delivery".
- **Variants kept (doc wording first, then CAP 413/common European):** readback correct / correct; push and start approved / pushback and start up approved; taxi to holding point / taxi holding point; climb 5000 ft / climb to altitude 5000 ft (flight levels never take "to"); proceed direct / route direct; reduce speed 180 knots / reduce speed to 180 knots; read back the squawk / read back squawk; say again / say again your request; contact Barcelona Tower / contact Tower (not for area control); for the push-and-start handoff: when ready for push and start / for push and start / plain contact.
- **Wind placement:** design doc order "wind 250/12, runway 24L, cleared for takeoff", or CAP 413 edition 24 order "runway 24L, cleared for takeoff, surface wind 250/12". Same for landing.
- **"Negative, I say again"** is used only on the second or later correction of the same exchange (CAP 413: for repeated incorrect readbacks), not as a random variant.
- **Courtesy:** "good day" or "bye bye" (the latter counts as a European variant) on every handoff (`Contact`), not on "unable, contact".
- **Number rules beyond the doc table (CAP 413 2.13):** flight levels are spoken digit by digit without padding (FL70 "flight level seven zero") and whole hundreds use "hundred" (FL100 "flight level one hundred"); display follows (FL70). Frequencies drop both final zeros when both are zero (118.100 "one one eight decimal one"), read as the doc's "drop the last zero" rule. Altitudes that are not whole hundreds are spoken digit by digit.
- **Station name on first contact:** each unit's first transmission includes its callsign after the aircraft's (CAP 413 example: "BIGJET 347, Stourton Tower, ...").
- **Registrations:** full on a unit's first transmission, then abbreviated (first character + last two, "E-BC"). Airline designators without a known telephony are spelled.
- **Renderer seeding:** the renderer uses its own RNG stream derived from the session seed, so wording never changes the brain's choices.
- **Callsign table:** `CallsignTable` reads "ICAO,TELEPHONY" lines; the shipped data file is pending the owner's choice of source.
- **Airline telephony data (owner's choice):** Wikidata, CC0. `tools/airline-callsigns/generate.py` queries P230 + P432 and writes `data/airline-callsigns.csv`. When several entities share a designator, operating airlines win over dissolved ones, then the entity with the most Wikipedia sitelinks (Wikidata often tags subsidiaries with the parent's code, e.g. TAP Express under TAP). Remaining ties are left out. Display title-cases the telephony; words without vowels stay upper case ("KLM").

## 2026-10-16 - Answers before step 5

1. **Escalation timing:** level 1 after the grace time; level 2 one more grace period later and level 3 one more after that, never less than 15 s apart. Zero-grace checks (incursion, takeoff/landing without clearance) fire level 1 at once, repeat after 10 s where the doc has a level-2 text, and are logged as serious immediately.
2. **Debrief record:** every episode is logged (check, start/end, highest level, worst value). It counts as a deviation when it reached level 3 or is a serious check; level 1-2 episodes are warnings.
3. **Off route:** only in En route and Descent until SID/STAR geometry exists. Level 2 is a heading back towards the next waypoint at a 30 degree intercept.
4. **Heading check:** starts once the aircraft first comes within 20 degrees of the assigned heading, or 60 s after the assignment, whichever is first.
5. **Climb/descent started:** moved 300 ft towards the target, or vertical speed at least 500 fpm towards it.
6. **On final (landing without clearance):** airborne, below 200 ft AGL, within 2 nm of a destination runway threshold and within 10 degrees of its extended centreline, or over the runway.
7. **Missed handoff:** level 2 goes out on 121.5 only when a radio is tuned to it; otherwise straight to the lost-comms note. A handoff is complete once the new frequency is tuned; calling another unit logs a skipped handoff.
8. **Transponder:** once airborne only ALT (Mode C) passes.
- Also: 250 kt below FL100 with no exemptions; altitude busts compare indicated altitude with the cleared value.

## 2026-10-16 - Step 5

- **Monitor placement:** `ConformanceMonitor` runs every tick after the phase, from `AtcBrain`, frozen while paused or settling. Each check reads the `Clearance` and `SimState` only; level-2 instructions that need a readback (altitude, heading, re-route) go through the conversation manager as exchanges.
- **Which unit speaks:** normal warnings come from the unit in control (`Session.CurrentUnit`); serious ones (incursion, takeoff/landing without clearance) from the unit on the pilot's frequency, so they are heard. A missed-handoff repeat comes from the unit that issued the handoff.
- **Altitude bust direction:** set when the clearance changes: climb if the cleared altitude is more than 300 ft above, descent if more than 300 ft below, otherwise "maintain", where either side counts.
- **Not complying** counts from the later of the clearance and lift-off, so a clearance received at the gate is not punished.
- **Handoff completion:** tuning the new frequency completes a handoff (decision 7), done by the brain each tick; a call to a third unit while a handoff is pending is logged as `HandoffSkipped`.
- **Landing check** ignores departure phases (Preflight to Initial climb) and requires the aircraft to fly within 30 degrees of the landing direction, so a climb-out over the runway is not a "final".
- **Episodes fixed within the grace time** (level 0) are not logged unless serious.
- **Vector back to route** sets the assigned heading when sent, so the heading check takes over from the route check.
- **Simulated pilot:** the replay runner sets the transponder to the assigned squawk (`ReplayOptions.SetsSquawk`), since a recording cannot know the code in advance.
