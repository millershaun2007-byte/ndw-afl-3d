// 2026-08-29: lets the game relabel the single on-screen button per beat.
// One button, one gesture - only the word on it changes, so a child always
// knows what the tap does right now. No new controls, no new mis-tap risk.
mergeInto(LibraryManager.library, {
  SetTapLabel: function (ptr) {
    var s = UTF8ToString(ptr);
    var el = document.getElementById('tap-btn');
    if (el) el.textContent = s;
  }
});
