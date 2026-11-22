// "Use my phone": the computer shows a QR code, the admin opens it on their phone (signed in with the same admin account),
// scans a barcode or photographs the label there, and the result appears in the form on the computer.
// One short-lived document per link: phoneLinks/{code}. Only the admin who made it can read or write it (firestore.rules),
// it stops taking values after 15 minutes, and closing the window deletes it. No Cloud Functions needed.
import { db, auth } from "../_Database/firebase-console.js";
import { doc, setDoc, deleteDoc, onSnapshot, serverTimestamp } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { dialogFrame, icon } from "./adminShell.js";

const QR_LIB = "https://cdn.jsdelivr.net/npm/qrcode-generator@1.4.4/qrcode.js";
export const LINK_MINUTES = 15;
export const PHONE_PAGE = "../_Admin_Phone_Scan/phoneScan.html";
const ALPHABET = "abcdefghijkmnpqrstuvwxyz23456789";

export function newLinkCode(length = 12) {
    const bytes = new Uint8Array(length);
    crypto.getRandomValues(bytes);
    return Array.from(bytes, (b) => ALPHABET[b % ALPHABET.length]).join("");
}

let qrLoading = null;
function loadQr() {
    if (window.qrcode) return Promise.resolve();
    if (!qrLoading) qrLoading = new Promise((resolve, reject) => {
        const s = document.createElement("script");
        s.src = QR_LIB; s.async = true; s.crossOrigin = "anonymous";
        s.onload = resolve; s.onerror = () => { qrLoading = null; reject(new Error("QR code library did not load")); };
        document.head.appendChild(s);
    });
    return qrLoading;
}

// what: "barcode", "ingredients" or "any" (what the phone page offers first). onValue({ kind, value }) runs for every value sent.
export function openPhoneLink({ title, what = "any", onValue }) {
    const user = auth.currentUser;
    if (!user) return;
    const code = newLinkCode();
    const ref = doc(db, "phoneLinks", code);
    const url = new URL(`${PHONE_PAGE}?c=${code}&w=${encodeURIComponent(what)}`, window.location.href).href;
    let unsubscribe = () => {}, lastCount = 0, closed = false;

    const close = () => {
        if (closed) return;
        closed = true;
        unsubscribe();
        frame.close();
        deleteDoc(ref).catch(() => {});
    };
    const frame = dialogFrame(title || "Use your phone", close);
    frame.box.classList.add("qf-phone");
    frame.box.insertAdjacentHTML("beforeend", `
        <p class="modal-intro">Scan this code with your phone's camera. Sign in there with the same admin account, then scan the barcode or take a photo of the ingredient list. It appears here by itself.</p>
        <div class="qf-qr" aria-hidden="true"><span class="sk sk-qr"></span></div>
        <p class="field-hint qf-link"></p>
        <p class="field-hint qf-state" role="status" aria-live="polite">Waiting for the phone...</p>
        <div class="modal-actions"><button type="button" class="save-btn" data-act="done">Done</button></div>`);
    frame.box.querySelector(".qf-link").textContent = `Or open this address on the phone: ${url}`;
    const state = frame.box.querySelector(".qf-state");
    frame.box.querySelector('[data-act="done"]').addEventListener("click", close);

    loadQr().then(() => {
        if (closed) return;
        const qr = window.qrcode(0, "M");
        qr.addData(url); qr.make();
        frame.box.querySelector(".qf-qr").innerHTML = qr.createSvgTag({ cellSize: 5, margin: 2, scalable: true });
    }).catch(() => { frame.box.querySelector(".qf-qr").textContent = "The QR code could not be drawn. Type the address below on the phone."; });

    const expiresAtUnix = Math.floor(Date.now() / 1000) + LINK_MINUTES * 60;
    setDoc(ref, { uid: user.uid, what, kind: "", value: "", count: 0, createdAt: serverTimestamp(), expiresAtUnix })
        .then(() => {
            if (closed) { deleteDoc(ref).catch(() => {}); return; }
            unsubscribe = onSnapshot(ref, (snap) => {
                const data = snap.exists() ? snap.data() : null;
                if (!data) return;
                if (data.phoneSeen && lastCount === 0 && !data.count) state.textContent = "The phone is connected. Scan or take the photo there.";
                if (typeof data.count === "number" && data.count > lastCount) {
                    lastCount = data.count;
                    state.innerHTML = `${icon("check")} Received from the phone. You can send another one, or press Done.`;
                    try { onValue({ kind: String(data.kind || ""), value: String(data.value || "") }); } catch (error) { console.error(error); }
                }
            }, () => { state.textContent = "The link stopped. Close this and try again."; });
        })
        .catch(() => { state.textContent = "The phone link could not start. The database rules may not allow it yet."; });
    return { close };
}
