



import { launch, openPage } from "./harness.mjs";

let checks = 0, failed = 0;
const ok = (good, what) => { checks++; if (!good) { failed++; console.log("FAILED: " + what); } };
const openModals = (page) => page.locator(".modal-overlay[style*='flex']").count();

const synth = (page, init) => page.evaluate((init) => (document.activeElement || document.body).dispatchEvent(new KeyboardEvent("keydown", Object.assign({ bubbles: true, cancelable: true }, init))), init);

const browser = await launch();
try {

    const { page, errors, settle } = await openPage(browser, "inv", { width: 1440, height: 900 });
    await settle(1200);
    await page.mouse.click(700, 880);

    await page.keyboard.press("?");
    await settle(200);
    ok(await page.locator(".kb-help").count() === 1, "? opens the help");
    ok(/Add brand/.test(await page.textContent(".kb-help")) && /Built in/.test(await page.textContent(".kb-help")), "the help lists this page's shortcuts and the built-in keys");
    ok(!/Add ingredient/.test(await page.textContent(".kb-help")), "the help does not list another page's shortcuts");
    await page.keyboard.press("Escape");
    await settle(150);
    ok(await page.locator(".kb-help").count() === 0, "Esc closes the help");

    await page.keyboard.press("Control+Alt+KeyB");
    await settle(400);
    ok(await openModals(page) === 1 && /Brand/.test(await page.locator(".modal-overlay[style*='flex'] h3").first().textContent()), "Ctrl+Alt+B opens Add brand on Inventory");
    await page.keyboard.press("Control+Alt+KeyB");
    await page.keyboard.press("Control+Alt+KeyP");
    await settle(300);
    ok(await openModals(page) === 1, "a shortcut does nothing while a window is open (no stacked windows)");
    await page.keyboard.press("Escape");
    await settle(300);

    await page.click(".topbar .search-container input");
    await page.keyboard.press("Control+Alt+KeyB");
    await settle(300);
    ok(await openModals(page) === 0, "a shortcut does nothing while typing in the search box");
    await page.mouse.click(700, 880);

    await synth(page, { code: "KeyB", key: "b", ctrlKey: true, altKey: true, repeat: true });
    await settle(300);
    ok(await openModals(page) === 0, "a held-down key (repeat) does nothing");

    await page.keyboard.press("Control+Alt+KeyP");
    await settle(400);
    ok(await openModals(page) === 1 && /Product/i.test(await page.locator(".modal-overlay[style*='flex'] h3").first().textContent()), "Ctrl+Alt+P opens Add product on Inventory");
    await page.keyboard.press("Escape");
    await settle(300);

    await page.locator("#addMenuBtn").click();
    await settle(200);
    ok(await page.locator(".menu .menu-item .kb-combo").count() === 3, "the Add menu shows its three combos as key chips");
    ok(await page.locator(".menu .menu-item[aria-keyshortcuts]").count() === 3, "the Add menu items carry aria-keyshortcuts");
    await page.keyboard.press("Escape");
    ok(await page.getAttribute("#chooseInventoryFileBtn", "aria-keyshortcuts") === "Control+Alt+I", "a bound button carries aria-keyshortcuts");
    ok(await page.getAttribute("#chooseInventoryFileBtn", "title") === null, "no hover label is added");


    await page.focus("#addMenuBtn");
    await page.keyboard.press("Control+KeyK");
    await settle(200);
    ok(await page.locator(".kb-palette").count() === 1 && await page.evaluate(() => document.activeElement.classList.contains("kb-search")), "Ctrl+K opens the palette with the cursor in its search");
    await page.keyboard.type("restore");
    await settle(100);
    ok(/Restore selected/.test(await page.textContent(".kb-results")) && /On Recycle Bin/.test(await page.textContent(".kb-results")), "another page's action is found, and says where it works");
    await page.keyboard.press("Escape");
    await settle(150);
    ok(await page.locator(".kb-palette").count() === 0 && await page.evaluate(() => document.activeElement.id) === "addMenuBtn", "Esc closes the palette and focus goes back to where it was");
    await page.keyboard.press("Control+KeyK");
    await page.keyboard.type("add brand");
    await page.keyboard.press("Enter");
    await settle(400);
    ok(await openModals(page) === 1 && /Brand/.test(await page.locator(".modal-overlay[style*='flex'] h3").first().textContent()), "Enter in the palette runs the action");
    await page.keyboard.press("Escape");
    await settle(300);


    await page.click("#quickSettingsBtn");
    await settle(500);
    await page.locator(".settings-window .menu-item", { hasText: "Keyboard shortcuts" }).click();
    await settle(500);
    ok(await page.locator(".kb-group").count() === 6 && await page.locator(".kb-row").count() >= 15, "the list is grouped by page");


    const brandRow = page.locator(".kb-row", { hasText: "Add brand" });
    await brandRow.locator(".switch").click();
    await settle(400);
    ok(await brandRow.locator(".switch").getAttribute("aria-checked") === "false", "the switch turns a shortcut off");
    ok(await page.evaluate(() => Object.keys(localStorage).some((k) => k.startsWith("glowup.shortcuts.v1.") && /"inventory.addBrand":\{[^}]*"enabled":false/.test(localStorage.getItem(k)))), "the change is saved");
    await page.click("#swClose");
    await settle(400);
    await page.mouse.click(700, 880);
    await page.keyboard.press("Control+Alt+KeyB");
    await settle(300);
    ok(await openModals(page) === 0, "a switched-off shortcut does nothing");
    await page.click("#quickSettingsBtn");
    await settle(400);
    await page.locator(".settings-window .menu-item", { hasText: "Keyboard shortcuts" }).click();
    await settle(300);
    await page.locator(".kb-row", { hasText: "Add brand" }).locator(".switch").click();
    await settle(300);


    await page.locator(".kb-actions .btn.primary").click();
    await settle(300);
    ok(await page.locator(".kb-editor").count() === 1, "Add shortcut opens the dialog");
    await page.selectOption("#kbAction", "inventory.sort");
    await page.click("#kbRecord");
    await page.keyboard.press("Escape");
    await settle(150);
    ok(await page.locator(".kb-editor").count() === 1 && !(await page.locator(".kb-record.is-recording").count()), "the first Esc while recording stops recording only");
    const reason = async (keys) => {
        await page.click("#kbRecord");
        await synth(page, keys);
        await settle(200);
        return page.locator("#kbMsgs .kb-msg").evaluateAll((list) => list.map((m) => m.dataset.code + ":" + m.className.split(" ").pop() + ":" + m.textContent));
    };
    let r = await reason({ code: "KeyT", key: "t", ctrlKey: true });
    ok(r.length === 1 && r[0].startsWith("BROWSER_RESERVED:block") && /reserved by your browser for opening a new tab/.test(r[0]), "C in the dialog: " + r[0]);
    ok(await page.locator(".kb-editor [data-save]").isDisabled(), "Save stays off for a reserved combo");
    r = await reason({ code: "F4", key: "F4", altKey: true });
    ok(r[0] && r[0].startsWith("OS_RESERVED:block"), "D in the dialog: " + r[0]);
    r = await reason({ code: "KeyB", key: "B", shiftKey: true });
    ok(r[0] && r[0].startsWith("INVALID_FORMAT:block"), "F in the dialog: " + r[0]);
    r = await reason({ code: "KeyB", key: "b", ctrlKey: true, altKey: true });
    ok(r[0] && r[0].startsWith("IN_USE_SAME_PAGE:warn") && /Add brand/.test(r[0]), "A in the dialog: " + r[0]);
    ok(await page.locator("#kbMsgs .kb-key").count() >= 3, "the combo in the message is drawn as key chips");
    r = await reason({ code: "KeyD", key: "d", ctrlKey: true });
    ok(r[0] && r[0].startsWith("BROWSER_CONFLICT:warn") && /^BROWSER_CONFLICT:warn:In Chrome/.test(r[0]), "E in the dialog leads with the browser: " + r[0]);
    r = await reason({ code: "KeyR", key: "r", ctrlKey: true, altKey: true });
    ok(r.length >= 1 && r.every((m) => m.startsWith("IN_USE_SAME_PAGE:info")), "another page's combo is only a note: " + r[0]);


    await page.click("#kbRecord");
    await page.keyboard.press("Control+Alt+KeyJ");
    await settle(200);
    ok(/again to test it/.test(await page.textContent("#kbTest")), "the Test asks for the combo again");
    ok(await page.locator(".kb-editor [data-save]").isDisabled(), "Save waits for the Test");
    await page.keyboard.press("Control+Alt+KeyJ");
    await settle(200);
    ok(/Works in/.test(await page.textContent("#kbTest")), "G: received -> works");
    ok(!(await page.locator(".kb-editor [data-save]").isDisabled()), "Save is on after the Test");


    await page.evaluate(() => { window.__FAIL_WRITES = "internal"; });
    await page.click(".kb-editor [data-save]");
    await settle(400);
    ok(/Couldn't save\. Check your connection and try again\./.test(await page.textContent("#kbSaveError")) && await page.locator(".kb-editor").count() === 1, "H: SAVE_FAILED is said in the dialog, which stays open");
    ok(/Ctrl.*Alt.*J/.test(await page.textContent("#kbRecord")) && await page.inputValue("#kbDesc") !== "", "H: what was entered is kept");
    await page.evaluate(() => { window.__FAIL_WRITES = ""; });
    await page.locator("#kbSaveError button", { hasText: "Retry" }).click();
    await settle(500);
    ok(await page.locator(".kb-editor").count() === 0 && await page.locator(".kb-row", { hasText: "Sort" }).count() === 1, "Retry saves; the new shortcut is in the list");
    ok(await page.evaluate(() => document.activeElement && document.activeElement.dataset.kbFocus === "add"), "focus is back on Add shortcut");


    await page.locator(".kb-actions .btn.primary").click();
    await settle(300);
    await page.selectOption("#kbPage", "curator");
    await page.selectOption("#kbAction", "curator.sort");
    await page.click("#kbRecord");
    await page.keyboard.press("Control+Alt+KeyO");
    await page.waitForFunction(() => /didn't receive/.test(document.getElementById("kbTest").textContent), null, { timeout: 7000 }).catch(() => {});
    ok(/GlowUp didn't receive/.test(await page.textContent("#kbTest")) && await page.locator("#kbTest [data-code='TEST_NOT_RECEIVED']").count() === 1, "G: not received after 5 seconds");
    await page.locator("#kbTest button", { hasText: "Save anyway" }).click();
    ok(!(await page.locator(".kb-editor [data-save]").isDisabled()), "Save anyway allows saving");

    await page.keyboard.press("Escape");
    await settle(200);
    ok(await page.locator(".modal-content h3", { hasText: "Discard changes?" }).count() === 1, "closing a changed dialog asks Discard changes?");
    await page.locator(".modal-content button", { hasText: "Keep editing" }).click();
    await settle(150);
    ok(await page.locator(".kb-editor").count() === 1, "Keep editing keeps the dialog");
    await page.click(".kb-editor [data-save]");
    await settle(500);
    ok(await page.locator(".kb-row", { hasText: "Sort" }).filter({ hasText: "May not work" }).count() === 1, "a shortcut saved anyway carries the warning in the list");


    const sortRow = page.locator(".kb-group[aria-label='Brand & Product Inventory'] .kb-row", { hasText: "Sort" });
    await sortRow.locator(".icon-btn.danger").click();
    await settle(200);
    ok(await page.locator(".modal-content h3", { hasText: 'Delete the "Sort" shortcut?' }).count() === 1, "delete asks first and names the shortcut");
    await page.keyboard.press("Escape");
    await settle(200);
    ok(await sortRow.count() === 1, "Esc on the question keeps the shortcut");

    await page.context().setOffline(true);
    await settle(300);
    ok(/You're offline/.test(await page.textContent(".keys-view")) && await page.locator(".kb-actions .btn.primary").isDisabled() && await page.locator(".kb-row .switch").first().isDisabled(), "offline: the list says so and locks the changes");
    await page.context().setOffline(false);
    await settle(300);
    ok(!(await page.locator(".kb-actions .btn.primary").isDisabled()), "back online: changes are possible again");
    ok(errors.length === 0, "no page errors on Inventory: " + errors.slice(0, 3).join(" | "));
    await page.context().close();


    const bin = await openPage(browser, "bin", { width: 1280, height: 720 });
    await bin.settle(1200);
    await bin.page.mouse.click(640, 700);
    for (let i = 0; i < 4; i++) await bin.page.keyboard.press("Control+Alt+KeyR");
    await bin.settle(300);
    ok(await bin.page.locator(".toast", { hasText: "tick at least one entry first" }).count() === 1, "an unavailable action says why, in ONE toast however often it is pressed");
    await bin.page.keyboard.press("Control+Alt+KeyB");
    await bin.settle(300);
    ok(await openModals(bin.page) === 0, "Inventory's Ctrl+Alt+B does nothing on the Recycle Bin");
    await bin.page.keyboard.press("Control+Alt+Digit3");
    await bin.page.waitForURL(/adminIngredientCurator/, { timeout: 5000 }).catch(() => {});
    ok(/adminIngredientCurator/.test(bin.page.url()), "Ctrl+Alt+3 goes to the Ingredient Curator");
    ok(bin.errors.length === 0, "no page errors: " + bin.errors.slice(0, 3).join(" | "));
    await bin.page.context().close();

} finally { await browser.close(); }
console.log(`checks made: ${checks}`);
if (failed) { console.log(`${failed} FAILED`); process.exit(1); }
console.log("ALL PASSED");
