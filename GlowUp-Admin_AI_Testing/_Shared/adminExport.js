// "Export to CSV" without a database read: the rows come from the lists the page already holds, and the file is built in
// the browser. A hard cap keeps one click small (and a spreadsheet opens it at once).
//
// Things to change quickly:
export const EXPORT_MAX_ROWS = 100;          // the most rows one export holds (the newest first)
export const EXPORT_FILE_PREFIX = "glowup";  // file name start: glowup-products-2026-10-21.csv

/**
 * One CSV cell. A text that would be run as a formula by a spreadsheet (starts with = + - @ or a tab / return) gets a
 * leading apostrophe, so a product named "=HYPERLINK(...)" stays plain text.
 */
export function csvCell(value) {
    let text = value === null || value === undefined ? "" : Array.isArray(value) ? value.join("; ") : String(value);
    if (/^[=+\-@\t\r]/.test(text)) text = "'" + text;
    return /[",\r\n]/.test(text) ? '"' + text.replace(/"/g, '""') + '"' : text;
}

/** columns = [{ title, value(row) }]; rows = the objects; at most `limit` rows. Returns the CSV text (with a header line). */
export function toCsv(columns, rows, limit = EXPORT_MAX_ROWS) {
    const lines = [columns.map((column) => csvCell(column.title)).join(",")];
    rows.slice(0, Math.max(0, limit)).forEach((row) => lines.push(columns.map((column) => csvCell(column.value(row))).join(",")));
    return lines.join("\r\n");
}

/** Hands the text to the browser as a file to save (the byte order mark lets a spreadsheet read accents correctly). */
export function downloadCsv(name, text) {
    const blob = new Blob(["﻿" + text], { type: "text/csv;charset=utf-8" });
    const link = document.createElement("a");
    link.href = URL.createObjectURL(blob);
    link.download = name;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(link.href), 4000);
}

/** The day, for a file name: 2026-10-21. */
export const today = () => new Date().toISOString().slice(0, 10);
