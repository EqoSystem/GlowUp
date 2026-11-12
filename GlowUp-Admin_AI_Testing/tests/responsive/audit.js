// Runs inside the page. Returns the layout faults it finds at the current window size.
window.__audit = function () {
    const vw = document.documentElement.clientWidth, vh = window.innerHeight;
    const out = { hscroll: [], panelOverlap: [], textOverlap: [], spill: [], clipped: [], offscreen: [] };
    const IGNORE = ".page-skeleton, .brand-loader, .tip-pop, .guide-pop, .toast, .toasts, [aria-hidden=true], .sk, .skeleton, script, style, svg, .nav-drop";
    const name = (el) => {
        let s = el.tagName.toLowerCase();
        if (el.id) s += "#" + el.id;
        if (el.classList.length) s += "." + Array.from(el.classList).slice(0, 2).join(".");
        const p = el.closest("[data-panel]");
        return (p ? "[" + p.dataset.panel + "] " : "") + s;
    };
    const cs = (el) => getComputedStyle(el);
    const shown = (el) => {
        for (let n = el; n && n.nodeType === 1; n = n.parentElement) {
            const s = cs(n);
            if (s.display === "none" || s.visibility === "hidden" || +s.opacity === 0) return false;
        }
        return true;
    };
    const se = document.scrollingElement;
    if (se.scrollWidth > vw + 1) out.hscroll.push("document " + se.scrollWidth + " > " + vw);
    const main = document.querySelector(".main-content");
    if (main && main.scrollWidth > main.clientWidth + 1) out.hscroll.push("main " + main.scrollWidth + " > " + main.clientWidth);
    const board = document.querySelector(".board");
    if (board && board.scrollWidth > board.clientWidth + 1) out.hscroll.push("board " + board.scrollWidth + " > " + board.clientWidth);

    // panels must not cover each other
    const panels = Array.from(document.querySelectorAll(".board > .panel")).filter(shown);
    const inter = (a, b) => Math.max(0, Math.min(a.right, b.right) - Math.max(a.left, b.left)) * Math.max(0, Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top));
    for (let i = 0; i < panels.length; i++) for (let j = i + 1; j < panels.length; j++) {
        const a = panels[i].getBoundingClientRect(), b = panels[j].getBoundingClientRect();
        const w = Math.min(a.right, b.right) - Math.max(a.left, b.left), h = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top);
        if (w > 2 && h > 2) out.panelOverlap.push(panels[i].dataset.panel + " x " + panels[j].dataset.panel + " (" + Math.round(w) + "x" + Math.round(h) + ")");
    }
    // a panel's content must stay inside the panel (unless the panel scrolls)
    panels.forEach((p) => {
        const inner = p.querySelector(":scope > .panel-inner") || p;
        const s = cs(inner);
        if (s.overflowY === "visible" && inner.scrollHeight > inner.clientHeight + 2 && cs(p).overflow === "visible") {
            const pr = p.getBoundingClientRect();
            const last = Array.from(inner.children).filter(shown).map((c) => c.getBoundingClientRect().bottom);
            const bottom = Math.max(...last, 0);
            if (bottom > pr.bottom + 2) out.spill.push(p.dataset.panel + ": content " + Math.round(bottom - pr.bottom) + "px below the panel");
        }
    });

    // text nodes: where each line is drawn, cut by every clipping box above it
    const clipRect = (el) => {
        let r = { left: -1e9, top: -1e9, right: 1e9, bottom: 1e9 };
        let deliberate = false, scroller = false;
        for (let n = el; n && n.nodeType === 1 && n !== document.documentElement; n = n.parentElement) {
            const s = cs(n);
            if (s.overflowX !== "visible" || s.overflowY !== "visible") {
                const b = n.getBoundingClientRect();
                r = { left: Math.max(r.left, b.left), top: Math.max(r.top, b.top), right: Math.min(r.right, b.right), bottom: Math.min(r.bottom, b.bottom) };
                if (s.textOverflow === "ellipsis" || s.webkitLineClamp !== "none" && s.webkitLineClamp) deliberate = true;
                if (/auto|scroll/.test(s.overflowX + s.overflowY)) scroller = true;
            }
        }
        return { r, deliberate, scroller };
    };
    const items = [];
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, {
        acceptNode: (t) => (t.nodeValue.trim() && t.parentElement && !t.parentElement.closest(IGNORE) ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_REJECT)
    });
    const visCache = new Map();
    for (let t = walker.nextNode(); t; t = walker.nextNode()) {
        const el = t.parentElement;
        if (!visCache.has(el)) visCache.set(el, shown(el));
        if (!visCache.get(el)) continue;
        { const folded = el.closest("details:not([open])"); if (folded && !el.closest("summary")) continue; }       // the inside of a closed <details> is not on the page
        if (el.closest(".modal-overlay") && cs(el.closest(".modal-overlay")).display === "none") continue;
        const range = document.createRange(); range.selectNodeContents(t);
        const rects = Array.from(range.getClientRects()).filter((q) => q.width > 1 && q.height > 1);
        if (!rects.length) continue;
        const { r: clip, deliberate, scroller } = clipRect(el);
        rects.forEach((q) => {
            const v = { left: Math.max(q.left, clip.left), top: Math.max(q.top, clip.top), right: Math.min(q.right, clip.right), bottom: Math.min(q.bottom, clip.bottom) };
            const visibleW = v.right - v.left, visibleH = v.bottom - v.top;
            if (visibleW <= 1 || visibleH <= 1) return;               // scrolled away or folded
            if (!deliberate && !scroller && (visibleW < q.width - 3 || visibleH < q.height - 3))
                out.clipped.push(name(el) + ' "' + t.nodeValue.trim().slice(0, 40) + '" ' + Math.round(visibleW) + "/" + Math.round(q.width));
            if (!scroller && (v.right > vw + 1 || v.left < -1)) out.offscreen.push(name(el) + ' "' + t.nodeValue.trim().slice(0, 40) + '" x ' + Math.round(v.left) + "-" + Math.round(v.right));
            items.push({ el, text: t.nodeValue.trim().slice(0, 30), r: v, fixed: !!el.closest(".topbar, .sidebar, .settings-window, .modal-overlay") });
        });
    }
    // two pieces of text drawn on top of each other
    const fixedLayer = (el) => el.closest(".topbar, .sidebar, .settings-window, .modal-overlay, .login-tools, .sw-head");
    // layers that cover the page ON PURPOSE: the desktop hover menu, the mobile drawer, the Quick
    // settings sheet under 1024 px (with its backdrop) and dialogs. Text under them is hidden by design, not overlapping.
    const root = document.documentElement, narrow = innerWidth < 1024;
    const overlayOf = (el) => {
        if (el.closest(".modal-overlay, dialog[open]")) return "modal";
        if (narrow && root.classList.contains("qs-open") && el.closest(".settings-window")) return "qs-sheet";
        if ((root.classList.contains("sb-hover") || root.classList.contains("sb-mobile-open")) && el.closest(".sidebar")) return "menu";
        return null;
    };
    for (let i = 0; i < items.length; i++) for (let j = i + 1; j < items.length; j++) {
        const a = items[i], b = items[j];
        if (a.el === b.el || a.el.contains(b.el) || b.el.contains(a.el)) continue;
        const w = Math.min(a.r.right, b.r.right) - Math.max(a.r.left, b.r.left), h = Math.min(a.r.bottom, b.r.bottom) - Math.max(a.r.top, b.r.top);
        if (w > 3 && h > 3) {
            // the sticky top bar may scroll over page content: that is not a fault
            if (!!fixedLayer(a.el) !== !!fixedLayer(b.el)) continue;
            const oa = overlayOf(a.el), ob = overlayOf(b.el);
            if (oa !== ob) continue;                  // one of them is on a layer that covers the other on purpose
            out.textOverlap.push(name(a.el) + ' "' + a.text + '" x ' + name(b.el) + ' "' + b.text + '"');
        }
    }
    if ((narrow && root.classList.contains("qs-open")) || root.classList.contains("sb-mobile-open")) out.clipped = [];
    for (const k in out) out[k] = Array.from(new Set(out[k])).slice(0, 12);
    return out;
};
