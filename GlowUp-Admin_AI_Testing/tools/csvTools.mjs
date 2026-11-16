


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


export function recordsOf(rows) {
    if (rows.length === 0) return { headers: [], records: [] };
    const headers = rows[0].map((h) => String(h).trim().toLowerCase());
    return { headers, records: rows.slice(1).map((r) => Object.fromEntries(headers.map((h, i) => [h, String(r[i] === undefined ? "" : r[i]).trim()]))) };
}

const cell = (value) => { const t = String(value === undefined || value === null ? "" : value); return /[",\r\n]/.test(t) ? '"' + t.replace(/"/g, '""') + '"' : t; };


export function toCsv(keys, objects) {
    return [keys.map(cell).join(",")].concat(objects.map((o) => keys.map((k) => cell(o[k])).join(","))).join("\r\n") + "\r\n";
}
