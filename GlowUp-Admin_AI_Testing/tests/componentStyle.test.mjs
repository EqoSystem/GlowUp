// Checks of "Style and text" (_Shared/componentStyle.js): only known settings are kept, texts stay in their limits.
//   Run: node tests/componentStyle.test.mjs
import { cleanStyle, cleanTexts, cleanPanelMap, contrast, plainText, LIMITS, ANIMATIONS, ANIMATION_FRAMES } from "../_Shared/componentStyle.js";

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

equal(cleanTexts({ title: "  Scans   today ", subtitle: "x".repeat(500), note: "line 1\nline 2", notePlace: "middle" }),
    { title: "Scans today", subtitle: "x".repeat(LIMITS.subtitle), note: "line 1\nline 2", notePlace: "top" }, "texts are trimmed, cut to their limits, and the note goes to a fixed place");
equal(cleanTexts({ note: "a\nb\nc\nd\ne\nf\ng" }).note.split("\n").length, 5, "a note keeps at most 5 lines");
equal(cleanTexts({ note: "Hi", notePlace: "bottom" }).notePlace, "bottom", "the bottom is a fixed place too");
equal(cleanTexts({ labels: { 0: "Today", 1: "", 99: "far", x: "bad", 2: "y".repeat(80) } }), { labels: { 0: "Today", 2: "y".repeat(LIMITS.label) } }, "card labels: by number, in their limit");
equal(cleanTexts({ title: "<script>alert(1)</script>" }).title, "<script>alert(1)</script>", "text is kept as text (the page writes it with textContent, never as HTML)");
equal(plainText("a\u0000b\u0007c", 10), "a b c", "control characters become spaces");

equal(cleanPanelMap({ kpis: { anim: "fade" }, "bad id!": { anim: "fade" }, empty: { anim: "nope" } }, (raw) => cleanStyle(raw)), { kpis: { anim: "fade" } }, "only good panel ids with something set are kept");

equal(contrast("#000000", "#ffffff"), 21, "black on white is 21 to 1");
equal(contrast("#777777", "#777777"), 1, "the same colour is 1 to 1");
equal(contrast("red", "#ffffff"), 0, "a value that is not a colour gives 0");

check(ANIMATIONS.every((a) => a.id === "none" || ANIMATION_FRAMES[a.id]), "every entrance has frames for Play");

console.log(`checks made: ${checks}`);
if (failed) { console.log(`${failed} FAILED`); process.exit(1); }
console.log("ALL PASSED");
