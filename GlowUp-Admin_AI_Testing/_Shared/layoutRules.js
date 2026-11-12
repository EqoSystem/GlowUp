






export const ROW_UNIT = 4;


export function rowsFor(height, gap, unit = ROW_UNIT) {
    const h = Math.max(0, Number(height) || 0), g = Math.max(0, Number(gap) || 0), u = Math.max(1, Number(unit) || ROW_UNIT);
    return Math.max(1, Math.ceil((h + g) / u));
}








export function isFolded(choice, quiet) {
    if (choice === "open") return false;
    if (choice === "closed") return true;
    return !!quiet;
}


export function cleanChoice(value) {
    return value === "open" || value === "closed" ? value : "";
}






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
