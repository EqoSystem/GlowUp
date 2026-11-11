// KEYBOARD SHORTCUTS, the catalogue (9 November 2026): every action an admin may give a combo, on every page.
//
// The site is several pages, so the list in Quick settings must know the actions of pages that are not open: they are
// written down here once. A page gives an action its HANDLER at run time (shortcuts.js bindActions); an action whose page is
// not open is shown in the list and in the command palette, but runs only on its own page.
//
// Rules of the defaults (approved 9 November 2026):
//   Ctrl+Alt + a letter for a page's actions; the same letter keeps its meaning across pages (N = new, I = import, A = approve).
//   Ctrl+Alt+1..5 = go to a page (sidebar order, then the Recycle bin and the Archive).
//   Destructive actions have NO default combo; they are in the palette and still ask first.
//   ?, Ctrl/Cmd+K, Esc and / are built in and cannot be changed.
export const PAGES = {
    all: "Every page",
    dashboard: "Dashboard",
    inventory: "Brand & Product Inventory",
    curator: "Ingredient Curator",
    bin: "Recycle Bin",
    archive: "Archive"
};
export const PAGE_ORDER = ["all", "dashboard", "inventory", "curator", "bin", "archive"];
export const PAGE_URLS = {
    dashboard: "../_Admin_Dashboard/adminDashboard.html",
    inventory: "../_Admin_Brand_Inventory/adminBrandInventory.html",
    curator: "../_Admin_Ingredient_Curator/adminIngredientCurator.html",
    bin: "../_Admin_Recycle_Bin/adminRecycleBin.html",
    archive: "../_Admin_Archive/adminArchive.html"
};

/** { id, scope, label, description (≤ 120), keys (default or ""), destructive?, paletteOnly? } */
export const CATALOG = [
    { id: "nav.dashboard", scope: "all", label: "Go to Dashboard", description: "Opens the Dashboard.", keys: "Ctrl+Alt+Digit1" },
    { id: "nav.inventory", scope: "all", label: "Go to Brand & Product Inventory", description: "Opens Brand & Product Inventory.", keys: "Ctrl+Alt+Digit2" },
    { id: "nav.curator", scope: "all", label: "Go to Ingredient Curator", description: "Opens the Ingredient Curator.", keys: "Ctrl+Alt+Digit3" },
    { id: "nav.bin", scope: "all", label: "Go to Recycle Bin", description: "Opens the Recycle Bin.", keys: "Ctrl+Alt+Digit4" },
    { id: "nav.archive", scope: "all", label: "Go to Archive", description: "Opens the Archive.", keys: "Ctrl+Alt+Digit5" },
    { id: "app.quickSettings", scope: "all", label: "Quick settings", description: "Opens or closes Quick settings.", keys: "" },

    { id: "dashboard.activityLog", scope: "dashboard", label: "Activity log", description: "Opens the activity log.", keys: "Ctrl+Alt+KeyL" },
    { id: "dashboard.addProduct", scope: "dashboard", label: "Add product", description: "Opens Brand & Product Inventory with the Add product form.", keys: "Ctrl+Alt+KeyP" },

    { id: "inventory.new", scope: "inventory", label: "New…", description: "Opens the Add menu: brand or product.", keys: "Ctrl+Alt+KeyN" },
    { id: "inventory.addBrand", scope: "inventory", label: "Add brand", description: "Opens the Add brand form.", keys: "Ctrl+Alt+KeyB" },
    { id: "inventory.addProduct", scope: "inventory", label: "Add product", description: "Opens the Add product form.", keys: "Ctrl+Alt+KeyP" },
    { id: "inventory.import", scope: "inventory", label: "Import a file", description: "Opens the file chooser to import brands and products.", keys: "Ctrl+Alt+KeyI" },
    { id: "inventory.filters", scope: "inventory", label: "Filters", description: "Opens the Filters panel of the inventory list.", keys: "Ctrl+Alt+KeyF" },
    { id: "inventory.approveDrafts", scope: "inventory", label: "Approve reviewed drafts", description: "Asks first, then approves the drafts you ticked as reviewed.", keys: "Ctrl+Alt+KeyA" },
    { id: "inventory.sort", scope: "inventory", label: "Sort", description: "Opens the sort choices of the inventory list.", keys: "", paletteOnly: true },

    { id: "curator.addIngredient", scope: "curator", label: "Add ingredient", description: "Opens the Add ingredient form.", keys: "Ctrl+Alt+KeyN" },
    { id: "curator.import", scope: "curator", label: "Import a file", description: "Opens the file chooser to import ingredients.", keys: "Ctrl+Alt+KeyI" },
    { id: "curator.classifications", scope: "curator", label: "Classifications", description: "Opens the list of ingredient classifications.", keys: "Ctrl+Alt+KeyC" },
    { id: "curator.approveDrafts", scope: "curator", label: "Approve reviewed drafts", description: "Asks first, then approves the drafts you ticked as reviewed.", keys: "Ctrl+Alt+KeyA" },
    { id: "curator.sort", scope: "curator", label: "Sort", description: "Opens the sort choices of the ingredient list.", keys: "", paletteOnly: true },

    { id: "bin.restore", scope: "bin", label: "Restore selected", description: "Restores the ticked entries to their lists.", keys: "Ctrl+Alt+KeyR" },
    { id: "bin.archive", scope: "bin", label: "Move selected to Archive", description: "Asks first, then moves the ticked entries to the Archive.", keys: "", destructive: true, paletteOnly: true },
    { id: "bin.delete", scope: "bin", label: "Delete selected permanently", description: "Asks first, then deletes the ticked entries for good.", keys: "", destructive: true, paletteOnly: true },

    { id: "archive.restore", scope: "archive", label: "Restore selected", description: "Restores the ticked entries.", keys: "Ctrl+Alt+KeyR" },
    { id: "archive.delete", scope: "archive", label: "Delete selected now", description: "Asks first, then deletes the ticked entries for good.", keys: "", destructive: true, paletteOnly: true }
];

/** The keys every page has and nobody can change (shown in the "?" help). */
export const BUILT_IN = [
    { keys: "?", label: "Shortcut help", description: "Shows the shortcuts of this page." },
    { keys: "Ctrl+KeyK", label: "Command palette", description: "Search every page and action." },
    { keys: "Esc", label: "Close", description: "Closes the dialog, menu or panel on top." },
    { keys: "/", label: "Search", description: "Puts the cursor in this page's search box." }
];

export const catalogById = new Map(CATALOG.map((a) => [a.id, a]));
/** The actions that can be given a combo (palette-only ones too: an admin may bind them; destructive ones still ask first). */
export const bindable = (scope) => CATALOG.filter((a) => !scope || a.scope === scope);
