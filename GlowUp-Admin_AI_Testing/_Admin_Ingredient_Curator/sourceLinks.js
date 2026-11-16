







import { escapeHtml, notify } from "../_Shared/adminCommon.js";
import { SOURCE_LINK_FIELDS, SOURCES, COSING_INDEX_ENABLED, finderWhat } from "../_Shared/fieldSources.js";
import { checkSourceUrl } from "../_Shared/sourceLinkRules.mjs";

const OPEN_ICON = '<svg class="ic" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14 4h6v6"/><path d="M20 4l-9 9"/><path d="M19 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1h5"/></svg>';
const WARN_ICON = '<span class="src-glyph" aria-hidden="true">!</span>';
const OK_ICON = '<span class="src-glyph ok" aria-hidden="true">&#10003;</span>';
const NONE_ICON = '<span class="src-glyph none" aria-hidden="true">&#8211;</span>';





export function mountSourceLinks({ getName, getCas, getToken, onChange }) {
    const fields = SOURCE_LINK_FIELDS.filter((f) => f.enabled && document.getElementById(f.input));
    const rows = new Map();
    const finder = buildFinder();
    let openFor = null;
    const cosingAnswers = new Map();

    for (const f of fields) {
        const valueBox = document.getElementById(f.input);
        const row = valueBox.closest(".field");
        row.classList.add("has-src");
        const id = f.input + "Source";
        const label = document.createElement("div");
        label.className = "src-label";
        label.innerHTML = `<label for="${id}">Source link <span class="optional">(optional)</span></label>`;
        const body = document.createElement("div");
        body.className = "src-body";
        body.innerHTML = `<div class="src-input-row"><input type="url" id="${id}" data-own-validity inputmode="url" autocomplete="off" spellcheck="false" maxlength="600" placeholder="https://..." aria-describedby="${id}Status">`
            + `<a class="icon-btn src-open" target="_blank" rel="noopener noreferrer" aria-label="Open source for ${escapeHtml(f.label)} in new tab" aria-disabled="true">${OPEN_ICON}</a></div>`
            + `<p class="src-status" id="${id}Status" role="status"></p>`
            + `<div class="src-actions"><button type="button" class="row-btn src-check">Check link</button><button type="button" class="link-btn src-find" aria-expanded="false">Find source</button><span class="src-find-hint"></span></div>`;

        valueBox.insertAdjacentElement("afterend", body);
        valueBox.insertAdjacentElement("afterend", label);
        const r = {
            field: f, valueBox, input: body.querySelector("input"), status: body.querySelector(".src-status"), open: body.querySelector(".src-open"),
            check: body.querySelector(".src-check"), find: body.querySelector(".src-find"), findHint: body.querySelector(".src-find-hint"), row, checked: null
        };
        rows.set(f.key, r);
        r.input.addEventListener("input", () => { r.checked = null; paint(r); onChange(); });
        r.input.addEventListener("blur", () => { const v = checkSourceUrl(r.input.value); if (v.ok && v.value !== r.input.value) r.input.value = v.value; paint(r); });
        valueBox.addEventListener("input", () => { paint(r); onChange(); });
        r.check.addEventListener("click", () => checkLink(r));
        r.find.addEventListener("click", () => (openFor === r ? closeFinder(true) : openFinder(r)));
    }


    function paint(r) {
        const v = checkSourceUrl(r.input.value);
        const hasValue = r.valueBox.value.trim().length > 0;
        const parts = [];
        if (v.empty) { if (hasValue) parts.push(`<span class="src-tag">${NONE_ICON}No source</span>`); }
        else if (!v.ok) parts.push(`<span class="src-msg bad">${WARN_ICON}${escapeHtml(v.error)}</span>`);
        else {
            parts.push(`<span class="chip src-tier ${v.tier.key}">${escapeHtml(v.tier.label)}</span>`);
            v.warnings.forEach((w) => parts.push(`<span class="src-msg warn">${WARN_ICON}${escapeHtml(w.text)}</span>`));
            if (r.checked) parts.push(`<span class="src-msg ${r.checked.result === "checked" ? "ok" : r.checked.result === "dead" ? "bad" : "info"}">${r.checked.result === "checked" ? OK_ICON : WARN_ICON}${escapeHtml(r.checked.message)}</span>`);
        }
        r.status.innerHTML = parts.join(" ");
        r.input.setAttribute("aria-invalid", v.ok ? "false" : "true");
        r.input.classList.toggle("src-bad", !v.ok);
        const usable = v.ok && !v.empty;
        if (usable) { r.open.href = v.value; r.open.removeAttribute("aria-disabled"); r.open.tabIndex = 0; }
        else { r.open.removeAttribute("href"); r.open.setAttribute("aria-disabled", "true"); r.open.tabIndex = -1; }
        r.check.disabled = !usable;
        const named = getName().trim().length >= 2;
        r.find.disabled = !named;
        r.findHint.textContent = named ? "" : "Enter the INCI name first";
    }

    async function checkLink(r) {
        const v = checkSourceUrl(r.input.value);
        if (!v.ok || v.empty) return;
        r.check.disabled = true;
        r.checked = { result: "info", message: "Checking..." };
        paint(r); r.check.disabled = true;
        try {
            const token = await getToken();
            const answer = await fetch("/api/source-link", {
                method: "POST", headers: { "Content-Type": "application/json", Authorization: "Bearer " + token },
                body: JSON.stringify({ mode: "check", url: v.value, inci: getName(), cas: getCas() }), signal: AbortSignal.timeout(12000)
            });
            const body = await answer.json().catch(() => ({}));
            r.checked = answer.ok ? { result: body.result, message: body.message } : { result: "cantcheck", message: body.error || "Can't check right now." };
        } catch (error) { r.checked = { result: "cantcheck", message: "Can't check right now." }; }
        paint(r);
    }




    function buildFinder() {
        const box = document.createElement("section");
        box.className = "src-finder";
        box.setAttribute("aria-label", "Source finder");
        box.hidden = true;
        box.addEventListener("keydown", (event) => { if (event.key === "Escape") { event.stopPropagation(); event.preventDefault(); closeFinder(true); } });
        return box;
    }

    function openFinder(r) {
        if (openFor) closeFinder(false);
        openFor = r;
        r.find.setAttribute("aria-expanded", "true");
        r.row.appendChild(finder);
        finder.hidden = false;
        renderFinder(r, getName().trim());
        const first = finder.querySelector("input, a, button");
        if (first) first.focus({ preventScroll: true });
        finder.scrollIntoView({ block: "nearest" });
    }

    function closeFinder(giveFocusBack) {
        if (!openFor) return;
        const r = openFor;
        openFor = null;
        r.find.setAttribute("aria-expanded", "false");
        finder.hidden = true;
        finder.innerHTML = "";
        if (giveFocusBack) r.find.focus();
    }

    function renderFinder(r, name) {
        const f = r.field;
        const rowsHtml = f.finder.map((id) => {
            const s = SOURCES[id];
            const signIn = id === "incipedia" ? ' <span class="src-note">(sign-in required)</span>' : "";
            return `<li class="finder-row"><a class="source-link" href="${escapeHtml(s.landing)}" target="_blank" rel="noopener noreferrer">${escapeHtml(s.label)}</a>${signIn}`
                + `<span class="finder-what">${escapeHtml(finderWhat(f.key, id))}</span>`
                + `<button type="button" class="row-btn finder-copy" data-copy-name>Copy INCI name</button></li>`;
        }).join("");
        finder.innerHTML = `<div class="finder-head"><h5 class="finder-title">Find a source for ${escapeHtml(f.label)}</h5>`
            + `<button type="button" class="icon-btn finder-close" aria-label="Close the source finder"><svg class="ic" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18"/></svg></button></div>`
            + `<div class="finder-name"><label for="finderName">INCI name</label><input type="text" id="finderName" value="${escapeHtml(name)}" autocomplete="off"></div>`
            + `<h6 class="finder-sub">Where to look</h6><ul class="finder-list">${rowsHtml}</ul>`
            + `<p class="field-hint">Found the page? Copy its address from the browser and paste it into the Source link box.</p>`
            + (COSING_INDEX_ENABLED ? `<div class="finder-cosing" aria-live="polite"></div>` : "");
        finder.querySelector(".finder-close").addEventListener("click", () => closeFinder(true));
        const nameBox = finder.querySelector("#finderName");
        finder.querySelectorAll("[data-copy-name]").forEach((b) => b.addEventListener("click", () => copyName(nameBox.value)));
        if (COSING_INDEX_ENABLED) {
            const area = finder.querySelector(".finder-cosing");
            const run = () => searchCosing(r, nameBox.value.trim(), area);
            nameBox.addEventListener("keydown", (event) => { if (event.key === "Enter") { event.preventDefault(); run(); } });
            run();
        }
    }

    function copyName(name) {
        const text = String(name || "").trim();
        if (!text) return;
        const done = () => notify("INCI name copied. Paste it into the site's search box.", "ok");
        if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(text).then(done, () => notify("Copy did not work. Select the name and copy it.", "error"));
    }

    async function searchCosing(r, name, area) {
        const head = `<h6 class="finder-sub">CosIng index</h6><p class="field-hint">Unofficial index of EU CosIng data. Verify at the official source.</p>`;
        const official = `<button type="button" class="row-btn finder-official">Open official CosIng</button>`;
        const foot = `<p class="src-note">Data: CosIng Checker (CC BY 4.0)</p>`;
        const wire = () => {
            const b = area.querySelector(".finder-official");
            if (b) b.addEventListener("click", () => { window.open(SOURCES.cosing.landing, "_blank", "noopener,noreferrer"); copyName(name); });
            area.querySelectorAll("[data-use]").forEach((u) => u.addEventListener("click", () => {
                r.input.value = u.getAttribute("data-use");
                r.checked = null;
                paint(r); onChange();
                notify(`Source link set for ${r.field.label}. Write the value yourself.`, "ok");
                r.input.focus();
            }));
        };
        if (name.length < 2) { area.innerHTML = head + `<p class="field-hint">Enter the INCI name first.</p>` + official; wire(); return; }
        let answer = cosingAnswers.get(name.toLowerCase());
        if (!answer) {
            area.innerHTML = head + `<p class="field-hint">Searching...</p>`;
            try {
                const token = await getToken();
                const res = await fetch("/api/source-link", { method: "POST", headers: { "Content-Type": "application/json", Authorization: "Bearer " + token }, body: JSON.stringify({ mode: "cosing", name }), signal: AbortSignal.timeout(12000) });
                const body = await res.json().catch(() => ({}));
                answer = res.ok ? body : { error: body.error || "The CosIng index could not be reached." };
                if (res.ok) cosingAnswers.set(name.toLowerCase(), answer);
            } catch (error) { answer = { error: "The CosIng index could not be reached." }; }
            if (openFor !== r) return;
        }
        if (answer.error) { area.innerHTML = head + `<p class="src-msg warn">${WARN_ICON}${escapeHtml(answer.error)} Use the sites above.</p>` + official + foot; wire(); return; }
        const results = Array.isArray(answer.results) ? answer.results : [];
        const list = results.length === 0 ? `<p class="field-hint">No ingredient with this name in the index. Use the sites above.</p>`
            : `<ul class="finder-list">${results.map((x) => `<li class="finder-row result"><span class="finder-inci">${escapeHtml(x.inci)}${x.exact ? ' <span class="chip ok">Same name</span>' : ""}</span>`
                + `<span class="finder-what">${escapeHtml(x.statement)}</span>`
                + (x.page ? `<button type="button" class="row-btn" data-use="${escapeHtml(x.page)}">Use as source</button>` : `<span class="src-note">No page to link</span>`) + `</li>`).join("")}</ul>`;
        area.innerHTML = head + list + official + foot;
        wire();
    }

    return {

        fill(item) {
            closeFinder(false);
            for (const r of rows.values()) { r.input.value = item && item.sourceLinks ? item.sourceLinks[r.field.key] || "" : ""; r.checked = null; paint(r); }
            onChange();
        },

        refresh() { for (const r of rows.values()) paint(r); },

        firstError() {
            for (const r of rows.values()) { const v = checkSourceUrl(r.input.value); if (!v.ok) return { input: r.input, message: `${r.field.label} source link: ${v.error}` }; }
            return null;
        },

        values() { const out = {}; for (const r of rows.values()) out[r.field.data] = checkSourceUrl(r.input.value).value; return out; },

        count() {
            let n = 0, total = 0;
            for (const r of rows.values()) if (r.valueBox.value.trim()) { total++; if (checkSourceUrl(r.input.value).value && checkSourceUrl(r.input.value).ok) n++; }
            return { n, total };
        },
        closeFinder
    };
}


export function countSaved(item) {
    let n = 0, total = 0;
    const values = { purpose: item.purpose, description: item.description, chemicalDescription: item.chemicalDescription, aliases: (item.aliases || []).join(", "), cautionNote: item.cautionNote, commonUses: item.commonUses };
    for (const f of SOURCE_LINK_FIELDS) {
        if (!f.enabled || !String(values[f.key] || "").trim()) continue;
        total++;
        if (item.sourceLinks && item.sourceLinks[f.key]) n++;
    }
    return { n, total };
}
