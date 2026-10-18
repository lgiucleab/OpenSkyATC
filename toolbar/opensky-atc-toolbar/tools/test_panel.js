// Tests for the panel's view logic: node toolbar/opensky-atc-toolbar/tools/test_panel.js
const assert = require("assert");
const path = require("path");
const View = require(path.join(__dirname, "..", "PackageSources", "html_ui", "InGamePanels", "OpenSkyATC", "OpenSkyATCView.js"));

const state = {
    version: 1, flight: true, sim: true, paused: false, status: "MSFS connected", ptt: "PTT ready", transmitting: false,
    callsign: "Iberia 1234", frequency: "121.630", talkingTo: "Barajas Ground", hint: "Next: Barajas Tower 118.080", hintWarning: true,
    calls: [{ station: "A", text: "1" }, { station: "B", text: "2" }, { station: "C", text: "3" }, { station: "D", text: "4" }],
    buttons: [
        { id: 0, key: "0:Readback", label: "Read back: 118.080", primary: true },
        { id: 3, key: "3:Mayday", label: "Mayday", primary: false },
    ],
    arrows: true, arrowsLabel: "Taxi arrows",
};

let passed = 0;
function test(name, fn) {
    fn();
    passed++;
    console.log("ok  " + name);
}

test("no state: not connected", () => {
    const v = View.build(null, null, 1000);
    assert.strictEqual(v.connected, false);
    assert.strictEqual(v.statusText, View.NOT_CONNECTED);
    assert.deepStrictEqual(v.buttons, []);
    assert.strictEqual(v.arrows, null);
});

test("fresh state: shown", () => {
    const v = View.build(state, 1000, 2000);
    assert.strictEqual(v.connected, true);
    assert.strictEqual(v.statusText, "Iberia 1234 · MSFS connected");
    assert.strictEqual(v.frequency, "121.630");
    assert.strictEqual(v.unit, "Barajas Ground");
});

test("silent for more than 12 s: not connected", () => {
    assert.strictEqual(View.build(state, 1000, 1000 + View.ALIVE_MS + 1).connected, false);
    assert.strictEqual(View.build(state, 1000, 1000 + View.ALIVE_MS).connected, true);
});

test("last three calls, newest first", () => {
    const v = View.build(state, 1000, 1000);
    assert.deepStrictEqual(v.calls.map((c) => c.station), ["D", "C", "B"]);
});

test("buttons carry their command; mayday is marked", () => {
    const v = View.build(state, 1000, 1000);
    assert.deepStrictEqual(v.buttons[0].command, { cmd: "intent", id: 0, key: "0:Readback" });
    assert.strictEqual(v.buttons[0].primary, true);
    assert.strictEqual(v.buttons[1].danger, true);
    assert.strictEqual(v.buttons[0].danger, false);
});

test("arrows toggle sends the opposite", () => {
    assert.deepStrictEqual(View.build(state, 1000, 1000).arrows.command, { cmd: "arrows", on: false });
    assert.deepStrictEqual(View.build(Object.assign({}, state, { arrows: false }), 1000, 1000).arrows.command, { cmd: "arrows", on: true });
    assert.strictEqual(View.build(Object.assign({}, state, { flight: false }), 1000, 1000).arrows, null);
});

test("another protocol version: not connected", () => {
    assert.strictEqual(View.build(Object.assign({}, state, { version: 2 }), 1000, 1000).connected, false);
});

console.log(passed + " passed");
