const MAX_FILE_BYTES = 15 * 1024 * 1024;
const MAX_TEXT_CHARACTERS = 30000;

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

async function readPdf(file) {
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
    if (pdf.numPages > 100) pages.push("[Only the first 100 pages were read.]");
    return pages.join("\n");
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

/** Reads supported office/PDF formats in this browser; only extracted text is sent to the admin AI endpoint. */
export async function readAdminDocument(file) {
    if (!file) throw new Error("Choose a PDF, Excel, or Word document.");
    if (file.size > MAX_FILE_BYTES) throw new Error("The document is larger than 15 MB. Split it into smaller files and try again.");
    const extension = (file.name.split(".").pop() || "").toLowerCase();
    const isPdf = extension === "pdf" || file.type === "application/pdf";
    let text;
    if (isPdf) text = await readPdf(file);
    else if (["xlsx", "xls"].includes(extension)) text = await readSpreadsheet(file);
    else if (extension === "docx") text = await readWord(file);
    else throw new Error("Use a PDF, .xlsx/.xls Excel workbook, or .docx Word document.");
    const clean = String(text || "").replace(/\u0000/g, "").trim().slice(0, MAX_TEXT_CHARACTERS);
    if (clean.length < 20) {
        if (isPdf) throw new Error("No selectable text was found in this PDF. It may be scanned; use a text-based PDF or convert it with OCR first.");
        throw new Error("No readable text was found. Old .doc Word files are not supported; use a .docx document.");
    }
    return clean;
}
