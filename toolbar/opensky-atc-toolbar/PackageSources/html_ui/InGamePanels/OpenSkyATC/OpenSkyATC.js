// OpenSkyATC toolbar panel (step 15): shows the app's state and sends button presses back, both over the
// MSFS 2024 CommBus ("OpenSkyATC.State" from the app, "OpenSkyATC.Command" to it). All text from the app is
// set with textContent, never as HTML.

class OpenSkyAtcPanel extends TemplateElement {
    constructor() {
        super(...arguments);
        this.listener = null;
        this.state = null;
        this.lastStateMs = null;
        this.timer = null;
    }

    connectedCallback() {
        super.connectedCallback();
        this.ingameUi = this.querySelector("ingame-ui");
        this.el = null;

        this.listener = RegisterCommBusListener(() => this.send({ cmd: "hello" }));
        this.listener.on("OpenSkyATC.State", (json) => this.onState(json));
        if (this.ingameUi) {
            this.ingameUi.addEventListener("panelActive", () => this.send({ cmd: "hello" }));
        }
        this.timer = setInterval(() => this.render(), 1000);
        this.render();
    }

    disconnectedCallback() {
        super.disconnectedCallback();
        if (this.timer) clearInterval(this.timer);
        if (this.listener) this.listener.unregister();
    }

    send(command) {
        if (this.listener) this.listener.callSimConnect("OpenSkyATC.Command", JSON.stringify(command));
    }

    onState(json) {
        try {
            this.state = JSON.parse(json);
            this.lastStateMs = Date.now();
        } catch (e) {
            return;
        }
        this.render();
    }

    /// The panel's elements, once the page has them (the element can connect before its children are parsed).
    elements() {
        if (this.el) return this.el;
        const ids = ["osa-dot", "osa-status-text", "osa-ptt", "osa-frequency", "osa-unit", "osa-hint", "osa-calls",
                     "osa-voice-readback", "osa-buttons", "osa-arrows", "osa-radio"];
        const found = {};
        for (const id of ids) {
            found[id] = document.getElementById(id);
            if (!found[id]) return null;
        }
        this.el = found;
        return found;
    }

    render() {
        if (!this.elements()) return;
        const v = OpenSkyAtcView.build(this.state, this.lastStateMs, Date.now());
        this.el["osa-dot"].className = "osa-dot" + (v.connected ? " on" : "");
        this.el["osa-status-text"].textContent = v.statusText;
        this.el["osa-ptt"].textContent = v.ptt;
        this.el["osa-ptt"].className = "osa-ptt" + (v.transmitting ? " live" : "");
        this.el["osa-frequency"].textContent = v.frequency;
        this.el["osa-unit"].textContent = v.unit;
        this.el["osa-radio"].style.display = v.frequency ? "" : "none";
        this.el["osa-hint"].textContent = v.hint;
        this.el["osa-hint"].className = "osa-hint" + (v.hintWarning ? " warn" : "");
        this.el["osa-hint"].style.display = v.hint ? "" : "none";
        this.el["osa-voice-readback"].textContent = v.voiceReadback;
        this.el["osa-voice-readback"].style.display = v.voiceReadback ? "" : "none";

        const calls = this.el["osa-calls"];
        calls.innerHTML = "";
        v.calls.forEach((c) => {
            const line = document.createElement("div");
            line.className = "osa-call";
            const who = document.createElement("span");
            who.className = "osa-who";
            who.textContent = c.station;
            const text = document.createElement("span");
            text.textContent = " " + c.text;
            line.appendChild(who);
            line.appendChild(text);
            calls.appendChild(line);
        });
        calls.style.display = v.calls.length ? "" : "none";

        const buttons = this.el["osa-buttons"];
        buttons.innerHTML = "";
        v.buttons.forEach((b) => {
            const button = document.createElement("div");
            button.className = "osa-button" + (b.primary ? " primary" : "") + (b.danger ? " danger" : "");
            button.textContent = b.label;
            button.addEventListener("click", () => this.send(b.command));
            buttons.appendChild(button);
        });

        const arrows = this.el["osa-arrows"];
        arrows.innerHTML = "";
        if (v.arrows) {
            const toggle = document.createElement("div");
            toggle.className = "osa-switch" + (v.arrows.on ? " on" : "");
            const label = document.createElement("span");
            label.textContent = v.arrows.label;
            arrows.appendChild(toggle);
            arrows.appendChild(label);
            const command = v.arrows.command;
            arrows.onclick = () => this.send(command);
            arrows.style.display = "";
        } else {
            arrows.onclick = null;
            arrows.style.display = "none";
        }
    }
}

window.customElements.define("opensky-atc-panel", OpenSkyAtcPanel);
checkAutoload();
