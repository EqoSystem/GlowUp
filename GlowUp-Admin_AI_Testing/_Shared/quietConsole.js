// No debug output on the live website (9 November 2026, the user's wish): the browser's console stays empty there, and a
// failure the page did not handle itself becomes ONE plain message in the corner (the site's own toast), saying why the
// action did not work. On a computer of a developer (localhost, the test rig) the console works as normal.
const LOCAL = /^(localhost|127\.0\.0\.1|glowup\.test)$/i.test(location.hostname);
if (!LOCAL) ["log", "info", "debug", "warn", "error", "trace", "table", "group", "groupCollapsed", "groupEnd", "dir"].forEach((name) => { try { console[name] = () => {}; } catch (error) { /* read-only console */ } });

/** Why an action did not work, in plain words (the same wording as describeError in adminCommon.js). */
export function whyItFailed(reason) {
    const code = reason && reason.code ? String(reason.code) : "";
    if (code.includes("permission-denied")) return "You are not allowed to do this: the database refused it. Your account may not be in the admins list, or the change breaks a database rule.";
    if (code.includes("unauthenticated")) return "You are signed out. Sign in again.";
    if (code.includes("unavailable") || code.includes("network") || (reason && /failed to fetch|network/i.test(String(reason.message || ""))))
        return "The database or the server could not be reached. Check your internet connection and try again.";
    if (code.includes("not-found")) return "That item no longer exists. Refresh the page.";
    if (code.includes("resource-exhausted")) return "Today's free database quota is used up. Try again tomorrow.";
    if (reason && reason.name === "AbortError") return "It took too long and was stopped. Try again.";
    const text = reason && reason.message ? String(reason.message) : typeof reason === "string" ? reason : "";
    return text ? `It did not work: ${text.slice(0, 200)}` : "Something went wrong. Try again, or reload the page.";
}

let lastText = "", lastAt = 0;
/** Shows `text` as an error toast (the same message twice within 4 seconds is shown once). `show(text)` = the page's toast. */
export function catchFailures(show) {
    const say = (text) => {
        if (text === lastText && Date.now() - lastAt < 4000) return;
        lastText = text; lastAt = Date.now();
        show(text);
    };
    window.addEventListener("unhandledrejection", (event) => { if (!LOCAL) event.preventDefault(); say(whyItFailed(event.reason)); });
    window.addEventListener("error", (event) => {
        if (!event.error && !event.message) return;                  // a picture or script that did not load: not an action of the admin
        if (!LOCAL) event.preventDefault();
        say(whyItFailed(event.error || { message: event.message }));
    });
}
