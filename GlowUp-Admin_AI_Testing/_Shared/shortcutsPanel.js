// KEYBOARD SHORTCUTS in Quick settings (9 November 2026): the list (grouped by page) and the Add / Edit dialog.
//
// The dialog, top to bottom: page, action, the "Press keys…" field, what is wrong or risky with the combo (right under the field,
// with the choices it needs), the Test, the description (required, 120 characters), and Cancel / Save in the footer.
// Save stays off until the combo is allowed, every warning has an answer, and the Test was passed (or "Save anyway" was chosen,
// which puts ⚠ on the row). Escape while recording only stops the recording; Escape, the X or the dark area otherwise close the
// dialog, after "Discard changes?" when something was changed. Focus goes back to the button that opened the dialog.
import { icon, toast, askConfirm } from "./adminShell.js";
import { CATALOG, PAGES, PAGE_ORDER, catalogById } from "./shortcutCatalog.js";
import { comboFromEvent, comboText, comboLabels, checkCombo, detectBrowser, isMac, REASONS, testVerdict } from "./shortcutKeys.js";
import { shortcutState, onShortcuts, loadShortcuts, saveShortcuts, resetShortcuts, whyFailed } from "./shortcutStore.js";
import { keyChips, setRecording, trapTab, textWithChips } from "./shortcuts.js";

const DESCRIPTION_MAX = 120;
const TEST_MS = 5000;
let host = null, watching = false, busy = false;

const el = (tag, cls, text) => { const n = document.createElement(tag); if (cls) n.className = cls; if (text !== undefined) n.textContent = text; return n; };
const labelOf = (id) => (catalogById.get(id) || {}).label || id;
const offline = () => typeof navigator !== "undefined" && navigator.onLine === false;

// ---------------------------------------------------------------------------------------------
// The list
// ---------------------------------------------------------------------------------------------
/** Draws the Keyboard shortcuts view into `into` (the settings window calls it each time the view is shown). */
export function paintShortcutsView(into) {
    host = into;
    if (!watching) {
        watching = true;
        onShortcuts(() => { if (host && !host.hidden) paint(); });
        window.addEventListener("online", () => { if (host && !host.hidden) paint(); });
        window.addEventListener("offline", () => { if (host && !host.hidden) paint(); });
    }
    paint();
    loadShortcuts(false);
}

function paint() {
    if (!host) return;
    const state = shortcutState();
    const keepFocus = document.activeElement && host.contains(document.activeElement) ? document.activeElement.dataset.kbFocus : "";
    const nodes = [];
    const locked = offline() || state.denied;

    const intro = el("p", "drawer-note", "");
    intro.append("Press ", keyChips("?"), " on any page to see its shortcuts, and ", keyChips("Ctrl+KeyK"), " for the command palette. A shortcut only works on its own page.");
    nodes.push(intro);
    if (offline()) nodes.push(note("warn", "You're offline. Shortcuts can be changed once you're back online."));
    if (state.denied) nodes.push(note("bad", "Only admins can change keyboard shortcuts. Sign in again with an admin account."));
    if (state.error && !state.denied && !offline()) {
        const box = note("bad", "The shortcuts could not be loaded. " + state.error);
        const retry = el("button", "row-btn", "Retry"); retry.type = "button"; retry.dataset.kbFocus = "retry";
        retry.addEventListener("click", () => loadShortcuts(true));
        box.appendChild(retry);
        nodes.push(box);
    }
    const actions = el("div", "drawer-actions kb-actions");
    const add = button("btn primary", "plus", "Add shortcut", () => openEditor({ scope: "" }), "add");
    const reset = button("btn", "reset", "Reset to defaults", confirmReset, "reset");
    add.disabled = reset.disabled = locked || !state.loaded;
    actions.append(add, reset);
    nodes.push(actions);

    PAGE_ORDER.forEach((page) => {
        const group = el("section", "kb-group");
        group.setAttribute("aria-label", PAGES[page]);
        group.appendChild(el("div", "menu-heading", PAGES[page]));
        if (!state.loaded) {                                  // only the data is grey: the page names are not data
            for (let i = 0; i < (page === "inventory" ? 3 : 1); i++) group.appendChild(skeletonRow());
            nodes.push(group);
            return;
        }
        const rows = state.list.filter((s) => s.pageScope === page)
            .sort((a, b) => CATALOG.findIndex((c) => c.id === a.actionId) - CATALOG.findIndex((c) => c.id === b.actionId));
        if (rows.length === 0) {
            const empty = el("div", "kb-empty-group");
            empty.appendChild(el("span", "field-hint", "No shortcuts here."));
            const more = button("row-btn", "plus", "Add shortcut", () => openEditor({ scope: page }), "add-" + page);
            more.disabled = locked;
            empty.appendChild(more);
            group.appendChild(empty);
        }
        rows.forEach((s) => group.appendChild(row(s, locked)));
        nodes.push(group);
    });
    host.replaceChildren(...nodes);
    if (keepFocus) { const again = host.querySelector(`[data-kb-focus="${CSS.escape(keepFocus)}"]`); if (again) again.focus({ preventScroll: true }); }
}

function note(kind, text) { const n = el("div", "kb-note " + kind); n.setAttribute("role", kind === "bad" ? "alert" : "status"); n.innerHTML = icon("alert"); n.appendChild(el("span", "", text)); return n; }
function button(cls, iconName, label, run, focusKey) {
    const b = el("button", cls); b.type = "button";
    b.innerHTML = iconName ? icon(iconName) : "";
    if (label) b.appendChild(document.createTextNode(label));
    if (focusKey) b.dataset.kbFocus = focusKey;
    b.addEventListener("click", run);
    return b;
}
function skeletonRow() {
    const r = el("div", "kb-row is-loading");
    r.innerHTML = '<div class="kb-row-main"><i class="sk sk-text w45"></i><i class="sk sk-text w30"></i><i class="sk sk-text w70 thin"></i></div><i class="sk sk-pill"></i>';
    r.setAttribute("aria-hidden", "true");
    return r;
}

function row(s, locked) {
    const r = el("div", "kb-row" + (s.enabled ? "" : " is-off"));
    const main = el("div", "kb-row-main");
    const head = el("div", "kb-row-head");
    head.appendChild(el("b", "kb-action", labelOf(s.actionId)));
    head.appendChild(keyChips(s.keys));
    main.appendChild(head);
    main.appendChild(el("p", "kb-desc", s.description));
    if (s.conflictFlag) {                                   // said in words on the row (the site shows no hover labels)
        const warn = el("p", "kb-flag");
        warn.innerHTML = icon("alert");
        warn.appendChild(el("span", "", "May not work in every browser: a browser, the computer or an extension may use this combo."));
        main.appendChild(warn);
    }
    const tools = el("div", "kb-row-tools");
    const toggle = el("button", "switch"); toggle.type = "button";
    toggle.setAttribute("role", "switch"); toggle.setAttribute("aria-checked", String(!!s.enabled));
    toggle.setAttribute("aria-label", `${labelOf(s.actionId)} shortcut ${s.enabled ? "on" : "off"}`);
    toggle.dataset.kbFocus = s.actionId + "-on";
    toggle.innerHTML = "<i></i>";
    toggle.disabled = locked;
    toggle.addEventListener("click", () => saveChange(shortcutState().list.map((x) => x.actionId === s.actionId ? { ...x, enabled: !x.enabled, updatedAt: Date.now() } : x),
        s.enabled ? `"${labelOf(s.actionId)}" is off.` : `"${labelOf(s.actionId)}" is on.`));
    const edit = el("button", "icon-btn small"); edit.type = "button"; edit.innerHTML = icon("edit");
    edit.setAttribute("aria-label", `Edit the ${labelOf(s.actionId)} shortcut`); edit.dataset.kbFocus = s.actionId + "-edit"; edit.disabled = locked;
    edit.addEventListener("click", () => openEditor({ existing: s }));
    const del = el("button", "icon-btn small danger"); del.type = "button"; del.innerHTML = icon("trash");
    del.setAttribute("aria-label", `Delete the ${labelOf(s.actionId)} shortcut`); del.dataset.kbFocus = s.actionId + "-del"; del.disabled = locked;
    del.addEventListener("click", () => confirmDelete(s));
    tools.append(toggle, edit, del);
    r.append(main, tools);
    return r;
}

async function saveChange(list, okText) {
    if (busy) return false;
    busy = true;
    try { await saveShortcuts(list); if (okText) toast(okText, "ok"); return true; }
    catch (error) { toast(whyFailed(error), "error"); return false; }
    finally { busy = false; }
}

async function confirmDelete(s) {
    const yes = await askConfirm({
        title: `Delete the "${labelOf(s.actionId)}" shortcut?`,
        message: `${comboText(s.keys)} will do nothing on ${PAGES[s.pageScope]}. You can add a shortcut for it again later.`,
        confirmLabel: "Delete shortcut", danger: true
    });
    if (!yes) { focusIn(s.actionId + "-del"); return; }
    if (await saveChange(shortcutState().list.filter((x) => x.actionId !== s.actionId), "Shortcut deleted.")) focusIn("add");
}

async function confirmReset() {
    const yes = await askConfirm({
        title: "Reset keyboard shortcuts?",
        message: "Every shortcut goes back to GlowUp's defaults. Your own combos, descriptions and switched-off shortcuts are replaced.",
        confirmLabel: "Reset to defaults", danger: true
    });
    if (!yes) { focusIn("reset"); return; }
    busy = true;
    try { await resetShortcuts(); toast("Shortcuts are back to the defaults.", "ok"); }
    catch (error) { toast(whyFailed(error), "error"); }
    finally { busy = false; focusIn("reset"); }
}
function focusIn(key) { requestAnimationFrame(() => { const b = host && host.querySelector(`[data-kb-focus="${CSS.escape(key)}"]`); if (b) b.focus({ preventScroll: true }); }); }

// ---------------------------------------------------------------------------------------------
// The Add / Edit dialog
// ---------------------------------------------------------------------------------------------
/** opts = { existing } to edit a shortcut, or { scope } to add one (scope "" = let the admin choose). */
export function openEditor({ existing = null, scope = "" } = {}) {
    const browser = detectBrowser(), mac = isMac();
    const returnKey = existing ? existing.actionId + "-edit" : scope ? "add-" + scope : "add";
    const first = existing ? { scope: existing.pageScope, actionId: existing.actionId, keys: existing.keys, description: existing.description }
        : { scope: scope || "inventory", actionId: "", keys: "", description: "" };
    const f = { ...first, answers: {}, test: existing ? "kept" : "", conflictFlag: existing ? existing.conflictFlag : false };
    // f.test: "" (not yet) | "waiting" | "ok" | "missed" | "anyway" | "kept" (an existing combo that was not changed)

    const overlay = el("div", "modal-overlay kb-layer");
    overlay.style.display = "flex";
    const box = el("div", "modal-content xl kb-modal kb-editor");
    box.setAttribute("role", "dialog"); box.setAttribute("aria-modal", "true"); box.setAttribute("aria-labelledby", "kbEdTitle");
    box.innerHTML = `
        <div class="xl-head"><div class="xl-titles"><h3 id="kbEdTitle"></h3><p class="modal-intro">A shortcut only works on its own page. It does nothing while you type in a box or while a window is open.</p></div>
            <button type="button" class="icon-btn small" data-x aria-label="Close">${icon("x")}</button></div>
        <form class="xl-body modal-form kb-form" novalidate>
            <div class="form-row">
                <div class="field"><label for="kbPage">1. Page</label><select id="kbPage"></select></div>
                <div class="field"><label for="kbAction">2. Action</label><select id="kbAction"></select></div>
            </div>
            <div class="field">
                <label id="kbKeysLabel" for="kbRecord">3. Keys</label>
                <button type="button" class="kb-record" id="kbRecord" aria-describedby="kbKeysHint kbMsgs"></button>
                <span class="field-hint" id="kbKeysHint">Hold Ctrl, Alt or Shift and press a letter or number. Backspace clears. Esc stops recording.</span>
                <span class="sr-only" id="kbHeard" aria-live="assertive"></span>
                <div class="kb-collapse" id="kbMsgsWrap"><div class="kb-msgs" id="kbMsgs" aria-live="polite"></div></div>
            </div>
            <div class="field">
                <span class="field-label">4. Test</span>
                <div class="kb-test" id="kbTest" aria-live="polite"></div>
            </div>
            <div class="field">
                <label for="kbDesc">5. Description <span class="required">*</span></label>
                <textarea id="kbDesc" rows="2" maxlength="${DESCRIPTION_MAX}" placeholder="What this shortcut does, in a few words."></textarea>
                <span class="field-hint"><span id="kbDescCount"></span> · Shown in the "?" help and the command palette.</span>
            </div>
        </form>
        <div class="xl-foot">
            <div class="kb-save-error" id="kbSaveError" role="alert" hidden></div>
            <span class="spacer"></span>
            <button type="button" class="cancel-btn" data-cancel>Cancel</button>
            <button type="button" class="save-btn" data-save disabled>Save</button>
        </div>`;
    box.querySelector("#kbEdTitle").textContent = existing ? "Edit shortcut" : "Add shortcut";
    overlay.appendChild(box);
    document.body.appendChild(overlay);
    const q = (s) => box.querySelector(s);
    const pageSel = q("#kbPage"), actionSel = q("#kbAction"), rec = q("#kbRecord"), msgs = q("#kbMsgs"), wrap = q("#kbMsgsWrap"),
        test = q("#kbTest"), desc = q("#kbDesc"), count = q("#kbDescCount"), save = q("[data-save]"), saveErr = q("#kbSaveError"), heard = q("#kbHeard");

    PAGE_ORDER.forEach((p) => { const o = el("option", "", PAGES[p]); o.value = p; pageSel.appendChild(o); });
    pageSel.value = f.scope;
    const fillActions = () => {
        const list = shortcutState().list;
        actionSel.replaceChildren(...CATALOG.filter((a) => a.scope === pageSel.value).map((a) => {
            const bound = list.find((s) => s.actionId === a.id);
            const o = el("option", "", a.label + (bound && (!existing || existing.actionId !== a.id) ? ` (has ${comboText(bound.keys, { mac })})` : ""));
            o.value = a.id; return o;
        }));
        if (!CATALOG.some((a) => a.id === f.actionId && a.scope === pageSel.value)) {
            // a new action: its first one without a shortcut, else the first one
            const free = CATALOG.find((a) => a.scope === pageSel.value && !list.some((s) => s.actionId === a.id));
            f.actionId = (free || CATALOG.find((a) => a.scope === pageSel.value) || {}).id || "";
        }
        actionSel.value = f.actionId;
    };
    fillActions();
    let descTouched = !!existing;
    desc.value = f.description || (catalogById.get(f.actionId) || {}).description || "";
    const paintCount = () => { count.textContent = `${desc.value.length} / ${DESCRIPTION_MAX}`; };
    paintCount();

    // ---- recording ----
    let recording = false, partial = "";
    const paintRecord = () => {
        rec.replaceChildren();
        rec.classList.toggle("is-recording", recording);
        if (recording && !partial) rec.appendChild(el("span", "kb-placeholder", "Press keys…"));
        else if (recording) rec.appendChild(partialChips(partial));
        else if (f.keys) rec.appendChild(keyChips(f.keys));
        else rec.appendChild(el("span", "kb-placeholder", "Press keys…"));
        rec.setAttribute("aria-label", recording ? "Recording: press the keys now" : f.keys ? "Keys: " + comboText(f.keys, { mac }) + ". Press Enter to record again." : "Keys: none. Press Enter to record.");
    };
    // the live chips for modifiers only ("Ctrl+Alt+") use the same formatter, without the last key
    const partialChips = (mods) => { const box2 = el("span", "kb-combo is-partial"); comboLabels(mods + "+KeyA", { mac }).slice(0, -1).forEach((l) => box2.appendChild(el("kbd", "kb-key", l))); box2.appendChild(el("span", "kb-dots", "…")); return box2; };
    const startRecording = () => { if (recording) return; stopTest(); recording = true; partial = ""; setRecording(true); paintRecord(); rec.focus(); };
    const stopRecording = () => { if (!recording) return; recording = false; partial = ""; setRecording(testing()); paintRecord(); };
    rec.addEventListener("click", startRecording);
    rec.addEventListener("keydown", (event) => { if (!recording && (event.key === "Enter" || event.key === " ")) { event.preventDefault(); startRecording(); } });
    rec.addEventListener("blur", () => stopRecording());

    // ---- checks, warnings, test ----
    const others = () => shortcutState().list.filter((s) => !existing || s.actionId !== existing.actionId);
    const evaluate = () => {
        msgs.replaceChildren();
        if (!f.keys) { wrap.classList.remove("open"); return []; }
        const found = checkCombo({ keys: f.keys, actionId: f.actionId, scope: pageSel.value, shortcuts: others(), actions: Object.fromEntries(CATALOG.map((a) => [a.id, a])), pages: PAGES, browser, mac });
        found.forEach((m) => msgs.appendChild(message(m)));
        wrap.classList.toggle("open", found.length > 0);
        return found;
    };
    const message = (m) => {
        const box3 = el("div", "kb-msg " + m.level);
        box3.dataset.code = m.code;
        box3.innerHTML = icon(m.level === "info" ? "bulb" : "alert");
        const body = el("div", "kb-msg-body");
        const words = el("p", ""); words.appendChild(textWithChips(m.text)); body.appendChild(words);
        const choices = el("div", "kb-msg-actions");
        const choice = (label, run, primary) => { const b = el("button", primary ? "btn primary small" : "btn small", label); b.type = "button"; b.addEventListener("click", run); choices.appendChild(b); };
        const answered = f.answers[m.code];
        if (m.level === "warn" && answered) body.appendChild(el("p", "kb-answer", answered === "replace" ? "✓ It will be replaced when you save." : "✓ You chose to save it anyway."));
        else if (m.code === REASONS.IN_USE_SAME_PAGE && m.level === "warn") { choice("Replace it", () => answer(m.code, "replace"), true); choice("Choose another combo", chooseAnother); }
        else if (m.code === REASONS.ACTION_ALREADY_BOUND) { choice("Replace", () => answer(m.code, "replace"), true); choice("Keep the old combo", keepOld); }
        else if (m.code === REASONS.BROWSER_CONFLICT) { choice("Save anyway", () => answer(m.code, "anyway")); choice("Choose another combo", chooseAnother, true); }
        else if (m.level === "block") choice("Choose another combo", chooseAnother, true);
        if (choices.childElementCount) body.appendChild(choices);
        box3.appendChild(body);
        return box3;
    };
    const answer = (code, value) => { f.answers[code] = value; refresh(); };
    const chooseAnother = () => { f.keys = ""; f.answers = {}; f.test = ""; f.conflictFlag = false; refresh(); startRecording(); };
    const keepOld = () => { f.keys = existing ? existing.keys : ""; f.answers = {}; f.test = existing ? "kept" : ""; refresh(); rec.focus(); };

    let testTimer = 0, testListener = null;
    const testing = () => !!testListener;
    function stopTest() { clearTimeout(testTimer); if (testListener) document.removeEventListener("keydown", testListener, true); testListener = null; setRecording(recording); }
    const runTest = () => {
        stopTest();
        f.test = "waiting";
        setRecording(true);
        testListener = (event) => {
            if (event.key === "Escape") return;                 // the dialog's own Esc handling below
            const { keys } = comboFromEvent(event);
            if (!keys) return;
            event.preventDefault(); event.stopPropagation();
            if (keys !== f.keys) return;
            stopTest(); f.test = "ok"; paintTest(); paintSave();
            heard.textContent = `Works in ${browser}.`;
        };
        document.addEventListener("keydown", testListener, true);
        testTimer = setTimeout(() => { stopTest(); f.test = "missed"; paintTest(); paintSave(); }, TEST_MS);
        paintTest();
    };
    const paintTest = () => {
        test.replaceChildren();
        test.className = "kb-test";
        const blocked = currentChecks.some((m) => m.level === "block");
        if (!f.keys || blocked) { test.appendChild(el("span", "field-hint", f.keys ? "Choose a combo that can be used first." : "Record a combo first.")); return; }
        if (f.test === "kept") { test.appendChild(el("span", "field-hint", "This combo is already saved.")); return; }
        if (f.test === "waiting") {
            test.classList.add("is-waiting");
            const t = el("p", "");
            t.append("Press ", keyChips(f.keys), " again to test it.");
            test.appendChild(t);
            test.appendChild(el("span", "kb-bar"));
            return;
        }
        if (f.test === "ok") { test.classList.add("ok"); test.appendChild(el("p", "", testVerdict(true, f.keys, browser).text)); return; }
        if (f.test === "missed" || f.test === "anyway") {
            test.classList.add(f.test === "anyway" ? "anyway" : "missed");
            const box4 = el("div", "kb-msg warn");
            box4.dataset.code = REASONS.TEST_NOT_RECEIVED;
            box4.innerHTML = icon("alert");
            const body = el("div", "kb-msg-body");
            const words = el("p", ""); words.appendChild(textWithChips(testVerdict(false, f.keys, browser, { mac }).text)); body.appendChild(words);
            if (f.test === "anyway") body.appendChild(el("p", "kb-answer", "✓ You chose to save it anyway. It gets ⚠ in the list."));
            else {
                const choices = el("div", "kb-msg-actions");
                const b1 = el("button", "btn primary small", "Try again"); b1.type = "button"; b1.addEventListener("click", runTest);
                const b2 = el("button", "btn small", "Choose another combo"); b2.type = "button"; b2.addEventListener("click", chooseAnother);
                const b3 = el("button", "btn small", "Save anyway"); b3.type = "button"; b3.addEventListener("click", () => { f.test = "anyway"; paintTest(); paintSave(); });
                choices.append(b1, b2, b3);
                body.appendChild(choices);
            }
            box4.appendChild(body);
            test.appendChild(box4);
            return;
        }
        const start = el("button", "btn", "Test it"); start.type = "button"; start.addEventListener("click", runTest);
        test.appendChild(start);
    };

    let currentChecks = [];
    const ready = () => !!f.actionId && !!f.keys && desc.value.trim().length > 0
        && !currentChecks.some((m) => m.level === "block")
        && currentChecks.filter((m) => m.level === "warn").every((m) => f.answers[m.code])
        && ["ok", "anyway", "kept"].includes(f.test);
    const paintSave = () => { save.disabled = !ready() || saving; };
    const refresh = () => { currentChecks = evaluate(); paintRecord(); paintTest(); paintSave(); };

    // ---- keys while the dialog is on top ----
    const onKey = (event) => {
        const top = Array.from(document.querySelectorAll(".modal-overlay")).filter((o) => getComputedStyle(o).display !== "none").pop();
        if (top !== overlay) return;
        if (recording) {
            if (event.key === "Tab" && !event.ctrlKey && !event.altKey && !event.metaKey) { stopRecording(); return; }
            event.preventDefault(); event.stopPropagation();
            if (event.key === "Escape") { stopRecording(); heard.textContent = "Recording stopped."; return; }       // the FIRST Esc only stops recording
            if (event.key === "Backspace" && !event.ctrlKey && !event.altKey && !event.metaKey && !event.shiftKey) { f.keys = ""; f.answers = {}; f.test = ""; partial = ""; refresh(); heard.textContent = "Cleared."; return; }
            if (event.repeat) return;
            const { keys, partial: mods } = comboFromEvent(event);
            if (!keys) { partial = mods; rec.replaceChildren(mods ? partialChips(mods) : el("span", "kb-placeholder", "Press keys…")); return; }
            f.keys = keys; f.answers = {}; f.conflictFlag = false; f.test = existing && keys === existing.keys ? "kept" : "";
            stopRecording();
            heard.textContent = "Recorded " + comboText(keys, { mac }) + ".";
            refresh();
            if (!currentChecks.some((m) => m.level === "block") && f.test !== "kept") runTest();
            return;
        }
        if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); if (testing()) { stopTest(); f.test = ""; paintTest(); paintSave(); return; } tryClose(); return; }
        if (event.key === "Tab") trapTab(event, box);
    };
    document.addEventListener("keydown", onKey, true);

    // ---- changes in the boxes ----
    pageSel.addEventListener("change", () => { f.scope = pageSel.value; fillActions(); if (!descTouched) desc.value = (catalogById.get(f.actionId) || {}).description || ""; paintCount(); refresh(); });
    actionSel.addEventListener("change", () => { f.actionId = actionSel.value; if (!descTouched) desc.value = (catalogById.get(f.actionId) || {}).description || ""; paintCount(); f.answers = {}; refresh(); });
    desc.addEventListener("input", () => { descTouched = true; paintCount(); paintSave(); });

    // ---- closing ----
    const dirty = () => pageSel.value !== first.scope || actionSel.value !== first.actionId || f.keys !== first.keys || desc.value !== (first.description || desc.defaultValue || desc.value);
    let closing = false;
    const close = () => {
        stopTest(); stopRecording(); setRecording(false);
        document.removeEventListener("keydown", onKey, true);
        overlay.remove();
        focusIn(returnKey);
    };
    const tryClose = async () => {
        if (closing || saving) return;
        if (!dirty()) { close(); return; }
        closing = true;
        const discard = await askConfirm({ title: "Discard changes?", message: "The shortcut you were setting up is not saved.", confirmLabel: "Discard", cancelLabel: "Keep editing", danger: true });
        closing = false;
        if (discard) close(); else rec.focus();
    };
    q("[data-x]").addEventListener("click", tryClose);
    q("[data-cancel]").addEventListener("click", tryClose);
    overlay.addEventListener("mousedown", (event) => { if (event.target === overlay) tryClose(); });

    // ---- saving ----
    let saving = false;
    const doSave = async () => {
        if (!ready() || saving) return;
        saving = true; paintSave(); saveErr.hidden = true;
        save.textContent = "Saving…";
        const now = Date.now();
        const action = catalogById.get(f.actionId);
        let list = shortcutState().list.filter((s) => s.actionId !== f.actionId && (!existing || s.actionId !== existing.actionId));
        if (f.answers[REASONS.IN_USE_SAME_PAGE] === "replace") {
            const owner = currentChecks.find((m) => m.code === REASONS.IN_USE_SAME_PAGE && m.level === "warn");
            if (owner) list = list.filter((s) => s.actionId !== owner.owner);          // the other action loses the combo
        }
        const text = desc.value.replace(/\s+/g, " ").trim().slice(0, DESCRIPTION_MAX);
        list.push({
            actionId: f.actionId, pageScope: action.scope, keys: f.keys, description: text,
            enabled: existing && existing.actionId === f.actionId ? existing.enabled : true,
            isDefault: f.keys === action.keys && text === action.description,
            conflictFlag: f.test === "anyway" || f.answers[REASONS.BROWSER_CONFLICT] === "anyway" || (f.test === "kept" && !!(existing && existing.conflictFlag)),
            createdAt: existing ? existing.createdAt : now, updatedAt: now
        });
        try {
            await saveShortcuts(list);
            saving = false;
            close();
            toast(existing ? "Shortcut saved." : "Shortcut added.", "ok");
        } catch (error) {
            saving = false;
            save.textContent = "Save";
            saveErr.replaceChildren(el("span", "", whyFailed(error)));
            saveErr.dataset.code = REASONS.SAVE_FAILED;
            const retry = el("button", "row-btn", "Retry"); retry.type = "button"; retry.addEventListener("click", doSave);
            saveErr.appendChild(retry);
            saveErr.hidden = false;
            paintSave();
        }
    };
    save.addEventListener("click", doSave);
    q("form").addEventListener("submit", (event) => { event.preventDefault(); doSave(); });

    refresh();
    (existing ? rec : actionSel).focus();
    return { overlay, close };
}
