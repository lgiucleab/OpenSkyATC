# ATC Brain — Single-Pilot Design

Oct 16, 2026 · @Lucas Giuclea Balan

## Scope and principles

The brain is a deterministic, tick-driven state machine that controls one IFR flight gate to gate. It reads sim state, issues ICAO/European phraseology, checks readbacks, and catches pilot deviations. There is no AI traffic in v1; the airspace is empty except for the player.

**In scope for v1**

- IFR flight from a SimBrief plan: clearance, push/start, taxi, takeoff, departure, en route, descent, approach, landing, taxi in
- One controller unit per role (DEL, GND, TWR, DEP, CTR, APP, ATIS), with frequencies from sim airport data where available
- Click-to-respond UI first; voice input plugs in later through the same intent layer
- Conformance monitoring (altitude, heading, speed, frequency, runway safety)
- Go-around and missed approach

**Out of scope for v1**

AI traffic, VFR flight following, holding patterns, emergencies beyond a simple MAYDAY/PAN acknowledgement, and FAA phraseology. Each is a later module behind the same interfaces.

**Design principles**

1. **The brain never touches SimConnect.** It receives a `SimState` snapshot each tick and emits `Transmission` objects. This makes it fully testable with recorded flights.
2. **One source of truth for clearances.** Every instruction ATC issues updates the `Clearance` record; monitoring compares the aircraft against that record, never against the phraseology text.
3. **Phases decide who talks; the clearance decides what is legal.** Keep these two concerns separate.
4. **Deterministic with seeded randomness.** Timing jitter and controller choices use a seeded RNG so a replay produces the same session.
5. **ATC acts like a person.** Short delays, one transmission at a time, repeats when ignored, and escalation only when needed.

## Architecture

The brain is a pure library between the sim and the voice: snapshots and pilot intents go in, structured ATC messages come out.

&#91;embedded content: ATC brain architecture · 4 inputs, 5 core parts, 3 outputs\]

Inside the brain, the event engine feeds the active phase; the phase writes to the clearance record; the conformance monitor reads that record against the aircraft. Rendering text and audio happens outside the brain, so the same logic runs in tests, replays and the live sim.

## Data model

Five records hold everything the brain knows. Everything else is derived each tick.

| Record | Holds | Updated by |
| --- | --- | --- |
| `SimState` | Lat/lon, altitude MSL and AGL, IAS/GS, heading, vertical speed, on-ground, parking brake, engines running, COM1/COM2 active+standby, transponder code and mode, gear/flaps, sim time, wind at aircraft | SimConnect bridge, every tick |
| `FlightPlan` | Callsign, telephony, type, origin, destination, alternate, route waypoints, cruise level, planned SID/STAR, departure/arrival runway hints | SimBrief import, once |
| `Clearance` | Cleared altitude, assigned heading (or own nav), speed restriction, squawk, cleared route/SID/STAR/approach, runway, taxi route + hold-short points, current and next frequency, active clearances (line up, takeoff, land, cross) | Every ATC transmission |
| `FlightSession` | Current phase, current controller unit, exchange in progress, pending ATC actions, warnings issued, last pilot transmission time, RNG seed, event log | Brain |
| `ControllerUnit` | Role (ATIS, DEL, GND, TWR, DEP, APP, CTR), callsign ("Barcelona Ground"), frequency, area of responsibility, voice id | Built at session start, CTR sectors built along the route |

**Clearance flags matter more than text.** Store runway permissions as explicit booleans with the runway they apply to: `LineUpCleared("24L")`, `TakeoffCleared("24L")`, `LandingCleared("24R")`, `CrossCleared("02")`. Conformance checks read only these.

**Altitude is stored with its reference.** `AltitudeTarget { Value, Kind = Altitude | FlightLevel }`, with the transition altitude and level of each airport. This drives both phraseology and the bust check.

The event log records every transmission, pilot action and detected event with sim time. It is the debugging tool and the replay format.

## Flight phase state machine

Fifteen phases run gate to gate, with one loop: a go-around returns the flight from final to approach. Each phase owns exactly one controller unit, a set of available pilot requests, and an exit condition. Thresholds below are starting values to tune in testing.

| # | Phase | Unit | Enters when | ATC does | Pilot can request | Exits when |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Preflight | ATIS, DEL | Session starts on ground, engines off | ATIS on its frequency | IFR clearance | Clearance read back correctly |
| 2 | Cleared | GND | Readback correct | Waits | Pushback and start | Push approved |
| 3 | Pushback | GND | Push approved | "Push and start approved, facing west" | Taxi | Pilot requests taxi |
| 4 | Taxi out | GND | Taxi requested | Taxi route to holding point, hold-short points | Re-route, say again | Within 150 m of holding point and GS < 5 kt, then handoff to TWR |
| 5 | Holding | TWR | Checks in with tower | Line up and wait, takeoff clearance with wind | Ready for departure | Takeoff cleared and GS > 40 kt on that runway |
| 6 | Takeoff roll | TWR | GS > 40 kt on cleared runway | Silent | Reject takeoff | Airborne for 3 s |
| 7 | Initial climb | TWR | Airborne | "Contact departure 124.7" at about 1,000 ft AGL | None | Checks in on DEP frequency |
| 8 | Departure | DEP | Checks in | Radar contact, climb steps, direct-to | Higher, direct | Above FL150 or 40 nm from origin, then handoff to CTR |
| 9 | En route | CTR (n) | Checks in | Climb to cruise, sector handoffs, direct-to | Level change, direct | Inside the top-of-descent window |
| 10 | Descent | CTR | TOD reached or pilot request | Descend, STAR, QNH, handoff to APP | Lower, direct | Checks in with APP |
| 11 | Approach | APP | Checks in | Expect approach, vectors or STAR, cleared approach, speed control | Report established | Established and inside about 10 nm, then handoff to TWR |
| 12 | Final | TWR | Checks in | Landing clearance with wind | Go-around | Touchdown, on ground for 3 s |
| 13 | Landing roll | TWR | Touchdown | "Vacate when able, contact ground" | None | Off the runway and GS < 30 kt |
| 14 | Taxi in | GND | Checks in | Taxi to stand via route | Different stand | Parked: brake set, GS 0, near stand |
| 15 | Parked | GND | Parked | Closing call | None | Engines off, session ends |

**Go-around branch.** From Final or Approach, a go-around is either reported by the pilot or detected (below 300 ft AGL near the runway, then climbing more than 300 ft without touchdown). Tower issues the published missed approach or "runway heading, climb 5000 ft", hands off to APP, and the flight re-enters Approach.

**Implementation.** Each phase is a class implementing `IPhase { OnEnter, Tick, OnPilotIntent, OnExit }`. Transitions are returned, never set directly, so the machine logs every change in one place.

## Event engine

Phases never read raw numbers; they react to named events that the engine derives from consecutive `SimState` snapshots. Run the tick at 4 Hz; ATC logic does not need more.

Every event uses **edge detection with hysteresis and a hold time**, so noise near a threshold does not fire it twice. Example: `Airborne` fires when on-ground is false for 3 s continuously, and does not re-arm until on-ground has been true for 5 s.

| Event | Fires when | Used by |
| --- | --- | --- |
| `EnginesStarted` / `EnginesStopped` | Any engine running flag changes, held 2 s | Pushback, Parked |
| `TaxiStarted` | GS > 3 kt, on ground, held 3 s | Pushback, Taxi out, Taxi in |
| `EnteredRunway(rwy)` / `ExitedRunway(rwy)` | Aircraft position enters or leaves the runway polygon (built from runway ends + width) | Holding, Landing roll, conformance |
| `PassedHoldShort(rwy)` | Crosses a hold-short line from the taxi graph | Runway incursion check |
| `TakeoffRoll` | GS > 40 kt inside a runway polygon | Holding |
| `Airborne` / `Touchdown` | On-ground flips, held 3 s | Takeoff roll, Final |
| `AltitudeCrossed(alt)` | Indicated altitude crosses a registered value | Handoffs, speed restriction |
| `LevelOff(alt)` | Vertical speed within ±200 fpm for 10 s | Climb/descent steps |
| `DistanceTo(point) < d` | Great-circle distance crosses a registered value | TOD, approach handoffs |
| `FrequencyChanged(com, freq)` | Active COM frequency changes | Check-in and missed handoff logic |
| `SquawkChanged(code)` / `TransponderModeChanged` | Transponder values change, held 2 s | Conformance |
| `Established(loc)` | Within ±1° of the final approach course and closing on the runway | Approach |

**Registration, not polling.** Phases register what they care about in `OnEnter` ("tell me when altitude crosses 1,000 ft AGL") and clear it in `OnExit`. This keeps each phase's logic readable and the engine cheap.

**Sim pauses and slew.** Freeze all timers when the sim is paused, and treat a position jump over 2 nm in one tick as a teleport: re-infer the phase instead of firing events (see Edge cases).

## Conversation model

Every radio interaction is an **Exchange**: an opener, a response, an expected readback, and a verdict. Who opens depends on the case: the pilot opens requests, ATC opens instructions.

**Pilot intents** are the only input the brain accepts, whether they come from a button or from speech recognition. A starting catalog:

- `RequestClearance`, `RequestPushStart`, `RequestTaxi`, `ReadyForDeparture`
- `CheckIn(altitude, clearedAltitude)`, `RequestLevel(alt)`, `RequestDirect(fix)`, `RequestDescent`
- `ReportEstablished`, `GoingAround`, `ReportVacated`
- `Readback(slots)`, `SayAgain`, `Wilco`, `Unable(reason)`
- `Mayday`, `PanPan` (acknowledge only in v1)

**Readback verification.** Each ATC instruction declares its required readback items. ATC compares the pilot's `Readback` slots against them.

| Instruction | Required readback items |
| --- | --- |
| IFR clearance | Destination, SID or route, initial climb, squawk |
| Taxi | Holding point, runway, every hold-short |
| Line up / takeoff / landing | Runway and the clearance itself |
| Altitude or level | Value and its reference (FL or altitude) |
| Heading, speed, direct-to | Value or fix |
| Frequency change | Frequency |
| QNH | Value |

Outcomes: all items correct → "readback correct" only where ICAO practice says it (clearance delivery), otherwise silence. A wrong item → ATC corrects that item only: "Negative, climb flight level one two zero." A missing item → "Read back the hold short." No readback within 10 s → ATC repeats once, then follows the missed-readback path in Conformance.

**Click mode generates the readback.** In button mode the UI offers the correct readback plus, optionally, a "hard mode" where the pilot picks values. This keeps one code path for both input methods.

**Exchange rules.** Only one exchange is open per frequency. A pilot request while ATC has an open exchange is answered with "Stand by" and queued. Requests that make no sense in the current phase get "Unable, contact \[correct unit\] on \[frequency\]" or "Say again".

## Conformance monitoring

A monitor runs every tick and compares `SimState` with `Clearance`. This is what makes ATC feel alive: it notices when you do something wrong. Every check has a tolerance, a grace time, and three escalation levels.

| Check | Triggers when | Grace | Level 1 (reminder) | Level 2 (instruction) | Level 3 (logged deviation) |
| --- | --- | --- | --- | --- | --- |
| Altitude bust | More than 300 ft past cleared altitude in the direction of travel | 5 s | "Check altitude, cleared FL120" | "Descend immediately FL120" | Deviation logged in debrief |
| Altitude not complying | No climb/descent started 60 s after the instruction | 60 s | "Confirm climbing FL240?" | Repeat instruction | Logged |
| Heading | More than 20° off an assigned heading | 15 s | "Check heading, fly heading 270" | "Turn left immediately heading 270" | Logged |
| Off route | More than 3 nm off cleared route with no heading assigned | 30 s | "Say intentions" | Vectors back to route | Logged |
| Speed | Above 250 kt IAS below FL100, or above an assigned speed by 15 kt | 20 s | "Reduce speed 250 knots" | Repeat | Logged |
| Squawk | Wrong code or mode not ALT once airborne | 30 s | "Squawk 4521" | "Recycle squawk 4521" | Logged |
| Missed handoff | Not on the new frequency 45 s after the handoff | 45 s | Repeat on old frequency | Call on guard 121.5 | Lost-comms note |
| Runway incursion | `PassedHoldShort` without a cross or line-up clearance | 0 s | "Stop immediately" | Stays stopped | Logged, serious |
| Takeoff without clearance | `TakeoffRoll` without `TakeoffCleared` for that runway | 0 s | "Stop immediately, I say again, stop" | None | Logged, serious |
| Landing without clearance | Below 200 ft AGL on final without `LandingCleared` | 0 s | "Go around" | Repeats | Logged, serious |
| Taxi off route | More than 50 m from the cleared taxi route | 10 s | "Hold position" | Re-route | Logged |

**Rules.** Never issue two warnings in the same transmission. Reset the escalation level when the aircraft returns inside tolerance. Suppress altitude checks during the 30 s after a new clearance so turns and level-offs are not punished. Store all logged deviations for an end-of-flight debrief, which is a cheap and very satisfying feature.

## Airspace, sectors and handoffs

V1 uses simple geometric volumes instead of real airspace, because handoff timing matters far more to realism than exact sector borders. Build them once at session start from the flight plan.

| Unit | Owns the aircraft while | Frequency source |
| --- | --- | --- |
| DEL, GND, TWR | On or near the origin/destination airport | Sim airport facility data (SimConnect facilities API) |
| DEP | Origin within 40 nm and below FL150 | Sim airport data; fallback to APP frequency |
| CTR sectors | Everything between DEP and APP | Generated: a sector every 150 to 250 nm along the route, named after the nearest FIR or large airport |
| APP | Destination within 40 nm, or below FL150 inbound | Sim airport data |

**Handoff rule.** A handoff is issued when the aircraft is about to leave the current volume: 5 nm before a CTR sector boundary, or at the DEP/APP thresholds. ATC says "contact \[unit\] \[frequency\]", expects a readback of the frequency, and starts the missed-handoff timer.

**Check-in.** The next unit responds to `CheckIn` with "\[callsign\], \[unit\], radar contact" (first radar unit only) or with "identified", then confirms or amends the clearance. If the pilot's reported altitude differs from Mode C by more than 200 ft, ATC asks to confirm it.

**Upgrade path.** Replace the generated CTR sectors with real FIR/sector polygons from an open dataset (for example, VATSIM's public sector files) without touching the phases: they only see `ControllerUnit` objects. Check each dataset's licence before shipping it in a paid product.

## Vertical and lateral planning

This is the controller's "judgement": which runway, when to climb, when to descend, and how to vector onto final.

**Runway selection.** Score each runway at the airport by its headwind component and pick the highest. Allow up to 5 kt of tailwind to keep a preferred runway (from an optional per-airport SOP file), and never pick a runway with a crosswind above 25 kt if another is available. Lock the runway for the session unless the wind shifts by more than 60° or 10 kt.

```latex
\text{headwind} = V_{wind} \cdot \cos(\theta_{wind} - \theta_{runway})
```

**Departure clearance.** Initial climb comes from the SID's top altitude if the sim data has it, otherwise 5,000 ft (or FL070 above a high transition altitude). Squawk is drawn from a configurable per-region code range, never 7500, 7600, 7700, 2000 or 1000.

**Step climbs.** After the aircraft levels off or passes 2,000 ft below its cleared level, wait a random 30–120 s, then clear it higher in 2–6 blocks until cruise. About 1 in 5 steps should be a "traffic" hold-down to feel real, even with no traffic.

**Top of descent.** Use the 3:1 rule plus a deceleration buffer, then issue the descent when the distance to destination falls inside that value plus a random 0–15 nm. A pilot `RequestDescent` inside 30 nm of the computed TOD is granted.

```latex
\text{TOD}_{nm} = \frac{\text{cruise ft} - \text{target ft}}{1000} \times 3 + 10
```

**Approach.** If the plan has a STAR and the sim has its data, clear the STAR and the approach together ("cleared ILS runway 24R via ..."). Otherwise, vector:

1. Pick the final approach fix 8–12 nm out on the extended centreline.
2. Build a downwind and base point on the side closer to the aircraft's inbound track.
3. Issue headings toward each point in turn, with descents so the aircraft is at intercept altitude by base.
4. Final vector: a heading that intercepts the localizer at 20–30°, at least 2 nm before the FAF, then "cleared ILS approach runway 24R, report established".
5. Speed control: 220 kt on downwind, 180 kt on base, 160 kt to 4 nm, if the aircraft type is a jet.

Recompute vectors every tick but only transmit when the new heading differs from the last one by 10° or more.

## Taxi routing

Taxi routes are shortest paths on the airport's taxiway graph, then compressed into spoken instructions. MSFS exposes taxi points, taxi paths, taxi names, runways and parking through SimConnect facility data requests, so no external airport database is needed.

**Graph.** Nodes are taxi points; edges are taxi paths carrying their taxiway name, width and type (taxiway, runway, parking). Mark every edge that enters a runway's polygon and the hold-short node before it.

**Search.** A\* from the aircraft's nearest node (or parking spot) to the holding point of the departure runway. Edge cost:

- Length in metres
- +400 m per change of taxiway name, so routes use few named segments
- +1,500 m per runway crossing, +10,000 m for taxiing along a runway that is not the departure runway
- Infinite cost for one-way edges in the wrong direction and parking lanes not connected to the start or goal

**Instruction compression.** Collapse consecutive edges with the same name, drop unnamed connector segments shorter than 60 m, and append a hold-short for every runway crossing. Result: "Taxi to holding point runway 24L via K, M, hold short runway 02." Crossing clearances are given as separate exchanges when the aircraft reaches each hold-short.

**Bad data.** Default airports often have broken or unnamed networks. If no path exists or more than 40% of the route is unnamed, fall back to "taxi to holding point runway 24L" with no route and turn off the off-route check for that taxi. Log the airport so you can build a fixes list later.

**Taxi in.** After `ExitedRunway`, pick the gate from SimBrief's stand if available, otherwise the nearest free parking of a suitable size, and route the same way.

## Phraseology generator

Transmissions are built from structured messages, never typed strings. The brain emits `AtcMessage { Unit, Callsign, Parts[] }` and a phraseology layer renders it twice: as display text and as speech text. This lets you add FAA phraseology later as a second renderer.

**Message parts** (each renders itself): `ClimbTo(alt)`, `DescendTo(alt)`, `TurnHeading(dir, hdg)`, `Direct(fix)`, `Speed(kt)`, `Squawk(code)`, `Contact(unit, freq)`, `Qnh(hpa)`, `Wind(dir, kt)`, `ClearedTakeoff(rwy)`, `ClearedLand(rwy)`, `TaxiVia(holdingPoint, segments, holdShorts)`, `IfrClearance(...)`.

**Number rules (ICAO, speech text):**

| Value | Display | Spoken |
| --- | --- | --- |
| Flight level | FL120 | "flight level one two zero" |
| Altitude | 5000 ft | "five thousand feet" |
| Altitude | 3500 ft | "three thousand five hundred feet" |
| Heading | 090 | "heading zero nine zero" |
| Frequency | 121.850 | "one two one decimal eight five zero" (drop the last zero if both final digits are zero) |
| Squawk | 4521 | "squawk four five two one" |
| QNH | 1013 | "QNH one zero one three" |
| Runway | 24L | "runway two four left" |
| Wind | 250/12 | "wind two five zero degrees, one two knots" |

Say "niner" and "tree" only if the TTS voice pronounces plain digits badly; make it a setting.

**Callsigns.** Map the ICAO airline code to its telephony from a lookup table (IBE → "Iberia", VLG → "Vueling", RYR → "Ryanair"). For general aviation registrations, use the full form on first contact and the abbreviated form only after ATC has used it first.

**Variety.** Each part can have 2–3 equivalent wordings where ICAO allows ("when ready", "when able"). Pick with the seeded RNG so the same replay sounds the same.

## ATIS, timing and transmission scheduling

**ATIS.** Build it from sim weather at the airport: wind, visibility, cloud layers, temperature/dewpoint, QNH, active runway, transition level, and the information letter. Advance the letter (A → Z, then wrap) when the runway changes, QNH changes by 1 hPa or more, or the wind shifts enough to change runway scoring. ATC asks "confirm information Bravo" if the pilot's clearance request names an old letter. Loop the broadcast on the ATIS frequency only when that frequency is tuned.

**Response timing.** Real controllers are not instant. Sample delays from these ranges:

| Situation | Delay before ATC speaks |
| --- | --- |
| Reply to a pilot request | 1.5–4 s |
| Readback correction | 1–2 s |
| Check-in after handoff | 2–5 s |
| ATC-initiated instruction (climb, vector) | Fires when due, then waits for a free channel |
| Urgent (stop, go around) | Immediate, preempts the queue |

**Transmission queue.** One queue per frequency, ordered by priority (urgent, reply, instruction, info) and then by due time. Rules:

1. Never start a transmission while the pilot is transmitting (push-to-talk held) or while another ATC transmission is playing.
2. Leave a 1 s gap after any transmission ends.
3. If the aircraft leaves the frequency before a queued message plays, drop it and let the missed-handoff logic handle it.
4. Urgent messages cut the current transmission and play immediately.

**Voices.** Assign a fixed TTS voice per controller unit for the whole session, so the pilot can recognise "the same controller" on a frequency. Add light radio filtering (band-pass \~300–3,000 Hz, slight noise) in the audio layer, not the brain.

## Core interfaces (C#)

The brain is a plain .NET class library with no SimConnect, audio or UI references. The host app wires it to those.

```csharp
public interface IAtcBrain
{
    void Start(FlightPlan plan, AirportData origin, AirportData destination, int seed);
    void Tick(SimState state, TimeSpan simTime);          // called at 4 Hz
    void HandlePilotIntent(PilotIntent intent);           // from buttons or speech
    IReadOnlyList<PilotIntent> AvailableIntents { get; }  // drives the UI buttons
    event Action<AtcMessage> Transmission;                 // to phraseology + TTS
    FlightSession Session { get; }                        // read-only for UI/debrief
}

public interface IPhase
{
    FlightPhase Id { get; }
    void OnEnter(PhaseContext ctx);
    PhaseResult Tick(PhaseContext ctx);                   // may return a transition
    PhaseResult OnPilotIntent(PhaseContext ctx, PilotIntent intent);
    void OnExit(PhaseContext ctx);
}

public sealed record PhaseContext(
    SimState State, FlightSession Session, Clearance Clearance,
    EventBus Events, TransmissionQueue Radio, AirspaceModel Airspace,
    IRandom Rng);

public interface IAirportDataProvider   // SimConnect facilities in the app, JSON fixtures in tests
{
    Task<AirportData> LoadAsync(string icao);
}
```

**Project layout:**

- `Atc.Core` — brain, phases, events, clearance, conformance, airspace, planning (no dependencies)
- `Atc.Phraseology` — ICAO renderer, number rules, callsign table
- `Atc.SimConnect` — bridge that produces `SimState` and `AirportData`
- `Atc.Voice` — TTS (Piper) and later STT (Whisper) plus intent parser
- `Atc.App` — Windows desktop UI and settings
- `Atc.Tests` — unit tests and flight replays

## Edge cases

| Case | Handling |
| --- | --- |
| Session starts in the air or on the runway | Infer the phase from position, altitude and distance to origin/destination, issue a fresh clearance to current altitude, and continue from there |
| Slew, teleport or flight restart | Position jump over 2 nm in one tick: freeze ATC, re-infer the phase, announce nothing until the aircraft is stable for 10 s |
| Sim paused or active pause | Freeze every timer and the queue |
| Pilot ignores ATC entirely | Escalate per conformance, then stop repeating after level 3; never loop forever |
| Pilot skips a frequency (tunes the one after next) | The tuned unit takes the check-in normally and logs the skipped handoff |
| Pilot tunes a wrong frequency | Silence, as in reality; the missed-handoff timer handles it |
| Diversion request | Re-plan destination and runway, build APP for the new airport, continue in Descent or En route |
| Flight plan without SID/STAR data | Use vectors (departure: runway heading to 3,000 ft AGL, then direct to first route fix) |
| Airport without tower frequency | Treat as uncontrolled: departure clearance from CTR/APP, no taxi or takeoff clearance, blind-call reminder |
| Very short flights (< 80 nm) | Skip CTR; DEP hands off directly to APP; cap cruise at the planned level |
| SimConnect disconnects | Pause the brain, keep the session, resume on reconnect |

## Build order and testing

Build the brain against recorded flights before connecting it to the live sim. A recorder that dumps `SimState` at 4 Hz to a JSON-lines file is the first thing to write; one real flight then becomes a repeatable test.

1. **Recorder + replay harness.** Record two or three real flights (one with a go-around). Replay them into the brain at 10× speed and print the transmissions.
2. **Event engine** with unit tests per event, including hysteresis cases.
3. **Phases 1–7 (gate to departure)** with click intents and display text only, no audio.
4. **Phraseology renderer** with snapshot tests of spoken text for every message part.
5. **Conformance monitor** with synthetic states (altitude bust, incursion, missed handoff).
6. **Phases 8–15 and the go-around branch.**
7. **Runway selection, TOD and vectoring** tested against the recorded arrival.
8. **Taxi routing** on 5–10 airports, including a known-bad default airport.
9. **Live sim, TTS and UI.** Only now wire `Atc.SimConnect`, `Atc.Voice` and `Atc.App`.

**Test types:**

- **Unit tests** for each event, phase transition, readback check and number rule.
- **Replay tests:** a recorded flight plus a script of pilot intents at given sim times must produce an expected transcript. Approve transcripts once and diff them on every change.
- **Simulated pilot:** a small bot that flies the `Clearance` perfectly (snaps altitude/heading towards targets) lets you run hundreds of synthetic flights to catch stuck phases and infinite loops.

**Working with Claude Code.** Give it this doc as the spec, ask for one numbered step at a time, and require the tests for that step to pass before moving on. The replay harness is what keeps AI-written changes from silently breaking earlier phases.
