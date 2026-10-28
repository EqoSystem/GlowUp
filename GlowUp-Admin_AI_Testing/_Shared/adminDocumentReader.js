const MAX_FILE_BYTES = 15 * 1024 * 1024;
const MAX_TEXT_CHARACTERS = 200000;       // the plain import reads all of it; the AI import sends only its first 30,000 characters (AI_TEXT_CHARACTERS)
export const AI_TEXT_CHARACTERS = 30000;
// A scanned PDF (pictures of pages, no text layer) is read with text recognition (OCR) in this browser: tesseract.js, in its own worker, one page
// after the other so the page stays usable. Nothing is uploaded; the OCR program and its English data are downloaded from the CDN the first time.
const OCR_MAX_PAGES = 10;
const OCR_SCALE = 2;                      // pages are drawn at twice their size: small label print is read far better
const TESSERACT = "https://cdn.jsdelivr.net/npm/tesseract.js@5.1.1/dist/tesseract.min.js";

function loadScript(src) {
    return new Promise((resolve, reject) => {
        const existing = document.querySelector(`script[data-admin-import="${src}"]`);
        if (existing) {
            if (existing.dataset.loaded === "true") resolve();
            else {
                existing.addEventListener("load", resolve, { once: true });
                existing.addEventListener("error", () => reject(new Error("The document reader could not be loaded.")), { once: true });
            }
            return;
        }
        const script = document.createElement("script");
        script.src = src;
        script.async = true;
        script.dataset.adminImport = src;
        script.addEventListener("load", () => { script.dataset.loaded = "true"; resolve(); }, { once: true });
        script.addEventListener("error", () => { script.remove(); reject(new Error("The document reader could not be loaded.")); }, { once: true });
        document.head.appendChild(script);
    });
}

async function readPdf(file, onProgress) {
    const pdfjsBase = "https://cdn.jsdelivr.net/npm/pdfjs-dist@4.4.168/build";
    let pdfjs;
    try {
        pdfjs = await import(`${pdfjsBase}/pdf.min.mjs`);
    } catch (error) {
        throw new Error("The PDF reader could not load. Check your internet connection, then try the PDF again.");
    }
    pdfjs.GlobalWorkerOptions.workerSrc = `${pdfjsBase}/pdf.worker.min.mjs`;
    let pdf;
    try {
        pdf = await pdfjs.getDocument({ data: await file.arrayBuffer() }).promise;
    } catch (error) {
        if (error && error.name === "PasswordException") throw new Error("This PDF is password-protected. Remove its password and try again.");
        if (error && error.name === "InvalidPDFException") throw new Error("This file is not a readable PDF. Re-save or export it as a PDF and try again.");
        throw new Error(`The PDF could not be opened: ${error && error.message ? error.message : "unknown PDF reader error"}`);
    }
    const pages = [];
    for (let pageNumber = 1; pageNumber <= Math.min(pdf.numPages, 100); pageNumber++) {
        const page = await pdf.getPage(pageNumber);
        const content = await page.getTextContent();
        pages.push(content.items.map((item) => `${item.str || ""}${item.hasEOL ? "\n" : " "}`).join("").trim());
    }
    if (pages.join("").replace(/\s+/g, "").length >= 20) {
        if (pdf.numPages > 100) pages.push("[Only the first 100 pages were read.]");
        return pages.join("\n");
    }
    return readScannedPdf(pdf, onProgress);
}

/** The text of a PDF that has no text layer, by OCR of each page (at most OCR_MAX_PAGES). */
async function readScannedPdf(pdf, onProgress) {
    const total = Math.min(pdf.numPages, OCR_MAX_PAGES);
    onProgress(`This PDF has no selectable text. Loading text recognition (OCR) for ${total} page${total === 1 ? "" : "s"}...`);
    try { await loadScript(TESSERACT); } catch (error) { throw new Error("Text recognition (OCR) could not load. Check your internet connection, or use a PDF with selectable text."); }
    if (!window.Tesseract) throw new Error("Text recognition (OCR) did not start.");
    let page = 0;
    const worker = await window.Tesseract.createWorker("eng", 1, {
        logger: (m) => { if (m && m.status === "recognizing text" && page > 0) onProgress(`Reading page ${page} of ${total} with text recognition: ${Math.round((m.progress || 0) * 100)}%`); }
    });
    try {
        const texts = [];
        for (page = 1; page <= total; page++) {
            onProgress(`Reading page ${page} of ${total} with text recognition...`);
            const pdfPage = await pdf.getPage(page);
            const viewport = pdfPage.getViewport({ scale: OCR_SCALE });
            const canvas = document.createElement("canvas");
            canvas.width = Math.ceil(viewport.width);
            canvas.height = Math.ceil(viewport.height);
            await pdfPage.render({ canvasContext: canvas.getContext("2d"), viewport }).promise;
            const { data } = await worker.recognize(canvas);
            texts.push((data && data.text) || "");
            canvas.width = canvas.height = 0;               // let the browser free the picture before the next page
        }
        if (pdf.numPages > total) texts.push(`[Only the first ${total} pages were read by text recognition.]`);
        return texts.join("\n");
    } finally {
        await worker.terminate();
    }
}

async function readSpreadsheet(file) {
    const xlsx = await import("https://cdn.sheetjs.com/xlsx-0.20.3/package/xlsx.mjs");
    const workbook = xlsx.read(await file.arrayBuffer(), { type: "array" });
    return workbook.SheetNames.map((name) => {
        const sheet = workbook.Sheets[name];
        return `SHEET: ${name}\n${xlsx.utils.sheet_to_csv(sheet)}`;
    }).join("\n\n");
}

async function readWord(file) {
    await loadScript("https://cdnjs.cloudflare.com/ajax/libs/mammoth/1.8.0/mammoth.browser.min.js");
    if (!window.mammoth) throw new Error("The Word document reader did not start.");
    const result = await window.mammoth.extractRawText({ arrayBuffer: await file.arrayBuffer() });
    return result.value;
}

/**
 * Reads supported office/PDF/text formats in this browser; only extracted text is sent to the admin AI endpoint.
 * onProgress(message) is told what is happening during a long read (OCR of a scanned PDF).
 */
export async function readAdminDocument(file, onProgress = () => {}) {
    if (!file) throw new Error("Choose a PDF, Excel, Word, CSV or text file.");
    if (file.size > MAX_FILE_BYTES) throw new Error("The document is larger than 15 MB. Split it into smaller files and try again.");
    const extension = (file.name.split(".").pop() || "").toLowerCase();
    const isPdf = extension === "pdf" || file.type === "application/pdf";
    let text;
    if (isPdf) text = await readPdf(file, onProgress);
    else if (["xlsx", "xls"].includes(extension)) text = await readSpreadsheet(file);
    else if (extension === "docx") text = await readWord(file);
    else if (["csv", "txt"].includes(extension) || file.type === "text/csv" || file.type === "text/plain") text = await file.text();      // plain text: no library, nothing leaves the browser
    else throw new Error("Use a PDF, .xlsx/.xls Excel workbook, .docx Word document, .csv or .txt file.");
    const clean = String(text || "").replace(/\u0000/g, "").trim().slice(0, MAX_TEXT_CHARACTERS);
    if (clean.length < 20) {
        if (isPdf) throw new Error("No readable text was found in this PDF, also not by text recognition (OCR). Use a sharper scan or a PDF with selectable text.");
        throw new Error("No readable text was found. Old .doc Word files are not supported; use a .docx document.");
    }
    return clean;
}
