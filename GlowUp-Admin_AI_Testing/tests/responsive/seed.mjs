// Pretend data with long names and one brand that dominates the scans.
export function makeSeed() {
    const now = Date.now();
    const ts = (agoMin) => ({ __ts: now - agoMin * 60000 });
    const brandNames = [
        ["maybelline", "Maybelline New York", 890], ["loreal", "L'Oréal Paris", 40], ["barenbliss", "barenbliss", 22],
        ["longname", "Laboratoires Dermatologiques Internationale Professionnelle Très Longue Marque Cosmétique", 18],
        ["happyskin", "Happy Skin", 11], ["blkcosmetics", "BLK Cosmetics", 9], ["ever", "Ever Bilena Advance Professional Make-up Line", 6],
        ["colourette", "Colourette Cosmetics", 3], ["detail", "Detail Cosmetics", 0], ["sunnies", "Sunnies Face", 0]
    ];
    const brands = {};
    brandNames.forEach(([id, name, scans], i) => {
        brands[id] = { name, category: "Makeup", aliases: [], market: "PH", status: i % 4 === 3 ? "Pending" : "Active", scans, source: "admin", reviewed: i % 4 !== 3, createdAt: ts(1000 + i), updatedAt: ts(500 + i) };
    });
    const ingNames = ["Dimethicone", "Polyglyceryl-4 Diisostearate/Polyhydroxystearate/Sebacate", "Titanium Dioxide (CI 77891)", "Iron Oxides (CI 77491, CI 77492, CI 77499)",
        "Cyclopentasiloxane", "Phenoxyethanol", "Tocopheryl Acetate", "Bis-Diglyceryl Polyacyladipate-2", "Methylparaben", "Fragrance (Parfum)", "Talc", "Mica",
        "Hydrogenated Polyisobutene", "Ethylhexyl Methoxycinnamate", "Trimethylsiloxysilicate", "Caprylic/Capric Triglyceride", "Butylene Glycol", "Glycerin",
        "Alcohol Denat.", "Sodium Hyaluronate"];
    const ingredients = {};
    ingNames.forEach((name, i) => {
        ingredients["ing" + i] = { name, classificationId: "other", purpose: i % 2 ? "Skin-conditioning agent that keeps the formula smooth and spreadable across the skin" : "Colourant", riskLevel: ["safe", "caution", "harmful"][i % 3], description: "A longer description of what this ingredient does in make-up products, written in plain words for the admin.", aliases: [], sources: [], source: "admin", reviewed: i % 5 !== 0, createdAt: ts(900 + i), updatedAt: ts(400 + i) };
    });
    const products = {};
    const types = ["Lipstick", "Foundation", "Mascara", "Blush", "Eyeliner", "Concealer"];
    for (let i = 0; i < 28; i++) {
        const [bid, bname] = brandNames[i % brandNames.length];
        products["p" + i] = { name: (i % 5 === 0 ? "Superstay Matte Ink Liquid Lipstick Longwear Extra Long Product Name Edition " : "Fit Me Matte + Poreless ") + i, brandId: bid, brandName: bname, cosmeticType: types[i % types.length], ingredients: ingNames.slice(0, 3 + (i % 8)), shades: [{ name: "Shade " + i, hex: "#c46a7a" }], sources: [], status: i % 6 === 5 ? "Pending" : "Active", scans: (i * 7) % 40, source: i % 7 === 0 ? "scan" : "admin", reviewed: i % 6 !== 5, barcode: "48000" + i, createdAt: ts(800 + i), updatedAt: ts(300 + i) };
    }
    // a pending product with no ingredient list and a long name: its card has the wide "No ingredient list | Find with AI" chip
    // (the name once broke into one letter per line beside it, 9 November 2026)
    products.p28 = { name: "Colourbounce Jelly-Tint Lip & Cheek Stain Long Lasting", brandId: brandNames[0][0], brandName: brandNames[0][1], cosmeticType: "Lip Tint", ingredients: [], shades: [], sources: [], status: "Pending", scans: 0, source: "scan", reviewed: false, createdAt: ts(5), updatedAt: ts(5) };
    const scanActivity = {};
    for (let i = 0; i < 14; i++) {
        const [bid, bname] = brandNames[i % 4];
        scanActivity["s" + i] = { productName: i % 3 === 0 ? "Superstay Matte Ink Liquid Lipstick Longwear Extra Long Product Name " + i : "Fit Me Foundation " + i, brandId: bid, brandName: bname, cosmeticType: "Lipstick", status: ["safe", "caution", "harmful"][i % 3], source: "scan", scannedAtUnix: Math.floor(now / 1000) - i * 600, ingredientCount: 12, harmfulCount: i % 3 === 2 ? 1 : 0, cautionCount: 1, allergenFlagged: i % 4 === 1, flaggedIngredients: i % 3 ? ["Methylparaben", "Fragrance (Parfum)"] : [] };
    }
    scanActivity.old1 = { productName: "Superstay Vinyl Ink Liquid Lipcolor Long Lasting Extra Long Product Name", brandName: "Laboratoires Dermatologiques Internationale Professionnelle", cosmeticType: "Lipstick", status: "harmful", source: "scan", scannedAtUnix: Math.floor(now / 1000) - 2 * 86400, ingredientCount: 9, harmfulCount: 1, cautionCount: 0, allergenFlagged: true, flaggedIngredients: ["Fragrance"] };
    scanActivity.old2 = { productName: "Fit Me Matte Poreless Foundation 128 Warm Nude", brandName: "Maybelline New York", cosmeticType: "Foundation", status: "harmful", source: "scan", scannedAtUnix: Math.floor(now / 1000) - 400 * 86400, ingredientCount: 9, harmfulCount: 1, cautionCount: 0, allergenFlagged: true, flaggedIngredients: [] };
    const allergenSuggestions = {}, conditionSuggestions = {};
    ["Balsam of Peru (Myroxylon Pereirae Resin) contact allergy", "Nickel", "Lanolin alcohol", "Propylene glycol"].forEach((name, i) => { allergenSuggestions["a" + i] = { name, text: name, status: "pending", createdAtUnix: Math.floor(now / 1000) - 3600 * i, createdAt: ts(60 * i + 5), reviewed: false, count: i + 1 }; });
    ["Perioral dermatitis with very sensitive and reactive skin", "Rosacea"].forEach((name, i) => { conditionSuggestions["c" + i] = { name, text: name, status: "pending", createdAtUnix: Math.floor(now / 1000) - 5000 * i, createdAt: ts(60 * i + 7), reviewed: false, count: 1 }; });
    const productIngredientSuggestions = { pis1: { productId: "p1", productName: "Fit Me Matte + Poreless 1", brandName: "L'Oréal Paris", ingredients: ["Talc", "Mica"], createdAt: ts(30), status: "Pending" } };
    const auditLog = { l1: { at: ts(5), by: "admin@example.test", action: "update", collection: "brands", docId: "maybelline", summary: "Changed status" } };
    const supportReports = { r1: { uid: "user1abcdef", category: "Bug", message: "log test delay problem", status: "new", appVersion: "1.0", device: "Pixel 7, Android 14", createdAtUnix: Math.floor(now / 1000) - 600, lastScan: { product: "Superstay Matte Ink", brand: "Maybelline New York", result: "caution", summary: "1 ingredient to look at", ingredients: "Dimethicone, Mica" } },
        r3: { uid: "user3abcdef", category: "Wrong scan result", message: "the scan said harmful but the pack is fine", status: "new", appVersion: "1.0", device: "Pixel 6, Android 13", createdAtUnix: Math.floor(now / 1000) - 200 },
        r4: { uid: "user4abcdef", category: "Other", message: "how do I change my email", status: "new", appVersion: "1.0", device: "Pixel 8, Android 14", createdAtUnix: Math.floor(now / 1000) - 100 },
        r2: { uid: "user2abcdef", category: "Suggestion", message: "Please add a shade finder for foundation", status: "new", appVersion: "1.0", device: "Galaxy A14, Android 14", createdAtUnix: Math.floor(now / 1000) - 300, lastScan: { product: "Fit Me Matte", brand: "Maybelline New York", result: "safe", summary: "No ingredients to look at", ingredients: "Water, Talc" } } };
    return { brands, products, ingredients, scanActivity, allergenSuggestions, conditionSuggestions, supportReports, productIngredientSuggestions, auditLog, admins: { admin1: { role: "admin" } }, ingredientClassifications: { other: { name: "Other" }, colourant: { name: "Colourant" } } };
}
