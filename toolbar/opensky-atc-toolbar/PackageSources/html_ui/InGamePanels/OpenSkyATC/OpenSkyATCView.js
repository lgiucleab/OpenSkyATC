// OpenSkyATC toolbar panel: what to show, from the app's state (no DOM here, so it can be tested with Node:
// node toolbar/opensky-atc-toolbar/tools/test_panel.js). The app sends the state over the CommBus; see
// PanelState in src/OpenSkyAtc.Host/ToolbarPanel.cs.

var OpenSkyAtcView = (function () {
    // The app sends a state at least every 2 s (every 5 s while the sim is paused); after this much silence the
    // app is gone (closed, or no flight running).
    var ALIVE_MS = 12000;
    var NOT_CONNECTED = "Not connected";
    var NOT_CONNECTED_HINT = "Start a flight in the OpenSkyATC app.";

    function isAlive(lastStateMs, nowMs) {
        return lastStateMs !== null && nowMs - lastStateMs <= ALIVE_MS;
    }

    /// "Barajas Ground" -> "GROUND", "Madrid Control" -> "CONTROL": the short station label of a call.
    function shortStation(station) {
        var words = String(station || "").trim().split(/\s+/);
        return (words[words.length - 1] || "ATC").toUpperCase();
    }

    function hasCallsign(state) {
        return !!state.callsign && state.callsign !== "—";
    }

    /// The view: plain values the panel copies into the page.
    function build(state, lastStateMs, nowMs) {
        if (!state || !isAlive(lastStateMs, nowMs) || state.version !== 1) {
            return {
                connected: false, title: NOT_CONNECTED, subtitle: NOT_CONNECTED_HINT, ptt: "", transmitting: false,
                frequency: "", unit: "", hint: "", calls: [], buttons: [], voiceReadback: "", arrows: null,
            };
        }
        var calls = (state.calls || []).slice(-3).reverse();
        return {
            connected: !!state.sim,
            title: hasCallsign(state) ? state.callsign.toUpperCase() : "OpenSkyATC",
            subtitle: state.status || "",
            ptt: (state.ptt || "").toUpperCase(),
            transmitting: !!state.transmitting,
            frequency: state.frequency || "",
            unit: state.talkingTo || "",
            hint: state.hint || "",
            calls: calls.map(function (c, i) { return { who: shortStation(c.station), text: c.text, latest: i === 0 }; }),
            buttons: (state.buttons || []).map(function (b, i) {
                return {
                    number: String(i + 1), label: b.label, primary: !!b.primary,
                    danger: /^(MAYDAY|PAN)/i.test(String(b.key).split(":")[1] || ""),
                    command: { cmd: "intent", id: b.id, key: b.key },
                };
            }),
            voiceReadback: state.voiceReadback || "",
            arrows: state.flight ? { label: state.arrowsLabel, on: !!state.arrows, command: { cmd: "arrows", on: !state.arrows } } : null,
        };
    }

    return { build: build, isAlive: isAlive, shortStation: shortStation, ALIVE_MS: ALIVE_MS, NOT_CONNECTED: NOT_CONNECTED };
})();

if (typeof module !== "undefined") module.exports = OpenSkyAtcView;
