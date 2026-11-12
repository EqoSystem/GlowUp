



export const EXPORT_MAX_ROWS = 100;
export const EXPORT_FILE_PREFIX = "glowup";





export function csvCell(value) {
    let text = value === null || value === undefined ? "" : Array.isArray(value) ? value.join("; ") : String(value);
    if (/^[=+\-@\t\r]/.test(text)) text = "'" + text;
    return /[",\r\n]/.test(text) ? '"' + text.replace(/"/g, '""') + '"' : text;
}


export function toCsv(columns, rows, limit = EXPORT_MAX_ROWS) {
    const lines = [columns.map((column) => csvCell(column.title)).join(",")];
    rows.slice(0, Math.max(0, limit)).forEach((row) => lines.push(columns.map((column) => csvCell(column.value(row))).join(",")));
    return lines.join("\r\n");
}


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


export const today = () => new Date().toISOString().slice(0, 10);
