// Character limits are enforced by the native maxlength attribute. The fields stay
// quiet while typing; no extra indicator is inserted below them.

/** Keeps the existing call sites compatible without adding visible UI. */
export function attachCounters() {
    return () => { };
}
