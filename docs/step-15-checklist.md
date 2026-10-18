# Step 15 checklist (in-sim toolbar panel)

Done when: the panel works in the sim and in VR.

## Install the panel (once, MSFS closed)

From the repository root:

```
python toolbar/opensky-atc-toolbar/tools/assemble_package.py --install "%LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\Packages\Community"
```

1. [ ] It prints "Installed to ...\Community\opensky-atc-toolbar".

## In the sim (desktop)

Start MSFS 2024 and load a flight at a gate. Do not start the app yet.

2. [ ] The toolbar has an OpenSkyATC button (a radio mast). If not: toolbar gear icon > enable OpenSkyATC.
3. [ ] Opening it shows a small dark panel with a red dot and "Not connected: start a flight in the OpenSkyATC app".
4. [ ] Start the app and Import plan. Within a few seconds the panel shows a green dot, your callsign and status, COM1 and who you talk to, and the buttons the app shows.
5. [ ] Press a button in the panel (Request clearance): ATC answers, the call appears in the panel's last calls and in the app's transcript.
6. [ ] The readback button in the panel (blue, with the countdown) is accepted like the app's.
7. [ ] The yellow box shows the next handoff or the frequency to tune, as in the app.
8. [ ] Hold push-to-talk: the panel shows "Transmitting" (or "Listening" with voice input) in red.
9. [ ] The Taxi arrows switch turns the arrows off and on during a taxi, and the app's checkbox follows it (and the other way round).
10. [ ] Hard mode on (app): the panel shows "Read back by voice: hold push-to-talk" instead of the readback button.
11. [ ] Pause the sim (Esc): nothing freezes; unpausing, the panel catches up within a few seconds.
12. [ ] Gear > End flight in the app (or close it): within about 12 s the panel says "Not connected".
13. [ ] App language Spanish: the panel's texts follow (the "Not connected" line stays English).
14. [ ] The panel can be moved, resized and undocked like the sim's own panels.

## In VR

15. [ ] The panel opens from the VR toolbar and is readable at its default size.
16. [ ] Buttons and the arrows switch work with the controller pointer.
