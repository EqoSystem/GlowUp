// Log-in lock-out (20 November 2026): after 3 wrong passwords for one e-mail, the log-in page refuses that e-mail for 5 minutes
// and recommends "Forgot password?". The count lives in this browser only (localStorage), keyed by a hash of the e-mail so the
// address itself is not stored. Firebase Authentication still applies its own server-side limit ("too many requests") on top;
// this lock is the friendly, visible part of it. A successful log-in clears the count.

export const MAX_TRIES = 3;
export const LOCK_MS = 5 * 60 * 1000;
const KEY = "glowup.loginLock.v1";

// FNV-1a: a short, stable key for an e-mail (not a security hash; it only keeps the address out of storage)
function keyOf(email) {
    let h = 0x811c9dc5;
    const s = String(email || "").trim().toLowerCase();
    for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 0x01000193) >>> 0; }
    return h.toString(16);
}

export function createLockout(storage, now = () => Date.now()) {
    const read = () => {
        try { const v = JSON.parse(storage.getItem(KEY) || "{}"); return v && typeof v === "object" ? v : {}; }
        catch (error) { return {}; }
    };
    const write = (all) => { try { storage.setItem(KEY, JSON.stringify(all)); } catch (error) { } };
    const tidy = (all) => {
        const t = now();
        for (const k of Object.keys(all)) {
            const e = all[k];
            // a finished lock starts again from zero; a stale count (older than a day) is forgotten
            if (!e || (e.until && e.until <= t) || (!e.until && t - (e.at || 0) > 86400000)) delete all[k];
        }
        return all;
    };
    return {
        /** Milliseconds this e-mail is still locked for (0 = not locked). */
        remaining(email) {
            const e = tidy(read())[keyOf(email)];
            return e && e.until ? Math.max(0, e.until - now()) : 0;
        },
        /** A wrong password for this e-mail. Returns { fails, left, lockedMs }. */
        fail(email) {
            const all = tidy(read());
            const k = keyOf(email);
            const e = all[k] || { fails: 0 };
            e.fails += 1;
            e.at = now();
            if (e.fails >= MAX_TRIES) e.until = now() + LOCK_MS;
            all[k] = e;
            write(all);
            return { fails: e.fails, left: Math.max(0, MAX_TRIES - e.fails), lockedMs: e.until ? e.until - now() : 0 };
        },
        /** A successful log-in. */
        clear(email) {
            const all = read();
            delete all[keyOf(email)];
            write(all);
        }
    };
}

export function mmss(ms) {
    const s = Math.max(0, Math.ceil(ms / 1000));
    return Math.floor(s / 60) + ":" + String(s % 60).padStart(2, "0");
}
