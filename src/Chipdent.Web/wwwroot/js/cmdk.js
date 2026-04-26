// Chipdent — command palette (⌘K / Ctrl+K)

(function () {
    "use strict";

    var palette = document.querySelector('[data-cmdk]');
    var trigger = document.querySelector('[data-search-trigger]');
    if (!palette) return;

    var input = palette.querySelector('input');
    var items = function () { return Array.prototype.slice.call(palette.querySelectorAll('.cmdk__item')); };

    function open() {
        palette.hidden = false;
        document.body.style.overflow = 'hidden';
        setTimeout(function () { input && input.focus(); input && input.select(); }, 30);
    }
    function close() {
        palette.hidden = true;
        document.body.style.overflow = '';
        if (input) input.value = '';
        items().forEach(function (i) { i.style.display = ''; });
    }
    function toggle() { palette.hidden ? open() : close(); }

    if (trigger) trigger.addEventListener('click', open);

    palette.addEventListener('click', function (e) {
        if (e.target === palette) close();
    });

    document.addEventListener('keydown', function (e) {
        var isMac = navigator.platform.toUpperCase().indexOf('MAC') >= 0;
        var modifier = isMac ? e.metaKey : e.ctrlKey;
        if (modifier && (e.key === 'k' || e.key === 'K')) {
            e.preventDefault();
            toggle();
            return;
        }
        if (palette.hidden) return;
        if (e.key === 'Escape') { e.preventDefault(); close(); }
    });

    if (input) {
        input.addEventListener('input', function () {
            var q = input.value.toLowerCase().trim();
            items().forEach(function (i) {
                var text = (i.textContent || '').toLowerCase();
                i.style.display = !q || text.indexOf(q) !== -1 ? '' : 'none';
            });
        });
    }

    // shortcut keys: navigate with arrow + enter
    palette.addEventListener('keydown', function (e) {
        if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp' && e.key !== 'Enter') return;
        var visible = items().filter(function (i) { return i.style.display !== 'none'; });
        if (!visible.length) return;
        var current = palette.querySelector('.cmdk__item.is-focused');
        var idx = visible.indexOf(current);
        if (e.key === 'ArrowDown') { e.preventDefault(); idx = (idx + 1) % visible.length; }
        else if (e.key === 'ArrowUp') { e.preventDefault(); idx = (idx - 1 + visible.length) % visible.length; }
        else if (e.key === 'Enter' && current) { current.click(); return; }
        visible.forEach(function (i) { i.classList.remove('is-focused'); });
        visible[idx].classList.add('is-focused');
        visible[idx].scrollIntoView({ block: 'nearest' });
    });
})();
