# 10. Admin website: pages, features and controls

Admin pages share a rendered navigation/header/sidebar frame through `_Shared/pageFrame.js`, with behavior and controls supplied by `_Shared/adminShell.js`, `_Shared/adminSettingsMenu.js`, `_Shared/adminCommon.js` and each page module. Static pages found in `GlowUp-Admin_AI_Testing/` are listed below. Controls generated dynamically by JavaScript are called out as partially traced when their full action list is not fixed in the HTML.

## Shared layout

The page frame provides navigation, page title/subtitle, account identity and settings/sign-out menu. Shared utilities supply theme toggle, help/guide badges, dialogs, confirmations, search/filter/layout components, and sign-out. Exact generated menu item inventory is **PARTIALLY TRACED**; see `GlowUp-Admin_AI_Testing/_Shared/pageFrame.js`, `adminShell.js`, `adminSettingsMenu.js`, `adminCommon.js`.

| Page / route | Purpose and source | Controls and actions present in source |
|---|---|---|
| Admin login: `/_Index/index.html` | Signs in authorized staff; `_Index/indexApp.js` implements auth. | Email input; password input; show/hide password eye; Log in submit (enabled when both fields have content); guide/info badge; theme toggle in login tools. Failed sign-in/admin check shows feedback. |
| Operations Dashboard: `/_Admin_Dashboard/adminDashboard.html` | Live operations and scan activity. `adminDashboard.js`. | KPI tiles: Scans today and Allergen matches today filter activity; Brands/Products link to inventory; Ingredients links to curator. Quick actions link to pending inventory, ingredients, snapshots and inventory/curator. Recent activity rows expand; pager; today's result chart/legend filters; most-scanned brands link to inventory; suggestions section displays submitted allergen/condition suggestions. Shared help badges and menus. |
| Brand & Product Inventory: `/_Admin_Brand_Inventory/adminBrandInventory.html` | Catalogue, imports, moderation, brand/product management. `adminBrandInventory.js`. | Search and clear; More menu for organization/cleanup tools; Add menu for brand/product forms; Choose file and drop-box for PDF/Excel/Word/CSV/text import; Pending Snapshot Reviews opens photo/AI reading review; stats/count tiles filter; inventory list rows/cards option; Sort and Filters menus; brand selector/filter; pagers; selection checkboxes and bulk action bar; folders and recycle-bin controls via organizer; product row opens details and double-click edits; brand row opens/closes group. Forms include brand name/category/market/aliases and website data; product form controls are dynamic and **PARTIALLY TRACED**. |
| Ingredient Curator: `/_Admin_Ingredient_Curator/adminIngredientCurator.html` | Ingredient records, imports, review and list configuration. `adminIngredientCurator.js`. | Search and clear; More cleanup/duplicate menu; Add ingredient form; file chooser/drop box for PDF/Excel/CSV/text; snapshot count/review panel; count tiles for all/review/harmful/caution/from-products; risk filter; Sort; rows/cards; pagers; select-all/selection bar; folder and recycle-bin actions; ingredient card opens detail and double-click edits. “Edit list” controls configuration arrays; full dialog fields are **PARTIALLY TRACED**. |
| Recycle Bin: `/_Admin_Recycle_Bin/adminRecycleBin.html` | Browse removed records and restore/archive/delete permanently. `adminRecycleBin.js`. | Kind tabs (ingredients/brands/products); search and clear; Archive link; guide badge; select all; restore selected; move selected to Archive; delete selected permanently; Cancel selection; row actions and pagers. |
| Archive: `/_Admin_Archive/adminArchive.html` | Browse archived entries retained for 30 days. `_Admin_Archive/adminArchive.js`. | Kind tabs; search and clear; Recycle bin link; select all; Restore selected; Delete selected now; Cancel selection; row actions and pagers. |

## Dashboard content requested

The dashboard source includes **Recent activity**, **Today's results**, **Most scanned brands**, and suggestions. It has scan/allergen KPI controls and catalogue-count links. Snapshot review and pending-review shortcuts link to the inventory. See `adminDashboard.html` and `adminDashboard.js`. The dashboard does not itself contain a file import drop zone; that is in the inventory page.

## Settings and NOT FOUND IN CODE

- **NOT FOUND IN CODE:** a separate settings HTML route/page. The settings menu is shared website UI, not a standalone settings page in the enumerated static routes.
- The dashboard import file control is **NOT FOUND IN CODE**; importing is provided on inventory and ingredient curator pages.
- “Default layout” controls are shared layout tools. The precise full list of menu items injected at runtime is **PARTIALLY TRACED** from `pageFrame.js` and the shared modules.

## Page trace limits

Some forms, context menus, row buttons, bulk controls and snapshot-review dialogs are created at runtime. This outline identifies their host controls and documented actions but does not claim every generated icon or dialog field is fully traced. Those dynamic parts are **PARTIALLY TRACED** in the corresponding page module and shared helper files.
