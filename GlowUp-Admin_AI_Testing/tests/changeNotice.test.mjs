// New23: the change notices the app shows when an admin saves a product or an ingredient (_Shared/changeNotice.js).
import { noticeFor, TITLE_MAX, BODY_MAX } from "../_Shared/changeNotice.js";
let checks = 0, failed = 0;
const ok = (cond, what) => { checks++; if (!cond) { failed++; console.log("FAILED: " + what); } };
const it = (collection, name, extra) => Object.assign({ collection, docId: "d", action: "update", fields: ["name"], name }, extra || {});

ok(noticeFor([]) === null && noticeFor(null) === null, "no items, no notice");
ok(noticeFor([it("brands", "Maybelline")]) === null, "a brand save is not announced");
ok(noticeFor([it("products", "Lip Gloss", { fields: ["scans"] })]) === null, "a scan counter alone is not announced");
let n = noticeFor([it("ingredients", "Niacinamide")]);
ok(n && n.kind === "ingredient" && n.title === "Ingredient updated" && n.body === "Niacinamide was updated.", `one ingredient ${JSON.stringify(n)}`);
n = noticeFor([it("products", "Velvet Tint", { action: "delete", fields: [] })]);
ok(n && n.kind === "product" && n.title === "Product removed" && /Velvet Tint was removed/.test(n.body), `a removed product ${JSON.stringify(n)}`);
n = noticeFor([it("ingredients", "A"), it("ingredients", "B"), it("ingredients", "C"), it("ingredients", "D"), it("ingredients", "E")]);
ok(n.title === "5 ingredients updated" && /A, B, C and 2 more ingredients were updated/.test(n.body), `five ingredients ${n.body}`);
n = noticeFor([it("ingredients", "Mica"), it("products", "Blush")]);
ok(n.kind === "product" && /Blush/.test(n.body) && !/Mica/.test(n.body), "products win when a save touches both");
n = noticeFor([it("products", "")]);
ok(/A product was updated/.test(n.body), "no name: a plain sentence");
n = noticeFor([it("products", "x".repeat(500)), it("products", "y".repeat(500)), it("products", "z".repeat(500))]);
ok(n.title.length <= TITLE_MAX && n.body.length <= BODY_MAX && n.body.length > 20, "long names are cut to the rule limits");
const many = Array.from({ length: 450 }, (_, i) => it("ingredients", "Name " + i));
n = noticeFor(many);
ok(n.title === "450 ingredients updated" && n.body.length <= BODY_MAX, "a big import is still one short notice");
ok(noticeFor([it("products", "P", { action: "set", fields: ["updatedAt"] })]) === null, "only a time stamp changed: nothing to say");
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED  (checks made: ${checks})`);
process.exit(failed === 0 ? 0 : 1);
