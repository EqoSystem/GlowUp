


import { createRequire } from "node:module";
import crypto from "node:crypto";
const require = createRequire(import.meta.url);
const handler = require("../api/archive-purge.js");
const { purge, settingsOf, isArchiveDocument } = handler.helpers;

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };

const { privateKey } = crypto.generateKeyPairSync("rsa", { modulusLength: 2048 });
const account = JSON.stringify({ project_id: "glowup-admin-portal", client_email: "purge-test@example.invalid", private_key: privateKey.export({ type: "pkcs8", format: "pem" }) });
const ROOT = "projects/glowup-admin-portal/databases/(default)/documents";
const now = new Date("2026-11-30T03:00:00Z");
const day = 86400000;
const archived = (id, daysAgoExpired) => ({ name: `${ROOT}/archive/${id}`, updateTime: "2026-10-30T10:00:00.000000Z", fields: { expires_at: { timestampValue: new Date(now.getTime() - daysAgoExpired * day).toISOString() } } });


function google(rows) {
    const calls = [];
    const fetchFn = async (url, options = {}) => {
        calls.push({ url, method: options.method || "GET", body: options.body });
        const reply = (status, body) => ({ ok: status < 300, status, json: async () => body });
        if (url.startsWith("https://oauth2.googleapis.com/token")) return reply(200, { access_token: "pretend-token" });
        if (url.endsWith(":runQuery")) return reply(200, rows.map((document) => ({ document })));
        if (url.includes("/archive_purge_log?documentId=")) return reply(200, {});
        if (options.method === "DELETE") return reply(200, {});
        return reply(404, {});
    };
    return { calls, fetchFn };
}
const env = (more = {}) => Object.assign({ CRON_SECRET: "s3cret-for-test", FIREBASE_SERVICE_ACCOUNT: account }, more);
const auth = "Bearer s3cret-for-test";


check((await purge({ env: {}, fetchFn: google([]).fetchFn, now, authorization: auth })).status === 503, "without CRON_SECRET nothing runs (503)");
check((await purge({ env: env(), fetchFn: google([]).fetchFn, now, authorization: "Bearer wrong" })).status === 401, "a wrong secret is refused (401)");
check((await purge({ env: env(), fetchFn: google([]).fetchFn, now, authorization: "" })).status === 401, "no secret is refused (401)");
check((await purge({ env: env({ FIREBASE_SERVICE_ACCOUNT: "" }), fetchFn: google([]).fetchFn, now, authorization: auth })).status === 503, "without a service account nothing runs (503)");
check((await purge({ env: env({ FIREBASE_SERVICE_ACCOUNT: account.replace("glowup-admin-portal", "another-project") }), fetchFn: google([]).fetchFn, now, authorization: auth })).status === 503, "a service account of another project is refused");


check(settingsOf({}).live === false && settingsOf({ ARCHIVE_PURGE_LIVE: "true" }).live === false && settingsOf({ ARCHIVE_PURGE_LIVE: "1" }).live === true, "dry run unless ARCHIVE_PURGE_LIVE is exactly 1");
check(settingsOf({}).max === 50 && settingsOf({ ARCHIVE_PURGE_MAX: "5000" }).max === 200 && settingsOf({ ARCHIVE_PURGE_MAX: "0" }).max === 1, "cap: default 50, never above 200, at least 1");


check(isArchiveDocument(`${ROOT}/archive/products__p1`), "an archive document is accepted");
check(!isArchiveDocument(`${ROOT}/products/p1`) && !isArchiveDocument(`${ROOT}/archive/x/sub/y`) && !isArchiveDocument(`${ROOT}/archive_purge_log/x`) && !isArchiveDocument("projects/other/databases/(default)/documents/archive/x"), "anything else is refused");


const rows = [archived("products__p3", 2), archived("brands__b2", 1), { name: `${ROOT}/products/p1`, updateTime: "x", fields: { expires_at: { timestampValue: "2026-01-01T00:00:00Z" } } }, archived("ingredients__i9", -3)];
const dry = google(rows);
const dryRun = await purge({ env: env(), fetchFn: dry.fetchFn, now, authorization: auth });
check(dryRun.status === 200 && dryRun.body.mode === "dry-run", "dry run is the default");
check(dry.calls.every((c) => c.method !== "DELETE"), "a dry run sends no DELETE at all");
check(JSON.stringify(dryRun.body.wouldDelete) === JSON.stringify(["products__p3", "brands__b2"]), "it lists only expired archive documents");
check(dryRun.body.errors.length === 2, "the wrong path and the not-yet-expired entry are reported, not deleted");
check(dry.calls.some((c) => c.url.includes("/archive_purge_log?documentId=") && c.body.includes('"dry-run"')), "the dry run writes its log");
const query = JSON.parse(dry.calls.find((c) => c.url.endsWith(":runQuery")).body).structuredQuery;
check(query.from[0].collectionId === "archive" && query.where.fieldFilter.op === "LESS_THAN_OR_EQUAL" && query.limit === 51, "the query: archive, expires_at <= now, cap + 1");
if (process.argv.includes("--show")) console.log("DRY-RUN ANSWER:\n" + JSON.stringify(dryRun.body, null, 2));


const wet = google(rows);
const liveRun = await purge({ env: env({ ARCHIVE_PURGE_LIVE: "1" }), fetchFn: wet.fetchFn, now, authorization: auth });
const deletes = wet.calls.filter((c) => c.method === "DELETE");
check(liveRun.body.mode === "live" && liveRun.body.deleted === 2 && deletes.length === 2, "live deletes the two expired entries only");
check(deletes.every((c) => c.url.includes("/documents/archive/") && c.url.includes("currentDocument.updateTime=")), "every delete is in archive and has the precondition");


const many = Array.from({ length: 8 }, (_, i) => archived("products__m" + i, 1));
const capped = google(many);
const cappedRun = await purge({ env: env({ ARCHIVE_PURGE_LIVE: "1", ARCHIVE_PURGE_MAX: "5" }), fetchFn: capped.fetchFn, now, authorization: auth });
check(cappedRun.body.deleted === 5 && cappedRun.body.capped === true, "at most the cap per run, and it says it was capped");

console.log("checks made: " + checks);
console.log(failed === 0 ? "ALL PASSED" : failed + " FAILED");
process.exit(failed === 0 ? 0 : 1);
