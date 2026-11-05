// Ingredient classifications: the list in the database, the "Manage classifications" window and the one-time field change.
// The window is the same kind as the "Edit list" window of allergen families, highlights and skin conditions (adminLists.js): the same
// dialog frame, rows and confirm dialogs. Those lists live in one document; classifications have their own collection, because an
// ingredient links to one by its id.
//
// Only an admin can write them (firestore.rules). Everything typed goes on the page with textContent or .value, never as HTML.
import { db } from "../_Database/firebase-console.js";
import { collection, doc, serverTimestamp, deleteField, getDocs as readDocs } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { getDocs, setDoc, updateDoc, deleteDoc, writeBatch, noteReads } from "./adminStore.js";
import { dialogFrame, askConfirm, askText, toast } from "./adminShell.js";
import { collectionFor, isTestHost, FALLBACK_ID, MAX_NAME, cleanName, checkName, usersOf, canRemove, missingStarting, migrationPlan } from "./classificationRules.js";

/** True on any address that is not the live site: the classifications are then a test copy, and nothing that changes ingredients may run. */
export const TEST_MODE = isTestHost(typeof location !== "undefined" ? location.hostname : "");
const COLLECTION = collectionFor(typeof location !== "undefined" ? location.hostname : "");
const BATCH_LIMIT = 400;                              // Firestore allows 500 writes in one batch
let current = [];                                     // [{ id, name }], "Other" last

const many = (n, one, more) => `${n} ${n === 1 ? one : more}`;
const inOrder = (list) => list.slice().sort((a, b) => Number(a.id === FALLBACK_ID) - Number(b.id === FALLBACK_ID) || a.name.localeCompare(b.name, undefined, { sensitivity: "base" }));
const make = (tag, className, content) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (content !== undefined) node.textContent = content;
    return node;
};

/** The list from the database (empty when it does not exist yet or cannot be read: the page still works). */
export async function loadClassifications() {
    try {
        const snapshot = await getDocs(collection(db, COLLECTION));
        current = inOrder(snapshot.docs.map((d) => ({ id: d.id, name: String(d.data().name || "") })));
    } catch (error) {
        console.warn("The classifications could not be read:", error);
        current = [];
    }
    return classifications();
}

export const classifications = () => current.map((item) => ({ ...item }));

/**
 * Adds the starting classifications the list does not have yet. Safe to run again: a name that is there (capital letters and extra
 * spaces ignored) is skipped, and nothing is removed or renamed. Resolves to how many were added.
 */
export async function addStartingClassifications() {
    const missing = missingStarting(current);
    for (let i = 0; i < missing.length; i += BATCH_LIMIT) {
        const batch = writeBatch(db);
        missing.slice(i, i + BATCH_LIMIT).forEach((item) => batch.set(doc(db, COLLECTION, item.id), { name: item.name, createdAt: serverTimestamp(), updatedAt: serverTimestamp() }));
        await batch.commit();
    }
    if (missing.length > 0) current = inOrder(current.concat(missing));
    return missing.length;
}
/** The name of a classification, "" when it does not exist. */
export const classificationName = (id) => { const found = current.find((item) => item.id === id); return found ? found.name : ""; };

/** Adds one. Throws with the words to show when the name is not allowed. Resolves to { id, name }. */
export async function addClassification(typed) {
    const problem = checkName(current, typed, null);
    if (problem) throw new Error(problem);
    const name = cleanName(typed);
    const ref = doc(collection(db, COLLECTION));
    await setDoc(ref, { name, createdAt: serverTimestamp(), updatedAt: serverTimestamp() });
    const made = { id: ref.id, name };
    current = inOrder(current.concat(made));
    return made;
}

/** Asks for a name and adds it. Resolves to the new classification, or null when nothing was added. */
export async function askNewClassification() {
    let problem = "";
    for (;;) {
        const typed = await askText({ title: "Add new classification", label: problem || "Name", confirmLabel: "Add", maxLength: MAX_NAME });
        if (typed === null) return null;
        try { return await addClassification(typed); }
        catch (error) { problem = error.message; if (!/exists|characters|Type a name/.test(problem)) { toast("Not added. " + problem, "error"); return null; } }
    }
}

/**
 * The "Manage classifications" window. `context.ingredients()` = every ingredient, also the ones in the Recycle bin (a restored one must
 * not point at a deleted classification). `context.onChanged()` = draw the page again.
 */
export function openClassificationManager(context) {
    const frame = dialogFrame("Manage classifications", () => frame.close());
    frame.box.classList.add("wide", "lists-box");
    const rows = make("ul", "lists-rows");
    const starting = make("button", "row-btn", "Add the missing starting classifications"); starting.type = "button";
    starting.addEventListener("click", async () => {
        starting.disabled = true;
        try {
            const added = await addStartingClassifications();
            note.textContent = added === 0 ? "Every starting classification is there already." : "";
            if (added > 0) await changed();
        } catch (error) { note.textContent = "Not added. " + error.message; }
        starting.disabled = false;
    });
    const addBox = make("div", "lists-add");
    const addInput = make("input"); addInput.type = "text"; addInput.maxLength = MAX_NAME; addInput.autocomplete = "off"; addInput.placeholder = "New classification"; addInput.setAttribute("aria-label", "New classification");
    const addButton = make("button", "btn primary", "Add"); addButton.type = "button";
    addBox.append(addInput, addButton);
    const note = make("p", "form-error lists-note"); note.setAttribute("role", "alert");
    const actions = make("div", "modal-actions");
    const close = make("button", "cancel-btn", "Close"); close.type = "button";
    close.addEventListener("click", () => frame.close());
    actions.append(close);
    frame.box.append(rows, addBox, starting, note, actions);

    const paint = () => {
        rows.replaceChildren();
        current.forEach((item) => {
            const row = make("li", "lists-row class-row");
            row.append(make("span", "class-row-name", `${item.name} · ${many(usersOf(context.ingredients(), item.id).length, "ingredient", "ingredients")}`));
            const buttons = make("span", "lists-actions");
            const rename = make("button", "row-btn", "Rename"); rename.type = "button";
            rename.addEventListener("click", () => renameOne(item));
            buttons.append(rename);
            if (canRemove(item.id)) {
                const remove = make("button", "row-btn danger", "Remove"); remove.type = "button";
                remove.addEventListener("click", () => removeOne(item));
                buttons.append(remove);
            } else buttons.append(make("span", "field-hint", "cannot be removed"));
            row.append(buttons);
            rows.appendChild(row);
        });
    };
    const changed = async () => { paint(); await context.onChanged(); };

    async function renameOne(item) {
        const typed = await askText({ title: `Rename "${item.name}"`, label: "Name", value: item.name, confirmLabel: "Continue", maxLength: MAX_NAME });
        if (typed === null || cleanName(typed) === item.name) return;
        const problem = checkName(current, typed, item.id);
        if (problem) { note.textContent = problem; return; }
        const name = cleanName(typed);
        const count = usersOf(context.ingredients(), item.id).length;
        if (!(await askConfirm({ title: "Rename classification?", message: `Rename '${item.name}' to '${name}'? This updates ${many(count, "ingredient", "ingredients")}.`, confirmLabel: "Rename" }))) return;
        try {
            await updateDoc(doc(db, COLLECTION, item.id), { name, updatedAt: serverTimestamp() });       // ingredients link by id: none of them has to be written
            current = inOrder(current.map((c) => (c.id === item.id ? { ...c, name } : c)));
            note.textContent = "";
            await changed();
        } catch (error) { note.textContent = "Not renamed. " + error.message; }
    }

    /** The ingredients move first, in batches; the classification is deleted only after all of them have moved. */
    async function removeOne(item) {
        const users = usersOf(context.ingredients(), item.id);
        let target = "";
        if (users.length === 0) {
            if (!(await askConfirm({ title: "Remove classification?", message: `Remove '${item.name}'? No ingredient uses it.`, confirmLabel: "Remove", danger: true }))) return;
        } else {
            target = await askMoveTarget(item, users.length);
            if (!target) return;
        }
        try {
            for (let i = 0; i < users.length; i += BATCH_LIMIT) {
                const batch = writeBatch(db);
                users.slice(i, i + BATCH_LIMIT).forEach((user) => batch.update(doc(db, "ingredients", user.id), { classificationId: target, updatedAt: serverTimestamp() }));
                await batch.commit();
            }
            users.forEach((user) => { user.classificationId = target; });
            await deleteDoc(doc(db, COLLECTION, item.id));
            current = current.filter((c) => c.id !== item.id);
            note.textContent = "";
            await changed();
            toast(users.length > 0 ? `"${item.name}" removed. ${many(users.length, "ingredient", "ingredients")} moved to "${classificationName(target)}".` : `"${item.name}" removed.`, "ok");
        } catch (error) { note.textContent = "Not removed. " + error.message; await context.onChanged(); }
    }

    /** "N ingredients use 'X'. Move them to:" Resolves to the chosen id, or "" when cancelled. */
    function askMoveTarget(item, count) {
        return new Promise((resolve) => {
            const done = (answer) => { move.close(); resolve(answer); };
            const move = dialogFrame(`Remove "${item.name}"`, () => done(""));
            const label = make("label", "field-label", `${many(count, "ingredient uses", "ingredients use")} '${item.name}'. Move them to:`);
            const select = make("select");
            select.id = "classificationMoveTo";
            label.htmlFor = select.id;
            current.filter((c) => c.id !== item.id).forEach((c) => { const option = make("option", "", c.name); option.value = c.id; select.appendChild(option); });
            select.value = FALLBACK_ID;
            const buttons = make("div", "modal-actions");
            const cancel = make("button", "cancel-btn", "Cancel"); cancel.type = "button";
            const confirm = make("button", "save-btn", "Move and remove"); confirm.type = "button";
            cancel.addEventListener("click", () => done(""));
            confirm.addEventListener("click", () => done(select.value));
            buttons.append(cancel, confirm);
            const field = make("div", "field");
            field.append(label, select);
            move.box.append(field, buttons);
            select.focus();
        });
    }

    addButton.addEventListener("click", async () => {
        try {
            await addClassification(addInput.value);
            addInput.value = "";
            note.textContent = "";
            await changed();
            addInput.focus();
        } catch (error) { note.textContent = error.message; }
    });
    addInput.addEventListener("keydown", (event) => { if (event.key === "Enter") { event.preventDefault(); addButton.click(); } });
    paint();
    addInput.focus();
}

// ---------------------------------------------------------------------------------------------
// The one-time field change. It is run by an admin from the Curator, and it can be run again: what was changed already is left alone.
// ---------------------------------------------------------------------------------------------
/** Saves every ingredient, exactly as it is in the database, as a file on this computer. */
function downloadBackup(documents) {
    const stamp = new Date().toISOString().replace(/[:.]/g, "-");
    const url = URL.createObjectURL(new Blob([JSON.stringify(documents, null, 1)], { type: "application/json" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = `ingredients-backup-${stamp}.json`;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60000);
}

/** Shows what the field change will do (it reads the list fresh, about 940 reads, and writes nothing), and does it when confirmed. */
export async function openFieldMigration(onDone) {
    if (TEST_MODE) { toast("This is the test copy of the site: the field change writes live ingredients, so it is switched off here.", "error"); return; }
    let documents;
    try {
        const snapshot = await readDocs(collection(db, "ingredients"));
        noteReads("field change", snapshot.size);
        documents = snapshot.docs.map((d) => ({ id: d.id, ...d.data() }));
    } catch (error) { toast("The ingredients could not be read. " + error.message, "error"); return; }

    const plan = migrationPlan(documents);
    const missing = missingStarting(current);
    const frame = dialogFrame("Change the ingredient fields", () => frame.close());
    frame.box.classList.add("wide");
    const lines = make("ul", "migration-lines");
    [
        `A backup of all ${documents.length} ingredients is saved on this computer first.`,
        `${many(missing.length, "classification is", "classifications are")} added (the starting list).`,
        `${many(plan.moved, "ingredient moves", "ingredients move")} its old INCI value into Also Known As.`,
        `${many(plan.updates.length, "ingredient loses", "ingredients lose")} the old INCI field and ${plan.updates.length === 1 ? "is" : "are"} marked Needs classification. Nothing is guessed.`
    ].forEach((text) => lines.appendChild(make("li", "", text)));
    const samples = plan.updates.filter((u) => u.movedInci).slice(0, 8);
    const sample = make("p", "field-hint", samples.length ? "For example: " + samples.map((u) => `${u.name} (also known as ${u.techName})`).join("; ") + "." : "");
    const note = make("p", "form-error"); note.setAttribute("role", "alert");
    const actions = make("div", "modal-actions");
    const cancel = make("button", "cancel-btn", "Cancel"); cancel.type = "button";
    const run = make("button", "save-btn", "Save the backup and change"); run.type = "button";
    cancel.addEventListener("click", () => frame.close());
    actions.append(cancel, run);
    frame.box.append(lines, sample, note, actions);

    run.addEventListener("click", async () => {
        run.disabled = true; cancel.disabled = true;
        run.textContent = "Working...";
        try {
            downloadBackup(documents);
            for (let i = 0; i < missing.length; i += BATCH_LIMIT) {
                const batch = writeBatch(db);
                missing.slice(i, i + BATCH_LIMIT).forEach((item) => batch.set(doc(db, COLLECTION, item.id), { name: item.name, createdAt: serverTimestamp(), updatedAt: serverTimestamp() }));
                await batch.commit();
            }
            for (let i = 0; i < plan.updates.length; i += BATCH_LIMIT) {
                const batch = writeBatch(db);
                plan.updates.slice(i, i + BATCH_LIMIT).forEach((u) => {
                    const data = { aliases: u.aliases, updatedAt: serverTimestamp() };
                    if (u.hasTech) data.techName = deleteField();
                    if (!u.marked) data.classificationId = "";
                    if (!u.hasPurpose) data.purpose = "";
                    batch.update(doc(db, "ingredients", u.id), data);
                });
                await batch.commit();
            }
            frame.close();
            toast(`Done. ${many(plan.moved, "old INCI value", "old INCI values")} moved into Also Known As; ${many(plan.updates.length, "ingredient needs", "ingredients need")} a classification.`, "ok");
            await loadClassifications();
            await onDone();
        } catch (error) {
            console.error("The field change stopped:", error);
            note.textContent = "It stopped: " + error.message + " Nothing is lost: run it again and it finishes the rest. If the message says permissions, the new database rules are not published yet.";
            run.disabled = false; cancel.disabled = false;
            run.textContent = "Save the backup and change";
        }
    });
}
