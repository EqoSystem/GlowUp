// A line of help under a set of buttons (28 Nov 2026). Pointing at (or tabbing to) a button with data-hint shows its text in this
// reserved line, so nothing floats over the page and nothing can overlap or collide. Without a pointer the line keeps its own text.
export function mountHints(root) {
    const line = root && root.querySelector(".btn-hint");
    if (!line) return;
    const rest = line.textContent;
    const show = (event) => {
        const from = event.target.closest && event.target.closest("[data-hint]");
        const words = from && root.contains(from) ? from.getAttribute("data-hint") : rest;
        if (line.textContent !== words) line.textContent = words;
        line.classList.toggle("on", words !== rest);
    };
    root.addEventListener("pointerover", show);
    root.addEventListener("focusin", show);
    root.addEventListener("pointerleave", () => { line.textContent = rest; line.classList.remove("on"); });
}
