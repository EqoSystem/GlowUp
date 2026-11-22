// Quick fill on the admin pages: the phone camera as a barcode scanner, a photo of the label read into an ingredient
// list, and the look-ups (Open Beauty Facts, the FDA Philippines list that ships with the site, PubChem).
// The decisions live in smartAdd.js; this file only talks to the camera, the network and the screen.
import { dialogFrame, icon } from "./adminShell.js";
import {
    queryKind, offProductUrl, offSearchUrl, fromOpenBeautyFacts, searchCatalogue, mergeCandidates,
    pubchemSynonymsUrl, pubchemDescriptionUrl, aliasesFromPubchem, descriptionFromPubchem, ingredientsFromText
} from "./smartAdd.js";

const ZXING = "https://cdn.jsdelivr.net/npm/@zxing/library@0.21.3/umd/index.min.js";
const TESSERACT = "https://cdn.jsdelivr.net/npm/tesseract.js@5.1.1/dist/tesseract.min.js";
const FORMATS = ["ean_13", "ean_8", "upc_a", "upc_e", "code_128", "itf"];
const LOOKUP_TIMEOUT_MS = 9000;
const MAX_PHOTO_BYTES = 12 * 1024 * 1024;

const loaded = new Map();
function loadScript(src) {
    if (loaded.has(src)) return loaded.get(src);
    const promise = new Promise((resolve, reject) => {
        const s = document.createElement("script");
        s.src = src; s.async = true; s.crossOrigin = "anonymous";
        s.onload = () => resolve(); s.onerror = () => { loaded.delete(src); reject(new Error("Could not load " + src)); };
        document.head.appendChild(s);
    });
    loaded.set(src, promise);
    return promise;
}

async function getJson(url) {
    const control = new AbortController();
    const timer = setTimeout(() => control.abort(), LOOKUP_TIMEOUT_MS);
    try {
        const response = await fetch(url, { signal: control.signal, credentials: "omit" });
        if (!response.ok) return null;
        return await response.json();
    } catch (error) { return null; }
    finally { clearTimeout(timer); }
}

export const canUseCamera = () => !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia) && window.isSecureContext;
const nativeDetector = async () => {
    if (!("BarcodeDetector" in window)) return null;
    try {
        const supported = await window.BarcodeDetector.getSupportedFormats();
        const formats = FORMATS.filter((f) => supported.includes(f));
        return formats.length ? new window.BarcodeDetector({ formats }) : null;
    } catch (error) { return null; }
};

// Reads the barcode digits from a photo file (no live camera needed).
export async function barcodeFromPhoto(file) {
    if (!file || !/^image\//.test(file.type) || file.size > MAX_PHOTO_BYTES) return "";
    const detector = await nativeDetector();
    if (detector) {
        try {
            const bitmap = await createImageBitmap(file);
            const found = await detector.detect(bitmap);
            bitmap.close && bitmap.close();
            if (found && found[0] && found[0].rawValue) return found[0].rawValue;
        } catch (error) {                                                  }
    }
    try {
        await loadScript(ZXING);
        const url = URL.createObjectURL(file);
        try { const result = await new window.ZXing.BrowserMultiFormatReader().decodeFromImageUrl(url); return result ? result.getText() : ""; }
        finally { URL.revokeObjectURL(url); }
    } catch (error) { return ""; }
}

function pickPhoto(capture) {
    return new Promise((resolve) => {
        const input = document.createElement("input");
        input.type = "file"; input.accept = "image/*";
        if (capture) input.setAttribute("capture", "environment");
        input.addEventListener("change", () => resolve(input.files && input.files[0] ? input.files[0] : null), { once: true });
        input.click();
    });
}

// Opens the camera and resolves with the barcode digits, or "" when the admin closes it.
export function scanBarcode(options = {}) {
    return new Promise((resolve) => {
        let stream = null, timer = 0, reader = null, finished = false;
        const stop = () => {
            clearInterval(timer);
            if (reader) { try { reader.reset(); } catch (error) {                       } }
            if (stream) stream.getTracks().forEach((t) => t.stop());
        };
        const done = (value) => { if (finished) return; finished = true; stop(); frame.close(); resolve(String(value || "").replace(/\D/g, "")); };
        const frame = dialogFrame(options.title || "Scan a barcode", () => done(""));
        frame.box.classList.add("qf-scan");
        frame.box.insertAdjacentHTML("beforeend", `
            <p class="modal-intro">Point the camera at the bars. It reads by itself, hold still for a second.</p>
            <div class="qf-video"><video playsinline muted autoplay aria-label="Camera picture"></video><span class="qf-aim" aria-hidden="true"></span></div>
            <p class="field-hint qf-state" role="status" aria-live="polite">Starting the camera...</p>
            <div class="modal-actions">
                <button type="button" class="cancel-btn" data-act="close">Close</button>
                <button type="button" class="btn" data-act="photo">${icon("camera")}Use a photo</button>
            </div>`);
        const video = frame.box.querySelector("video"), state = frame.box.querySelector(".qf-state");
        frame.box.querySelector('[data-act="close"]').addEventListener("click", () => done(""));
        frame.box.querySelector('[data-act="photo"]').addEventListener("click", async () => {
            const file = await pickPhoto(true);
            if (!file) return;
            state.textContent = "Reading the photo...";
            const code = await barcodeFromPhoto(file);
            if (code) done(code); else state.textContent = "No barcode found in that photo. Take it closer, straight on, with the bars sharp.";
        });

        (async () => {
            if (!canUseCamera()) { state.textContent = "This browser cannot open the camera here. Use a photo instead."; return; }
            try {
                stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: "environment" }, width: { ideal: 1280 }, height: { ideal: 720 } }, audio: false });
            } catch (error) {
                state.textContent = error && error.name === "NotAllowedError" ? "The camera was not allowed. Allow it in the browser's site settings, or use a photo." : "The camera could not start. Use a photo instead.";
                return;
            }
            if (finished) { stop(); return; }
            video.srcObject = stream;
            try { await video.play(); } catch (error) {                                    }
            state.textContent = "Looking for a barcode...";
            const detector = await nativeDetector();
            if (detector) {
                let busy = false;
                timer = setInterval(async () => {
                    if (busy || finished || video.readyState < 2) return;
                    busy = true;
                    try { const found = await detector.detect(video); if (found && found[0] && found[0].rawValue) done(found[0].rawValue); }
                    catch (error) {                                                      }
                    busy = false;
                }, 180);
                return;
            }
            try {
                await loadScript(ZXING);
                if (finished) return;
                reader = new window.ZXing.BrowserMultiFormatReader();
                const result = await reader.decodeFromVideoElement(video);
                if (result) done(result.getText());
            } catch (error) { if (!finished) state.textContent = "The barcode reader could not start. Use a photo instead."; }
        })();
    });
}

// A photo of the ingredient list read with text recognition (OCR), returned as the ingredient names in printed order.
export async function readLabelPhoto(onProgress = () => {}) {
    const file = await pickPhoto(true);
    if (!file) return null;
    if (!/^image\//.test(file.type)) throw new Error("Choose a photo (JPEG or PNG).");
    if (file.size > MAX_PHOTO_BYTES) throw new Error("The photo is larger than 12 MB. Take it again at a lower size.");
    onProgress("Loading text recognition...");
    try { await loadScript(TESSERACT); } catch (error) { throw new Error("Text recognition could not load. Check the internet connection."); }
    const worker = await window.Tesseract.createWorker("eng", 1, {
        logger: (m) => { if (m && m.status === "recognizing text") onProgress(`Reading the label: ${Math.round((m.progress || 0) * 100)}%`); }
    });
    try {
        const { data } = await worker.recognize(file);
        const text = (data && data.text) || "";
        return { text, ingredients: ingredientsFromText(text) };
    } finally { await worker.terminate(); }
}

let catalogue = null;
export async function loadCatalogue() {
    if (catalogue) return catalogue;
    catalogue = (await getJson(new URL("../_Shared/data/fdaCatalogue.json", import.meta.url).href)) || { products: [] };
    return catalogue;
}

// Everything found for what the admin typed or scanned, best first, parts of the same product merged.
export async function lookupProduct(text) {
    const query = queryKind(text);
    if (query.kind === "empty") return { query, candidates: [] };
    const list = await loadCatalogue();
    const found = [];
    if (query.kind === "barcode") {
        const json = await getJson(offProductUrl(query.value));
        const hit = json && json.status === 1 ? fromOpenBeautyFacts(json.product) : null;
        if (hit) {
            found.push(hit);
            // only a near-certain FDA match joins a barcode hit (it then lends its list and number)
            found.push(...searchCatalogue(`${hit.brand} ${hit.name}`, list, 3).filter((c) => c.score >= 0.85));
        }
    } else {
        found.push(...searchCatalogue(query.value, list, 5));
        const json = await getJson(offSearchUrl(query.value));
        const products = (json && Array.isArray(json.products) ? json.products : []).map(fromOpenBeautyFacts).filter(Boolean);
        found.push(...products.slice(0, 5));
    }
    return { query, candidates: mergeCandidates(found).slice(0, 6) };
}

// What PubChem knows about an ingredient name: other names, a technical description and the compound page.
export async function lookupIngredient(name) {
    const typed = String(name || "").trim();
    if (typed.length < 3) return null;
    const [synonyms, description] = await Promise.all([getJson(pubchemSynonymsUrl(typed)), getJson(pubchemDescriptionUrl(typed))]);
    if (!synonyms && !description) return null;
    const a = aliasesFromPubchem(synonyms, typed), d = descriptionFromPubchem(description);
    return { cid: a.cid || d.cid, aliases: a.aliases, chemical: d.text };
}
