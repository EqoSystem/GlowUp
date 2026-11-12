








const PROJECT = "demo-glowup";
const write = process.argv.includes("--write");
const host = process.env.FIRESTORE_EMULATOR_HOST || "";

const DOCUMENTS = {
    "brands/seed_examplebrand": { name: "Examplebrand", category: "Makeup", market: "Global", status: "Active", scans: 0, source: "seed-dev", reviewed: true },
    "products/seed_velvet_lipstick": {
        name: "Velvet Matte Lipstick (sample)", brandId: "seed_examplebrand", brandName: "Examplebrand", cosmeticType: "Lipstick", status: "Active", scans: 0,
        ingredients: ["Mica", "Silica", "Tocopherol"], shades: ["#B03A4E", "#8F2D3F"], shadeCodes: ["N20", "N30"], sources: [], source: "seed-dev", reviewed: true
    },
    "products/seed_soft_powder": {
        name: "Soft Blur Powder (sample)", brandId: "seed_examplebrand", brandName: "Examplebrand", cosmeticType: "Powder", status: "Pending", scans: 0,
        ingredients: ["Talc", "Mica", "Silica"], shades: [], sources: [], source: "seed-dev", reviewed: false
    },
    "ingredients/seed_mica": { name: "Mica", techName: "Mica", description: "Sample entry for development. Replace with a reviewed description.", riskLevel: "", sources: [], source: "seed-dev", reviewed: false },
    "ingredients/seed_silica": { name: "Silica", techName: "Silica", description: "Sample entry for development. Replace with a reviewed description.", riskLevel: "", sources: [], source: "seed-dev", reviewed: false }
};


function typed(value) {
    if (typeof value === "string") return { stringValue: value };
    if (typeof value === "boolean") return { booleanValue: value };
    if (typeof value === "number") return Number.isInteger(value) ? { integerValue: String(value) } : { doubleValue: value };
    if (Array.isArray(value)) return { arrayValue: { values: value.map(typed) } };
    return { mapValue: { fields: Object.fromEntries(Object.entries(value).map(([k, v]) => [k, typed(v)])) } };
}

if (!write) {
    console.log("DRY RUN: nothing is sent. These documents would be created in the emulator:");
    for (const [path, data] of Object.entries(DOCUMENTS)) console.log("  " + path + "  " + JSON.stringify(data).slice(0, 90) + "...");
    console.log("\nAdd --write (with FIRESTORE_EMULATOR_HOST set) to create them.");
    process.exit(0);
}
if (!host) { console.log("Refused: FIRESTORE_EMULATOR_HOST is not set, so this could be the real database. Start the emulator (npm run emulators) and set it, for example 127.0.0.1:8085."); process.exit(1); }
if (!/^(127\.0\.0\.1|localhost|\[::1\])(:\d+)?$/.test(host)) { console.log("Refused: FIRESTORE_EMULATOR_HOST must be this computer (127.0.0.1 or localhost). It is: " + host); process.exit(1); }

let created = 0, existed = 0;
for (const [path, data] of Object.entries(DOCUMENTS)) {
    const slash = path.indexOf("/");
    const url = `http://${host}/v1/projects/${PROJECT}/databases/(default)/documents/${path.slice(0, slash)}?documentId=${encodeURIComponent(path.slice(slash + 1))}`;
    const response = await fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json", Authorization: "Bearer owner" },
        body: JSON.stringify({ fields: typed(data).mapValue.fields })
    });
    if (response.status === 409) existed++;
    else if (response.ok) created++;
    else { console.log(`Failed for ${path}: HTTP ${response.status} ${(await response.text()).slice(0, 120)}`); process.exit(1); }
}
console.log(`Emulator ${host}: ${created} document(s) created, ${existed} already there (left as they are).`);
