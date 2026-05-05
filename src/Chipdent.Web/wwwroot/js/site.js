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

    // ── Filtro tabelle lato DOM ────────────────────────────────────────────
    // Markup atteso:
    //   <div data-table-search>
    //     <input class="table-search__input" placeholder="Cerca…" />
    //     <span class="table-search__count" data-singular="risultato" data-plural="risultati"></span>
    //     ...table...
    //   </div>
    // L'input filtra le righe del primo <table> (o dell'elemento con data-table-search-target)
    // confrontando il testo normalizzato di tutta la riga con i token digitati (AND).
    function normalize(s) {
        return (s || "")
            .toString()
            .toLowerCase()
            .normalize("NFD")
            .replace(/[̀-ͯ]/g, "");
    }

    function setupTableSearch(root) {
        var input = root.querySelector(".table-search__input");
        if (!input) return;
        var targetSel = root.getAttribute("data-table-search-target");
        var table = targetSel ? root.querySelector(targetSel) : root.querySelector("table");
        if (!table) return;
        var tbody = table.tBodies[0];
        if (!tbody) return;
        var rows = Array.prototype.slice.call(tbody.rows);

        // Pre-calcola il testo normalizzato di ogni riga per evitare lavoro inutile a ogni keystroke.
        rows.forEach(function (r) {
            r.__searchText = normalize(r.textContent);
        });

        var counter = root.querySelector(".table-search__count");
        var emptyMsg = root.querySelector(".table-search__empty");
        var clearBtn = root.querySelector(".table-search__clear");

        function apply() {
            var q = normalize(input.value).trim();
            var tokens = q.length ? q.split(/\s+/) : [];
            var visible = 0;
            rows.forEach(function (r) {
                var match = tokens.every(function (t) { return r.__searchText.indexOf(t) !== -1; });
                r.style.display = match ? "" : "none";
                if (match) visible++;
            });
            if (counter) {
                if (tokens.length === 0) {
                    counter.textContent = "";
                } else {
                    var sing = counter.getAttribute("data-singular") || "risultato";
                    var plur = counter.getAttribute("data-plural") || "risultati";
                    counter.textContent = visible + " " + (visible === 1 ? sing : plur);
                }
            }
            if (emptyMsg) {
                emptyMsg.style.display = (visible === 0 && tokens.length > 0) ? "" : "none";
            }
            if (clearBtn) {
                clearBtn.style.visibility = input.value.length > 0 ? "visible" : "hidden";
            }
        }

        input.addEventListener("input", apply);
        if (clearBtn) {
            clearBtn.addEventListener("click", function () {
                input.value = "";
                apply();
                input.focus();
            });
        }
        // Esc per pulire rapidamente.
        input.addEventListener("keydown", function (ev) {
            if (ev.key === "Escape" && input.value) {
                ev.preventDefault();
                input.value = "";
                apply();
            }
        });
        apply();
    }

    document.querySelectorAll("[data-table-search]").forEach(setupTableSearch);
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

// Toast con bottone CTA (usato da videoassistenza: "Rispondi" → apre la sala).
Chipdent.toastAction = function (title, body, actionLabel, actionUrl) {
    var host = document.querySelector(".toasts");
    if (!host) {
        host = document.createElement("div");
        host.className = "toasts";
        document.body.appendChild(host);
    }
    var el = document.createElement("div");
    el.className = "toast toast--action";
    var s = document.createElement("strong"); s.textContent = title || "Chipdent";
    var b = document.createElement("small");  b.textContent = body || "";
    var a = document.createElement("a");
    a.className = "btn btn--primary btn--sm";
    a.href = actionUrl;
    a.textContent = actionLabel || "Apri";
    a.style.marginTop = "8px";
    a.style.display = "inline-block";
    el.appendChild(s);
    el.appendChild(b);
    el.appendChild(a);
    host.appendChild(el);
    // Toast persistente più a lungo (15s) perché richiede un'azione.
    setTimeout(function () {
        el.style.transition = "opacity .3s, transform .3s";
        el.style.opacity = "0";
        el.style.transform = "translateX(20px)";
        setTimeout(function () { el.remove(); }, 320);
    }, 15000);
};
