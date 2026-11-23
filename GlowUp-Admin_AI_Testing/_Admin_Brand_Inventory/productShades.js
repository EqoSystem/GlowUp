// "Find shades online" in the Add Product form: asks the Makeup API for the brand and kind in the form, lets the admin pick the
// matching product and tick its shades. Ticked shades go into the shade editor (colour, and the code when the shade's name
// starts with one) and are marked "Needs review": a seller's swatch is not a measured colour. Nothing is saved here.
import { escapeHtml } from "../_Shared/adminCommon.js";
import { dialogFrame, icon } from "../_Shared/adminShell.js";
import { makeupApiUrl, apiBrand, shadeMatches, newShades, MAKEUP_API_LABEL } from "../_Shared/shadeSources.js";

const TIMEOUT_MS = 10000;
const answers = new Map();                 // one answer per address while the page is open (the API is slow and free)

async function fetchList(url) {
    if (answers.has(url)) return answers.get(url);
    const control = new AbortController();
    const timer = setTimeout(() => control.abort(), TIMEOUT_MS);
    try {
        const response = await fetch(url, { signal: control.signal, credentials: "omit" });
        if (!response.ok) return null;
        const json = await response.json();
        if (Array.isArray(json)) answers.set(url, json);
        return Array.isArray(json) ? json : null;
    } catch (error) { return null; }
    finally { clearTimeout(timer); }
}

// readForm(): { name, brand, cosmeticType, shades }. room(): how many more shades fit. addShades(list of { hex, name, code }).
export function mountShadeFinder({ button, status, readForm, room, addShades }) {
    const say = (text, tone) => { status.textContent = text || ""; status.className = "field-hint" + (tone ? " " + tone : ""); };

    button.addEventListener("click", async () => {
        const form = readForm();
        if (!form.brand) { say("Choose the brand first.", "warn"); return; }
        if (!form.cosmeticType) { say("Choose the cosmetic type first.", "warn"); return; }
        const url = makeupApiUrl(form.brand, form.cosmeticType);
        if (!url) { say(`The ${MAKEUP_API_LABEL} has no shades for "${form.brand}". Add the colours by hand.`, "warn"); return; }
        button.disabled = true;
        say(`Asking the ${MAKEUP_API_LABEL} for ${apiBrand(form.brand)} shades...`);
        const list = await fetchList(url);
        button.disabled = false;
        if (!list) { say(`The ${MAKEUP_API_LABEL} did not answer. Try again later, or add the colours by hand.`, "warn"); return; }
        const matches = shadeMatches(list, form.name, form.cosmeticType);
        if (matches.length === 0) { say(`The ${MAKEUP_API_LABEL} lists no ${form.cosmeticType.toLowerCase()} shades for this brand. Add the colours by hand.`, "warn"); return; }
        say("");
        pick(matches, form);
    });

    function pick(matches, form) {
        const finish = () => frame.close();
        const frame = dialogFrame("Shades found online", finish);
        frame.box.classList.add("wide", "shade-find");
        let chosen = 0;
        frame.box.insertAdjacentHTML("beforeend", `
            <p class="modal-intro">From the ${escapeHtml(MAKEUP_API_LABEL)}: choose the product that is the one in your hand, then tick its shades. The colours are the seller's swatches: compare them with the pack before you approve the product.</p>
            <div class="shade-find-products" role="radiogroup" aria-label="Product">${matches.map((m, at) => `
                <label class="shade-find-product"><input type="radio" name="shadeFindProduct" value="${at}"${at === 0 ? " checked" : ""}>
                <span><b>${escapeHtml(m.name)}</b> <small class="muted">${m.shades.length} shades${m.category ? " · " + escapeHtml(m.category.replace(/_/g, " ")) : ""} · match ${Math.round(m.score * 100)}%</small></span></label>`).join("")}</div>
            <div class="shade-find-list"></div>
            <p class="field-hint" data-room role="status" aria-live="polite"></p>
            <div class="modal-actions"><button type="button" class="cancel-btn" data-act="cancel">Cancel</button><button type="button" class="save-btn" data-act="add">${icon("check")}Add the ticked shades</button></div>`);
        const listBox = frame.box.querySelector(".shade-find-list"), roomNote = frame.box.querySelector("[data-room]");
        const paint = () => {
            const shades = matches[chosen].shades;
            const have = new Set((form.shades || []).map((h) => String(h).toUpperCase()));
            listBox.innerHTML = shades.map((s, at) => `<label class="shade-find-row">
                <input type="checkbox" data-shade="${at}"${have.has(s.hex) ? " disabled" : (have.size === 0 ? " checked" : "")}>
                <span class="swatch" style="background:${escapeHtml(s.hex)}" aria-hidden="true"></span>
                <span>${escapeHtml(s.name || "no name")}${s.code ? ` <b>${escapeHtml(s.code)}</b>` : ""} <code class="small">${escapeHtml(s.hex)}</code>${have.has(s.hex) ? ' <small class="muted">already in the form</small>' : ""}</span></label>`).join("");
            roomNote.textContent = `Room for ${room()} more shades.`;
        };
        frame.box.querySelector(".shade-find-products").addEventListener("change", (event) => { chosen = Number(event.target.value) || 0; paint(); });
        frame.box.querySelector('[data-act="cancel"]').addEventListener("click", finish);
        frame.box.querySelector('[data-act="add"]').addEventListener("click", () => {
            const ticked = Array.from(listBox.querySelectorAll("[data-shade]:checked")).map((box) => matches[chosen].shades[Number(box.dataset.shade)]);
            const adding = newShades(ticked, form.shades, room());
            if (adding.length === 0) { roomNote.textContent = "Tick at least one shade that is not in the form yet."; return; }
            addShades(adding, matches[chosen].name);
            finish();
            say(`${adding.length} shade${adding.length === 1 ? "" : "s"} added from the ${MAKEUP_API_LABEL} ("${matches[chosen].name}"), marked Needs review.${adding.length < ticked.length ? " Some did not fit or were already there." : ""}`, "ok");
        });
        paint();
    }
}
