// A small CSV reader / writer for the tools in this folder (no packages). Quoted cells, doubled quotes and line breaks inside quotes are handled.
// A leading byte-order mark is dropped, and ";" is taken as the separator when the header row has no "," (some European exports).

export function parseCsv(text) {
    let source = String(text || "").replace(/^﻿/, "");
    const firstLine = source.split(/\r?\n/, 1)[0] || "";
    const separator = !firstLine.includes(",") && firstLine.includes(";") ? ";" : ",";
    const rows = [];
    let row = [], cell = "", quoted = false;
    for (let i = 0; i < source.length; i++) {
        const c = source[i];
        if (quoted) {
            if (c === '"') { if (source[i + 1] === '"') { cell += '"'; i++; } else quoted = false; } else cell += c;
        } else if (c === '"') quoted = true;
        else if (c === separator) { row.push(cell); cell = ""; }
        else if (c === "\n" || c === "\r") { if (c === "\r" && source[i + 1] === "\n") i++; row.push(cell); rows.push(row); row = []; cell = ""; }
        else cell += c;
    }
    row.push(cell);
    rows.push(row);
    return rows.filter((r) => r.some((x) => String(x).trim() !== ""));
}

/** [{ header: cell }] from the rows of a CSV, with the headers lower-cased and trimmed. */
export function recordsOf(rows) {
    if (rows.length === 0) return { headers: [], records: [] };
    const headers = rows[0].map((h) => String(h).trim().toLowerCase());
    return { headers, records: rows.slice(1).map((r) => Object.fromEntries(headers.map((h, i) => [h, String(r[i] === undefined ? "" : r[i]).trim()]))) };
}

const cell = (value) => { const t = String(value === undefined || value === null ? "" : value); return /[",\r\n]/.test(t) ? '"' + t.replace(/"/g, '""') + '"' : t; };

/** CSV text from a list of keys and a list of objects. */
export function toCsv(keys, objects) {
    return [keys.map(cell).join(",")].concat(objects.map((o) => keys.map((k) => cell(o[k])).join(","))).join("\r\n") + "\r\n";
}
