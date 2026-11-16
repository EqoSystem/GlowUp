// WCAG contrast of the colour pairs of the Edit Ingredient window. Run: node contrast.mjs   (or pass "ci" to read the live CSS values from the css file)
const lum = (hex) => {
    const v = hex.replace("#", "");
    const [r, g, b] = [0, 2, 4].map((i) => parseInt(v.slice(i, i + 2), 16) / 255).map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
};
export const ratio = (a, b) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05); };
if ((process.argv[1] || "").endsWith("contrast-check.mjs")) {
    const themes = {
        light: { modal: "#ffffff", input: "#ffffff", text: "#2b2426", text2: "#554a4d", hint: "#6a5e61", placeholder: "#6f6366", border: "#857a7d", link: "#1f5fae", danger: "#b42323", accent: "#c9456d", accentInk: "#ffffff", accentText: "#b03a5e", chipOnText: "#2b2426", chipOnBg: "#f9e3ea", muted: "#6a5e61" },
        dark: { modal: "#16161b", input: "#1d1d24", text: "#f2f1f4", text2: "#b9b6c0", hint: "#b0adb8", placeholder: "#a8a5b0", border: "#8b8894", link: "#86b7f7", danger: "#ff8a8a", accent: "#e0668a", accentInk: "#1a0d12", accentText: "#f08fab", chipOnText: "#f2f1f4", chipOnBg: "#4a2433", muted: "#b0adb8" }
    };
    for (const [name, t] of Object.entries(themes)) {
        const pairs = [
            ["body text on modal", t.text, t.modal, 4.5], ["label text on modal", t.text, t.modal, 4.5], ["hint on modal", t.hint, t.modal, 4.5],
            ["input text on input", t.text, t.input, 4.5], ["placeholder on input", t.placeholder, t.input, 4.5], ["input border on input", t.border, t.input, 3],
            ["input border on modal", t.border, t.modal, 3], ["link on modal", t.link, t.modal, 4.5], ["danger on modal", t.danger, t.modal, 4.5],
            ["focus ring on modal", t.accent, t.modal, 3], ["focus ring on input", t.accent, t.input, 3], ["save button text on accent", t.accentInk, t.accent, 4.5],
            ["muted icon on modal", t.muted, t.modal, 3], ["active tab text on modal", t.accentText, t.modal, 4.5], ["selected chip text", t.chipOnText, t.chipOnBg, 4.5]
        ];
        console.log("--- " + name);
        for (const [what, fg, bg, need] of pairs) { const r = ratio(fg, bg); console.log(`${r >= need ? "ok  " : "FAIL"} ${r.toFixed(2).padStart(5)}:1 (need ${need})  ${what}  ${fg} on ${bg}`); }
    }
}
