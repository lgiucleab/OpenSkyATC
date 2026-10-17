# Step 10 checklist (in-sim taxi arrows)

## One-time setup

The arrow has no textures, so the package needs no compiling. Assemble it and copy it into the Community folder (Microsoft Store version shown), then start the sim:

```
python toolbar\opensky-atc-taxi-arrows\tools\assemble_package.py --install "%LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\Packages\Community"
```

(The SDK's `fspackagetool.exe` also builds it, but it has to start the sim itself and waited forever with the Microsoft Store install.)

## Run

Start a flight at a gate, then:

```
dotnet run --project src/OpenSkyAtc.Recorder -- live --from LEMD --to LEBL --callsign IBE1234 --type A320 --cruise 330
```

Pick an airport and runway where the taxi route crosses a runway (the taxi clearance says "hold short runway ..."). The console prints `[arrows] showing N along the route` and `[arrows] none` as they come and go; `A` turns them on and off.

## Departure taxi with a runway crossing

1. [ ] After the taxi clearance is **sent** but before you read it back: no arrows.
2. [ ] Read it back correctly: blue arrows appear on the taxiway centreline ahead of the aircraft, pointing the way to go, about every 10 m for about 300 m.
3. [ ] Arrows lie flat on the ground (not floating or sunk), also where the taxiway slopes.
4. [ ] Taxiing: arrows behind the aircraft disappear and new ones appear ahead; the ones ahead do not jump around.
5. [ ] Approaching the runway to cross: the last arrow is on the hold-short line; nothing beyond it.
6. [ ] "Cross runway ..." is said within about 80 m of the hold-short; after you read it back, arrows continue across the runway.
7. [ ] At the holding point (within about 30 m of the end): all arrows are removed.
8. [ ] `A` during the taxi: arrows disappear at once; `A` again: they come back.

## Re-route

9. [ ] Leave the cleared route on purpose (turn onto another taxiway) until Ground says "hold position", then keep going until it gives a new taxi clearance. The old arrows disappear; after you read the new route back, new arrows follow it.

## Arrival taxi to a stand

10. [ ] After landing and vacating, call Ground (`CheckIn` or `RequestTaxi`): the taxi-in clearance names a stand (or "parking"). After the readback (or at once if there was nothing to read back), arrows lead to the stand.
11. [ ] Near the stand: all arrows are removed.

## Clean-up

12. [ ] During a taxi with arrows showing, close MSFS or end the flight: after starting again no arrows are left in the world.
13. [ ] During a taxi with arrows showing, press `Q` in the console: the arrows disappear.
14. [ ] Without the package installed, the console says once that taxi arrows are off because the sim has no "OpenSkyATC Taxi Arrow" object, and everything else keeps working.
