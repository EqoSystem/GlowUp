// Checks of ingredient classifications and of the one-time field change (_Shared/classificationRules.js), plus source checks of the Curator
// and of the rules file. No browser and no network: the windows themselves are not clicked here.
//   Run: node tests/classification.test.mjs
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { STARTING_NAMES, FALLBACK_ID, MAX_NAME, startingList, missingStarting, startingId, isTestHost, collectionFor, CLASSIFICATION_REQUIRED_ON_EXISTING, PURPOSE_REQUIRED_ON_EXISTING, checkName, nameKey, canRemove, usersOf, needsClassification, aliasesWithInci, firstAlias, migrationPlan, migrationWaits } from "../_Shared/classificationRules.js";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const read = (file) => readFileSync(path.join(root, file), "utf8");
let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };
const equal = (a, b, what) => check(JSON.stringify(a) === JSON.stringify(b), `${what} (got ${JSON.stringify(a)})`);

// ---- the list
const list = startingList();
equal(list.length, 23, "the starting list has 23 classifications");
equal(new Set(list.map((c) => c.id)).size, 23, "every classification has its own id");
check(list.some((c) => c.id === FALLBACK_ID && c.name === "Other"), '"Other" has the id "other"');
equal(startingList(), list, "the ids are the same every time, so seeding twice cannot make it twice");
equal(STARTING_NAMES.includes("Filler / Bulking agent"), true, "a name with a slash is in the list");
equal(STARTING_NAMES.length, new Set(STARTING_NAMES.map(nameKey)).size, "no starting name is there twice");
equal(STARTING_NAMES[STARTING_NAMES.length - 1], "Other", '"Other" is last');
// seeding again: only what is missing, nothing duplicated, nothing removed or renamed
equal(missingStarting(list).length, 0, "a full list has nothing missing: running it twice adds nothing");
const partial = [{ id: "mine", name: "  preservative " }, { id: "other", name: "Everything else" }, { id: "custom", name: "My own" }];
const added = missingStarting(partial);
equal(added.length, 21, "capital letters and spaces are ignored; a renamed fallback counts as Other: 23 - 2 = 21 added");
check(!added.some((c) => nameKey(c.name) === "preservative" || c.id === "other"), "an existing name and the existing fallback are not added again");
check(added.every((c) => !partial.some((p) => p.id === c.id)), "a new id never collides with an existing one");
equal([startingId("pH adjuster"), startingId("Wax / Structuring agent"), startingId("Filler / Bulking agent"), startingId("Other"), startingId("Preservative")],
      ["ph-adjuster", "wax-structuring-agent", "filler-bulking-agent", "other", "preservative"], "the id is the name: lowercase, spaces and slashes become hyphens");
check(list.every((c) => /^[a-z0-9]+(-[a-z0-9]+)*$/.test(c.id)), "every starting id is lowercase words joined by hyphens");
equal(list.map((c) => c.id), STARTING_NAMES.map(startingId), "the ids are the same for everybody: two admins seeding at once write the same documents");
const shared = read("_Shared/adminClassifications.js"), js2 = read("_Admin_Ingredient_Curator/adminIngredientCurator.js"), combo = read("_Shared/adminSelect.js");
check(/document\.body\.appendChild\(pop\)/.test(combo) && /box\.appendChild\(pop\)/.test(combo), "the open list is moved to the page body, out of the window's scroll area");
check(/if \(down\) \{ pop\.style\.top/.test(combo) && /pop\.style\.bottom = window\.innerHeight/.test(combo), "the list opens upward when there is no room below");
check(/CLASSIFICATION_REQUIRED_ON_EXISTING \|\| !lacksClass\(existing\)/.test(js2) && /PURPOSE_REQUIRED_ON_EXISTING \|\| existing\.purpose/.test(js2), "classification and purpose are two separate decisions about existing ingredients; a new one needs both");
check(/if \(TEST_MODE\) \{ setFormError/.test(js2) && /if \(TEST_MODE\) \{ toast/.test(shared), "on a test address saving an ingredient and the field change are switched off");


// ---- names
equal(checkName(list, "preservative", null), "This classification already exists", "capital letters do not make a new name");
equal(checkName(list, "  Preservative  ", null), "This classification already exists", "extra spaces do not make a new name");
equal(checkName(list, "Chelating   agent", null), "This classification already exists", "extra spaces inside do not make a new name");
equal(checkName(list, "Preservative", "preservative"), "", "a classification may keep its own name in another spelling");
equal(checkName(list, "   ", null), "Type a name.", "a name is required");
check(checkName(list, "x".repeat(MAX_NAME + 1), null).includes(String(MAX_NAME)), "a name that is too long is refused");
equal(checkName(list, "x".repeat(MAX_NAME), null), "", "a name of the longest length is allowed");
equal(nameKey(" A   b "), "a b", "the key ignores capitals and spaces");
equal(canRemove("other"), false, '"Other" cannot be removed');
equal(canRemove("preservative"), true, "the others can be removed");

// ---- usage
const items = [{ id: "a", classificationId: "binder" }, { id: "b", classificationId: "binder", binned: true }, { id: "c", classificationId: "" }, { id: "d", classificationId: "gone" }];
equal(usersOf(items, "binder").length, 2, "an ingredient in the Recycle bin counts too: restoring it must not point at a deleted classification");
equal(items.map((i) => needsClassification(i, list)), [false, false, true, true], "empty, or pointing at a deleted one, needs a classification");

// ---- the move of an old INCI value
equal(aliasesWithInci("Salt", "Sodium Chloride", []), ["Sodium Chloride"], "a different value becomes an alias");
equal(aliasesWithInci("PHENOXYETHANOL", "Phenoxyethanol", []), [], "only capitals differ: no alias");
equal(aliasesWithInci("Salt", "sodium  chloride", ["Sodium Chloride"]), ["Sodium Chloride"], "no duplicates");
equal(aliasesWithInci("Salt", "Sodium Chloride", ["NaCl"]), ["NaCl", "Sodium Chloride"], "an existing first alias stays first");
equal(aliasesWithInci("Salt", "", ["NaCl"]), ["NaCl"], "no old value, nothing changes");
equal(firstAlias({ aliases: ["NaCl", "x"] }), "NaCl", "the first alias is the common name");
equal(firstAlias({ aliases: [] }), "", "no alias, no common name");

// ---- the plan
const documents = [
    { id: "moves", name: "Salt", techName: "Sodium Chloride", aliases: [] },
    { id: "same", name: "Water", techName: "WATER", aliases: [] },
    { id: "there", name: "Talc", techName: "Cosmetic Talc", aliases: ["cosmetic talc"] },
    { id: "binned", name: "Old", techName: "Older", aliases: [], binned: true },
    { id: "done", name: "Clay", aliases: ["Kaolin"], classificationId: "binder", purpose: "Binds." }
];
const plan = migrationPlan(documents);
equal(plan.updates.length, 4, "everything not changed yet is in the plan, also what is in the Recycle bin");
equal(plan.moved, 2, "two values move into the aliases");
equal(plan.sameAsName, 1, "one value is the same as the name");
equal(plan.alreadyAlias, 1, "one value is an alias already");
check(!plan.updates.some((u) => u.id === "done"), "an entry that was changed already is left alone: a second run only finishes the rest");
equal(plan.updates.find((u) => u.id === "moves").aliases, ["Sodium Chloride"], "the alias is in the plan");
check(!Object.keys(plan.updates[0]).includes("classificationId"), "nothing is guessed: the plan holds no classification");
equal(migrationPlan([{ id: "x", name: "Y", classificationId: "", purpose: "" }]).updates.length, 0, "marked and without the old field: nothing to do");
equal(migrationPlan([{ id: "x", name: "Y", techName: "Z", classificationId: "binder", purpose: "Keeps" }]).updates[0].marked, true, "a chosen classification is kept when the AI look-up's old field is taken off");
equal(migrationWaits(documents.slice(4), []), true, "without the list the change still waits");
equal(migrationWaits(documents.slice(4), list), false, "with the list and nothing to move it is finished");

// ---- the test copy: only the live site writes the real list
equal(collectionFor("glowup-admin-snowy.vercel.app"), "ingredientClassifications", "the live site uses the real list");
equal(collectionFor("glowup-admin-test.vercel.app"), "ingredientClassificationsTest", "a preview uses the test list");
equal(collectionFor("localhost"), "ingredientClassificationsTest", "localhost uses the test list");
equal(collectionFor(""), "ingredientClassificationsTest", "an unknown address uses the test list");
equal(isTestHost("GLOWUP-ADMIN-SNOWY.vercel.app"), false, "capital letters in the address do not matter");
equal([CLASSIFICATION_REQUIRED_ON_EXISTING, PURPOSE_REQUIRED_ON_EXISTING], [false, false], "the two decisions are separate constants");

// ---- the page
const js = read("_Admin_Ingredient_Curator/adminIngredientCurator.js"), html = read("_Admin_Ingredient_Curator/adminIngredientCurator.html");
// (shared is read above)
check(/<label for="ingredientNameInput">INCI name/.test(html), 'the name box is called "INCI name"');
check(!/ingredientTechInput/.test(html + js), "the old INCI box is gone");
check(/<select id="ingredientClassInput">/.test(html), "the classification is a drop-down, not free text");
check(/\+ Add new classification/.test(js), 'the drop-down ends with "+ Add new classification"');
check(/One line on what this ingredient does in a product\./.test(html) && /ingredientPurposeCount/.test(html) && /MAX_PURPOSE/.test(js), "Purpose has its help text and a counter");
check(/Choose the ingredient classification/.test(js) && /Write one line on what this ingredient does/.test(js), "classification and purpose are required");
check(/classificationId: target/.test(shared) && /usersOf\(context\.ingredients\(\), item\.id\)/.test(shared), "removing moves the users first");
check(shared.indexOf("batch.commit()") < shared.indexOf("deleteDoc(doc(db, COLLECTION, item.id))"), "the classification is deleted only after the move");
check(/N?\$\{many\(count, "ingredient uses", "ingredients use"\)\} '\$\{item\.name\}'\. Move them to:/.test(shared), "the remove window says who uses it and asks where to move them");
check(/Rename '\$\{item\.name\}' to '\$\{name\}'\? This updates/.test(shared), "a rename says how many ingredients it updates");
const migration = shared.slice(shared.indexOf("export async function openFieldMigration"));
check(migration.indexOf("downloadBackup(documents);") < migration.indexOf("batch.set(doc(db, COLLECTION"), "the backup is saved before anything is written");
check(/classifications\(\)\.length === 0/.test(js) && /addStartingClassifications\(\)/.test(js), "an empty database gets the starting list once, when the Curator opens");
check(/makeCombo\(select\)/.test(js) && /data-add/.test(js) && /placeholder\.disabled = true/.test(js), "the dropdown is the site's own: placeholder not selectable, + Add under a line");

// ---- the rules
const rules = read("firestore.rules");
const block = rules.split("match /ingredientClassifications/")[1].split("match /")[0];
check(/allow read: if true/.test(block), "everyone reads the classifications (the app shows them)");
check(/allow create, update: if isAdmin\(\)/.test(block), "only an admin adds or renames");
check(/allow delete: if isAdmin\(\) && classificationId != 'other'/.test(block), '"other" can never be deleted');

console.log(`\nclassification: ${checks} checks made`);
console.log(failed === 0 ? "ALL PASSED" : `${failed} FAILED`);
process.exit(failed === 0 ? 0 : 1);
