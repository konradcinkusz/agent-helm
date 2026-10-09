// Dock helpers: the remembered open/tab state (localStorage, per browser) and
// the splitter drag. Storage can be blocked, so every access is guarded.
window.helmDock = {
  KEY: "agenthelm.dock",
  MIN_WIDTH: 300,
  read() {
    try { return JSON.parse(localStorage.getItem(this.KEY)) || {}; } catch { return {}; }
  },
  load() { return this.read(); },
  save(patch) {
    try { localStorage.setItem(this.KEY, JSON.stringify({ ...this.read(), ...patch })); } catch { }
  },
  attach(dock, splitter) {
    const clamp = (w) => Math.min(Math.max(w, this.MIN_WIDTH), Math.max(this.MIN_WIDTH, window.innerWidth - 480));
    const saved = this.read().width;
    if (saved) dock.style.setProperty("--dock-w", clamp(saved) + "px");
    splitter.addEventListener("pointerdown", (e) => {
      e.preventDefault();
      const startX = e.clientX;
      const startWidth = dock.getBoundingClientRect().width;
      let width = startWidth;
      const move = (ev) => {
        width = clamp(startWidth + (startX - ev.clientX));
        dock.style.setProperty("--dock-w", width + "px");
      };
      const up = () => {
        window.removeEventListener("pointermove", move);
        window.removeEventListener("pointerup", up);
        this.save({ width: Math.round(width) });
      };
      window.addEventListener("pointermove", move);
      window.addEventListener("pointerup", up);
    });
  }
};
