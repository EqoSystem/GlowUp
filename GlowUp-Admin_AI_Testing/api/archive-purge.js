// Archive purge: once a day (Vercel Cron, see vercel.json) the entries of the "archive" collection whose expires_at has
// passed are deleted for good. Design and impact check: ARCHIVE_DESIGN.md.
//
//   Who may call:  only a caller that sends "Authorization: Bearer <CRON_SECRET>" (Vercel Cron does that by itself once the
//                  env var CRON_SECRET is set). Without CRON_SECRET the function answers 503 and does nothing.
//   Dry run:       THE DEFAULT. It lists what it would delete and deletes nothing. Only ARCHIVE_PURGE_LIVE = "1" deletes.
//   What it may delete: documents directly in "archive" whose expires_at is in the past, checked in the query AND again for each
//                  document, each delete with a precondition on the document's update time. No other path is ever built.
//   How many:      at most ARCHIVE_PURGE_MAX per run (default 50, never more than 200); the rest waits for the next day.
//   Log:           every run (dry or live) writes archive_purge_log/<run> and prints one JSON line to the Vercel log.
//   Credentials:   a cron job has no signed-in admin, so it needs a Google service account: FIREBASE_SERVICE_ACCOUNT = the JSON
//                  key in one line. Only the project's owner creates it and sets it in Vercel. Nothing to install (plain fetch + crypto).
"use strict";

const crypto = require("crypto");

const PROJECT = "glowup-admin-portal";
const DEFAULT_MAX = 50, HARD_MAX = 200, MAX_LOGGED_IDS = 200;
const SCOPE = "https://www.googleapis.com/auth/datastore";
const TOKEN_URL = "https://oauth2.googleapis.com/token";
const COLLECTION = "archive";
const LOG_COLLECTION = "archive_purge_log";

function send(res, status, body) {
    res.statusCode = status;
    res.setHeader("Content-Type", "application/json; charset=utf-8");
    res.setHeader("Cache-Control", "no-store");
    res.end(JSON.stringify(body));
}

/** Compares two secrets in constant time. */
function sameSecret(given, expected) {
    const a = Buffer.from(String(given || "")), b = Buffer.from(String(expected || ""));
    return a.length > 0 && a.length === b.length && crypto.timingSafeEqual(a, b);
}

/** Dry run unless ARCHIVE_PURGE_LIVE is exactly "1"; the cap between 1 and 200. */
function settingsOf(env) {
    const asked = parseInt(env.ARCHIVE_PURGE_MAX, 10);
    return { live: env.ARCHIVE_PURGE_LIVE === "1", max: Math.min(HARD_MAX, Math.max(1, Number.isFinite(asked) ? asked : DEFAULT_MAX)) };
}

/** The service account from FIREBASE_SERVICE_ACCOUNT, only for this project. Null when it is missing or for another project. */
function serviceAccountOf(env) {
    try {
        const account = JSON.parse(env.FIREBASE_SERVICE_ACCOUNT || "");
        if (account && account.client_email && account.private_key && account.project_id === PROJECT) return account;
    } catch (error) { /* not set, or not JSON */ }
    return null;
}

async function accessToken(account, fetchFn, nowSeconds) {
    const part = (value) => Buffer.from(JSON.stringify(value)).toString("base64url");
    const unsigned = part({ alg: "RS256", typ: "JWT" }) + "." + part({ iss: account.client_email, scope: SCOPE, aud: TOKEN_URL, iat: nowSeconds, exp: nowSeconds + 600 });
    const signature = crypto.createSign("RSA-SHA256").update(unsigned).sign(account.private_key).toString("base64url");
    const answer = await fetchFn(TOKEN_URL, {
        method: "POST", headers: { "Content-Type": "application/x-www-form-urlencoded" },
        body: "grant_type=" + encodeURIComponent("urn:ietf:params:oauth:grant-type:jwt-bearer") + "&assertion=" + unsigned + "." + signature
    });
    if (!answer.ok) throw new Error("Google refused the service account (" + answer.status + ").");
    const token = (await answer.json()).access_token;
    if (!token) throw new Error("Google sent no access token.");
    return token;
}

const DOCS_PATH = `projects/${PROJECT}/databases/(default)/documents`;
const API = "https://firestore.googleapis.com/v1/";

/** True only for a document directly in "archive" of this project: archive/<id>, nothing deeper, nothing else. */
function isArchiveDocument(name) {
    const prefix = DOCS_PATH + "/" + COLLECTION + "/";
    return typeof name === "string" && name.startsWith(prefix) && /^[^/]+$/.test(name.slice(prefix.length));
}

/** The expired archive documents, oldest first, at most `limit`. */
async function findExpired(token, fetchFn, nowIso, limit) {
    const answer = await fetchFn(API + DOCS_PATH + ":runQuery", {
        method: "POST", headers: { Authorization: "Bearer " + token, "Content-Type": "application/json" },
        body: JSON.stringify({ structuredQuery: {
            from: [{ collectionId: COLLECTION }],
            where: { fieldFilter: { field: { fieldPath: "expires_at" }, op: "LESS_THAN_OR_EQUAL", value: { timestampValue: nowIso } } },
            orderBy: [{ field: { fieldPath: "expires_at" }, direction: "ASCENDING" }],
            limit
        } })
    });
    if (!answer.ok) throw new Error("The archive could not be read (" + answer.status + ").");
    const rows = await answer.json();
    return (Array.isArray(rows) ? rows : []).map((row) => row && row.document).filter(Boolean);
}

async function writeLog(token, fetchFn, runId, record) {
    const fields = {
        ranAt: { timestampValue: record.ranAt }, mode: { stringValue: record.mode },
        found: { integerValue: String(record.found) }, deleted: { integerValue: String(record.deleted) }, capped: { booleanValue: record.capped },
        ids: { arrayValue: { values: record.ids.slice(0, MAX_LOGGED_IDS).map((id) => ({ stringValue: id })) } },
        errors: { arrayValue: { values: record.errors.slice(0, 20).map((text) => ({ stringValue: String(text).slice(0, 300) })) } }
    };
    const answer = await fetchFn(API + DOCS_PATH + "/" + LOG_COLLECTION + "?documentId=" + encodeURIComponent(runId), {
        method: "POST", headers: { Authorization: "Bearer " + token, "Content-Type": "application/json" }, body: JSON.stringify({ fields })
    });
    return answer.ok;
}

/**
 * One run. `env` = the environment, `fetchFn` = fetch (pretend in the tests), `now` = Date. Returns { status, body }.
 * Nothing is deleted unless env.ARCHIVE_PURGE_LIVE === "1".
 */
async function purge({ env, fetchFn, now, authorization }) {
    if (!env.CRON_SECRET) return { status: 503, body: { error: "Not set up: CRON_SECRET is missing, so nothing runs." } };
    if (!sameSecret(String(authorization || "").replace(/^Bearer\s+/i, ""), env.CRON_SECRET)) return { status: 401, body: { error: "Not allowed." } };
    const account = serviceAccountOf(env);
    if (!account) return { status: 503, body: { error: "Not set up: FIREBASE_SERVICE_ACCOUNT is missing or not for " + PROJECT + "." } };

    const { live, max } = settingsOf(env);
    const nowIso = now.toISOString();
    const record = { ranAt: nowIso, mode: live ? "live" : "dry-run", found: 0, deleted: 0, capped: false, ids: [], errors: [] };
    const token = await accessToken(account, fetchFn, Math.floor(now.getTime() / 1000));
    const found = await findExpired(token, fetchFn, nowIso, max + 1);
    record.capped = found.length > max;
    const due = found.slice(0, max).filter((d) => {
        const end = d.fields && d.fields.expires_at && d.fields.expires_at.timestampValue;
        const ok = isArchiveDocument(d.name) && !!end && Date.parse(end) <= now.getTime();
        if (!ok) record.errors.push("skipped (not an expired archive document): " + String(d.name).slice(-120));
        return ok;
    });
    record.found = due.length;
    record.ids = due.map((d) => d.name.split("/").pop());

    if (live) {
        for (const d of due) {
            const answer = await fetchFn(API + d.name + "?currentDocument.updateTime=" + encodeURIComponent(d.updateTime), { method: "DELETE", headers: { Authorization: "Bearer " + token } });
            if (answer.ok) record.deleted++;
            else record.errors.push("not deleted (" + answer.status + "): " + d.name.split("/").pop());
        }
    }
    const runId = nowIso.replace(/[^0-9]/g, "").slice(0, 17) + "_" + record.mode;
    let logged = false;
    try { logged = await writeLog(token, fetchFn, runId, record); } catch (error) { record.errors.push("log not written: " + error.message); }
    const body = Object.assign({ logged, max, wouldDelete: live ? undefined : record.ids }, record);
    console.log(JSON.stringify({ archivePurge: body }));
    return { status: 200, body };
}

module.exports = async (req, res) => {
    if (req.method !== "GET" && req.method !== "POST") return send(res, 405, { error: "Use GET or POST." });
    try {
        const result = await purge({ env: process.env, fetchFn: fetch, now: new Date(), authorization: req.headers && req.headers.authorization });
        send(res, result.status, result.body);
    } catch (error) {
        console.error("archive purge failed:", error && error.message);
        send(res, 500, { error: "The purge did not run: " + (error && error.message ? error.message : "unknown error") });
    }
};
module.exports.helpers = { purge, settingsOf, isArchiveDocument, sameSecret, serviceAccountOf, DEFAULT_MAX, HARD_MAX };
