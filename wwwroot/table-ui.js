function positionTooltipAt(tooltip, pointer) {
    if (!tooltip || !pointer) return;
    const view = window.visualViewport;
    const left = (view?.offsetLeft ?? 0) + 8;
    const top = (view?.offsetTop ?? 0) + 8;
    const right = left + (view?.width ?? document.documentElement.clientWidth) - 16;
    const bottom = top + (view?.height ?? document.documentElement.clientHeight) - 16;
    tooltip.style.maxWidth = `${Math.max(0, Math.min(400, right - left))}px`;
    tooltip.style.maxHeight = `${Math.max(0, Math.min(240, bottom - top))}px`;
    const rect = tooltip.getBoundingClientRect();
    let x = pointer.x + 14, y = pointer.y + 18;
    if (y + rect.height > bottom) y = pointer.y - rect.height - 12;
    tooltip.style.left = `${Math.max(left, Math.min(x, right - rect.width))}px`;
    tooltip.style.top = `${Math.max(top, Math.min(y, bottom - rect.height))}px`;
}

export function attachReferenceTooltip(root) {
    const abort = new AbortController();
    const signal = abort.signal;
    const tooltip = document.createElement('div');
    tooltip.className = 'ut-tooltip ut-reference-tooltip';
    tooltip.setAttribute('role', 'tooltip');
    tooltip.setAttribute('popover', 'manual');
    const text = document.createElement('div');
    text.className = 'ut-tooltip-text';
    const description = document.createElement('div');
    description.className = 'ut-tooltip-description';
    text.append(description);
    tooltip.append(text);
    root.append(tooltip);
    let active = null, pointer = null;
    const hide = () => {
        active = null;
        pointer = null;
        if (tooltip.matches(':popover-open')) tooltip.hidePopover();
    };
    const position = () => positionTooltipAt(tooltip, pointer);
    root.addEventListener('pointermove', e => {
        const item = e.target.closest?.('[data-ut-tooltip]');
        if (!item || !root.contains(item) || !item.dataset.utTooltip) { hide(); return; }
        active = item;
        pointer = { x: e.clientX, y: e.clientY };
        if (description.textContent !== item.dataset.utTooltip) description.textContent = item.dataset.utTooltip;
        if (!tooltip.matches(':popover-open')) tooltip.showPopover();
        position();
    }, { signal });
    root.addEventListener('pointerleave', hide, { signal });
    root.addEventListener('pointerdown', hide, { signal });
    root.addEventListener('keydown', hide, { signal });
    root.addEventListener('input', hide, { signal });
    root.addEventListener('focusout', hide, { signal });
    document.addEventListener('scroll', hide, { capture: true, signal });
    window.addEventListener('resize', position, { signal });
    window.visualViewport?.addEventListener('resize', position, { signal });
    window.visualViewport?.addEventListener('scroll', hide, { signal });
    const observer = new MutationObserver(() => {
        if (active && (!root.contains(active) || active.dataset.utTooltip !== description.textContent)) hide();
    });
    observer.observe(root, { subtree: true, childList: true, attributes: true, attributeFilter: ['data-ut-tooltip'] });
    const resize = new ResizeObserver(position);
    resize.observe(tooltip);
    return { dispose() { abort.abort(); observer.disconnect(); resize.disconnect(); hide(); tooltip.remove(); } };
}

export function attach(root, viewport, dotnet) {
    const abort = new AbortController();
    const signal = abort.signal;
    const syncChecks = () => document.querySelectorAll('input[data-indeterminate]').forEach(input => { input.indeterminate = input.dataset.indeterminate === 'true'; });
    const checksObserver = new MutationObserver(syncChecks);
    checksObserver.observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['data-indeterminate'] });
    let resize = null, dragged = null, locked = false, scroll = null, suppress = 0;
    let editorFrame = null;
    function stopEditorTracking() {
        if (editorFrame !== null) cancelAnimationFrame(editorFrame);
        editorFrame = null;
    }
    function trackEditor(host, id, column) {
        stopEditorTracking();
        // Position changes do not necessarily resize the cell (for example, a banner above the table).
        const update = () => {
            editorFrame = null;
            if (!locked || !root.isConnected || !host.isConnected) return;
            const rect = cellRect(root, id, column, false);
            const visibility = rect ? '' : 'hidden';
            if (host.style.visibility !== visibility) host.style.visibility = visibility;
            if (rect) {
                for (const [property, value] of [['left', rect[0]], ['top', rect[1]], ['width', rect[2]]]) {
                    const pixels = `${value}px`;
                    if (host.style[property] !== pixels) host.style[property] = pixels;
                }
            }
            editorFrame = requestAnimationFrame(update);
        };
        update();
    }
    // The editor is outside the table root; Escape must also work after focus is lost.
    document.addEventListener('keydown', e => {
        if (e.key !== 'Escape' || !locked || !root.isConnected || !root.getClientRects().length) return;
        e.preventDefault();
        e.stopImmediatePropagation();
        if (!e.repeat) dotnet.invokeMethodAsync('CancelCellEditingAsync').catch(console.error);
    }, { capture: true, signal });
    const style = document.createElement('style');
    document.head.appendChild(style);
    const scope = 'ut-' + crypto.randomUUID();
    root.dataset.utScope = scope;
    let menu = null;
    const hideMenu = () => {
        if (!menu) return;
        closeMenuBranches(menu);
        if (menu.matches(':popover-open')) menu.hidePopover();
    };
    root.utMenuOpened = host => { menu = host; };
    document.addEventListener('pointerdown', e => {
        if (!menu?.isConnected || !menu.matches(':popover-open') || menu.contains(e.target)) return;
        hideMenu();
        if (e.button !== 2 || !root.contains(e.target)) dotnet.invokeMethodAsync('CloseRowMenu');
    }, { capture: true, signal });
    document.addEventListener('contextmenu', e => {
        if (menu?.isConnected && menu.contains(e.target)) e.preventDefault();
        else if (menu?.matches(':popover-open') && !root.contains(e.target)) {
            hideMenu();
            dotnet.invokeMethodAsync('CloseRowMenu');
        }
    }, { signal });
    root.addEventListener('contextmenu', e => {
        e.preventDefault();
        hideMenu();
        const row=e.target.closest('[data-row]');
        const column=e.target.closest('[data-cell],[data-column],[data-width]');
        const group=e.target.closest('[data-group]');
        dotnet.invokeMethodAsync('OpenContextMenu',row?.dataset.row ?? null,
            group?.dataset.group ?? column?.dataset.cell ?? column?.dataset.column ?? column?.dataset.width ?? null,
            !!group,e.clientX,e.clientY);
    }, { signal });
    const q = id => CSS.escape(id);
    let pointer = null, tooltip = null;
    function positionTooltip() {
        positionTooltipAt(tooltip, pointer);
    }
    const tooltipResize = new ResizeObserver(positionTooltip);
    function updateTooltip() {
        positionFilter();
        const next = root.querySelector('.ut-tooltip:not(.ut-reference-tooltip)');
        if (next !== tooltip) {
            tooltipResize.disconnect();
            tooltip = next;
            if (tooltip) tooltipResize.observe(tooltip);
        }
        if (!tooltip || !pointer) return;
        if (!tooltip.matches(':popover-open')) tooltip.showPopover();
        positionTooltip();
    }
    root.addEventListener('pointermove', e => {
        pointer = { x: e.clientX, y: e.clientY };
        positionTooltip();
    }, { signal });
    root.addEventListener('pointerleave', () => {
        pointer = null;
        if (tooltip?.matches(':popover-open')) tooltip.hidePopover();
    }, { signal });
    window.addEventListener('resize', positionTooltip, { signal });
    window.visualViewport?.addEventListener('resize', positionTooltip, { signal });
    window.visualViewport?.addEventListener('scroll', positionTooltip, { signal });
    function positionFilter() {
        const panel = root.querySelector('.ut-filter');
        if (!panel) return;
        const anchor = panel.closest('.ut-filter-group')?.querySelector('.ut-buttons')
            ?? root.querySelector(`[data-filter-anchor="${q(panel.dataset.filterColumn)}"]`);
        if (!anchor) { if (panel.matches(':popover-open')) panel.hidePopover(); return; }
        if (!panel.matches(':popover-open')) panel.showPopover();
        const rect = anchor.getBoundingClientRect();
        const view = window.visualViewport;
        const left = (view?.offsetLeft ?? 0) + 8;
        const top = (view?.offsetTop ?? 0) + 8;
        const right = left + (view?.width ?? document.documentElement.clientWidth) - 16;
        const bottom = top + (view?.height ?? document.documentElement.clientHeight) - 16;
        const y = Math.max(top, Math.min(rect.bottom + 2, bottom));
        panel.style.left = `${Math.max(left, Math.min(rect.left, right - panel.offsetWidth))}px`;
        panel.style.top = `${y}px`;
        panel.style.maxHeight = `${Math.max(0, bottom - y)}px`;
    }
    document.addEventListener('scroll', positionFilter, { capture: true, signal });
    window.addEventListener('resize', positionFilter, { signal });
    window.visualViewport?.addEventListener('resize', positionFilter, { signal });
    window.visualViewport?.addEventListener('scroll', positionFilter, { signal });
    function clear() { style.textContent = ''; document.body.style.removeProperty('cursor'); document.body.style.removeProperty('user-select'); }
    root.addEventListener('pointerdown', e => {
        const handle = e.target.closest?.('[data-resize]');
        if (!handle || locked || e.button !== 0) return;
        e.preventDefault(); e.stopPropagation();
        const head = handle.closest('[data-column]');
        resize = { id: head.dataset.column, pointer: e.pointerId, x: e.clientX,
            width: head.getBoundingClientRect().width, min: Number(head.dataset.min),
            content: root.querySelector('.ut-content').getBoundingClientRect().width };
        document.body.style.cursor = 'col-resize'; document.body.style.userSelect = 'none';
    }, { signal });
    window.addEventListener('pointermove', e => {
        if (!resize || resize.pointer !== e.pointerId) return;
        const width = Math.max(resize.min, resize.width + e.clientX - resize.x);
        style.textContent = `[data-ut-scope="${scope}"] [data-column="${q(resize.id)}"],
          [data-ut-scope="${scope}"] [data-width="${q(resize.id)}"]{width:${width}px!important;min-width:${width}px!important;max-width:${width}px!important}
          [data-ut-scope="${scope}"] .ut-content{width:${resize.content + width - resize.width}px!important}`;
    }, { signal });
    async function finish(e) {
        if (!resize || resize.pointer !== e.pointerId) return;
        const current = resize; resize = null; suppress = performance.now() + 250;
        try {
            if (e.type !== 'pointercancel') await dotnet.invokeMethodAsync('ResizeColumn', current.id, Math.max(current.min, current.width + e.clientX - current.x));
        } catch (error) { console.error(error); } finally { clear(); }
    }
    window.addEventListener('pointerup', finish, { signal });
    window.addEventListener('pointercancel', finish, { signal });
    root.addEventListener('click', e => { if (performance.now() < suppress) { e.preventDefault(); e.stopPropagation(); } }, { capture:true, signal });
    root.addEventListener('dragstart', e => {
        const head = e.target.closest?.('[data-column]');
        if (!head || locked || resize) { e.preventDefault(); return; }
        dragged = head.dataset.column; e.dataTransfer.setData('text/plain', dragged); e.dataTransfer.effectAllowed = 'move';
    }, { signal });
    const clearDrop = () => root.querySelectorAll('.ut-drop-before,.ut-drop-after').forEach(head => head.classList.remove('ut-drop-before','ut-drop-after'));
    root.addEventListener('dragover', e => {
        clearDrop();
        const head = e.target.closest?.('[data-column]');
        if (!dragged || locked || !head || head.dataset.column === dragged) return;
        e.preventDefault(); e.dataTransfer.dropEffect = 'move';
        const rect = head.getBoundingClientRect();
        head.classList.add(e.clientX >= rect.left + rect.width / 2 ? 'ut-drop-after' : 'ut-drop-before');
    }, { signal });
    root.addEventListener('dragleave', e => { if (!e.target.closest?.('[data-column]')?.contains(e.relatedTarget)) clearDrop(); }, { signal });
    root.addEventListener('drop', e => {
        const head = e.target.closest?.('[data-column]');
        if (!head || !dragged || locked) return;
        e.preventDefault(); const from = dragged; dragged = null; suppress = performance.now()+250;
        const rect = head.getBoundingClientRect();
        clearDrop();
        dotnet.invokeMethodAsync('MoveColumn', from, head.dataset.column, e.clientX >= rect.left + rect.width / 2).catch(console.error);
    }, { signal });
    root.addEventListener('dragend', () => { dragged=null; clearDrop(); }, { signal });
    // Lock page scrolling too: a fixed editor must not outlive its cell position.
    document.addEventListener('wheel', e => { if(locked && !e.target.closest?.('.ut-reference-options')) e.preventDefault(); }, { passive:false, signal });
    document.addEventListener('touchmove', e => { if(locked && !e.target.closest?.('.ut-reference-options')) e.preventDefault(); }, { passive:false, signal });
    viewport.addEventListener('scroll', () => { if(locked && scroll) { viewport.scrollTop=scroll.top; viewport.scrollLeft=scroll.left; } }, { signal });
    return {
        tooltip: updateTooltip,
        trackEditor,
        lock(value) { locked=value; scroll=value ? {top:viewport.scrollTop,left:viewport.scrollLeft}:null; if (!value) stopEditorTracking(); },
        dispose() { stopEditorTracking(); hideMenu(); delete root.utMenuOpened; abort.abort(); checksObserver.disconnect(); tooltipResize.disconnect(); if (tooltip?.matches(':popover-open')) tooltip.hidePopover(); clear(); style.remove(); delete root.dataset.utScope; }
    };
}
export function cellRect(root, id, column, reveal = true) {
    const cell=root.querySelector(`[data-row="${CSS.escape(id)}"] [data-cell="${CSS.escape(column)}"]`);
    if(!cell || !cell.getClientRects().length) return null;
    // Reveal outer scroll containers first, then account for the sticky table header.
    if (reveal) cell.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' });
    const viewport = root.querySelector('.ut-viewport');
    const bounds = viewport.getBoundingClientRect();
    const header = root.querySelector('.ut-header').getBoundingClientRect();
    const view = window.visualViewport;
    const screenLeft = view?.offsetLeft ?? 0;
    const screenTop = view?.offsetTop ?? 0;
    let left = Math.max(bounds.left + viewport.clientLeft + 64, screenLeft);
    let right = Math.min(bounds.left + viewport.clientLeft + viewport.clientWidth,
        screenLeft + (view?.width ?? document.documentElement.clientWidth));
    let top = Math.max(bounds.top + viewport.clientTop, header.bottom, screenTop);
    let bottom = Math.min(bounds.top + viewport.clientTop + viewport.clientHeight,
        screenTop + (view?.height ?? document.documentElement.clientHeight));
    for (let parent = viewport.parentElement; parent; parent = parent.parentElement) {
        const css = getComputedStyle(parent), rect = parent.getBoundingClientRect();
        if (/(auto|scroll|hidden|clip)/.test(css.overflowX)) {
            left = Math.max(left, rect.left + parent.clientLeft);
            right = Math.min(right, rect.left + parent.clientLeft + parent.clientWidth);
        }
        if (/(auto|scroll|hidden|clip)/.test(css.overflowY)) {
            top = Math.max(top, rect.top + parent.clientTop);
            bottom = Math.min(bottom, rect.top + parent.clientTop + parent.clientHeight);
        }
    }
    if (right <= left || bottom <= top) return null;
    const before = cell.getBoundingClientRect();
    const dx = before.width > right - left || before.left < left ? before.left - left : Math.max(0, before.right - right);
    const dy = before.height > bottom - top || before.top < top ? before.top - top : Math.max(0, before.bottom - bottom);
    if (reveal) viewport.scrollBy({ left: dx, top: dy, behavior: 'instant' });
    const r=cell.getBoundingClientRect();
    if (!reveal) {
        left = Math.max(left, r.left); right = Math.min(right, r.right);
        top = Math.max(top, r.top); bottom = Math.min(bottom, r.bottom);
        if (right <= left || bottom <= top) return null;
        return [left, top, right - left, r.height];
    }
    return [Math.max(left,r.left),Math.max(top,r.top),Math.min(r.width,right-left),r.height];
}
export function focusEditor(host) {
    const input=host.querySelector('input:not([disabled]),select:not([disabled]),textarea:not([disabled])');
    input?.focus({preventScroll:true});
    if(input instanceof HTMLInputElement) { try { input.select(); } catch {} }
}
function closeMenuBranches(panel) {
    for (const child of panel.querySelectorAll('.ut-submenu')) {
        if (child.matches(':popover-open')) child.hidePopover();
        child.previousElementSibling?.setAttribute('aria-expanded', 'false');
    }
}
function positionMenu(panel, x, y) {
    const rect = panel.getBoundingClientRect();
    panel.style.left = `${Math.max(8, Math.min(x, document.documentElement.clientWidth - rect.width - 8))}px`;
    panel.style.top = `${Math.max(8, Math.min(y, document.documentElement.clientHeight - rect.height - 8))}px`;
}
export function showRowMenu(host, x, y, root) {
    const opening = !host.matches(':popover-open');
    if (!host.matches(':popover-open')) host.showPopover();
    root.utMenuOpened?.(host);
    positionMenu(host, x, y);
    if (opening) host.querySelector('button:not([disabled])')?.focus({ preventScroll: true });
    if (host.utMenuAttached) return;
    host.utMenuAttached = true;
    const openBranch = (button, focus = false) => {
        const parent = button.closest('.ut-row-menu');
        const branch = button.parentElement;
        const child = branch.matches('.ut-menu-branch') ? branch.querySelector(':scope > .ut-submenu') : null;
        for (const sibling of parent.querySelectorAll(':scope > .ut-menu-branch > .ut-submenu')) {
            if (sibling === child) continue;
            closeMenuBranches(sibling);
            if (sibling.matches(':popover-open')) sibling.hidePopover();
            sibling.previousElementSibling.setAttribute('aria-expanded', 'false');
        }
        if (!child || button.disabled) return;
        if (!child.matches(':popover-open')) child.showPopover();
        button.setAttribute('aria-expanded', 'true');
        const anchor = button.getBoundingClientRect();
        const bounds = parent.getBoundingClientRect();
        const width = child.getBoundingClientRect().width;
        positionMenu(child, bounds.right + width <= document.documentElement.clientWidth - 8 ? bounds.right : bounds.left - width, anchor.top);
        if (focus) child.querySelector('button:not([disabled])')?.focus({ preventScroll: true });
    };
    host.addEventListener('pointerover', e => {
        const button = e.target.closest('button');
        if (button && !button.contains(e.relatedTarget)) openBranch(button);
    });
    host.addEventListener('click', e => {
        const button = e.target.closest('.ut-menu-branch > button');
        if (button) openBranch(button, true);
    });
    host.addEventListener('scroll', e => {
        if (e.target.matches?.('.ut-row-menu')) closeMenuBranches(e.target);
    }, true);
    host.addEventListener('keydown', e => {
        const button = e.target.closest('button');
        const panel = e.target.closest('.ut-row-menu');
        if (!panel) return;
        if (e.key === 'ArrowRight' && button) {
            e.preventDefault(); openBranch(button, true);
        } else if ((e.key === 'ArrowLeft' || e.key === 'Escape') && panel !== host) {
            e.preventDefault(); e.stopPropagation();
            closeMenuBranches(panel); panel.hidePopover();
            panel.previousElementSibling.setAttribute('aria-expanded', 'false');
            panel.previousElementSibling.focus({ preventScroll: true });
        } else if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(e.key)) {
            e.preventDefault();
            const buttons = [...panel.querySelectorAll('button:not([disabled])')].filter(b => b.closest('.ut-row-menu') === panel);
            const index = buttons.indexOf(button);
            const next = e.key === 'Home' ? 0 : e.key === 'End' ? buttons.length - 1 : (index + (e.key === 'ArrowDown' ? 1 : -1) + buttons.length) % buttons.length;
            buttons[next]?.focus({ preventScroll: true });
        }
    });
}