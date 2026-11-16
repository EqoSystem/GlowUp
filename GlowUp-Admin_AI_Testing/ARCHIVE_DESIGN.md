# Archive: design (checkpoint 6, 29 October 2026)

Additive only. Nothing that exists in the database is renamed, reordered, changed or removed by this design,
and nothing is deployed by the agent: the rule block below is published by the owner, by hand, after review.

## 1. How data is stored today (inspected in code, no live reads)

| Store | What | Who writes | Who reads |
|---|---|---|---|
| Firestore `glowup-admin-portal` (ONE database, shared by the app and this website) | `brands`, `products`, `ingredients` | website (admins); app creates `scan_` / `ai_` Pending entries and adds 1 to `scans` | app (public read), website |
| same | Recycle bin = fields on those documents: `binned: true`, `binnedAt`, `statusBeforeBin`, `binnedWith`, `binnedReason` | website only | website lists; app skips `binned` entries (BrandRepository.cs, ProductRepository.cs, IngredientRepository.cs) |
| same | `admins`, `users`, `scanActivity`, `pending_products`, `pending_product_images`, `productIngredientSuggestions`, `config/lists`, `snapshot_quota`, `allergenSuggestions`, `conditionSuggestions` | see `firestore.rules` | see `firestore.rules` |
| browser localStorage | kept copies of the three lists (`glowup.store.v2.*`), layouts, theme | website | website |

Rules end with "anything not listed above is closed": a new collection needs its own `match` block, or every read and write of it is refused.
There is no separate dev database; the Firestore emulator (`tests/rules`) is the test database.

## 2. Existing impact check

| Existing rule / feature / query / function / screen | Touched? | Why |
|---|---|---|
| `firestore.rules` match blocks for brands, products, ingredients, admins, users, scanActivity, pending_*, config, quotas, suggestions | **no** | The design ADDS two new match blocks (`archive`, `archive_purge_log`). No existing line changes. |
| Rule "anything not listed above is closed" | no | Still true for every other path. |
| Admin delete of brands / products / ingredients (`allow delete: if isAdmin()`) | no (used as is) | "Move to Archive" deletes the original document with the same right "Delete permanently" already uses. |
| Admin create of brands / products / ingredients (`adminCatalogueWrite`, `websiteConfirmedOnCreate`) | no (used as is) | "Restore" from the Archive re-creates the original document under these existing rules. A brand whose confirmed website was set by ANOTHER admin cannot be restored (the existing rule refuses it); the Archive keeps it and says why. |
| Recycle bin: Restore, Delete permanently, Restore all / Empty bin chips, the bin page | no | Unchanged code paths. The bin page gets an extra, separate "Move to Archive" action. |
| Import (file, CosIng, FDA catalogue), Clean up, Find duplicates | no | They never read or write `archive`. |
| Website lists (Inventory, Curator, Dashboard, bin) and adminStore.js kept copies | no (behaviour) | They read whole `brands` / `products` / `ingredients`. An archived entry is no longer in those collections, so it cannot show there. The batch that moves it goes through adminStore's writeBatch, which already marks the deleted document for a re-read. |
| Mobile app (catalogue sync, scanner results, AR, history) | no | The app reads `brands`, `products`, `ingredients`, `config`, `pending_products`, `users` only (grep of `GlowUp/Assets/_Project/Scripts`: no `archive`). Entries in the bin were already skipped (binned); in the Archive they are not in the app's collections at all. Non-admins cannot read `archive` (new rule: admins only). |
| Vercel functions `api/assist-ingredient.js`, `api/scan-snapshot.js` | no | Untouched. |
| `vercel.json` | **yes (added lines only)** | One cron entry and one function entry are ADDED for the purge job. Existing redirects, headers and functions stay. |
| Vercel settings / environment variables | no | The agent sets none. The purge refuses to run until the OWNER adds `CRON_SECRET` and `FIREBASE_SERVICE_ACCOUNT` (see 5). |
| Firestore indexes | no | The purge asks `expires_at <= now` ordered by `expires_at` in one collection: Firestore's automatic single-field index covers it. |
| Auth settings, Storage rules, triggers | no | Not used. |

Evidence that archived items cannot appear in the app, the scanner or the admin lists:
- app queries: `ProductRepository.cs`, `BrandRepository.cs`, `IngredientRepository.cs`, `CatalogSync.cs` read the `products`, `brands`, `ingredients` collections only;
- website lists: `adminStore.js` `LISTS = ["brands", "products", "ingredients"]`; the bin page reads the same three; the Dashboard counts the same three;
- an archived entry exists ONLY as `archive/{collection}__{id}` after the move (the original is deleted in the same atomic batch).

## 3. Archive design (two stages: Recycle bin -> Archive 30 days -> deleted)

New collection `archive`, document id `{collection}__{id}` (for example `products__abc123`), so an entry is archived at most once.

| Field | Type | Meaning |
|---|---|---|
| `type` | `"ingredient"`, `"brand"` or `"product"` | kind of entry |
| `sourceCollection` | `"ingredients"`, `"brands"` or `"products"` | where it came from (matches `type`) |
| `sourceId` | string | its id there |
| `name` | string | for the list and the search |
| `data` | map | the whole original document, as it was in the Recycle bin (still `binned: true`) |
| `deleted_at` | timestamp | when it was archived (server time) |
| `expires_at` | timestamp | `deleted_at` + 30 days; counts whether or not anybody opens the Archive |
| `deleted_by` | string | the admin's uid |
| `deleted_by_email` | string | the admin's e-mail, for the list |
| `archivedWith` | string | for a product moved with its brand: the brand's id, else "" |

- **Move to Archive** (Recycle Bin page, per row and for the selection): re-reads each entry, refuses one that is no longer in the bin, then in ONE batch writes the archive document and deletes the original. A brand takes the products that are in the bin under it, like "Delete permanently" does.
- **Archive page** (`_Admin_Archive/adminArchive.html`): tabs Ingredients, Brands, Products; search; "X days left" per entry, a warning from 3 days left; Restore; Delete now.
- **Restore**: back to the Recycle bin (the original document is re-created as it was, still `binned: true`; the bin's own Restore then brings it back). Never overwrites: an entry whose id is in use again is refused. A brand brings back the products archived with it; a product whose brand is archived brings the brand back too.
- **Delete now**: asks first, saves a JSON copy to the admin's computer (as the bin does), deletes only the archive document.

## 4. Automatic purge (server side)

`api/archive-purge.js`, a Vercel function, called once a day by Vercel Cron (`0 3 * * *`, UTC).
- Protected: needs `Authorization: Bearer <CRON_SECRET>` (Vercel Cron sends it when the env var is set). Without `CRON_SECRET` it answers 503 and does nothing.
- Dry run is the default: it only lists what it would delete. It deletes only when the env var `ARCHIVE_PURGE_LIVE` is `1`.
- Deletes ONLY documents in `archive` whose `expires_at` has passed (checked in the query and again per document), with a precondition on the document's update time. It builds no other path.
- At most `ARCHIVE_PURGE_MAX` documents per run (default 50, never more than 200).
- Log: every run (dry or live) writes `archive_purge_log/{run}` and prints one JSON line to the Vercel log.
- Credentials: a Google service account (`FIREBASE_SERVICE_ACCOUNT`, the JSON key as one env var), because a cron job has no signed-in admin. **The agent cannot and does not create or set it.**
  Alternative without a secret: a Firestore TTL policy on `archive.expires_at` (Firebase console), but that has no dry run, cap or log.

## 5. New schema elements and rule changes

New: collection `archive` (fields above), collection `archive_purge_log` (`ranAt`, `mode`, `found`, `deleted`, `ids`, `capped`, `errors`).
Existing rules changed: **none**. Rules ADDED (published on 5 November 2026 with `firebase deploy --only firestore:rules`; until then the Archive page answered "permission denied"):

```
    // ---- the Archive: entries that left the Recycle bin, kept 30 days, then deleted by the server job ----
    // Admins only. An entry is created once (id "<collection>__<id>"), never changed; the job deletes it after expires_at.
    match /archive/{entryId} {
      allow read, delete: if isAdmin();
      allow update: if false;
      allow create: if isAdmin()
        && request.resource.data.keys().hasOnly(['type', 'sourceCollection', 'sourceId', 'name', 'data', 'deleted_at', 'expires_at',
             'deleted_by', 'deleted_by_email', 'archivedWith'])
        && ((request.resource.data.type == 'ingredient' && request.resource.data.sourceCollection == 'ingredients')
            || (request.resource.data.type == 'brand' && request.resource.data.sourceCollection == 'brands')
            || (request.resource.data.type == 'product' && request.resource.data.sourceCollection == 'products'))
        && request.resource.data.sourceId is string
        && entryId == request.resource.data.sourceCollection + '__' + request.resource.data.sourceId
        && request.resource.data.name is string && request.resource.data.name.size() <= 200
        && request.resource.data.data is map
        && request.resource.data.deleted_by == request.auth.uid
        && request.resource.data.deleted_at == request.time
        && request.resource.data.expires_at is timestamp
        && request.resource.data.expires_at > request.time + duration.value(29, 'd')
        && request.resource.data.expires_at < request.time + duration.value(31, 'd');
    }

    // what the purge job did on each run; written only by the job (its service account is outside these rules)
    match /archive_purge_log/{runId} {
      allow read: if isAdmin();
      allow write: if false;
    }
```

That publish also brought the 28 October 2026 changes live: the admin custom claim, size limits on `scanActivity`, and `pending_products` readable only by its sender or an admin.
