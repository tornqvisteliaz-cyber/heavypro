// HeavyPro in-game panel. Talks to the HeavyPro app through L:vars:
//   app -> panel: L:HEAVYPRO_ALIVE (counter), _MASTER, _STATE, _PRESET, _INERTIA, _IN_P/_OUT_P/_IN_R/_OUT_R
//   panel -> app: L:HEAVYPRO_REQ_MASTER (1 off, 2 on), _REQ_PRESET (1-3), _REQ_INERTIA (value + 1); 0 = no request
// The app clears a request to 0 once it has applied it.
// Booted like other MSFS 2024 community panels: plain elements, init once the page has loaded.
(function () {
	const STATES = {
		0: ["OFFLINE", "HeavyPro is not connected to the sim yet."],
		1: ["PASS-THROUGH", "Your stick reaches the sim untouched."],
		2: ["SHAPING", "The bar is what the aircraft gets; the tick is your stick."],
		3: ["AP ON - PAUSED", "Autopilot is flying; HeavyPro waits."],
		4: ["SET UP STICK", "Pick your stick in the HeavyPro app."],
		5: ["UNBIND IN MSFS", "MSFS still sends your stick. Clear its axis bindings."],
		6: ["MOVE STICK", "Waiting for the stick."],
		7: ["FAULT", "Disconnect and connect HeavyPro again."]
	};

	function read(name) {
		try {
			return SimVar.GetSimVarValue("L:HEAVYPRO_" + name, "number") || 0;
		} catch (e) {
			return 0;
		}
	}

	function request(name, value) {
		try {
			SimVar.SetSimVarValue("L:HEAVYPRO_" + name, "number", value);
		} catch (e) {
		}
	}

	function init() {
		const root = document.querySelector("rotaidem-heavypro");
		const el = id => document.getElementById(id);
		let lastAlive = -1;
		let lastAliveChange = 0;

		el("hp-master").addEventListener("click", () => request("REQ_MASTER", read("MASTER") > 0.5 ? 1 : 2));
		document.querySelectorAll(".hp-preset").forEach(b => b.addEventListener("click", () =>
			request("REQ_PRESET", Number(b.getAttribute("data-preset")))));
		document.querySelectorAll(".hp-step").forEach(b => b.addEventListener("click", () => {
			const next = Math.max(0, Math.min(100, Math.round(read("INERTIA") + Number(b.getAttribute("data-step")))));
			request("REQ_INERTIA", next + 1);
		}));

		function bar(outId, inId, out, input) {
			const clamp = x => Math.max(-1, Math.min(1, x));
			const o = clamp(out);
			el(outId).style.left = (50 + Math.min(0, o) * 50) + "%";
			el(outId).style.width = (Math.abs(o) * 50) + "%";
			el(inId).style.left = (50 + clamp(input) * 50) + "%";
		}

		function update() {
			const alive = read("ALIVE");
			const now = Date.now();
			if (alive !== lastAlive) {
				lastAlive = alive;
				lastAliveChange = now;
			}
			// The app bumps ALIVE several times a second; if it stops, the app is closed.
			const running = alive > 0 && now - lastAliveChange < 2000;
			const state = running ? (STATES[Math.round(read("STATE"))] || STATES[0])
				: ["APP NOT RUNNING", "Start HeavyPro on your PC. This panel controls it."];
			const badge = el("hp-state");
			badge.textContent = state[0];
			badge.className = "hp-badge" + (state[0] === "SHAPING" ? " good" : state[0] === "UNBIND IN MSFS" || state[0] === "FAULT" ? " bad" : "");
			el("hp-hint").textContent = state[1];
			if (root)
				root.className = running ? "" : "offline";

			const masterOn = running && read("MASTER") > 0.5;
			el("hp-master").className = "hp-master" + (masterOn ? " on" : "");
			el("hp-master").textContent = masterOn ? "MASTER ON" : "MASTER OFF";

			const preset = running ? Math.round(read("PRESET")) : -1;
			document.querySelectorAll(".hp-preset").forEach(b =>
				b.className = "hp-preset" + (Number(b.getAttribute("data-preset")) === preset ? " on" : ""));

			const inertia = read("INERTIA");
			el("hp-inertia-value").textContent = running ? String(Math.round(inertia)) : "-";
			el("hp-inertia-fill").style.width = Math.max(0, Math.min(100, inertia)) + "%";

			bar("hp-out-p", "hp-in-p", read("OUT_P"), read("IN_P"));
			bar("hp-out-r", "hp-in-r", read("OUT_R"), read("IN_R"));
		}

		setInterval(update, 100);
		update();
	}

	if (document.readyState === "loading")
		document.addEventListener("DOMContentLoaded", init);
	else
		init();
})();
