# Step 14 checklist (voice in: push-to-talk, Whisper, intent parser)

Done when: the parser tests pass on recorded ATC phrases (yours and the Piper ones), and voice works in a live flight.

## 1. Record the phrases (about 10 minutes, once)

From the repository root, with your headset microphone:

```
dotnet run --project src/OpenSkyAtc.Recorder -- stt record
```

For each of the 31 lines: press Enter, say the line as you would on the radio, press Enter. `s` skips a line, `q` stops (run it again to continue where you left off; `--redo` records everything again, `--only ID` one line, `--mic N` another microphone). The files go to `tests/fixtures/speech/owner/` and stay on your PC (git-ignored).

1. [ ] All 31 lines recorded; no "very quiet" warning.
2. [ ] `dotnet test` passes, including `Whisper_OnTheOwnersRecordings` (at least 80% of your calls understood). Its output lists each call with what Whisper heard: send me the failures.

## 2. Benchmark with MSFS running (the default model)

Start a flight in MSFS 2024 (in the cockpit, not paused), then in a terminal:

```
dotnet run --project src/OpenSkyAtc.Recorder -- stt bench --models tiny.en,base.en,small.en --set both
```

3. [ ] Send me the table. Without the sim (16 cores, CPU only) base.en took about 1.3 s a call and small.en about 4.2 s; the default stays base.en unless your numbers with the sim say otherwise.
4. [ ] While it runs, the sim's frame rate drops only a little (note roughly how much).

## 3. Microphone check

```
dotnet run --project src/OpenSkyAtc.Recorder -- stt listen
```

5. [ ] Enter, say "Madrid Ground, Iberia 1234, request taxi", Enter: it shows what was heard and `RequestTaxi`.

## 4. Live flight with voice

App > Settings > Push-to-talk: set "Keyboard key in any window" (for example `F12`, or keep your sim binding / the ATC key). Settings > Speech recognition: "Talk to ATC with your voice" on, your microphone, base.en. Save, then Import plan.

6. [ ] The status shows "Loading speech recognition" once (the first time it downloads the model, about 140 MB), then "MSFS connected".
7. [ ] Holding push-to-talk shows "Listening"; letting go shows "Understanding" for about a second, then the transcript shows `Heard: "..."` and your call.
8. [ ] The keyboard key works while MSFS has focus and while the app has focus; the sim key or joystick button and the ATC key also work.
9. [ ] Say "request IFR clearance, information ..." to Delivery: the clearance comes. Read it back by voice: "readback correct". Read it back with a wrong squawk: ATC corrects it.
10. [ ] Say something that is not a radio call ("testing one two"): ATC answers "say again".
11. [ ] Push and start, taxi (readback with the holding point and runway), ready for departure, takeoff readback, contact departure readback and check-in, all by voice. Buttons still work alongside.
12. [ ] A few calls in cruise (request level, request direct) and the arrival (readbacks, "runway vacated").
13. [ ] ATC never talks while you hold push-to-talk.
