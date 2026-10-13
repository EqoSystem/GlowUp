// The small sums behind the page layout, kept apart from the page so they can be checked (tests/layout.test.mjs).
//
//   Auto resize  - panels are packed without gaps: the board is a grid of very low rows, every panel takes as many
//                  rows as its own height needs, and the browser slots each panel into the first free place.
//   Folding      - a panel can be folded to its title line; a panel with nothing to show folds by itself.

/** The height of one row of the packing grid, in pixels. Low enough that a panel sits as close to the one above as the gap allows. */
export const ROW_UNIT = 4;

/** How many rows of the packing grid a panel takes: its own height plus the gap under it, rounded up to whole rows. */
export function rowsFor(height, gap, unit = ROW_UNIT) {
    const h = Math.max(0, Number(height) || 0), g = Math.max(0, Number(gap) || 0), u = Math.max(1, Number(unit) || ROW_UNIT);
    return Math.max(1, Math.ceil((h + g) / u));
}

/**
 * Whether a panel is folded to its title line.
 *   choice - what the admin chose for this panel: "open", "closed", or "" when they chose nothing
 *   quiet  - the panel has nothing to show right now
 * The admin's own choice always wins. Without one, a panel shows when it has something to show and folds when it has
 * not: an empty panel takes one line instead of a box, and it opens by itself as soon as there is something in it.
 */
export function isFolded(choice, quiet) {
    if (choice === "open") return false;
    if (choice === "closed") return true;
    return !!quiet;
}

/** Only "open" and "closed" are kept; anything else in a saved choice counts as "no choice". */
export function cleanChoice(value) {
    return value === "open" || value === "closed" ? value : "";
}

/**
 * The biggest hole between panels of a page, in pixels: for every panel, how far it is to the next panel below it in
 * the same columns. Used by the checks to say whether a layout has "large gaps". `boxes` = [{ left, right, top, bottom }].
 * The last panel of a column has nothing below it and does not count.
 */
export function biggestGap(boxes) {
    let biggest = 0;
    boxes.forEach((box) => {
        let next = Infinity;
        boxes.forEach((other) => {
            if (other === box || other.top < box.bottom - 1) return;
            if (other.left < box.right - 1 && other.right > box.left + 1) next = Math.min(next, other.top - box.bottom);
        });
        if (Number.isFinite(next)) biggest = Math.max(biggest, next);
    });
    return Math.round(biggest);
}
