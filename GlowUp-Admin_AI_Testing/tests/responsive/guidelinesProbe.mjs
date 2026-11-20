// The New21 guideline probe, shared by guidelines.scenario.mjs (pages as they open) and guidelinesOpen.scenario.mjs
// (dropdowns, drawers, modals and the sign-up card). Runs inside the page: page.evaluate(probe).
export const probe = () => {
    const parse = (c) => {
        // color-mix() is reported as color(srgb r g b / a) with 0..1 channels (New23: hover fills were skipped before)
        const s = c.match(/color\(srgb ([^)]+)\)/);
        if (s) { const p = s[1].split(/[ /]+/).filter(Boolean).map(Number); return { r: p[0] * 255, g: p[1] * 255, b: p[2] * 255, a: p.length > 3 ? p[3] : 1 }; }
        const m = c.match(/rgba?\(([^)]+)\)/); if (!m) return null; const p = m[1].split(/[ ,/]+/).filter(Boolean).map(Number); return { r: p[0], g: p[1], b: p[2], a: p.length > 3 ? p[3] : 1 }; };
    const lum = ({ r, g, b }) => { const f = (v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; }; return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b); };
    const ratio = (x, y) => { const a = lum(x), b = lum(y); return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05); };
    const over = (top, under) => ({ r: top.r * top.a + under.r * (1 - top.a), g: top.g * top.a + under.g * (1 - top.a), b: top.b * top.a + under.b * (1 - top.a), a: 1 });
    const shown = (el) => { for (let n = el; n && n.nodeType === 1; n = n.parentElement) { const s = getComputedStyle(n); if (s.display === "none" || s.visibility === "hidden" || +s.opacity < 0.95) return false; } return true; };
    // The colour behind an element: blend the backgrounds up the tree; give up (null) over pictures and gradients.
    const behind = (el) => {
        const layers = [];
        for (let n = el; n && n.nodeType === 1; n = n.parentElement) {
            const s = getComputedStyle(n);
            if (s.backgroundImage && s.backgroundImage !== "none") return null;
            const c = parse(s.backgroundColor);
            if (c && c.a > 0) { layers.push(c); if (c.a >= 1) break; }
        }
        let base = { r: 255, g: 255, b: 255, a: 1 };
        const root = parse(getComputedStyle(document.body).backgroundColor); if (root && root.a >= 1) base = root;
        for (let i = layers.length - 1; i >= 0; i--) base = over(layers[i], base);
        return base;
    };
    const name = (el) => el.tagName.toLowerCase() + (el.id ? "#" + el.id : "") + (el.classList.length ? "." + [...el.classList].slice(0, 2).join(".") : "");
    const low = new Map(), small = new Map();
    const skip = ".page-skeleton, .brand-loader, .sk, .skeleton, [aria-hidden=true], [disabled], .disabled, script, style, svg, .tip-pop, .guide-pop";
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, { acceptNode: (t) => (t.nodeValue.trim() && t.parentElement && !t.parentElement.closest(skip) ? 1 : 2) });
    for (let t = walker.nextNode(); t; t = walker.nextNode()) {
        const el = t.parentElement;
        if (!shown(el) || el.closest("details:not([open])")) continue;
        const r = el.getBoundingClientRect(); if (r.width < 2 || r.height < 2) continue;
        const s = getComputedStyle(el), fg = parse(s.color), bg = behind(el);
        if (!fg || !bg) continue;
        const px = parseFloat(s.fontSize), large = px >= 24 || (px >= 18.66 && +s.fontWeight >= 700);
        const need = large ? 3 : 4.5, got = ratio(over(fg, bg), bg);
        if (got < need - 0.05) { const k = name(el); if (!low.has(k)) low.set(k, `${k} "${t.nodeValue.trim().slice(0, 28)}" ${got.toFixed(2)}:1 (need ${need}) ${s.color} on rgb(${Math.round(bg.r)},${Math.round(bg.g)},${Math.round(bg.b)})`); }
    }
    // Touch targets. A control counts as big enough when its box, or the label/row it sits in that also reacts, is 44 px.
    const controls = document.querySelectorAll("button, a[href], input:not([type=hidden]), select, textarea, summary, [role=button], [role=checkbox], [role=tab], [role=switch], [role=menuitem]");
    for (const el of controls) {
        if (!shown(el) || el.closest(skip + ", .modal-overlay:not(.open), [inert]")) continue;
        const r = el.getBoundingClientRect(); if (r.width < 2 || r.height < 2) continue;
        if (r.bottom < 0 || r.top > innerHeight * 3) continue;
        const s = getComputedStyle(el);
        if ((el.tagName === "A" || el.matches("button.text-link")) && (s.display === "inline" || s.display === "inline-block") && el.closest("p, li, small, .hint, .muted")) continue; // links inside a sentence (WCAG 2.5.8 exception), also the inline text buttons of the sign-up sentence
        const hit = el.closest("label") || el;
        const hr = hit.getBoundingClientRect();
        // an invisible touch area drawn by ::before (position: absolute; inset: -N px) counts too
        const pb = getComputedStyle(el, "::before"); let pw = 0, ph = 0;
        if (pb.content !== "none" && pb.position === "absolute") {
            pw = r.width - (parseFloat(pb.left) || 0) - (parseFloat(pb.right) || 0);
            ph = r.height - (parseFloat(pb.top) || 0) - (parseFloat(pb.bottom) || 0);
        }
        if (Math.max(r.width, hr.width, pw) < 43.5 || Math.max(r.height, hr.height, ph) < 43.5) { const k = name(el); if (!small.has(k)) small.set(k, `${k} ${Math.round(r.width)}x${Math.round(r.height)}`); }
    }
    return { low: [...low.values()].slice(0, 40), small: [...small.values()].slice(0, 60) };
};
