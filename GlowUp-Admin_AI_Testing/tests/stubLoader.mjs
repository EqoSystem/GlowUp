// A module loader for the checks in tests/: the imports that only work in a browser (Firebase from gstatic, the Firebase set-up,
// the store and the shell of the admin pages) are replaced by empty stand-ins that export the same names. Nothing is sent anywhere.
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const STAND_IN = /^https:|firebase-console\.js$|adminStore\.js$|adminShell\.js$/;

/** The names the importing file asks for from this specifier. */
function namesAsked(specifier, parentURL) {
    const names = new Set();
    try {
        const source = readFileSync(fileURLToPath(parentURL), "utf8");
        const pattern = /import\s*\{([^}]*)\}\s*from\s*["']([^"']+)["']/g;
        for (const match of source.matchAll(pattern)) {
            if (match[2] !== specifier) continue;
            match[1].split(",").forEach((part) => { const name = part.trim().split(/\s+as\s+/).pop(); if (name) names.add(name); });
        }
    } catch (error) { /* a stand-in without names */ }
    return names;
}

export async function resolve(specifier, context, nextResolve) {
    if (!STAND_IN.test(specifier)) return nextResolve(specifier, context);
    const code = [...namesAsked(specifier, context.parentURL)].map((name) => `export const ${name} = (...args) => undefined;`).join("\n") + "\nexport default {};\n";
    return { url: "data:text/javascript," + encodeURIComponent(code), shortCircuit: true };
}
