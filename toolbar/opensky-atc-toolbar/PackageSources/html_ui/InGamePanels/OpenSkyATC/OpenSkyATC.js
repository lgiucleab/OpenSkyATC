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
        this.el = null;
    }

    connectedCallback() {
        super.connectedCallback();
        this.ingameUi = this.querySelector("ingame-ui");
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
        const ids = ["osa-dot", "osa-title", "osa-subtitle", "osa-ptt", "osa-radio", "osa-frequency", "osa-unit", "osa-hint",
                     "osa-calls", "osa-voice-readback", "osa-buttons", "osa-arrows"];
        const found = {};
        for (const id of ids) {
            found[id] = document.getElementById(id);
            if (!found[id]) return null;
        }
        this.el = found;
        return found;
    }

    static show(element, visible) {
        element.style.display = visible ? "" : "none";
    }

    static add(parent, tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        parent.appendChild(node);
        return node;
    }

    render() {
        const el = this.elements();
        if (!el) return;
        const v = OpenSkyAtcView.build(this.state, this.lastStateMs, Date.now());
        const add = OpenSkyAtcPanel.add, show = OpenSkyAtcPanel.show;

        el["osa-dot"].className = "osa-dot" + (v.connected ? " on" : "");
        el["osa-title"].textContent = v.title;
        el["osa-subtitle"].textContent = v.subtitle;
        el["osa-ptt"].textContent = v.ptt;
        el["osa-ptt"].className = "osa-ptt" + (v.transmitting ? " live" : "");

        show(el["osa-radio"], !!v.frequency);
        el["osa-frequency"].textContent = v.frequency;
        el["osa-unit"].textContent = v.unit;
        show(el["osa-unit"], !!v.unit);
        el["osa-hint"].textContent = v.hint;
        show(el["osa-hint"], !!v.hint);

        const calls = el["osa-calls"];
        calls.innerHTML = "";
        v.calls.forEach((c) => {
            const line = add(calls, "div", "osa-call" + (c.latest ? " latest" : ""));
            add(line, "span", "osa-who", c.who);
            add(line, "span", "osa-said", c.text);
        });
        show(calls, v.calls.length > 0);

        el["osa-voice-readback"].textContent = v.voiceReadback;
        show(el["osa-voice-readback"], !!v.voiceReadback);

        const buttons = el["osa-buttons"];
        buttons.innerHTML = "";
        v.buttons.forEach((b) => {
            const row = add(buttons, "div", "osa-row" + (b.primary ? " primary" : "") + (b.danger ? " danger" : ""));
            add(row, "span", "osa-num", b.number);
            add(row, "span", "osa-text", b.label);
            row.addEventListener("click", () => this.send(b.command));
        });
        show(buttons, v.buttons.length > 0);

        const arrows = el["osa-arrows"];
        arrows.innerHTML = "";
        if (v.arrows) {
            add(arrows, "span", "", v.arrows.label);
            add(arrows, "span", "osa-state" + (v.arrows.on ? " on" : ""), v.arrows.on ? "ON" : "OFF");
            const command = v.arrows.command;
            arrows.onclick = () => this.send(command);
        } else {
            arrows.onclick = null;
        }
        show(arrows, !!v.arrows);
    }
}

window.customElements.define("opensky-atc-panel", OpenSkyAtcPanel);
checkAutoload();
