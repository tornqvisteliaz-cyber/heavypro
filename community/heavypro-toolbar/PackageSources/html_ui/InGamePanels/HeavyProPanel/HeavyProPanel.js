// HeavyPro toolbar panel. Talks to the HeavyPro Windows app only through L:vars:
//   app -> panel: L:HEAVYPRO_ALIVE, MASTER, FEEL (1-3), MODE (0 pass, 1 shaping, 2 no stick), RAW_X/Y, OUT_X/Y
//   panel -> app: L:HEAVYPRO_REQ_MASTER, L:HEAVYPRO_REQ_FEEL, then L:HEAVYPRO_REQ_SEQ (+1) to submit
const HP_MODES = ["PASS-THROUGH", "SHAPING", "NO STICK"];
const HP_PAD_CENTRE = 70;
const HP_PAD_REACH = 60;

function hpGet(name) {
    return SimVar.GetSimVarValue("L:HEAVYPRO_" + name, "number") || 0;
}

function hpSet(name, value) {
    return SimVar.SetSimVarValue("L:HEAVYPRO_" + name, "number", value);
}

class HeavyProPanel extends TemplateElement {
    constructor() {
        super(...arguments);
        this.timer = null;
        this.lastAlive = -1;
        this.lastAliveAt = 0;
        this.connected = false;
        this.master = false;
        this.feel = 0;
    }

    connectedCallback() {
        super.connectedCallback();
        this.ingameUi = this.querySelector("ingame-ui");
        this.el = {
            dot: document.getElementById("hp-dot"),
            status: document.getElementById("hp-status-text"),
            master: document.getElementById("hp-master"),
            mode: document.getElementById("hp-mode"),
            raw: document.getElementById("hp-raw"),
            out: document.getElementById("hp-out"),
            feel: Array.from(document.querySelectorAll(".hp-feel-btn"))
        };

        this.el.master.addEventListener("click", () => this.submit(!this.master, this.feel));
        this.el.feel.forEach((btn) =>
            btn.addEventListener("click", () => this.submit(this.master, parseInt(btn.dataset.feel, 10))));

        if (this.ingameUi) {
            this.ingameUi.addEventListener("panelActive", () => this.start());
            this.ingameUi.addEventListener("panelInactive", () => this.stop());
        }
    }

    disconnectedCallback() {
        this.stop();
        super.disconnectedCallback();
    }

    start() {
        if (!this.timer)
            this.timer = setInterval(() => this.refresh(), 100);
    }

    stop() {
        if (this.timer) {
            clearInterval(this.timer);
            this.timer = null;
        }
    }

    submit(master, feel) {
        if (!this.connected)
            return;
        const seq = (hpGet("REQ_SEQ") + 1) % 100000;
        hpSet("REQ_MASTER", master ? 1 : 0)
            .then(() => hpSet("REQ_FEEL", feel))
            .then(() => hpSet("REQ_SEQ", seq));
    }

    refresh() {
        const now = Date.now();
        const alive = hpGet("ALIVE");
        if (alive !== this.lastAlive) {
            this.lastAlive = alive;
            this.lastAliveAt = now;
        }
        this.connected = alive > 0 && now - this.lastAliveAt < 2000;

        this.el.dot.classList.toggle("ok", this.connected);
        this.el.status.textContent = this.connected ? "HeavyPro connected" : "Start the HeavyPro app";
        this.el.master.disabled = !this.connected;
        this.el.feel.forEach((btn) => (btn.disabled = !this.connected));
        if (!this.connected) {
            this.el.mode.textContent = "—";
            return;
        }

        this.master = hpGet("MASTER") >= 0.5;
        this.feel = Math.round(hpGet("FEEL"));
        this.el.master.textContent = this.master ? "MASTER ON" : "MASTER OFF";
        this.el.master.classList.toggle("on", this.master);
        this.el.feel.forEach((btn) => btn.classList.toggle("active", parseInt(btn.dataset.feel, 10) === this.feel));
        this.el.mode.textContent = HP_MODES[Math.round(hpGet("MODE"))] || "—";

        this.place(this.el.raw, hpGet("RAW_X"), hpGet("RAW_Y"), 9);
        this.place(this.el.out, hpGet("OUT_X"), hpGet("OUT_Y"), 6);
    }

    place(el, x, y, half) {
        el.style.left = (HP_PAD_CENTRE + x * HP_PAD_REACH - half) + "px";
        el.style.top = (HP_PAD_CENTRE + y * HP_PAD_REACH - half) + "px";
    }
}

window.customElements.define("heavypro-panel", HeavyProPanel);
checkAutoload();
