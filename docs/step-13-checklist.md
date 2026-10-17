# Step 13 checklist (the app: a full live flight with buttons and audio)

Start the app from the repository root:

```
dotnet run --project src/OpenSkyAtc.App
```

Before the flight: generate a short plan on SimBrief (for example LEMD to LEBL) or export one as an MSFS .pln, and load the flight in MSFS 2024 at a gate.

## Setup

1. [ ] The app follows the Windows light/dark setting.
2. [ ] Settings: enter your SimBrief Pilot ID (or choose "MSFS .pln file" and browse to the file), Save. Close and reopen the app: the settings are still there.
3. [ ] Settings > Voice > Test: a sample call plays with the radio effect; turning "Radio effect" off and testing again gives a clean voice.
4. [ ] Settings > Language > Español, Save: every label changes to Spanish at once; switch back to English.

## Flight

5. [ ] Import plan (starts the flight; the gear menu has Settings, Compact mode and End flight): the status goes through importing, waiting for MSFS, loading airports and finding control centres, then "MSFS connected". The flight strip shows your callsign (spoken form), route and phase.
6. [ ] The radio block shows COM1 and who you are talking to; when COM1 is on another frequency, the yellow box says which unit and frequency to tune.
7. [ ] Request clearance (button): Delivery answers by voice and in the transcript. The read-back button shows the values; pressing it is accepted.
8. [ ] Hard mode on: the next readback opens the form with one field per item and no values. Type them (for example "121.63", "FL 120", "36l"): accepted. Type one wrong: ATC corrects it ("negative...") and asks again.
9. [ ] Push and start, taxi, line up, takeoff, departure and climb with the buttons only: every ATC call is heard, buttons change with the phase, the "Next: contact..." box shows each handoff.
10. [ ] Holding push-to-talk (your sim binding or the ATC key): the "Transmitting" badge shows, and ATC does not talk over you.
11. [ ] Gear > Compact: the window becomes a small always-on-top strip with the frequency, the last ATC call and the buttons, usable on top of the sim; Expand brings the full window back. Closing the strip also brings it back.
12. [ ] Taxi arrows checkbox: turns the arrows off and on at once during a taxi.
13. [ ] Continue to the destination: approach, landing, taxi to the stand; the flight can be flown to the stand with buttons and audio.
14. [ ] Gear > End flight: the voice stops, arrows (if any) disappear; Import plan starts a new flight.
15. [ ] Close the app during a flight: it closes cleanly; reopening restores the window position (and the compact strip if it was open).
