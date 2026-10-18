# Step 16 checklist (end-of-flight debrief)

Done when: a replayed flight with known errors produces the expected debrief. That part is automatic:
`DebriefTests.AFlightWithKnownErrors_GetsTheExpectedDebrief` flies the synthetic route with a wrong squawk readback,
a missed readback, a late handoff and a 700 ft altitude bust, and checks the debrief against
`tests/fixtures/transcripts/debrief-known-errors.approved.txt` (score 82). These checks are for the app.

Start the app (`dotnet run --project src/OpenSkyAtc.App`) and fly a short flight (or part of one).

1. [ ] Gear > End flight (after at least one ATC call): a debrief window opens with the route, date, duration, "ended early (phase)", a score out of 100 and the findings.
2. [ ] Fly a flight to the stand and shut the engines down: the debrief opens by itself and says "flown to the stand".
3. [ ] Make a mistake on purpose (read back a wrong squawk in hard mode, or climb 500 ft past a cleared level for a while): it appears in the debrief with its time, and the score is lower.
4. [ ] Export: saves an .html file; opened in a browser it shows the same debrief (and the handoff times).
5. [ ] Gear > Past flights: lists the saved debriefs, newest first; Open (or double-click) shows one again.
6. [ ] App language Spanish: the debrief window and the export are in Spanish.
7. [ ] The files are in `%LOCALAPPDATA%\OpenSkyATC\debriefs` (one .json per flight).
