














export const ANIMATIONS = [
    { id: "", label: "Rise (default)", hint: "Comes up a little and fades in." },
    { id: "none", label: "None", hint: "Is simply there." },
    { id: "fade", label: "Fade", hint: "Fades in where it is." },
    { id: "zoom", label: "Zoom", hint: "Grows from a little smaller." },
    { id: "pop", label: "Pop", hint: "Grows, overshoots a touch, settles." },
    { id: "left", label: "Slide from the left", hint: "Comes in from the left." },
    { id: "right", label: "Slide from the right", hint: "Comes in from the right." },
    { id: "drop", label: "Drop", hint: "Comes down from above." },
    { id: "flip", label: "Flip", hint: "Tilts forward into place." },
    { id: "blur", label: "Focus", hint: "Comes into focus from a blur." }
];

export const ANIMATION_FRAMES = {
    "": [{ opacity: 0, transform: "translateY(10px)" }, { opacity: 1, transform: "none" }],
    fade: [{ opacity: 0 }, { opacity: 1 }],
    zoom: [{ opacity: 0, transform: "scale(.92)" }, { opacity: 1, transform: "none" }],
    pop: [{ opacity: 0, transform: "scale(.9)" }, { opacity: 1, transform: "scale(1.03)", offset: 0.7 }, { opacity: 1, transform: "none" }],
    left: [{ opacity: 0, transform: "translateX(-26px)" }, { opacity: 1, transform: "none" }],
    right: [{ opacity: 0, transform: "translateX(26px)" }, { opacity: 1, transform: "none" }],
    drop: [{ opacity: 0, transform: "translateY(-18px)" }, { opacity: 1, transform: "none" }],
    flip: [{ opacity: 0, transform: "perspective(900px) rotateX(-14deg)" }, { opacity: 1, transform: "none" }],
    blur: [{ opacity: 0, filter: "blur(6px)" }, { opacity: 1, filter: "none" }]
};
export const TRANSITIONS = [
    { id: "", label: "Standard (default)", hint: "The site's own speed." },
    { id: "quick", label: "Quick", hint: "Hover and press changes are almost instant." },
    { id: "slow", label: "Slow", hint: "Changes take half a second." },
    { id: "springy", label: "Springy", hint: "Changes overshoot a little and settle." },
    { id: "none", label: "None", hint: "Changes happen at once, with no movement." }
];
export const EDGES = [
    { id: "", label: "Under the pointer (default)", hint: "The edge light shows while the pointer is over the panel." },
    { id: "always", label: "Always, moving", hint: "A light travels round the edge all the time." },
    { id: "off", label: "Off", hint: "No edge light." }
];

export const BUTTON_FX = [
    { id: "", label: "Glow ring (default)", hint: "A pressed button sends out one soft ring of light." },
    { id: "pixel", label: "Pixel sweep", hint: "A pressed button gets a band of small squares across it, with the ring." },
    { id: "none", label: "Plain", hint: "A pressed button only moves a little: no ring, no squares." }
];

export const SHADOWS = [
    { id: "", label: "Page default", hint: "The page's own shadow." },
    { id: "none", label: "None", hint: "No shadow." },
    { id: "soft", label: "Soft", hint: "A small, light shadow." },
    { id: "medium", label: "Medium", hint: "A clear shadow." },
    { id: "strong", label: "Strong", hint: "A deep, wide shadow." },
    { id: "glow", label: "Glow", hint: "A shadow that spreads evenly on all sides, like a glow (the accent colour unless a shadow colour is set)." }
];

export function shadowCss(id, colour) {
    const c = hex(colour);
    const mix = (pct, base) => `color-mix(in srgb, ${c || base} ${pct}%, transparent)`;
    switch (id) {
        case "none": return "none";
        case "soft": return `0 2px 8px ${mix(16, "#000000")}`;
        case "medium": return `0 6px 18px ${mix(26, "#000000")}`;
        case "strong": return `0 12px 32px ${mix(38, "#000000")}`;
        case "glow": return `0 0 20px ${mix(50, "var(--accent)")}`;
        default: return "";
    }
}
export const ORIENTS = [
    { id: "", label: "Automatic (default)", hint: "As the page sets it: side by side when there is room." },
    { id: "row", label: "Side by side", hint: "The cards stand in one row. A narrow panel puts them two by two." },
    { id: "column", label: "One under the other", hint: "Each card takes the whole width." }
];
export const NOTE_PLACES = [
    { id: "top", label: "At the top, under the title" },
    { id: "bottom", label: "At the bottom of the panel" }
];
export const LIMITS = { title: 40, subtitle: 120, note: 300, label: 40, labels: 12, scaleMin: 0.85, scaleMax: 1.25, radiusMax: 24 };

const ids = (list) => list.map((item) => item.id);
const oneOf = (value, list) => (ids(list).includes(String(value == null ? "" : value)) ? String(value == null ? "" : value) : "");
const hex = (value) => (/^#[0-9a-f]{6}$/i.test(String(value || "")) ? String(value).toLowerCase() : "");

export function plainText(value, most, lines = false) {
    let text = String(value == null ? "" : value).replace(/[\u0000-\u0009\u000b-\u001f\u007f]/g, " ");
    text = lines ? text.split(/\r?\n/).map((line) => line.replace(/\s+/g, " ").trim()).filter(Boolean).slice(0, 5).join("\n")
        : text.replace(/\s+/g, " ").trim();
    return text.slice(0, most);
}
const COLOUR_KEYS = ["bg", "card", "text", "accent", "border", "shadow"];


export function cleanStyle(raw, fonts = []) {
    const s = raw && typeof raw === "object" ? raw : {};
    const out = {};
    const put = (key, value) => { if (value !== "" && value !== undefined) out[key] = value; };
    put("anim", oneOf(s.anim, ANIMATIONS));
    put("trans", oneOf(s.trans, TRANSITIONS));
    put("edge", oneOf(s.edge, EDGES));
    put("orient", oneOf(s.orient, ORIENTS));
    put("btn", oneOf(s.btn, BUTTON_FX));
    put("shadow", oneOf(s.shadow, SHADOWS));
    put("cardShadow", oneOf(s.cardShadow, SHADOWS));
    if (fonts.includes(String(s.font || "")) && s.font) out.font = String(s.font);
    const scale = Number(s.scale);
    if (s.scale !== undefined && s.scale !== "" && scale >= LIMITS.scaleMin && scale <= LIMITS.scaleMax && scale !== 1) out.scale = Math.round(scale * 100) / 100;
    const radius = Number(s.radius);
    if (s.radius !== undefined && s.radius !== "" && Number.isFinite(radius) && radius >= 0 && radius <= LIMITS.radiusMax) out.radius = Math.round(radius);
    const colors = {};
    ["dark", "light"].forEach((theme) => {
        const c = (s.colors && s.colors[theme]) || {};
        const kept = {};
        COLOUR_KEYS.forEach((key) => { if (hex(c[key])) kept[key] = hex(c[key]); });
        if (Object.keys(kept).length) colors[theme] = kept;
    });
    if (Object.keys(colors).length) out.colors = colors;
    return out;
}


export function cleanTexts(raw) {
    const t = raw && typeof raw === "object" ? raw : {};
    const out = {};
    const title = plainText(t.title, LIMITS.title), subtitle = plainText(t.subtitle, LIMITS.subtitle), note = plainText(t.note, LIMITS.note, true);
    if (title) out.title = title;
    if (subtitle) out.subtitle = subtitle;
    if (note) { out.note = note; out.notePlace = oneOf(t.notePlace, NOTE_PLACES) || "top"; }
    const labels = {};
    Object.keys(t.labels && typeof t.labels === "object" ? t.labels : {}).slice(0, LIMITS.labels).forEach((key) => {
        const value = plainText(t.labels[key], LIMITS.label);
        if (/^\d{1,2}$/.test(key) && Number(key) < LIMITS.labels && value) labels[key] = value;
    });
    if (Object.keys(labels).length) out.labels = labels;
    return out;
}


export function cleanPanelMap(raw, clean) {
    const out = {};
    Object.keys(raw && typeof raw === "object" ? raw : {}).slice(0, 60).forEach((id) => {
        if (!/^[a-z0-9_-]{1,40}$/i.test(id)) return;
        const value = clean(raw[id]);
        if (Object.keys(value).length) out[id] = value;
    });
    return out;
}


function luminance(colour) {
    const v = hex(colour);
    if (!v) return null;
    const channel = (i) => { const c = parseInt(v.slice(i, i + 2), 16) / 255; return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
    return 0.2126 * channel(1) + 0.7152 * channel(3) + 0.0722 * channel(5);
}

export function contrast(a, b) {
    const x = luminance(a), y = luminance(b);
    if (x === null || y === null) return 0;
    return Math.round(((Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05)) * 100) / 100;
}


const DATA = { anim: "anim", trans: "trans", edge: "edge", orient: "orient", btn: "btn", shadow: "pshadow", cardShadow: "cshadow" };
const PROPS = { bg: "--panel-bg", card: "--card-bg", text: "--text", accent: "--accent", border: "--border" };


export function applyPanelStyle(panel, style, theme, fontStack) {
    if (!panel || !panel.style) return;
    const s = style || {};
    Object.keys(DATA).forEach((key) => { if (s[key]) panel.dataset[DATA[key]] = s[key]; else delete panel.dataset[DATA[key]]; });
    const set = (name, value) => { if (value === undefined || value === null || value === "") panel.style.removeProperty(name); else panel.style.setProperty(name, value); };
    const colours = (s.colors && s.colors[theme]) || {};
    set("--panel-shadow", shadowCss(s.shadow, colours.shadow));
    set("--card-shadow", shadowCss(s.cardShadow, colours.shadow));
    Object.keys(PROPS).forEach((key) => set(PROPS[key], colours[key]));
    set("--accent-text", colours.accent);
    set("--text-2", colours.text ? `color-mix(in srgb, ${colours.text} 78%, transparent)` : "");
    set("--muted", colours.text ? `color-mix(in srgb, ${colours.text} 62%, transparent)` : "");
    if (colours.card) panel.dataset.cardBg = "1"; else delete panel.dataset.cardBg;
    if (colours.bg) panel.dataset.panelBg = "1"; else delete panel.dataset.panelBg;
    set("--pscale", s.scale ? String(s.scale) : "");
    if (s.scale) panel.dataset.pscale = "1"; else delete panel.dataset.pscale;
    set("--radius", s.radius !== undefined ? s.radius + "px" : "");
    set("--radius-sm", s.radius !== undefined ? Math.max(2, Math.round(s.radius * 0.64)) + "px" : "");
    set("font-family", fontStack || "");
    const any = Object.keys(s).length > 0;
    if (any) panel.dataset.styled = "1"; else delete panel.dataset.styled;
}


export const LABEL_SELECTOR = ".kpi-title, .stat-title";
const own = (panel, selector) => Array.from(panel.querySelectorAll(selector)).filter((node) => node.closest(".panel") === panel);


export function applyPanelTexts(panel, texts) {
    if (!panel || !panel.querySelector) return;
    const t = texts || {};
    const inner = panel.querySelector(":scope > .panel-inner") || panel;
    const head = inner.querySelector(":scope > .panel-head");
    const heading = head ? head.querySelector(".panel-title") : null;
    if (heading) {
        if (heading.dataset.ownText === undefined) heading.dataset.ownText = heading.textContent;
        heading.textContent = t.title || heading.dataset.ownText;
    }
    const slot = (cls, text, place) => {
        let node = inner.querySelector(`:scope > .${cls}`);
        if (!text) { if (node) node.remove(); return; }
        if (!node) { node = document.createElement("p"); node.className = cls; }
        node.textContent = text;
        if (place === "bottom") inner.appendChild(node);
        else {

            const after = cls === "panel-note" ? inner.querySelector(":scope > .panel-subtitle-text") || head : head;
            if (after) after.insertAdjacentElement("afterend", node); else inner.insertBefore(node, inner.firstChild);
        }
    };
    slot("panel-subtitle-text", t.subtitle, "top");
    slot("panel-note", t.note, t.notePlace);
    own(panel, LABEL_SELECTOR).forEach((label, index) => {
        if (label.dataset.ownText === undefined) label.dataset.ownText = label.textContent;
        const mine = t.labels && t.labels[String(index)];
        label.textContent = mine || label.dataset.ownText;
    });
}


export function ownTexts(panel) {
    const inner = panel.querySelector(":scope > .panel-inner") || panel;
    const heading = inner.querySelector(":scope > .panel-head .panel-title");
    return {
        title: heading ? (heading.dataset.ownText !== undefined ? heading.dataset.ownText : heading.textContent) : null,
        labels: own(panel, LABEL_SELECTOR).map((label) => (label.dataset.ownText !== undefined ? label.dataset.ownText : label.textContent))
    };
}
