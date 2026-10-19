// Tests for the panel's view logic: node toolbar/opensky-atc-toolbar/tools/test_panel.js
const assert = require("assert");
const path = require("path");
const View = require(path.join(__dirname, "..", "PackageSources", "html_ui", "InGamePanels", "OpenSkyATC", "OpenSkyATCView.js"));

const state = {
    version: 1, flight: true, sim: true, paused: false, status: "MSFS connected", ptt: "PTT ready", transmitting: false,
    callsign: "Iberia 1234", frequency: "121.630", com: "COM1", standby: "130.080", talkingTo: "Barajas Ground",
    hint: "Next: Barajas Tower 118.080", hintWarning: true,
    calls: [{ station: "A", text: "1" }, { station: "B", text: "2" }, { station: "Barajas Ground", text: "taxi via A" }],
    buttons: [
        { id: 0, key: "0:Readback", label: "Read back: runway 36L · 14 s", primary: true },
        { id: 2, key: "2:SayAgain", label: "Say again", primary: false },
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
    assert.strictEqual(v.live, false);
    assert.strictEqual(v.subtitle, View.NOT_CONNECTED);
    assert.ok(v.hint.length > 0);
    assert.deepStrictEqual(v.buttons, []);
    assert.strictEqual(v.action, null);
    assert.strictEqual(v.arrows, null);
});

test("fresh state: the radio head", () => {
    const v = View.build(state, 1000, 2000);
    assert.strictEqual(v.connected, true);
    assert.strictEqual(v.title, "IBERIA 1234");
    assert.strictEqual(v.subtitle, "MSFS connected");
    assert.strictEqual(v.com, "COM1");
    assert.strictEqual(v.frequency, "121.630");
    assert.strictEqual(v.standby, "130.080");
    assert.strictEqual(v.unit, "BARAJAS GROUND");
    assert.strictEqual(v.hintWarning, true);
});

test("silent for more than 12 s: not connected", () => {
    assert.strictEqual(View.build(state, 1000, 1000 + View.ALIVE_MS + 1).connected, false);
    assert.strictEqual(View.build(state, 1000, 1000 + View.ALIVE_MS).connected, true);
});

test("only the latest call is shown", () => {
    assert.deepStrictEqual(View.build(state, 1000, 1000).message, { who: "GROUND", text: "taxi via A" });
    assert.strictEqual(View.build(Object.assign({}, state, { calls: [] }), 1000, 1000).message, null);
});

test("the primary button is the large action, split into title, detail and timer", () => {
    const a = View.build(state, 1000, 1000).action;
    assert.deepStrictEqual([a.title, a.detail, a.timer], ["READ BACK", "runway 36L", "14 s"]);
    assert.deepStrictEqual(a.command, { cmd: "intent", id: 0, key: "0:Readback" });
    assert.deepStrictEqual(View.splitAction("Ready for departure"), { title: "Ready for departure", detail: "", timer: "" });
    assert.deepStrictEqual(View.splitAction("Read back: 124.230 · 3 s"), { title: "READ BACK", detail: "124.230", timer: "3 s" });
});

test("the other buttons follow; mayday is marked", () => {
    const v = View.build(state, 1000, 1000);
    assert.deepStrictEqual(v.buttons.map((b) => b.label), ["Say again", "Mayday"]);
    assert.deepStrictEqual(v.buttons[0].command, { cmd: "intent", id: 2, key: "2:SayAgain" });
    assert.deepStrictEqual(v.buttons.map((b) => b.danger), [false, true]);
});

test("no primary button: no action, every button a row", () => {
    const s = Object.assign({}, state, { buttons: state.buttons.slice(1) });
    const v = View.build(s, 1000, 1000);
    assert.strictEqual(v.action, null);
    assert.strictEqual(v.buttons.length, 2);
});

test("arrows toggle sends the opposite", () => {
    assert.deepStrictEqual(View.build(state, 1000, 1000).arrows.command, { cmd: "arrows", on: false });
    assert.deepStrictEqual(View.build(Object.assign({}, state, { arrows: false }), 1000, 1000).arrows.command, { cmd: "arrows", on: true });
    assert.strictEqual(View.build(Object.assign({}, state, { flight: false }), 1000, 1000).arrows, null);
});

test("an older app without com/standby still shows", () => {
    const s = Object.assign({}, state);
    delete s.com;
    delete s.standby;
    const v = View.build(s, 1000, 1000);
    assert.strictEqual(v.com, "COM1");
    assert.strictEqual(v.standby, "");
});

test("another protocol version: not connected", () => {
    assert.strictEqual(View.build(Object.assign({}, state, { version: 2 }), 1000, 1000).connected, false);
});

test("short station labels", () => {
    assert.strictEqual(View.shortStation("Barajas Ground"), "GROUND");
    assert.strictEqual(View.shortStation("Madrid Control"), "CONTROL");
    assert.strictEqual(View.shortStation(""), "ATC");
});

console.log(passed + " passed");
