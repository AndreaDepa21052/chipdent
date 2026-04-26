// Chipdent — global UX helpers

(function () {
    "use strict";

    // Auto-dismiss flash alerts
    document.querySelectorAll("[data-auto-dismiss]").forEach(function (el) {
        var ms = parseInt(el.getAttribute("data-auto-dismiss"), 10) || 5000;
        setTimeout(function () {
            el.style.transition = "opacity .3s";
            el.style.opacity = "0";
            setTimeout(function () { el.remove(); }, 300);
        }, ms);
    });

    // Reveal current year, etc.
    document.querySelectorAll("[data-year]").forEach(function (el) {
        el.textContent = new Date().getFullYear();
    });
})();

window.Chipdent = window.Chipdent || {};

Chipdent.toast = function (title, body) {
    var host = document.querySelector(".toasts");
    if (!host) {
        host = document.createElement("div");
        host.className = "toasts";
        document.body.appendChild(host);
    }
    var el = document.createElement("div");
    el.className = "toast";
    el.innerHTML = "<strong></strong><small></small>";
    el.querySelector("strong").textContent = title || "Chipdent";
    el.querySelector("small").textContent = body || "";
    host.appendChild(el);
    setTimeout(function () {
        el.style.transition = "opacity .3s, transform .3s";
        el.style.opacity = "0";
        el.style.transform = "translateX(20px)";
        setTimeout(function () { el.remove(); }, 320);
    }, 5000);
};
