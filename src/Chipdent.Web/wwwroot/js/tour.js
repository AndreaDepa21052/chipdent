// Chipdent — Guided onboarding tour
// Vanilla JS, no dependencies. Persists across page navigations via localStorage.

(function () {
    "use strict";

    var STORAGE_KEYS = {
        completed: "chipdent.tour.completed",
        dismissed: "chipdent.tour.dismissed",
        active:    "chipdent.tour.active",
        step:      "chipdent.tour.step",
        flow:      "chipdent.tour.flow"
    };

    // Tour steps — each step can target a CSS selector and an optional `route`.
    // If `route` is set and differs from the current path, the tour will
    // navigate to that page and resume on load.
    // `condition` receives the document and returns true to include the step;
    // missing target elements automatically cause the step to be skipped.
    var STEPS = [
        {
            id: "welcome",
            route: "/dashboard",
            hero: true,
            title: "Benvenuto in Chipdent",
            body:
                "In un paio di minuti ti mostriamo tutto: <strong>turni, presenze, compliance, " +
                "videoassistenza, NPS pazienti, predizioni, ronde di sicurezza</strong> e molto altro. " +
                "Puoi interrompere il tour quando vuoi.",
            primaryLabel: "Inizia il tour",
            secondaryLabel: "Salta"
        },
        {
            id: "workspace",
            route: "/dashboard",
            target: "[data-tour='workspace']",
            position: "right",
            title: "Il tuo workspace",
            body:
                "Qui vedi il <strong>workspace attivo</strong> e puoi cambiarlo. " +
                "Ogni clinica, turno e documento è isolato per workspace."
        },
        {
            id: "search",
            route: "/dashboard",
            target: "[data-tour='search']",
            position: "right",
            title: "Cerca rapidamente",
            body:
                "Premi <kbd>⌘K</kbd> (o <kbd>Ctrl+K</kbd>) per aprire la ricerca globale e saltare " +
                "in qualsiasi sezione: clinica, dottore, documento."
        },
        {
            id: "sidebar",
            route: "/dashboard",
            target: "[data-tour='sidebar-nav']",
            position: "right",
            title: "Navigazione del portale",
            body:
                "Tutta la navigazione vive qui sulla sinistra, organizzata per area: " +
                "<strong>Operatività</strong>, <strong>Anagrafiche</strong>, <strong>Compliance</strong> e <strong>Amministrazione</strong>."
        },
        {
            id: "dashboard-stats",
            route: "/dashboard",
            target: "[data-tour='dashboard-stats']",
            position: "bottom",
            title: "Stato in tempo reale",
            body:
                "Le metriche del workspace si aggiornano <strong>live</strong>: cliniche operative, " +
                "dottori attivi, dipendenti e scadenze RLS in arrivo."
        },
        {
            id: "dashboard-feed",
            route: "/dashboard",
            target: "[data-tour='dashboard-feed']",
            position: "left",
            title: "Attività recenti",
            body:
                "Ogni cambiamento nel workspace appare qui in tempo reale grazie a SignalR — niente refresh."
        },
        {
            id: "turni",
            route: "/turni",
            target: "[data-tour='nav-turni']",
            position: "right",
            title: "Pianifica i turni",
            body:
                "La sezione <strong>Turni</strong> mostra il calendario settimanale: clicca un giorno " +
                "per assegnare un dottore o un dipendente a una clinica."
        },
        {
            id: "comunicazioni",
            route: "/comunicazioni",
            target: "[data-tour='nav-comunicazioni']",
            position: "right",
            title: "Comunicazioni interne",
            body:
                "Inbox condivisa per il team: <strong>annunci, richieste e note</strong> categorizzate, " +
                "con notifiche live e tracciamento di chi ha letto cosa."
        },
        {
            id: "videoassistenza",
            route: "/videoassistenza",
            target: "[data-tour='nav-videoassistenza']",
            position: "right",
            title: "📞 Videoassistenza on-demand",
            body:
                "Da qui Staff e Direttori aprono una <strong>richiesta di assistenza al Backoffice</strong>. " +
                "Tutti gli operatori online ricevono un toast realtime con tasto «Rispondi»: alla presa in carico " +
                "si apre una <strong>videocall Jitsi</strong> per entrambi nella stessa stanza.",
            requiresElement: "[data-tour='nav-videoassistenza']"
        },
        {
            id: "mie-timbrature",
            route: "/mie-timbrature",
            target: "[data-tour='nav-mie-timbrature']",
            position: "right",
            title: "⏱ Timbrature self-service (con anti-frode)",
            body:
                "Inizio/fine turno, pause e smart-working dal browser. " +
                "Le timbrature web catturano <strong>posizione GPS</strong> con consenso del dipendente " +
                "e marcano «fuori area» quelle oltre il raggio della sede (configurabile).",
            requiresElement: "[data-tour='nav-mie-timbrature']"
        },
        {
            id: "cliniche",
            route: "/cliniche",
            target: "[data-tour='nav-cliniche']",
            position: "right",
            title: "Cliniche del network",
            body:
                "Gestisci tutte le sedi dentali: dati amministrativi, team, stato operativo e timeline storica.",
            requiresElement: "[data-tour='nav-cliniche']"
        },
        {
            id: "dottori",
            route: "/dottori",
            target: "[data-tour='nav-dottori']",
            position: "right",
            title: "Anagrafica dottori",
            body:
                "Profili medici con licenze, specializzazioni, alert su scadenze e timeline trasferimenti tra cliniche.",
            requiresElement: "[data-tour='nav-dottori']"
        },
        {
            id: "dipendenti",
            route: "/dipendenti",
            target: "[data-tour='nav-dipendenti']",
            position: "right",
            title: "Anagrafica dipendenti",
            body:
                "Personale di studio con stato di onboarding, ferie residue e storico assegnazioni.",
            requiresElement: "[data-tour='nav-dipendenti']"
        },
        {
            id: "rls",
            route: "/rls",
            target: "[data-tour='nav-rls']",
            position: "right",
            title: "RLS / Sicurezza",
            body:
                "Tutto ciò che riguarda la <strong>compliance</strong>: visite mediche, corsi obbligatori e documenti DVR " +
                "con avvisi sulle scadenze.",
            requiresElement: "[data-tour='nav-rls']"
        },
        {
            id: "documentazione",
            route: "/documentazione",
            target: "[data-tour='nav-documentazione']",
            position: "right",
            title: "Archivio documentale",
            body:
                "Documenti raggruppati per clinica con scadenze monitorate. Carica nuovi file con un click.",
            requiresElement: "[data-tour='nav-documentazione']"
        },
        {
            id: "scadenziario",
            route: "/scadenziario",
            target: "[data-tour='nav-scadenziario']",
            position: "right",
            title: "📅 Scadenziario unificato",
            body:
                "<strong>Tutte</strong> le scadenze del workspace in un'unica vista — visite mediche, corsi, " +
                "DVR, contratti, documenti, albo dottori — ordinate per <strong>impatto × urgenza</strong>. " +
                "Niente più scadenze sparse in 8 pagine.",
            requiresElement: "[data-tour='nav-scadenziario']"
        },
        {
            id: "operations",
            route: "/operations/ronda",
            target: "[data-tour='nav-operations']",
            position: "right",
            title: "🔐 Operations: ronda + inventario",
            body:
                "<strong>Ronda apertura/chiusura sede</strong> con checklist firmata digitalmente " +
                "(allarme, frigo farmaci, autoclave) e <strong>inventario consumabili</strong> con alert " +
                "automatico al riordino sotto-soglia.",
            requiresElement: "[data-tour='nav-operations']"
        },
        {
            id: "predizioni",
            route: "/predizioni/assenze",
            target: "[data-tour='nav-predizioni']",
            position: "right",
            title: "🔮 Predizione assenze",
            body:
                "Score di rischio <strong>esplicabile</strong> per ogni turno futuro, basato sui pattern " +
                "storici personali (giorno della settimana sfavorevole, ponti, prossimità a ferie). " +
                "Niente ML black-box: vedi sempre i fattori che alzano il punteggio.",
            requiresElement: "[data-tour='nav-predizioni']"
        },
        {
            id: "feedback",
            route: "/feedback",
            target: "[data-tour='nav-feedback']",
            position: "right",
            title: "💬 Feedback NPS pazienti",
            body:
                "Genera un <strong>QR per sede</strong> da esporre a fine visita: i pazienti rispondono " +
                "anonimamente (0–10 + commento), tu vedi NPS aggregato per sede e dottore con alert sui " +
                "feedback critici (≤6).",
            requiresElement: "[data-tour='nav-feedback']"
        },
        {
            id: "bacheca-tv",
            route: "/cliniche",
            hero: true,
            title: "📺 Bacheca TV per sede",
            body:
                "Per ogni clinica puoi attivare una <strong>bacheca anonima full-screen</strong> da " +
                "proiettare su un monitor in sala riposo: turni del giorno e comunicazioni recenti, " +
                "auto-aggiornata. Apri il dettaglio di una clinica e usa il tasto " +
                "«📺 Apri bacheca TV».",
            requiresElement: "[data-tour='nav-cliniche']"
        },
        {
            id: "audit",
            route: "/audit",
            target: "[data-tour='nav-audit']",
            position: "right",
            title: "Audit log",
            body:
                "Tracciato completo di chi ha fatto cosa: filtra per entità, azione o utente. Utile per ispezioni e governance.",
            requiresElement: "[data-tour='nav-audit']"
        },
        {
            id: "users",
            route: "/users",
            target: "[data-tour='nav-users']",
            position: "right",
            title: "Utenti e ruoli",
            body:
                "Inviti, ruoli (<strong>Owner, Management, Direttore, Backoffice, Staff</strong>) e " +
                "matrice dei permessi: qui controlli chi vede cosa nel workspace.",
            requiresElement: "[data-tour='nav-users']"
        },
        {
            id: "bell",
            route: "/dashboard",
            target: "[data-tour='bell']",
            position: "bottom",
            title: "Notifiche live",
            body:
                "Eventi importanti compaiono qui in tempo reale: turni modificati, nuove comunicazioni, scadenze."
        },
        {
            id: "user-menu",
            route: "/dashboard",
            target: "[data-tour='user-menu']",
            position: "right",
            title: "Profilo, preferenze e tour",
            body:
                "Da qui accedi al tuo <strong>profilo</strong>, alle <strong>preferenze</strong> e puoi " +
                "<strong>riavviare questo tour</strong> in qualsiasi momento."
        },
        {
            id: "done",
            route: "/dashboard",
            hero: true,
            title: "Tutto pronto",
            body:
                "Hai visto tutto il portale: turni, presenze, compliance, comunicazioni, " +
                "videoassistenza, scadenziario, NPS, predizioni, ronda e bacheca TV. " +
                "Puoi sempre riavviare questo tour dal <strong>menu utente</strong>. Buon lavoro!",
            primaryLabel: "Inizia",
            secondaryLabel: null
        }
    ];

    // ---------- helpers ----------

    function safeStorage() {
        try {
            var k = "__chipdent_test__";
            window.localStorage.setItem(k, k);
            window.localStorage.removeItem(k);
            return window.localStorage;
        } catch (_) {
            return null;
        }
    }

    var storage = safeStorage();

    function getFlag(key) { return storage ? storage.getItem(key) : null; }
    function setFlag(key, value) { if (storage) storage.setItem(key, value); }
    function clearFlag(key) { if (storage) storage.removeItem(key); }

    function pathMatches(route) {
        if (!route) return true;
        var current = (window.location.pathname || "/").toLowerCase();
        var target = route.toLowerCase();
        if (current === target) return true;
        // ASP.NET MVC default action — `/dashboard` also matches `/dashboard/index`
        if (current === target + "/index") return true;
        return false;
    }

    function el(tag, className, html) {
        var node = document.createElement(tag);
        if (className) node.className = className;
        if (html != null) node.innerHTML = html;
        return node;
    }

    function svg(d, sw) {
        return '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="' + (sw || 2) + '" stroke-linecap="round" stroke-linejoin="round">' + d + '</svg>';
    }

    var ICONS = {
        x:        svg('<path d="M18 6 6 18M6 6l12 12"/>'),
        next:     svg('<path d="M5 12h14"/><path d="m12 5 7 7-7 7"/>'),
        prev:     svg('<path d="M19 12H5"/><path d="m12 19-7-7 7-7"/>'),
        sparkle:  svg('<path d="M12 2v4M12 18v4M4.93 4.93l2.83 2.83M16.24 16.24l2.83 2.83M2 12h4M18 12h4M4.93 19.07l2.83-2.83M16.24 7.76l2.83-2.83"/>', 2),
        check:    svg('<polyline points="20 6 9 17 4 12"/>', 2.4)
    };

    // ---------- Tour engine ----------

    function Tour(steps) {
        this.steps = steps;
        this.index = 0;
        this.overlay = null;
        this.cutout = null;
        this.popover = null;
        this.scrollHandler = null;
        this.resizeHandler = null;
        this.keyHandler = null;
    }

    Tour.prototype.start = function (fromIndex) {
        this.index = (typeof fromIndex === "number" && fromIndex >= 0) ? fromIndex : 0;
        setFlag(STORAGE_KEYS.active, "true");
        setFlag(STORAGE_KEYS.step, String(this.index));
        clearFlag(STORAGE_KEYS.dismissed);
        this._buildChrome();
        this._renderStep();
        this._bindEvents();
    };

    Tour.prototype.resume = function () {
        var saved = parseInt(getFlag(STORAGE_KEYS.step) || "0", 10);
        if (isNaN(saved) || saved < 0) saved = 0;
        if (saved >= this.steps.length) { this._finish(true); return; }
        this.start(saved);
    };

    Tour.prototype._buildChrome = function () {
        if (this.overlay) return;

        var overlay = el("div", "tour-overlay");
        overlay.setAttribute("role", "presentation");
        var cutout = el("div", "tour-overlay__cutout");
        overlay.appendChild(cutout);

        var pop = el("div", "tour-popover");
        pop.setAttribute("role", "dialog");
        pop.setAttribute("aria-modal", "true");
        pop.setAttribute("aria-live", "polite");

        document.body.appendChild(overlay);
        document.body.appendChild(pop);

        this.overlay = overlay;
        this.cutout = cutout;
        this.popover = pop;

        // Allow click-outside on overlay to dismiss
        var self = this;
        overlay.addEventListener("click", function (e) {
            if (e.target === overlay) { self._dismiss(); }
        });
    };

    Tour.prototype._teardown = function () {
        if (this.overlay) { this.overlay.remove(); this.overlay = null; this.cutout = null; }
        if (this.popover) { this.popover.remove(); this.popover = null; }
        if (this.scrollHandler) {
            window.removeEventListener("scroll", this.scrollHandler, true);
            this.scrollHandler = null;
        }
        if (this.resizeHandler) {
            window.removeEventListener("resize", this.resizeHandler);
            this.resizeHandler = null;
        }
        if (this.keyHandler) {
            window.removeEventListener("keydown", this.keyHandler);
            this.keyHandler = null;
        }
    };

    Tour.prototype._bindEvents = function () {
        var self = this;
        this.scrollHandler = function () { self._reposition(); };
        this.resizeHandler = function () { self._reposition(); };
        this.keyHandler = function (e) {
            if (e.key === "Escape") { self._dismiss(); }
            else if (e.key === "ArrowRight") { self.next(); }
            else if (e.key === "ArrowLeft") { self.prev(); }
        };
        window.addEventListener("scroll", this.scrollHandler, true);
        window.addEventListener("resize", this.resizeHandler);
        window.addEventListener("keydown", this.keyHandler);
    };

    Tour.prototype._currentStep = function () { return this.steps[this.index]; };

    Tour.prototype._stepIsAvailable = function (step) {
        if (step.requiresElement) {
            return !!document.querySelector(step.requiresElement);
        }
        if (step.target) {
            // If the step targets an element, but it does not exist on the
            // landing page (and the step has no route), we'll still try later;
            // here we only skip when an explicit requiresElement was set.
        }
        return true;
    };

    Tour.prototype._navigateAndPersist = function (route) {
        setFlag(STORAGE_KEYS.active, "true");
        setFlag(STORAGE_KEYS.step, String(this.index));
        window.location.href = route;
    };

    Tour.prototype._renderStep = function () {
        var step = this._currentStep();
        if (!step) { this._finish(true); return; }

        // Cross-page navigation
        if (step.route && !pathMatches(step.route)) {
            this._navigateAndPersist(step.route);
            return;
        }

        // Skip steps whose dependencies aren't visible to the current role
        if (!this._stepIsAvailable(step)) {
            return this.next(true);
        }

        // Auto-skip when target is explicitly required and missing
        if (step.target) {
            var t = document.querySelector(step.target);
            if (!t) {
                // Target is missing — skip rather than block the user
                return this.next(true);
            }
        }

        setFlag(STORAGE_KEYS.step, String(this.index));

        this._renderPopover(step);
        this._renderHighlight(step);
        // requestAnimationFrame to ensure DOM is updated before measuring
        var self = this;
        requestAnimationFrame(function () {
            self._reposition();
            self._show();
        });
    };

    Tour.prototype._show = function () {
        if (this.overlay) this.overlay.classList.add("is-visible");
        if (this.cutout)  this.cutout.classList.add("is-visible");
        if (this.popover) this.popover.classList.add("is-visible");
    };

    Tour.prototype._renderPopover = function (step) {
        var pop = this.popover;
        var hero = !!step.hero;
        pop.className = "tour-popover" + (hero ? " tour-popover--hero" : "");
        pop.classList.remove("is-visible");

        var totalSteps = this.steps.length;
        var current = this.index + 1;
        var primaryLabel = step.primaryLabel ||
            (this.index === this.steps.length - 1 ? "Fine" : "Avanti");
        var secondaryLabel = step.secondaryLabel === null
            ? null
            : (step.secondaryLabel || (this.index === 0 ? "Salta" : "Indietro"));

        var heroMark = hero
            ? '<span class="tour-popover__hero-mark">' + ICONS.sparkle + '</span>'
            : "";

        var headHtml =
            '<div class="tour-popover__head">' +
                (hero ? heroMark : '<span class="tour-popover__step">Passo ' + current + ' di ' + totalSteps + '</span>') +
                (hero ? "" : '<button type="button" class="tour-popover__close" data-tour-action="close" aria-label="Chiudi tour">' + ICONS.x + '</button>') +
            '</div>';

        var bodyHtml =
            '<h3 class="tour-popover__title">' + step.title + '</h3>' +
            '<p class="tour-popover__body">' + step.body + '</p>';

        // Progress pips
        var pipsHtml = "";
        for (var i = 0; i < totalSteps; i++) {
            var cls = "tour-popover__pip" +
                (i < this.index ? " is-done" : (i === this.index ? " is-active" : ""));
            pipsHtml += '<span class="' + cls + '"></span>';
        }

        var actionsHtml = '<div class="tour-popover__actions">';
        if (secondaryLabel) {
            var secondaryAction = (this.index === 0) ? "close" : "prev";
            actionsHtml += '<button type="button" class="tour-btn tour-btn--ghost" data-tour-action="' + secondaryAction + '">' +
                (secondaryAction === "prev" ? ICONS.prev : "") +
                '<span>' + secondaryLabel + '</span>' +
                '</button>';
        }
        var primaryAction = (this.index === this.steps.length - 1) ? "finish" : "next";
        var primaryIcon = primaryAction === "finish" ? ICONS.check : ICONS.next;
        actionsHtml +=
            '<button type="button" class="tour-btn tour-btn--primary" data-tour-action="' + primaryAction + '" autofocus>' +
                '<span>' + primaryLabel + '</span>' + primaryIcon +
            '</button>';
        actionsHtml += '</div>';

        var footHtml =
            '<div class="tour-popover__foot">' +
                (hero ? "" : '<div class="tour-popover__progress">' + pipsHtml + '</div>') +
                actionsHtml +
            '</div>';

        pop.innerHTML =
            headHtml +
            bodyHtml +
            footHtml +
            (hero ? "" : '<span class="tour-popover__arrow"></span>');

        var self = this;
        pop.querySelectorAll("[data-tour-action]").forEach(function (btn) {
            btn.addEventListener("click", function (e) {
                e.preventDefault();
                var action = btn.getAttribute("data-tour-action");
                if (action === "next")   self.next();
                else if (action === "prev")   self.prev();
                else if (action === "close")  self._dismiss();
                else if (action === "finish") self._finish();
            });
        });
    };

    Tour.prototype._renderHighlight = function (step) {
        if (!step.target) {
            // Centered popover (welcome / final)
            this.cutout.classList.add("is-centered");
            this.cutout.style.width = "0px";
            this.cutout.style.height = "0px";
            this.cutout.style.left = "50%";
            this.cutout.style.top = "50%";
            return;
        }
        this.cutout.classList.remove("is-centered");
    };

    Tour.prototype._reposition = function () {
        var step = this._currentStep();
        if (!step) return;
        var pop = this.popover;
        if (!pop) return;

        if (!step.target) {
            // centered popover
            pop.classList.add("is-centered");
            pop.classList.remove("is-pos-top", "is-pos-bottom", "is-pos-left", "is-pos-right");
            pop.style.left = "";
            pop.style.top = "";
            return;
        }

        var target = document.querySelector(step.target);
        if (!target) return;

        // Make sure the target is in view
        var rect = target.getBoundingClientRect();
        var vp = { w: window.innerWidth, h: window.innerHeight };
        if (rect.bottom < 0 || rect.top > vp.h || rect.right < 0 || rect.left > vp.w) {
            target.scrollIntoView({ behavior: "smooth", block: "center" });
            // After scroll, re-measure shortly
            var self = this;
            setTimeout(function () { self._reposition(); }, 320);
            return;
        }

        // Update cutout
        var pad = 6;
        this.cutout.style.left   = (rect.left - pad) + "px";
        this.cutout.style.top    = (rect.top  - pad) + "px";
        this.cutout.style.width  = (rect.width  + pad * 2) + "px";
        this.cutout.style.height = (rect.height + pad * 2) + "px";

        // Position popover
        pop.classList.remove("is-centered", "is-pos-top", "is-pos-bottom", "is-pos-left", "is-pos-right");
        var popRect = { w: pop.offsetWidth || 380, h: pop.offsetHeight || 200 };
        var gap = 14;
        var preferred = step.position || "auto";

        function fitsBottom() { return rect.bottom + gap + popRect.h < vp.h - 8; }
        function fitsTop()    { return rect.top    - gap - popRect.h > 8; }
        function fitsRight()  { return rect.right  + gap + popRect.w < vp.w - 8; }
        function fitsLeft()   { return rect.left   - gap - popRect.w > 8; }

        var pos = preferred;
        if (pos === "auto") {
            if (fitsBottom()) pos = "bottom";
            else if (fitsRight()) pos = "right";
            else if (fitsTop()) pos = "top";
            else if (fitsLeft()) pos = "left";
            else pos = "bottom";
        } else {
            // Fallback chain when preferred doesn't fit
            var fits = ({ top: fitsTop, bottom: fitsBottom, left: fitsLeft, right: fitsRight }[pos])();
            if (!fits) {
                if (fitsBottom()) pos = "bottom";
                else if (fitsRight()) pos = "right";
                else if (fitsTop()) pos = "top";
                else if (fitsLeft()) pos = "left";
            }
        }

        var top = 0, left = 0;
        if (pos === "bottom") {
            top  = rect.bottom + gap;
            left = rect.left + (rect.width / 2) - (popRect.w / 2);
        } else if (pos === "top") {
            top  = rect.top - gap - popRect.h;
            left = rect.left + (rect.width / 2) - (popRect.w / 2);
        } else if (pos === "right") {
            top  = rect.top + (rect.height / 2) - (popRect.h / 2);
            left = rect.right + gap;
        } else if (pos === "left") {
            top  = rect.top + (rect.height / 2) - (popRect.h / 2);
            left = rect.left - gap - popRect.w;
        }

        // Clamp to viewport
        var margin = 12;
        if (left < margin) left = margin;
        if (left + popRect.w > vp.w - margin) left = vp.w - margin - popRect.w;
        if (top < margin) top = margin;
        if (top + popRect.h > vp.h - margin) top = vp.h - margin - popRect.h;

        pop.classList.add("is-pos-" + pos);
        pop.style.left = left + "px";
        pop.style.top  = top  + "px";

        // Position arrow
        var arrow = pop.querySelector(".tour-popover__arrow");
        if (arrow) {
            arrow.style.left = "";
            arrow.style.top  = "";
            if (pos === "bottom" || pos === "top") {
                var ax = (rect.left + rect.width / 2) - left - 6;
                ax = Math.max(16, Math.min(popRect.w - 22, ax));
                arrow.style.left = ax + "px";
                arrow.style[pos === "bottom" ? "top" : "bottom"] = "-7px";
            } else {
                var ay = (rect.top + rect.height / 2) - top - 6;
                ay = Math.max(16, Math.min(popRect.h - 22, ay));
                arrow.style.top = ay + "px";
                arrow.style[pos === "right" ? "left" : "right"] = "-7px";
            }
        }
    };

    Tour.prototype.next = function (silentSkip) {
        var nextIndex = this.index + 1;
        if (nextIndex >= this.steps.length) {
            this._finish();
            return;
        }
        this.index = nextIndex;
        if (!silentSkip) this._fadeOut();
        this._renderStep();
    };

    Tour.prototype.prev = function () {
        if (this.index === 0) return;
        this.index -= 1;
        this._fadeOut();
        this._renderStep();
    };

    Tour.prototype._fadeOut = function () {
        if (this.popover) this.popover.classList.remove("is-visible");
    };

    Tour.prototype._dismiss = function () {
        setFlag(STORAGE_KEYS.dismissed, "true");
        clearFlag(STORAGE_KEYS.active);
        clearFlag(STORAGE_KEYS.step);
        this._teardown();
    };

    Tour.prototype._finish = function (silent) {
        setFlag(STORAGE_KEYS.completed, "true");
        clearFlag(STORAGE_KEYS.active);
        clearFlag(STORAGE_KEYS.step);
        this._teardown();
        if (!silent && window.Chipdent && typeof window.Chipdent.toast === "function") {
            window.Chipdent.toast("Tour completato", "Puoi riavviarlo dal menu utente.");
        }
    };

    // ---------- Public API ----------

    var instance = new Tour(STEPS);

    var api = {
        start: function () {
            // Restart from scratch
            clearFlag(STORAGE_KEYS.completed);
            clearFlag(STORAGE_KEYS.dismissed);
            // If we're not on the dashboard, navigate there first.
            var first = STEPS[0];
            if (first.route && !pathMatches(first.route)) {
                setFlag(STORAGE_KEYS.active, "true");
                setFlag(STORAGE_KEYS.step, "0");
                window.location.href = first.route;
                return;
            }
            instance.start(0);
        },
        stop: function () {
            instance._dismiss();
        },
        reset: function () {
            clearFlag(STORAGE_KEYS.completed);
            clearFlag(STORAGE_KEYS.dismissed);
            clearFlag(STORAGE_KEYS.active);
            clearFlag(STORAGE_KEYS.step);
        }
    };

    window.Chipdent = window.Chipdent || {};
    window.Chipdent.tour = api;

    // ---------- Auto-start logic ----------

    function init() {
        // Wire up any "data-tour-launcher" buttons (e.g. from the user menu)
        document.querySelectorAll("[data-tour-launcher]").forEach(function (btn) {
            btn.addEventListener("click", function (e) {
                e.preventDefault();
                api.start();
            });
        });

        // Resume an interrupted tour after navigation
        if (getFlag(STORAGE_KEYS.active) === "true") {
            instance.resume();
            return;
        }

        // First-visit auto-start: only on the dashboard, only if never completed/dismissed
        var completed = getFlag(STORAGE_KEYS.completed) === "true";
        var dismissed = getFlag(STORAGE_KEYS.dismissed) === "true";
        if (!completed && !dismissed && pathMatches("/dashboard")) {
            // Defer slightly so the page paints and SignalR doesn't steal focus
            setTimeout(function () { instance.start(0); }, 600);
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
