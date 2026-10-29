// THE SETTINGS MENU behind the GlowUp logo at the top of the sidebar, on every admin page.
//
// The logo and the name are one button (pageFrame.js draws it; with the menu folded, the logo alone). It opens:
//   Layout and style      - the floating Layout & Style window (adminLayouts.js)
//   Recycle bin           - the Recycle Bin page, with a red count when something is in it
//   Archive               - the Archive page (what left the Recycle bin; kept 30 days)
//   Appearance            - Light, Dark or System (System follows the computer); kept in this browser for every page
//   the signed-in e-mail, and Sign Out (the same "Sign out?" question as before: adminCommon.wireSignOut)
// It replaces the header buttons (Default layout, Recycle bin, dark mode, Refresh) and the e-mail / Sign Out at the
// bottom of the sidebar (29 October 2026).
//
// THE RECYCLE BIN COUNT is read-only and uses the existing data code (adminStore.js): first the lists this browser keeps
// (no read at all); only a list that is not kept is counted with one count query (binned == true, the same query the
// Dashboard already makes), remembered for 5 minutes in this tab.
import { auth, db } from "../_Database/firebase-console.js";
import { onAuthStateChanged } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-auth.js";
import { collection, query, where } from "https://www.gstatic.com/firebasejs/10.12.0/firebase-firestore.js";
import { keptBinnedCount, getCountFromServer, onListsRefreshed } from "./adminStore.js";
import { openMenu, themeChoice, setThemeChoice } from "./adminShell.js";
import { openLayoutWindow } from "./adminLayouts.js";

export const BIN_PAGE = "../_Admin_Recycle_Bin/adminRecycleBin.html";
export const ARCHIVE_PAGE = "../_Admin_Archive/adminArchive.html";
const BIN_LISTS = ["ingredients", "brands", "products"];
const COUNT_KEEP_MS = 5 * 60 * 1000;
const COUNT_KEY = "glowup.binCount.";

let binCount = 0;
let recountTimer = 0;

/** Wires the logo button (once). Called by adminShell.mountShell on every admin page. */
export function mountSettingsMenu() {
    const button = document.getElementById("brandMenuBtn");
    if (!button || button.dataset.wired === "1") return;
    button.dataset.wired = "1";
    button.addEventListener("click", () => openMenu(button, menuItems()));
    document.addEventListener("glowup:bin-count", queueRecount);
    onListsRefreshed(queueRecount);
    onAuthStateChanged(auth, (user) => { if (user) queueRecount(); });
}

function menuItems() {
    const choice = themeChoice();
    const user = auth.currentUser;
    const go = (address) => () => { window.location.href = address; };
    const here = location.pathname;
    return [
        { label: "Layout and style", icon: "sliders", description: "Arrange the panels, fonts and colours.", run: () => openLayoutWindow() },
        { label: "Recycle bin", icon: "trash", checked: here.includes("Recycle_Bin"), badge: binCount > 0 ? (binCount > 99 ? "99+" : String(binCount)) : "", description: binCount > 0 ? `${binCount} ${binCount === 1 ? "entry" : "entries"} removed` : "Nothing removed", run: go(BIN_PAGE) },
        { label: "Archive", icon: "archive", checked: here.includes("_Admin_Archive"), description: "Kept 30 days, then deleted.", run: go(ARCHIVE_PAGE) },
        { separator: true },
        { heading: "Appearance" },
        { label: "Light", icon: "sun", checked: choice === "light", run: () => setThemeChoice("light") },
        { label: "Dark", icon: "moon", checked: choice === "dark", run: () => setThemeChoice("dark") },
        { label: "System", icon: "monitor", checked: choice === "system", description: "Follows this computer's setting.", run: () => setThemeChoice("system") },
        { separator: true },
        { note: user && user.email ? user.email : "Signed in", title: "Signed in as" },
        { label: "Sign Out", icon: "logout", danger: true, run: () => document.dispatchEvent(new CustomEvent("glowup:sign-out")) }
    ];
}

function queueRecount() {
    clearTimeout(recountTimer);
    recountTimer = setTimeout(recount, 250);
}

async function recount() {
    if (!auth.currentUser) return;
    const parts = await Promise.all(BIN_LISTS.map((name) => {
        const kept = keptBinnedCount(name);
        return kept !== null ? kept : countedBinned(name);
    }));
    binCount = parts.reduce((sum, n) => sum + (Number(n) || 0), 0);
    paintBadge();
}

/** One count query for a list this browser does not keep, remembered for 5 minutes in this tab. 0 when it fails. */
async function countedBinned(name) {
    try {
        const saved = JSON.parse(sessionStorage.getItem(COUNT_KEY + name) || "null");
        if (saved && Date.now() - saved.at < COUNT_KEEP_MS) return Number(saved.n) || 0;
    } catch (error) { /* not kept: ask */ }
    try {
        const n = (await getCountFromServer(query(collection(db, name), where("binned", "==", true)))).data().count;
        try { sessionStorage.setItem(COUNT_KEY + name, JSON.stringify({ n, at: Date.now() })); } catch (error) { /* not kept */ }
        return n;
    } catch (error) {
        console.warn("The Recycle bin of " + name + " could not be counted:", error);
        return 0;
    }
}

function paintBadge() {
    const badge = document.getElementById("brandBinBadge"), button = document.getElementById("brandMenuBtn");
    if (!badge || !button) return;
    badge.textContent = binCount > 99 ? "99+" : String(binCount);
    badge.hidden = binCount === 0;
    const text = binCount > 0 ? `Settings. Recycle bin: ${binCount}` : "Settings";
    button.setAttribute("aria-label", text);
    button.setAttribute("data-tip", text);
}
