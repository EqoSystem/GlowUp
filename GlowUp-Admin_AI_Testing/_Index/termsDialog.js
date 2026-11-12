




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

    dialog.addEventListener("click", function (event) { if (event.target === dialog) dialog.close(); });
    dialog.addEventListener("close", function () { link.focus(); });
})();
