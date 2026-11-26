// Browser helpers for the dashboard and the receipt, loaded on demand as a module (LocalClock.cs).

/** The browser's IANA time zone, e.g. "Europe/Amsterdam", or null when unknown. */
export function timeZone() {
    try {
        return Intl.DateTimeFormat().resolvedOptions().timeZone || null;
    } catch (_) {
        return null;
    }
}
