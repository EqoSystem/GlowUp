// Text dialogs of the login page (Terms and Conditions, Privacy Policy): any button with data-opens="<dialog id>" opens one.
// Close, Escape and a click beside the box close it; focus goes back to the button that opened it.
(function () {
    var opener = null;
    function wire(dialog) {
        if (!dialog || typeof dialog.showModal !== "function") return;
        var body = dialog.querySelector(".terms-body");
        var close = document.getElementById(dialog.id === "termsDialog" ? "termsClose" : "") || dialog.querySelector("[data-closes]");
        if (close) close.addEventListener("click", function () { dialog.close(); });
        dialog.addEventListener("click", function (event) { if (event.target === dialog) dialog.close(); });
        dialog.addEventListener("close", function () { if (opener) opener.focus(); opener = null; });
    }
    wire(document.getElementById("termsDialog"));
    wire(document.getElementById("privacyDialog"));

    document.addEventListener("click", function (event) {
        var button = event.target.closest && event.target.closest("[data-opens]");
        if (!button) return;
        var dialog = document.getElementById(button.getAttribute("data-opens"));
        if (!dialog || dialog.open || typeof dialog.showModal !== "function") return;
        opener = button;
        dialog.showModal();
        var body = dialog.querySelector(".terms-body");
        if (body) body.scrollTop = 0;
        var close = dialog.querySelector("#termsClose, [data-closes]");
        if (close) close.focus();
    });
})();
