// READ-ONLY backup of the three public catalogue collections (brands, products, ingredients).
//
//   Run it from the website folder:   node tools/downloadCatalogue.mjs [folder]
//   Without a folder it writes to     ../GlowUp-Data_Backup_<today's date>   (next to the website folder)
//
// It reads through Firestore's public web address WITHOUT signing in: the security rules let anyone read these three
// collections, and nothing else. It never writes to the database. The other collections (users, scans, suggestions)
// are private and are NOT in this backup.
//
// Files written:  brands.json, products.json, ingredients.json  (one entry per document, plain values; "@id" is the
//                 document id, "@created" / "@updated" are Firestore's own times) and summary.json.
import { mkdirSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

const PROJECT = "glowup-admin-portal";
const BASE = `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents/`;
const here = path.dirname(fileURLToPath(import.meta.url));
const today = new Date().toISOString().slice(0, 10);
const out = process.argv[2] ? path.resolve(process.argv[2]) : path.join(here, "..", "..", "GlowUp-Data_Backup_" + today);

// Firestore's typed value -> an ordinary value
function plain(value) {
    if ("stringValue" in value) return value.stringValue;
    if ("integerValue" in value) return Number(value.integerValue);
    if ("doubleValue" in value) return Number(value.doubleValue);
    if ("booleanValue" in value) return value.booleanValue === true;
    if ("nullValue" in value) return null;
    if ("timestampValue" in value) return { "@time": value.timestampValue };
    if ("arrayValue" in value) return (value.arrayValue.values || []).map(plain);
    if ("mapValue" in value) return Object.fromEntries(Object.entries(value.mapValue.fields || {}).map(([key, inner]) => [key, plain(inner)]));
    if ("referenceValue" in value) return { "@ref": value.referenceValue };
    return null;
}

async function page(url) {
    for (let attempt = 1; attempt <= 4; attempt++) {
        try {
            const answer = await fetch(url, { signal: AbortSignal.timeout(40000) });
            if (answer.ok) return await answer.json();
            if ([401, 403, 404].includes(answer.status)) throw new Error("the database refused (" + answer.status + ")");
            console.log(`  the database answered ${answer.status}, trying again (${attempt} of 4)`);
        } catch (error) {
            if (/refused/.test(error.message) || attempt === 4) throw error;
            console.log(`  no answer (${error.name}), trying again (${attempt} of 4)`);
        }
        await new Promise((resolve) => setTimeout(resolve, 2000 * attempt));
    }
    throw new Error("no answer after 4 tries");
}

async function download(collection) {
    const documents = [];
    let token = "";
    do {
        const answer = await page(BASE + collection + "?pageSize=300" + (token ? "&pageToken=" + encodeURIComponent(token) : ""));
        (answer.documents || []).forEach((document) => {
            const entry = Object.fromEntries(Object.entries(document.fields || {}).map(([key, value]) => [key, plain(value)]));
            entry["@id"] = document.name.split("/").pop();
            entry["@created"] = document.createTime || "";
            entry["@updated"] = document.updateTime || "";
            documents.push(entry);
        });
        token = answer.nextPageToken || "";
    } while (token);
    return documents;
}

mkdirSync(out, { recursive: true });
const summary = { downloadedAt: new Date().toISOString() };
for (const collection of ["brands", "products", "ingredients"]) {
    const documents = await download(collection);
    writeFileSync(path.join(out, collection + ".json"), JSON.stringify(documents, null, 1), "utf8");
    summary[collection] = documents.length;
    console.log(`${collection}: ${documents.length} documents`);
}
writeFileSync(path.join(out, "summary.json"), JSON.stringify(summary, null, 1), "utf8");
console.log("Saved in " + out);
console.log("Nothing was changed in the database.");
