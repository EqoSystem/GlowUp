// The phone side of "Use my phone" (see _Shared/phoneLink.js): scan a barcode or photograph the ingredient list,
// and the value goes to the form open on the computer. Needs the same admin account as the computer.
import { db, auth } from "../_Database/firebase-console.js";
import { onAuthStateChanged } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-auth.js";
import { doc, getDoc, updateDoc, increment, serverTimestamp } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { isAdmin, LOGIN_PAGE } from "../_Shared/adminCommon.js";
import { scanBarcode, readLabelPhoto } from "../_Shared/quickFill.js";

export const AFTER_LOGIN_KEY = "glowup.afterLogin";
const el = (id) => document.getElementById(id);
const params = new URLSearchParams(location.search);
const code = String(params.get("c") || "").replace(/[^a-z0-9]/g, "").slice(0, 20);
const what = params.get("w") || "any";
let ref = null;

const say = (text, bad) => { el("psState").textContent = text; el("psState").classList.toggle("bad", !!bad); };
const busy = (on) => { el("psBarcode").disabled = on; el("psLabel").disabled = on; };
function sentLine(text) { const li = document.createElement("li"); li.textContent = text; el("psSent").prepend(li); }

async function send(kind, value) {
    try {
        await updateDoc(ref, { kind, value: String(value).slice(0, 4000), count: increment(1), sentAt: serverTimestamp() });
        return true;
    } catch (error) {
        say("It was not sent. The link may have ended: open a new QR code on the computer.", true);
        return false;
    }
}

el("psBarcode").addEventListener("click", async () => {
    busy(true); say("");
    const digits = await scanBarcode({ title: "Scan the barcode" });
    if (digits && (await send("barcode", digits))) { say("Sent. Look at the computer."); sentLine(`Barcode ${digits}`); }
    busy(false);
});
el("psLabel").addEventListener("click", async () => {
    busy(true); say("");
    try {
        const read = await readLabelPhoto((text) => say(text));
        if (!read) { say(""); return; }
        if (read.ingredients.length < 2) { say("No ingredient list was found in that photo. Take it closer and straight on, with the whole list sharp.", true); return; }
        if (await send("ingredients", read.ingredients.join(", "))) { say(`Sent ${read.ingredients.length} ingredients. Look at the computer.`); sentLine(`${read.ingredients.length} ingredients: ${read.ingredients.slice(0, 4).join(", ")}...`); }
    } catch (error) { say(error.message || "The photo could not be read.", true); }
    finally { busy(false); }
});

onAuthStateChanged(auth, async (user) => {
    if (!code) { el("psSub").textContent = "This address is not complete. Scan the QR code on the computer again."; return; }
    if (!user) {
        try { sessionStorage.setItem(AFTER_LOGIN_KEY, location.pathname + location.search); } catch (error) {                     }
        location.replace(LOGIN_PAGE);
        return;
    }
    if (!(await isAdmin(user))) { el("psSub").textContent = "This account is not an admin account."; return; }
    ref = doc(db, "phoneLinks", code);
    try {
        const snap = await getDoc(ref);
        if (!snap.exists()) { el("psSub").textContent = "This link has ended. Open a new QR code on the computer."; return; }
        if (snap.data().uid !== user.uid) { el("psSub").textContent = "This link belongs to another admin account."; return; }
        await updateDoc(ref, { phoneSeen: true }).catch(() => {});
    } catch (error) { el("psSub").textContent = "This link has ended or belongs to another account."; return; }
    el("psSub").textContent = `Connected as ${user.email || "admin"}.`;
    el("psActions").hidden = false;
    if (what === "ingredients") el("psActions").prepend(el("psLabel"));
});
