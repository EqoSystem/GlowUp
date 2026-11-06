// The Terms and Conditions window of the login page (7 November 2026).
//
// A plain script with no imports, so the terms can be read even when the sign-in service does not load. It uses the
// browser's own <dialog>: it opens over the page, keeps the keyboard inside it and closes with Esc. A click outside
// the window or the Close button closes it too. Every way of closing returns the keyboard to the link that opened it.
(function () {
    var link = document.getElementById("termsLink");
    var dialog = document.getElementById("termsDialog");
    if (!link || !dialog || typeof dialog.showModal !== "function") return;
    var body = dialog.querySelector(".terms-body");

    link.addEventListener("click", function () {
        if (dialog.open) return;
        dialog.showModal();
        if (body) body.scrollTop = 0;
        var close = document.getElementById("termsClose");
        if (close) close.focus();
    });
    document.getElementById("termsClose").addEventListener("click", function () { dialog.close(); });
    // the window fills the <dialog> exactly, so a click that lands on the <dialog> itself is a click on the dimmed page around it
    dialog.addEventListener("click", function (event) { if (event.target === dialog) dialog.close(); });
    dialog.addEventListener("close", function () { link.focus(); });
})();
