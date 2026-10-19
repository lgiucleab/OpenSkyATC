// OpenSkyATC toolbar panel: what to show, from the app's state (no DOM here, so it can be tested with Node:
// node toolbar/opensky-atc-toolbar/tools/test_panel.js). The app sends the state over the CommBus; see
// PanelState in src/OpenSkyAtc.Host/ToolbarPanel.cs. The look is a radio stack (owner's choice after the first
// flight, 2026-10-19): a radio head with the frequencies, a short chat of ATC's and the pilot's calls, and one
// large action.

var OpenSkyAtcView = (function () {
    // The app sends a state at least every 2 s (every 5 s while the sim is paused); after this much silence the
    // app is gone (closed, or no flight running).
    var ALIVE_MS = 12000;
    var NOT_CONNECTED = "Not connected";
    var NOT_CONNECTED_HINT = "Start a flight in the OpenSkyATC app.";

    // Calls in the chat: ATC's and the pilot's own (owner's request), oldest first, the newest at the bottom.
    var CHAT_CALLS = 6;

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

    /// "Read back: runway 36L · 14 s" -> title "READ BACK", detail "runway 36L", timer "14 s". A label without a
    /// colon is all title ("Ready for departure").
    function splitAction(label) {
        var text = String(label || "");
        var timer = "";
        var dot = text.lastIndexOf(" · ");
        if (dot >= 0 && /^\d+ s$/.test(text.slice(dot + 3))) {
            timer = text.slice(dot + 3);
            text = text.slice(0, dot);
        }
        var colon = text.indexOf(": ");
        if (colon < 0) return { title: text, detail: "", timer: timer };
        return { title: text.slice(0, colon).toUpperCase(), detail: text.slice(colon + 2), timer: timer };
    }

    function command(b) {
        return { cmd: "intent", id: b.id, key: b.key };
    }

    function isDanger(b) {
        return /^(MAYDAY|PAN)/i.test(String(b.key).split(":")[1] || "");
    }

    /// The view: plain values the panel copies into the page.
    function build(state, lastStateMs, nowMs) {
        if (!state || !isAlive(lastStateMs, nowMs) || state.version !== 1) {
            return {
                connected: false, live: false, title: "OpenSkyATC", subtitle: NOT_CONNECTED, ptt: "", transmitting: false,
                com: "COM1", frequency: "", standby: "", unit: "", hint: NOT_CONNECTED_HINT, hintWarning: false,
                chat: [], action: null, buttons: [], voiceReadback: "", arrows: null,
            };
        }
        var calls = (state.calls || []).slice(-CHAT_CALLS);
        var all = state.buttons || [];
        var primary = null;
        for (var i = 0; i < all.length; i++) if (all[i].primary) { primary = all[i]; break; }
        var action = primary ? splitAction(primary.label) : null;
        if (action) action.command = command(primary);
        return {
            connected: !!state.sim,
            live: true,
            title: hasCallsign(state) ? state.callsign.toUpperCase() : "OpenSkyATC",
            subtitle: state.status || "",
            ptt: (state.ptt || "").toUpperCase(),
            transmitting: !!state.transmitting,
            com: state.com || "COM1",
            frequency: state.frequency || "",
            standby: state.standby || "",
            unit: (state.talkingTo || "").toUpperCase(),
            hint: state.hint || "",
            hintWarning: !!state.hintWarning,
            chat: calls.map(function (c, i) {
                return { who: shortStation(c.station), text: c.text, pilot: !!c.pilot, latest: i === calls.length - 1 };
            }),
            action: action,
            buttons: all.filter(function (b) { return b !== primary; }).map(function (b) {
                return { label: b.label, danger: isDanger(b), command: command(b) };
            }),
            voiceReadback: state.voiceReadback || "",
            arrows: state.flight ? { label: state.arrowsLabel, on: !!state.arrows, command: { cmd: "arrows", on: !state.arrows } } : null,
        };
    }

    return { build: build, isAlive: isAlive, shortStation: shortStation, splitAction: splitAction, ALIVE_MS: ALIVE_MS, NOT_CONNECTED: NOT_CONNECTED, CHAT_CALLS: CHAT_CALLS };
})();

if (typeof module !== "undefined") module.exports = OpenSkyAtcView;
