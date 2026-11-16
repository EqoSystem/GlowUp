






import { db } from "../_Database/firebase-console.js";
import { doc, serverTimestamp } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { writeBatch } from "../_Shared/adminStore.js";
import { escapeHtml, sourceKindChip, notify, describeError } from "../_Shared/adminCommon.js";
import { icon, dialogFrame, askConfirm, hideTip } from "../_Shared/adminShell.js";
import {
    ROLES, MAX_SOURCES, BULK_PILOT_BRANDS, bulkAllowed, rolesOf, officialOf, isLocked, sourceUrl, sourceTitle,
    checkUrl, withOfficial, withPrimary, setRow, planApply, chunks
} from "../_Shared/sourceEdit.js";

const many = (n, one, more) => `${n} ${n === 1 ? one : more}`;
const PILOT_NOTE = `Applying to other products is switched on for ${BULK_PILOT_BRANDS.join(", ")} only while it is being tried out.`;






export function mountSourceEditor(opts) {
    let adding = false;
    const host = opts.host;

    const applyButton = (role, url, allowed) => `<button type="button" class="row-btn" data-apply="${role}" data-url="${escapeHtml(url)}"${allowed && url ? "" : " disabled"} data-tip="${escapeHtml(allowed ? (url ? "Choose other products that get this address too. You see how many change and confirm first." : "Type the address first.") : PILOT_NOTE)}">${icon("layers")}Apply to other products</button>`;
    const row = (index, role, line, allowed) => {
        const url = sourceUrl(line);
        return `<div class="source-row">
            <input type="url" class="text-input" maxlength="300" inputmode="url" autocomplete="off" data-row="${index}" value="${escapeHtml(url)}" placeholder="https://..." aria-label="${escapeHtml(ROLES[role])}">
            ${line ? sourceKindChip(line, (opts.brand() || {}).website || "") : ""}
            ${applyButton(role, url, allowed)}
            <button type="button" class="icon-btn small" data-remove="${index}" aria-label="Remove this source" data-tip="Remove this source from this product.">${icon("x")}</button>
        </div>`;
    };

    function paint() {
        const list = opts.sources(), brand = opts.brand();
        const allowed = !!brand && bulkAllowed(brand.name);
        const official = officialOf(list) || (brand ? brand.website : "") || "";
        const roles = rolesOf(list);
        const others = list.map((line, index) => ({ line, index })).filter((x) => x.index !== roles.official && x.index !== roles.primary);
        host.innerHTML = `
            <span class="field-label">Official website</span>
            <div class="source-row">
                <input type="url" class="text-input" id="productOfficialInput" maxlength="300" inputmode="url" autocomplete="off" value="${escapeHtml(official)}" placeholder="https://... (the brand owner's own site, not a shop)" aria-label="Official website">
                <button type="button" class="row-btn" id="applyOfficialBtn"${brand && allowed ? "" : " disabled"} data-tip="${escapeHtml(!brand ? "Choose the brand first." : allowed ? `Every product of ${brand.name} gets this address. You see how many change and confirm first.` : PILOT_NOTE)}">${icon("check")}Apply to all brand products</button>
            </div>
            <span class="field-label">Primary source</span>
            ${roles.primary >= 0 ? row(roles.primary, "primary", list[roles.primary], allowed) : `<div class="source-row"><input type="url" class="text-input" maxlength="300" inputmode="url" autocomplete="off" id="productPrimaryNew" placeholder="https://..." aria-label="Primary source"></div>`}
            <span class="field-label">Secondary and retailer sources</span>
            ${others.map(({ line, index }) => (isLocked(line)
                ? `<div class="source-row locked"><span class="source-fixed">${escapeHtml(sourceTitle(line) || line)}</span>${sourceUrl(line) ? sourceKindChip(line, (brand || {}).website || "") : ""}<span class="field-hint">kept as it is</span></div>`
                : row(index, "secondary", line, allowed))).join("")}
            ${adding ? `<div class="source-row"><input type="url" class="text-input" maxlength="300" inputmode="url" autocomplete="off" id="productSecondaryNew" placeholder="https://..." aria-label="New secondary source"></div>` : ""}
            ${list.length < MAX_SOURCES && !adding ? `<div><button type="button" class="row-btn" id="addSourceBtn">${icon("plus")}Add a source</button></div>` : ""}
            ${brand && !allowed ? `<span class="field-hint">${escapeHtml(PILOT_NOTE)}</span>` : ""}
            <span class="field-hint">Web addresses only (https). Saving the form saves this product's sources. The buttons change other products, after you confirm.</span>`;
    }


    const typed = (input) => {
        const checked = checkUrl(input.value);
        if (checked.error) { opts.setError(checked.error); paint(); return null; }
        opts.setError("");
        return checked.url;
    };

    host.addEventListener("change", (event) => {
        const input = event.target;
        if (input.matches("[data-row]")) {
            const url = typed(input);
            if (url === null) return;
            opts.setSources(setRow(opts.sources(), Number(input.dataset.row), url));
        } else if (input.id === "productOfficialInput") {
            const url = typed(input);
            if (url === null) return;
            opts.setSources(withOfficial(opts.sources(), url));
        } else if (input.id === "productPrimaryNew") {
            const url = typed(input);
            if (url === null) return;
            if (url) opts.setSources(withPrimary(opts.sources(), url));
        } else if (input.id === "productSecondaryNew") {
            const url = typed(input);
            if (url === null) return;
            adding = false;
            if (url) opts.setSources(setRow(opts.sources(), opts.sources().length, url));
        } else return;
        paint();
    });
    host.addEventListener("click", (event) => {
        const button = event.target.closest("button");
        if (!button || button.disabled) return;
        hideTip();
        const brand = opts.brand();
        if (button.id === "addSourceBtn") { adding = true; paint(); host.querySelector("#productSecondaryNew").focus(); }
        else if (button.id === "applyOfficialBtn") {
            const url = checkUrl(host.querySelector("#productOfficialInput").value);
            if (url.error || !url.url) { opts.setError(url.error || "Type the official website first."); return; }
            opts.openApply({ role: "official", url: url.url, brandId: brand.id });
        } else if (button.dataset.apply) opts.openApply({ role: button.dataset.apply, url: button.dataset.url, brandId: brand.id });
        else if (button.dataset.remove !== undefined) { opts.setSources(setRow(opts.sources(), Number(button.dataset.remove), "")); paint(); }
    });
    return { paint, reset: () => { adding = false; } };
}





export function openApplyWindow(opts) {
    const chosen = new Set(opts.chosen || []);
    const fixedBrand = opts.role === "official";
    let role = opts.role, brandId = opts.brandId;
    const brandName = (id) => (opts.brands().find((b) => b.id === id) || {}).name || "this brand";
    if (role === "official" && chosen.size === 0) opts.productsOf(brandId).forEach((p) => chosen.add(p.id));

    const frame = dialogFrame(role === "official" ? `Apply the official website to ${brandName(brandId)}` : "Apply a source to other products", () => frame.close());
    frame.box.classList.add("apply-window");
    const body = document.createElement("div");
    body.className = "apply-body";
    frame.box.appendChild(body);
    const offered = () => opts.brands().filter((b) => bulkAllowed(b.name));
    const nameSort = (a, b) => String(a.name || "").localeCompare(String(b.name || ""), undefined, { sensitivity: "base" });
    const shown = () => opts.productsOf(brandId).slice().sort(nameSort);

    function paint() {
        const list = shown(), inBrand = list.filter((p) => chosen.has(p.id)).length;
        const all = list.length > 0 && inBrand === list.length;
        body.innerHTML = `
            <p class="modal-intro">${role === "official" ? "Every product you tick gets this address as its official website. Its other sources stay." : "Choose the products that get this address. Their other sources stay."}</p>
            <div class="form-row">
                <div class="field"><label for="applyRole">Apply as</label>
                    <select id="applyRole"${fixedBrand ? " disabled" : ""}>${Object.entries(ROLES).filter(([key]) => fixedBrand || key !== "official").map(([key, label]) => `<option value="${key}"${key === role ? " selected" : ""}>${label}</option>`).join("")}</select></div>
                <div class="field"><label for="applyBrand">Brand</label>
                    <select id="applyBrand"${fixedBrand || BULK_PILOT_BRANDS.length > 0 ? " disabled" : ""}>${(fixedBrand ? opts.brands().filter((b) => b.id === brandId) : offered()).slice().sort(nameSort).map((b) => `<option value="${escapeHtml(b.id)}"${b.id === brandId ? " selected" : ""}>${escapeHtml(b.name)}</option>`).join("")}</select></div>
            </div>
            <div class="field"><label for="applyUrl">Web address</label><input type="url" class="text-input" id="applyUrl" maxlength="300" value="${escapeHtml(opts.url)}" autocomplete="off" inputmode="url"></div>
            ${BULK_PILOT_BRANDS.length > 0 ? `<p class="field-hint">${escapeHtml(PILOT_NOTE)}</p>` : ""}
            <label class="check-line apply-all"><input type="checkbox" id="applyAll"${all ? " checked" : ""}> <b>Select all in brand</b></label>
            <div class="apply-list" role="group" aria-label="Products">
                ${list.map((p) => { const now = currentFor(p); return `<label class="apply-item"><input type="checkbox" data-id="${escapeHtml(p.id)}"${chosen.has(p.id) ? " checked" : ""}> <span class="apply-name">${escapeHtml(p.name || "Unnamed Product")}</span>${now ? `<span class="apply-now">now: ${escapeHtml(now)}</span>` : ""}</label>`; }).join("") || `<p class="field-hint">This brand has no products.</p>`}
            </div>
            <p class="apply-count" id="applyCount" role="status">${inBrand} of ${list.length} in ${escapeHtml(brandName(brandId))} selected${chosen.size !== inBrand ? ` · ${chosen.size} selected in all` : ""}</p>
            ${role === "official" ? `<label class="field-hint check-line"><input type="checkbox" id="applyConfirmSite"> Also save it as the confirmed website of ${escapeHtml(brandName(brandId))}: I checked that this brand owns this address. Then its pages are labelled "Manufacturer's own site".</label>` : ""}
            <p class="form-error" id="applyError" role="alert"></p>
            <div class="modal-actions"><button type="button" class="cancel-btn" id="applyCancel">Cancel</button><button type="button" class="save-btn" id="applyGo">Preview the change</button></div>`;
    }
    const currentFor = (p) => {
        const list = Array.isArray(p.sources) ? p.sources : [];
        const roles = rolesOf(list);
        if (role === "official") return roles.official >= 0 ? sourceUrl(list[roles.official]) : "";
        if (role === "primary") return roles.primary >= 0 ? sourceUrl(list[roles.primary]) : "";
        return "";
    };
    const keepTyped = () => { const box = body.querySelector("#applyUrl"); if (box) opts.url = box.value; };

    body.addEventListener("change", (event) => {
        const input = event.target;
        if (input.id === "applyAll") { keepTyped(); shown().forEach((p) => { if (input.checked) chosen.add(p.id); else chosen.delete(p.id); }); paint(); }
        else if (input.dataset.id) { keepTyped(); if (input.checked) chosen.add(input.dataset.id); else chosen.delete(input.dataset.id); paint(); }
        else if (input.id === "applyRole") { keepTyped(); role = input.value; paint(); }
        else if (input.id === "applyBrand") { keepTyped(); brandId = input.value; paint(); }
    });
    body.addEventListener("click", (event) => {
        if (event.target.id === "applyCancel") frame.close();
        else if (event.target.id === "applyGo") preview(event.target);
    });

    async function preview(button) {
        const error = body.querySelector("#applyError");
        const checked = checkUrl(body.querySelector("#applyUrl").value);
        if (checked.error || !checked.url) { error.textContent = checked.error || "Type the web address."; return; }

        const reachable = (fixedBrand ? opts.brands().filter((b) => b.id === brandId) : offered()).flatMap((b) => opts.productsOf(b.id));
        const plan = planApply(reachable, chosen, role, checked.url);
        const confirmSite = role === "official" && !!(body.querySelector("#applyConfirmSite") || {}).checked;
        if (plan.change.length === 0 && !confirmSite) { error.textContent = chosen.size === 0 ? "Tick at least one product." : "Every product you ticked has this address already. Nothing to write."; return; }
        const batches = chunks(plan.change).length;
        const lines = [
            `${many(plan.change.length, "product changes", "products change")}.`,
            plan.same.length ? `${many(plan.same.length, "product has", "products have")} it already and ${plan.same.length === 1 ? "is" : "are"} not written.` : "",
            plan.full.length ? `${many(plan.full.length, "product is", "products are")} left out: too many sources.` : "",
            `Only the list of sources changes. Products you did not tick are not touched${batches > 1 ? ` (${batches} batches)` : ""}.`,
            confirmSite ? `${brandName(brandId)} is recorded with this confirmed website, under your account.` : ""
        ].filter(Boolean);
        const sure = await askConfirm({ title: `${ROLES[role]}: ${checked.url}`, message: lines.join(" "), items: plan.change.map((c) => c.name || "Unnamed Product"), confirmLabel: plan.change.length > 0 ? `Apply to ${many(plan.change.length, "product", "products")}` : "Save the brand's website" });
        if (!sure) return;
        button.disabled = true;
        const written = [];
        try {
            const pieces = chunks(plan.change);
            if (pieces.length === 0) pieces.push([]);
            for (let at = 0; at < pieces.length; at++) {
                const batch = writeBatch(db);
                pieces[at].forEach((change) => batch.update(doc(db, "products", change.id), { sources: change.after, updatedAt: serverTimestamp() }));
                if (at === 0 && confirmSite) batch.update(doc(db, "brands", brandId), { ...opts.confirmWebsite(checked.url), updatedAt: serverTimestamp() });
                await batch.commit();
                written.push(...pieces[at]);
            }
            frame.close();
            notify(`${ROLES[role]} applied to ${many(written.length, "product", "products")}.`, "ok");
        } catch (failure) {
            console.error("Applying a source failed: ", failure);
            notify(`Applied to ${written.length} of ${plan.change.length} products, then the database refused the rest. ${describeError(failure)}`, "error");
            button.disabled = false;
        }
        opts.onSaved({ role, url: checked.url, written, brandId: confirmSite && written.length === plan.change.length ? brandId : "" });
    }
    paint();
}
