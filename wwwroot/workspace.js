export function attach(host) {
    let active = null;
    let returnFocus = null;
    let disposed = false;
    const abort = new AbortController();
    const visible = element => element instanceof HTMLElement && element.getClientRects().length > 0;
    // Sibling layers share a z-index: the last window is painted above its parents.
    // Parents remain visible, but only the active layer participates in keyboard navigation.
    function current() { return host.querySelector(':scope > .ut5-layer[data-active="true"] > .ut5-dialog'); }
    function focusable(dialog) {
        return [...dialog.querySelectorAll('button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),a[href],[tabindex="0"]')].filter(visible);
    }
    function sync() {
        if (disposed) return;
        const next = current();
        if (next === active) return;
        if (!active && next) returnFocus = document.activeElement;
        active = next;
        if (next) next.focus({preventScroll:true});
        else if (returnFocus?.isConnected) { returnFocus.focus({preventScroll:true}); returnFocus = null; }
    }
    document.addEventListener('keydown', event => {
        const dialog = current();
        if (!dialog || event.key !== 'Tab') return;
        // Autocomplete popovers are rendered outside the dialog; do not steal their keyboard interaction.
        if (event.target instanceof Element && event.target.closest('.mud-popover')) return;
        const fields = focusable(dialog);
        if (!fields.length) { event.preventDefault(); dialog.focus(); return; }
        const first = fields[0], last = fields[fields.length - 1];
        if (event.shiftKey && (document.activeElement === first || document.activeElement === dialog)) {
            event.preventDefault(); last.focus();
        } else if (!event.shiftKey && (document.activeElement === last || !dialog.contains(document.activeElement))) {
            event.preventDefault(); first.focus();
        }
    }, {signal:abort.signal});
    return { sync, dispose() { disposed = true; abort.abort(); if (returnFocus?.isConnected) returnFocus.focus({preventScroll:true}); } };
}