// GlowUp landing page. Plain script, no Firebase: this page only shows the app.

// TODO: paste the app's download link here (for example a GitHub Release .apk link).
// While it is empty, the Download buttons show "Coming soon" instead.
const DOWNLOAD_URL = "";

// ---- Theme: same saved setting ("glowup.theme") as the admin pages ----
(function themeSwitch() {
    const root = document.documentElement;
    const buttons = document.querySelectorAll("[data-theme-pick]");
    const paint = () => buttons.forEach(b => b.setAttribute("aria-pressed", String(b.dataset.themePick === root.dataset.theme)));
    buttons.forEach(b => b.addEventListener("click", () => {
        root.dataset.theme = b.dataset.themePick;
        try { localStorage.setItem("glowup.theme", root.dataset.theme); } catch (e) { }
        paint();
    }));
    paint();
})();

// ---- Small-screen menu: the X closes it, so do Escape, a link click and a click outside ----
(function menu() {
    const btn = document.getElementById("lpMenuBtn");
    const nav = document.getElementById("lpNav");
    const set = open => {
        nav.classList.toggle("open", open);
        btn.setAttribute("aria-expanded", String(open));
        btn.setAttribute("aria-label", open ? "Close the menu" : "Open the menu");
    };
    btn.addEventListener("click", () => set(!nav.classList.contains("open")));
    nav.addEventListener("click", e => { if (e.target.closest("a")) set(false); });
    document.addEventListener("keydown", e => { if (e.key === "Escape" && nav.classList.contains("open")) { set(false); btn.focus(); } });
    document.addEventListener("click", e => { if (nav.classList.contains("open") && !e.target.closest(".lp-top")) set(false); });
    matchMedia("(min-width: 761px)").addEventListener("change", e => { if (e.matches) set(false); });
})();

// ---- Download buttons ----
(function download() {
    [["downloadBtn", "downloadNote"], ["downloadBtn2", "downloadNote2"]].forEach(([btnId, noteId]) => {
        const btn = document.getElementById(btnId);
        const note = document.getElementById(noteId);
        if (DOWNLOAD_URL) {
            btn.href = DOWNLOAD_URL;
            btn.setAttribute("rel", "noopener");
            return;
        }
        btn.addEventListener("click", e => {
            if (btnId === "downloadBtn") return;   // the hero button scrolls to the closing section
            e.preventDefault();
            note.hidden = false;
        });
    });
})();

// ---- Entrance: each block fades in and rises slightly when it scrolls into view ----
(function reveal() {
    const items = document.querySelectorAll(".reveal");
    if (!("IntersectionObserver" in window) || matchMedia("(prefers-reduced-motion: reduce)").matches) {
        items.forEach(el => el.classList.add("in"));
        return;
    }
    const seen = new IntersectionObserver(entries => {
        entries.forEach(entry => {
            if (!entry.isIntersecting) return;
            const el = entry.target;
            // Items in the same row come in one after another.
            const siblings = [...el.parentElement.children].filter(c => c.classList.contains("reveal"));
            el.style.transitionDelay = Math.min(siblings.indexOf(el), 5) * 70 + "ms";
            el.classList.add("in");
            seen.unobserve(el);
            // Once it has arrived, hand the card back its quick hover effect.
            setTimeout(() => { el.style.transitionDelay = ""; el.classList.add("done"); }, 1000);
        });
    }, { rootMargin: "0px 0px -8% 0px", threshold: 0.08 });
    items.forEach(el => seen.observe(el));
})();

// ---- Try-on mock-up: the lip colour cycles through the swatches on its own ----
(function tryOn() {
    const lips = document.querySelector(".face-lips");
    const swatches = [...document.querySelectorAll(".swatches i")];
    if (!lips || !swatches.length || matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    let i = swatches.findIndex(s => s.classList.contains("on"));
    setInterval(() => {
        swatches[i].classList.remove("on");
        i = (i + 1) % swatches.length;
        swatches[i].classList.add("on");
        lips.style.background = swatches[i].style.getPropertyValue("--s");
    }, 1800);
})();
