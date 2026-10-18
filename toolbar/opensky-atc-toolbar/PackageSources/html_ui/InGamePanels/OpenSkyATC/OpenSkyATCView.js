// OpenSkyATC toolbar panel: what to show, from the app's state (no DOM here, so it can be tested with Node:
// node toolbar/opensky-atc-toolbar/tools/test_panel.js). The app sends the state over the CommBus; see
// PanelState in src/OpenSkyAtc.Host/ToolbarPanel.cs.

var OpenSkyAtcView = (function () {
    // The app sends a state at least every 2 s (every 5 s while the sim is paused); after this much silence the
    // app is gone (closed, or no flight running).
    var ALIVE_MS = 12000;
    var NOT_CONNECTED = "Not connected: start a flight in the OpenSkyATC app";

    function isAlive(lastStateMs, nowMs) {
        return lastStateMs !== null && nowMs - lastStateMs <= ALIVE_MS;
    }

    /// The view: plain values the panel copies into the page.
    function build(state, lastStateMs, nowMs) {
        if (!state || !isAlive(lastStateMs, nowMs) || state.version !== 1) {
            return {
                connected: false, statusText: NOT_CONNECTED, ptt: "", transmitting: false,
                frequency: "", unit: "", hint: "", hintWarning: false, calls: [], buttons: [],
                voiceReadback: "", arrows: null,
            };
        }
        return {
            connected: !!state.sim,
            statusText: state.callsign && state.callsign !== "—" ? state.callsign + " · " + state.status : state.status,
            ptt: state.ptt || "",
            transmitting: !!state.transmitting,
            frequency: state.frequency || "",
            unit: state.talkingTo || "",
            hint: state.hint || "",
            hintWarning: !!state.hintWarning,
            calls: (state.calls || []).slice(-3).reverse(),
            buttons: (state.buttons || []).map(function (b) {
                return { label: b.label, primary: !!b.primary, danger: /^(MAYDAY|PAN)/i.test(b.key.split(":")[1] || ""), command: { cmd: "intent", id: b.id, key: b.key } };
            }),
            voiceReadback: state.voiceReadback || "",
            arrows: state.flight ? { label: state.arrowsLabel, on: !!state.arrows, command: { cmd: "arrows", on: !state.arrows } } : null,
        };
    }

    return { build: build, isAlive: isAlive, ALIVE_MS: ALIVE_MS, NOT_CONNECTED: NOT_CONNECTED };
})();

if (typeof module !== "undefined") module.exports = OpenSkyAtcView;
