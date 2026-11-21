

export const AI_TEXT_CHARACTERS = 30000;
export const AI_PART_CHARACTERS = 12000;


const MAX_DOCUMENT_BYTES = 20 * 1024 * 1024;
const OCR_MAX_PAGES = Infinity;
const OCR_SCALE = 2;
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
    for (let pageNumber = 1; pageNumber <= pdf.numPages; pageNumber++) {
        if (pdf.numPages > 20 && pageNumber % 10 === 0) onProgress(`Reading page ${pageNumber} of ${pdf.numPages}...`);
        const page = await pdf.getPage(pageNumber);
        const content = await page.getTextContent();
        pages.push(content.items.map((item) => `${item.str || ""}${item.hasEOL ? "\n" : " "}`).join("").trim());
    }
    if (pages.join("").replace(/\s+/g, "").length >= 20) {
        return pages.join("\n");
    }
    return readScannedPdf(pdf, onProgress);
}


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
            canvas.width = canvas.height = 0;
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





export async function readAdminDocument(file, onProgress = () => {}) {
    if (!file) throw new Error("Choose a PDF, Excel, Word, CSV or text file.");
    // security guideline 10: a size limit, and the file's first bytes must match its kind
    if (file.size > MAX_DOCUMENT_BYTES) throw new Error(`This file is ${Math.round(file.size / 1048576)} MB. Use a file of at most ${MAX_DOCUMENT_BYTES / 1048576} MB, or split it.`);
    const extension = (file.name.split(".").pop() || "").toLowerCase();
    const isPdf = extension === "pdf" || file.type === "application/pdf";
    const head = new Uint8Array(await file.slice(0, 4).arrayBuffer());
    const starts = (...bytes) => bytes.every((b, i) => head[i] === b);
    if (isPdf && !starts(0x25, 0x50, 0x44, 0x46)) throw new Error("This file is not a real PDF. Save it again as PDF and try once more.");
    if (["xlsx", "docx"].includes(extension) && !starts(0x50, 0x4b)) throw new Error(`This file is not a real .${extension} file. Save it again from Excel or Word and try once more.`);
    if (extension === "xls" && !starts(0xd0, 0xcf, 0x11, 0xe0)) throw new Error("This file is not a real .xls workbook. Save it again from Excel and try once more.");
    let text;
    if (isPdf) text = await readPdf(file, onProgress);
    else if (["xlsx", "xls"].includes(extension)) text = await readSpreadsheet(file);
    else if (extension === "docx") text = await readWord(file);
    else if (["csv", "txt"].includes(extension) || file.type === "text/csv" || file.type === "text/plain") text = await file.text();
    else throw new Error("Use a PDF, .xlsx/.xls Excel workbook, .docx Word document, .csv or .txt file.");
    const clean = String(text || "").replace(/\u0000/g, "").trim();
    if (clean.length < 20) {
        if (isPdf) throw new Error("No readable text was found in this PDF, also not by text recognition (OCR). Use a sharper scan or a PDF with selectable text.");
        throw new Error("No readable text was found. Old .doc Word files are not supported; use a .docx document.");
    }
    return clean;
}





export function splitForAi(text, size = AI_PART_CHARACTERS) {
    const source = String(text || "");
    const parts = [];
    let at = 0;
    while (at < source.length) {
        let end = Math.min(source.length, at + size);
        if (end < source.length) {
            const lineBreak = source.lastIndexOf("\n", end);
            if (lineBreak > at + size * 0.5) end = lineBreak + 1;
        }
        const part = source.slice(at, end);
        if (part.trim()) parts.push(part);
        at = end;
    }
    return parts;
}
