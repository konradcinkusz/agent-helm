// Local filesystem helpers (File System Access API).
window.helmFs = {
  supportsLocalPicker() {
    return typeof window.showDirectoryPicker === 'function';
  },
  async openLocalDirPicker() {
    try {
      const handle = await window.showDirectoryPicker({ mode: 'read' });
      return handle.name;
    } catch {
      return null; // cancelled or permission denied
    }
  }
};

// Minimal xterm.js host for AgentHelm. One xterm instance shows the active
// terminal; every call carries the terminal id so output that was still in
// flight from a previously shown terminal is dropped instead of misplaced.
// Input goes through the Blazor input box (there is no PTY server-side, so
// per-keystroke echo would be misleading). convertEol maps \n to \r\n.
window.helmTerm = {
  term: null,
  key: null,
  init(elementId, key) {
    if (this.term) { this.term.dispose(); this.term = null; }
    this.key = key;
    const host = document.getElementById(elementId);
    if (!host || typeof Terminal === "undefined") return;
    this.term = new Terminal({
      convertEol: true,
      fontSize: 13,
      fontFamily: '"Cascadia Code", "JetBrains Mono", Consolas, monospace',
      theme: { background: "#0d1220", foreground: "#e7ecf3", cursor: "#e0a458" }
    });
    this.term.open(host);
  },
  write(key, data) { if (this.term && key === this.key) this.term.write(data); },
  echoInput(key, line) {
    if (this.term && key === this.key) this.term.write("\x1b[38;2;224;164;88m$ " + line + "\x1b[0m\r\n");
  }
};
