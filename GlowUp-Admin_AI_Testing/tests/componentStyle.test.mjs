

import { cleanStyle, cleanTexts, cleanPanelMap, contrast, plainText, readPixels, LIMITS, ANIMATIONS, ANIMATION_FRAMES } from "../_Shared/componentStyle.js";

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };
const equal = (a, b, what) => check(JSON.stringify(a) === JSON.stringify(b), `${what} (got ${JSON.stringify(a)})`);
const FONTS = ["", "poppins", "inter"];

equal(cleanStyle({}), {}, "nothing set, nothing kept");
equal(cleanStyle({ anim: "zoom", trans: "slow", edge: "always", orient: "row", font: "inter", scale: 1.1, radius: 8 }, FONTS),
    { anim: "zoom", trans: "slow", edge: "always", orient: "row", font: "inter", scale: 1.1, radius: 8 }, "known settings are kept");
equal(cleanStyle({ anim: "explode", trans: "warp", edge: "x", orient: "diagonal", font: "comic" }, FONTS), {}, "unknown values are dropped");
equal(cleanStyle({ scale: 3 }), {}, "a text size over 125 % is not taken");
equal(cleanStyle({ scale: 0.5 }), {}, "a text size under 85 % is not taken");
equal(cleanStyle({ scale: 1 }), {}, "100 % is the page's own size: not kept");
equal(cleanStyle({ radius: 99 }), {}, "corners rounder than the limit are not taken");
equal(cleanStyle({ radius: 0 }), { radius: 0 }, "square corners are a real choice");
equal(cleanStyle({ colors: { dark: { bg: "#112233", text: "red", card: "#ABCDEF", evil: "#000000" }, light: {} } }),
    { colors: { dark: { bg: "#112233", card: "#abcdef" } } }, "only #rrggbb colours of known parts are kept, per mode");
equal(cleanStyle({ rotate: 45, transform: "rotate(45deg)" }), {}, "a panel is never turned at an angle");
equal(cleanStyle("not an object"), {}, "rubbish gives nothing");

equal(cleanTexts({ title: "  Scans   today ", subtitle: "x".repeat(500), note: "line 1 / line 2", notePlace: "middle" }),
    { title: "Scans today" }, "only the title is kept: added text (subtitle, note) is dropped, nothing new goes into a panel");
equal(cleanTexts({ labels: { 0: "A" }, labelStyles: { 0: { size: "20", style: "bold", align: "center" }, 1: { size: 5, style: "huge", align: "diagonal" }, 99: { size: 20 } } }).labelStyles,
    { 0: { size: 20, style: "bold", align: "center" } }, "a label look keeps a size of 12 to 28, a known style and a known alignment, and nothing else");
equal(readPixels("", 120, 1400, "The height"), { ok: true, value: 0, message: "" }, "an empty size is automatic");
equal(readPixels("abc", 120, 1400, "The height").ok, false, "text is refused");
equal(readPixels("50", 120, 1400, "The height").message, "The height must be between 120 and 1400 pixels.", "a too small size is named");
equal(readPixels("99999", 240, 2400, "The width").ok, false, "a huge size is refused");
equal(readPixels("300.4", 240, 2400, "The width").value, 300, "a size is a whole number of pixels");
equal(cleanTexts({ labels: { 0: "Today", 1: "", 99: "far", x: "bad", 2: "y".repeat(80) } }), { labels: { 0: "Today", 2: "y".repeat(LIMITS.label) } }, "card labels: by number, in their limit");
equal(cleanTexts({ title: "<script>alert(1)</script>" }).title, "<script>alert(1)</script>", "text is kept as text (the page writes it with textContent, never as HTML)");
equal(plainText("a\u0000b\u0007c", 10), "a b c", "control characters become spaces");

equal(cleanPanelMap({ kpis: { anim: "fade" }, "bad id!": { anim: "fade" }, empty: { anim: "nope" } }, (raw) => cleanStyle(raw)), { kpis: { anim: "fade" } }, "only good panel ids with something set are kept");

equal(contrast("#000000", "#ffffff"), 21, "black on white is 21 to 1");
equal(contrast("#777777", "#777777"), 1, "the same colour is 1 to 1");
equal(contrast("red", "#ffffff"), 0, "a value that is not a colour gives 0");

check(ANIMATIONS.every((a) => a.id === "none" || ANIMATION_FRAMES[a.id]), "every entrance has frames for Play");

import { BUTTON_FX, SHADOWS, shadowCss } from "../_Shared/componentStyle.js";
equal(cleanStyle({ btn: "pixel", shadow: "strong", cardShadow: "glow" }, FONTS), { btn: "pixel", shadow: "strong", cardShadow: "glow" }, "button effect and shadows are kept");
equal(cleanStyle({ btn: "boom", shadow: "huge", cardShadow: "x" }, FONTS), {}, "unknown button effect and shadows are dropped");
equal(cleanStyle({ colors: { light: { shadow: "#112233", bad: "#fff" } } }, FONTS), { colors: { light: { shadow: "#112233" } } }, "shadow colour kept");
check(SHADOWS.every((o) => o.id === "" || shadowCss(o.id, "") !== ""), "every shadow preset gives a box-shadow");
check(shadowCss("soft", "#112233").includes("#112233") && shadowCss("") === "" && shadowCss("none") === "none", "shadow css uses the colour");
check(BUTTON_FX[0].id === "" && BUTTON_FX.some((o) => o.id === "pixel"), "default has no pixel sweep, pixel is a choice");

console.log(`checks made: ${checks}`);
if (failed) { console.log(`${failed} FAILED`); process.exit(1); }
console.log("ALL PASSED");
