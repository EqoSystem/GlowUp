


const LOCAL = /^(localhost|127\.0\.0\.1|glowup\.test)$/i.test(location.hostname);
if (!LOCAL) ["log", "info", "debug", "warn", "error", "trace", "table", "group", "groupCollapsed", "groupEnd", "dir"].forEach((name) => { try { console[name] = () => {}; } catch (error) {                         } });


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

export function catchFailures(show) {
    const say = (text) => {
        if (text === lastText && Date.now() - lastAt < 4000) return;
        lastText = text; lastAt = Date.now();
        show(text);
    };
    window.addEventListener("unhandledrejection", (event) => { if (!LOCAL) event.preventDefault(); say(whyItFailed(event.reason)); });
    window.addEventListener("error", (event) => {
        if (!event.error && !event.message) return;

        if (/ResizeObserver loop/.test(String(event.message || ""))) { event.stopImmediatePropagation(); return; }
        if (!LOCAL) event.preventDefault();
        say(whyItFailed(event.error || { message: event.message }));
    });
}
