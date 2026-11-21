// Layout & Style: how this browser's saved layouts and the admin's own copy in the database are put together.
// Before, named layouts ("Echo") lived only in the browser where they were made; now every device the admin signs in on gets them.
// Pure: no Firebase here, so tests/layoutSync.test.mjs checks it in Node.
//
//   - No copy in the database yet: this browser's layouts are uploaded.
//   - The first time a browser meets the database copy: both are KEPT (nothing made on either side is lost). A layout that is on
//     both sides (same id) comes from the side that was saved last.
//   - After that, the side saved last wins as a whole, so deleting or renaming a layout on one device carries to the others.
//   - Which layout is in use stays a choice per device (a phone may use another one than the computer).

export const MAX_SYNCED_PROFILES = 4;

function profilesOf(store) { return store && Array.isArray(store.profiles) ? store.profiles : []; }

/**
 * @param local      this browser's store { active, profiles }
 * @param localAt    when this browser last changed its layouts (ms; 0 = never)
 * @param remote     the database copy (same shape) or null
 * @param remoteAt   when the database copy was saved (ms)
 * @param firstMeet  true the first time this browser syncs for this admin
 * @returns { store, savedAt, apply, push }  apply = this browser must show `store`; push = the database must get `store`
 */
export function mergeLayouts(local, localAt, remote, remoteAt, firstMeet) {
    const mine = { active: (local && local.active) || "default", profiles: profilesOf(local) };
    if (!remote) return { store: mine, savedAt: localAt || Date.now(), apply: false, push: mine.profiles.length > 0 };
    const theirs = profilesOf(remote);
    const keepActive = (profiles) => (profiles.some((p) => p.id === mine.active) ? mine.active : "default");

    if (firstMeet) {
        const remoteWins = (remoteAt || 0) >= (localAt || 0);
        const byId = new Map();
        const first = remoteWins ? theirs : mine.profiles, second = remoteWins ? mine.profiles : theirs;
        first.forEach((p) => byId.set(p.id, p));
        second.forEach((p) => { if (!byId.has(p.id)) byId.set(p.id, p); });
        const profiles = [...byId.values()].slice(0, MAX_SYNCED_PROFILES);
        const store = { active: keepActive(profiles), profiles };
        const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
        return { store, savedAt: Math.max(localAt || 0, remoteAt || 0), apply: !same(profiles, mine.profiles), push: !same(profiles, theirs) };
    }
    if ((remoteAt || 0) > (localAt || 0)) {
        const profiles = theirs.slice(0, MAX_SYNCED_PROFILES);
        return { store: { active: keepActive(profiles), profiles }, savedAt: remoteAt, apply: JSON.stringify(profiles) !== JSON.stringify(mine.profiles), push: false };
    }
    if ((localAt || 0) > (remoteAt || 0)) return { store: mine, savedAt: localAt, apply: false, push: true };
    return { store: mine, savedAt: localAt || 0, apply: false, push: false };
}
