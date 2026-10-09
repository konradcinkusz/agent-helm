// Chat transcript auto-scroll. The view follows new messages while the reader is
// at the bottom; scrolling up detaches it until they return to the bottom.
window.helmChat = {
  follow(el) {
    if (!el) return;
    if (!el.helmBound) {
      el.helmBound = true;
      el.helmPinned = true;
      el.addEventListener("scroll", () => {
        el.helmPinned = el.scrollHeight - el.scrollTop - el.clientHeight < 48;
      });
    }
    if (el.helmPinned) el.scrollTop = el.scrollHeight;
  }
};
