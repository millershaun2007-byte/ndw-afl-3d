mergeInto(LibraryManager.library, {
  SetTapLabel: function (ptr) {
    var s = UTF8ToString(ptr);
    var el = document.getElementById('tap-btn');
    if (el) el.textContent = s;
  }
});
