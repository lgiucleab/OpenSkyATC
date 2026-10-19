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
        this.actionCommand = null;
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
        const ids = ["osa-com", "osa-frequency", "osa-swap", "osa-standby", "osa-unit", "osa-ptt", "osa-title", "osa-dot", "osa-subtitle",
                     "osa-hint", "osa-message", "osa-who", "osa-said", "osa-action", "osa-action-title", "osa-action-detail",
                     "osa-action-timer", "osa-voice-readback", "osa-buttons", "osa-arrows"];
        const found = {};
        for (const id of ids) {
            found[id] = document.getElementById(id);
            if (!found[id]) return null;
        }
        found["osa-action"].addEventListener("click", () => { if (this.actionCommand) this.send(this.actionCommand); });
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

        // Radio head ("---.---" while there is no frequency, like a radio without power)
        el["osa-com"].textContent = v.com;
        el["osa-frequency"].textContent = v.frequency || "---.---";
        el["osa-standby"].textContent = v.standby;
        show(el["osa-swap"], !!v.standby);
        el["osa-unit"].textContent = v.unit;
        el["osa-ptt"].className = "osa-tx" + (v.transmitting ? " live" : "");
        show(el["osa-ptt"], v.live);
        el["osa-title"].textContent = v.title;
        el["osa-dot"].className = "osa-dot" + (v.connected ? " on" : "");
        el["osa-subtitle"].textContent = v.subtitle;

        el["osa-hint"].textContent = v.hint;
        el["osa-hint"].className = "osa-hint" + (v.hintWarning ? " warning" : "");
        show(el["osa-hint"], !!v.hint);

        show(el["osa-message"], !!v.message);
        el["osa-who"].textContent = v.message ? v.message.who : "";
        el["osa-said"].textContent = v.message ? v.message.text : "";

        this.actionCommand = v.action ? v.action.command : null;
        show(el["osa-action"], !!v.action);
        el["osa-action-title"].textContent = v.action ? v.action.title : "";
        el["osa-action-detail"].textContent = v.action ? v.action.detail : "";
        el["osa-action-timer"].textContent = v.action ? v.action.timer : "";

        el["osa-voice-readback"].textContent = v.voiceReadback;
        show(el["osa-voice-readback"], !!v.voiceReadback);

        const buttons = el["osa-buttons"];
        buttons.innerHTML = "";
        v.buttons.forEach((b) => {
            const row = add(buttons, "div", "osa-row" + (b.danger ? " danger" : ""), b.label);
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
