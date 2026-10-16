# Step 9 integration checklist (live SimConnect bridge)

Run from the repository root in a terminal where `MSFS2024_SDK` is set. Start a flight in MSFS 2024, cold and dark or ready at a gate, at the origin you pass with `--from`.

```
dotnet run --project src/OpenSkyAtc.Recorder -- live --from LEMD --to LEBL --callsign IBE1234 --type A320 --cruise 330 --ptt "VK_LCONTROL+T"
```

Keys in the console: `1`-`9` send the pilot call shown with that number, `L` shows the list again, `Q` quits. The console title shows the phase, the session clock and COM1.

Tick each line, and note what you saw if a line fails.

## Connection and data

1. [ ] Started **before** the sim: the console says "Waiting for MSFS 2024", then continues on its own within about 5 s of the flight loading.
2. [ ] Both airports print with the right name, runways, transition altitude and units (Delivery, Ground, Tower, Approach, Information). The frequencies match the sim's own airport info.
3. [ ] "[sim] connected, 4 Hz" and the aircraft title appear.
4. [ ] The console title's clock advances by 1 second per real second, and COM1 follows the radio when you tune it in the cockpit.

## State

5. [ ] Parked at the gate, the start phase is Preflight (no "phase inferred" line, or one that says Preflight).
6. [ ] Request clearance (`1` on RequestClearance) on the Delivery frequency: Delivery answers. Read back: the readback is accepted. The squawk in the clearance can be set in the cockpit, and no squawk warning appears once it is set.
7. [ ] Pause the sim (Esc or the pause key): the clock in the title stops (it says "paused"), and ATC says nothing while paused. Unpause: it continues.
8. [ ] Push back, start the engines and taxi on the cleared route: no "off route" or "hold position" while you follow it. Stopping at the holding point gives the handoff to Tower.

## Push-to-talk

9. [ ] Hold the key passed with `--ptt` (Left Ctrl+T above) with the sim focused: "[ptt] DOWN (SimBinding)" appears, and "[ptt] UP (SimBinding)" when you let go.
10. [ ] Try a joystick button instead (restart with `--ptt "joystick:0:button:N"`, using N from Windows' game controller panel): same DOWN/UP lines.
11. [ ] Press the sim's ATC key (Scroll Lock by default, "ATC" in MSFS Controls): "[ptt] DOWN (AtcKey)"; press again: "[ptt] UP (AtcKey)". **The default ATC window does not open.**
12. [ ] Restart with `--no-atc-key`: Scroll Lock opens the default ATC window again and prints nothing.

## Reconnect

13. [ ] With the flight running, close MSFS (or end the flight to the main menu and quit): "[sim] connection lost: ATC paused, retrying every 5 s". Wait a minute; no readback-missed or conformance lines appear while it is down.
14. [ ] Start MSFS again and load the same flight: "[sim] connected" returns within about 5 s of the flight loading, and the clock continues from where it stopped (no jump of the minutes it was down).
15. [ ] If the aircraft is now somewhere else (for example the flight restarted at another gate), a "phase inferred" line appears after about 10 s, and ATC continues from that phase.
16. [ ] `Q` quits cleanly ("Stopped."), and the sim's own ATC key works normally again afterwards.
