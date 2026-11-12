












export const VERIFIED_BY_GS1 = "https://gepir.gs1.org/";


const RANGES = [
    [0, 19, "GS1 US (United States and Canada)"], [20, 29, null, "a shop's own in-store number"], [30, 39, "GS1 US (United States and Canada)"],
    [40, 49, null, "a shop's own in-store number"], [50, 59, null, "a coupon number"], [60, 139, "GS1 US (United States and Canada)"],
    [200, 299, null, "a shop's own in-store number"], [300, 379, "GS1 France"], [380, 380, "GS1 Bulgaria"], [383, 383, "GS1 Slovenia"],
    [385, 385, "GS1 Croatia"], [387, 387, "GS1 Bosnia and Herzegovina"], [389, 389, "GS1 Montenegro"], [400, 440, "GS1 Germany"],
    [450, 459, "GS1 Japan"], [460, 469, "GS1 Russia"], [471, 471, "GS1 Taiwan"], [480, 480, "GS1 Philippines"], [489, 489, "GS1 Hong Kong"],
    [490, 499, "GS1 Japan"], [500, 509, "GS1 UK"], [520, 521, "GS1 Greece"], [539, 539, "GS1 Ireland"], [540, 549, "GS1 Belgium and Luxembourg"],
    [560, 560, "GS1 Portugal"], [570, 579, "GS1 Denmark"], [590, 590, "GS1 Poland"], [594, 594, "GS1 Romania"], [599, 599, "GS1 Hungary"],
    [600, 601, "GS1 South Africa"], [690, 699, "GS1 China"], [700, 709, "GS1 Norway"], [729, 729, "GS1 Israel"], [730, 739, "GS1 Sweden"],
    [750, 750, "GS1 Mexico"], [754, 755, "GS1 Canada"], [760, 769, "GS1 Switzerland"], [770, 771, "GS1 Colombia"], [778, 779, "GS1 Argentina"],
    [789, 790, "GS1 Brazil"], [800, 839, "GS1 Italy"], [840, 849, "GS1 Spain"], [859, 859, "GS1 Czechia"], [868, 869, "GS1 Turkiye"],
    [870, 879, "GS1 Netherlands"], [880, 880, "GS1 Korea"], [884, 884, "GS1 Cambodia"], [885, 885, "GS1 Thailand"], [888, 888, "GS1 Singapore"],
    [890, 890, "GS1 India"], [893, 893, "GS1 Vietnam"], [899, 899, "GS1 Indonesia"], [900, 919, "GS1 Austria"], [930, 939, "GS1 Australia"],
    [940, 949, "GS1 New Zealand"], [955, 955, "GS1 Malaysia"], [958, 958, "GS1 Macau"], [977, 977, null, "a magazine number (ISSN)"],
    [978, 979, null, "a book number (ISBN)"], [980, 980, null, "a refund receipt number"], [981, 984, null, "a coupon number"], [990, 999, null, "a coupon number"]
];


export function gtinCheck(barcode) {
    const digits = String(barcode == null ? "" : barcode).replace(/[^0-9]/g, "");
    if (![8, 12, 13, 14].includes(digits.length)) return { digits, valid: false };
    let sum = 0;
    for (let i = digits.length - 2, weight = 3; i >= 0; i--, weight = 4 - weight) sum += Number(digits[i]) * weight;
    return { digits, valid: (10 - (sum % 10)) % 10 === Number(digits[digits.length - 1]) };
}


export function gs1Office(barcode) {
    const digits = String(barcode == null ? "" : barcode).replace(/[^0-9]/g, "");
    if (digits.length === 8) return { office: "", note: "an 8-digit barcode (its office is not shown)" };
    const thirteen = digits.length === 12 ? "0" + digits : digits.length === 14 ? digits.slice(1) : digits;
    if (thirteen.length !== 13) return { office: "", note: "" };
    const prefix = Number(thirteen.slice(0, 3));
    const range = RANGES.find(([first, last]) => prefix >= first && prefix <= last);
    if (!range) return { office: "", note: "" };
    return range[2] ? { office: range[2], note: "" } : { office: "", note: range[3] };
}


export function gs1Hint(barcode) {
    const check = gtinCheck(barcode);
    if (check.digits.length === 0) return { valid: false, office: "", text: "" };
    if (!check.valid) return { valid: false, office: "", text: "The last digit does not match: this barcode was misread or typed wrong." };
    const where = gs1Office(check.digits);
    const text = where.office
        ? `Valid barcode. Its company number was issued by ${where.office}: this says where the number was issued, not where the product is made.`
        : where.note ? `Valid barcode, but it is ${where.note}, not a company's product number.` : "Valid barcode.";
    return { valid: true, office: where.office, text };
}
