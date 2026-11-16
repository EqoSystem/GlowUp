











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


function sameSecret(given, expected) {
    const a = Buffer.from(String(given || "")), b = Buffer.from(String(expected || ""));
    return a.length > 0 && a.length === b.length && crypto.timingSafeEqual(a, b);
}


function settingsOf(env) {
    const asked = parseInt(env.ARCHIVE_PURGE_MAX, 10);
    return { live: env.ARCHIVE_PURGE_LIVE === "1", max: Math.min(HARD_MAX, Math.max(1, Number.isFinite(asked) ? asked : DEFAULT_MAX)) };
}


function serviceAccountOf(env) {
    try {
        const account = JSON.parse(env.FIREBASE_SERVICE_ACCOUNT || "");
        if (account && account.client_email && account.private_key && account.project_id === PROJECT) return account;
    } catch (error) {                            }
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


function isArchiveDocument(name) {
    const prefix = DOCS_PATH + "/" + COLLECTION + "/";
    return typeof name === "string" && name.startsWith(prefix) && /^[^/]+$/.test(name.slice(prefix.length));
}


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
