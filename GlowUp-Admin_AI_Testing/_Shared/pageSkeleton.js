// Skeleton loading in GlowUp's own look. This small script is the first thing inside <body> of every page, so the
// outline of the page that is coming is on screen before anything else has loaded: the GlowUp sparkle, the menu,
// and soft blush shapes where that page's numbers, tools and lists will be, with a rose-to-gold shine moving across.
// adminCommon.css shows it while the page is checking the sign-in (html.auth-pending) and from the moment a link to
// another page is pressed (body.leaving); otherwise it is not displayed.
// html.resizing while the window edge moves (until 150 ms after the last resize event): adminCommon.css stops every transition
// and animation then, so the page follows the edge at once. pageFrame.js does the same on admin pages; the flag keeps it single.
(function () {
    if (window.__glowupResizing) return;
    window.__glowupResizing = true;
    var root = document.documentElement, timer = 0;
    window.addEventListener("resize", function () {
        if (!timer) root.classList.add("resizing");
        clearTimeout(timer);
        timer = setTimeout(function () { root.classList.remove("resizing"); timer = 0; }, 150);
    }, { passive: true });
})();
(function () {
    var path = location.pathname;
    var kind = path.indexOf("_Index") >= 0 ? "login" : path.indexOf("Inventory") >= 0 ? "inventory" : path.indexOf("Curator") >= 0 ? "curator" : "dashboard";
    var SPARKLE = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3l2.2 6.8L21 12l-6.8 2.2L12 21l-2.2-6.8L3 12l6.8-2.2z"/></svg>';
    var times = function (n, html) { var out = ""; for (var i = 0; i < n; i++) out += html; return out; };
    var row = '<div class="sk-line-row"><i class="sk sk-dot"></i><span><i class="sk sk-text w60"></i><i class="sk sk-text w35 thin"></i></span><i class="sk sk-pill"></i></div>';
    var tile = '<div class="sk sk-tile"><i class="sk-chip"></i><span><i class="sk-num"></i><i class="sk-cap"></i></span></div>';
    var card = '<div class="sk sk-card"><i class="sk-cap w70"></i><i class="sk-cap w45 thin"></i></div>';

    var main;
    if (kind === "login") {
        main = '<div class="sk-login"><div class="sk-mark big">' + SPARKLE + '</div><i class="sk sk-text w50"></i><i class="sk sk-field"></i><i class="sk sk-field"></i><i class="sk sk-button"></i></div>';
    } else {
        var body = kind === "dashboard"
            ? '<div class="sk-tiles five">' + times(5, tile) + '</div><div class="sk-split"><div class="sk-panel">' + times(6, row) + '</div><div class="sk-panel">' + times(4, '<i class="sk sk-field"></i>') + '</div></div>'
            : kind === "inventory"
                ? '<div class="sk-tiles">' + times(4, tile) + '</div><div class="sk-panel slim"><i class="sk sk-field w30"></i><i class="sk sk-field w15"></i><i class="sk sk-field w10"></i></div><div class="sk-panel">' + times(7, row) + '</div>'
                : '<div class="sk-tiles">' + times(4, tile) + '</div><div class="sk-panel slim"><i class="sk sk-field w30"></i><i class="sk sk-field w15"></i><i class="sk sk-field w10"></i></div><div class="sk-panel"><div class="sk-cards">' + times(8, card) + '</div></div>';
        main = '<div class="sk-side"><div class="sk-brand"><div class="sk-mark">' + SPARKLE + '</div><span><b>GlowUp</b><small>Admin Portal</small></span></div>'
            + '<i class="sk sk-nav' + (kind === "dashboard" ? " on" : "") + '"></i><i class="sk sk-nav' + (kind === "inventory" ? " on" : "") + '"></i><i class="sk sk-nav' + (kind === "curator" ? " on" : "") + '"></i></div>'
            + '<div class="sk-main"><div class="sk-top"><i class="sk sk-text title"></i><i class="sk sk-text w35 thin"></i></div>' + body + '</div>';
    }
    var holder = document.createElement("div");
    holder.className = "page-skeleton " + kind;
    holder.setAttribute("aria-hidden", "true");
    holder.innerHTML = main + '<div class="sk-wait"><div class="sk-mark pulse">' + SPARKLE + '</div><span>Getting things ready</span></div>';
    document.body.insertBefore(holder, document.body.firstChild);
})();
