// Search and import on the Curator: how typed names are split.
import assert from "node:assert/strict";
import { register } from "node:module";
register("./stubLoader.mjs", import.meta.url);
const { parseNames } = await import("../_Shared/draftIngredient.js");
let checks = 0;
const same = (text, want) => { assert.deepEqual(parseNames(text), want); checks++; };
same("Niacinamide", ["Niacinamide"]);
same("1,2-Hexanediol\r\nAqua; Mica, Talc,\n\naqua", ["1,2-Hexanediol", "Aqua", "Mica", "Talc"]);
same("  ", []);
console.log(`checks made: ${checks}\nALL PASSED`);
