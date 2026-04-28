// Chipdent — SignalR notifications client + bell panel

(function () {
    "use strict";

    if (typeof signalR === "undefined") {
        console.warn("[Chipdent] SignalR not loaded");
        return;
    }

    var indicator = document.querySelector("[data-conn]");
    var feed = document.querySelector("[data-feed]");

    var bellDot   = document.querySelector("[data-bell-dot]");
    var bellBody  = document.querySelector("[data-bell-body]");
    var bellEmpty = document.querySelector("[data-bell-empty]");
    var bellMark  = document.querySelector("[data-bell-mark]");
    var bellRoot  = document.querySelector(".bell");

    var unread = 0;

    function setState(state) {
        if (!indicator) return;
        indicator.setAttribute("data-state", state);
        var labels = { connected: "live", connecting: "connessione…", disconnected: "offline" };
        var label = indicator.querySelector(".label");
        if (label) label.textContent = labels[state] || state;
    }

    function escapeHtml(s) {
        return (s || "").replace(/[&<>"']/g, function (c) {
            return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
        });
    }

    function timeAgo() {
        return new Date().toLocaleTimeString("it-IT", { hour: "2-digit", minute: "2-digit" });
    }

    function bumpUnread() {
        unread++;
        if (bellDot) bellDot.hidden = false;
    }

    function clearUnread() {
        unread = 0;
        if (bellDot) bellDot.hidden = true;
    }

    function pushBell(kind, title, subtitle, when, silent) {
        if (!bellBody) return;
        if (bellEmpty) bellEmpty.hidden = true;
        var item = document.createElement("div");
        item.className = "bell__item bell__item--" + (kind || "default");
        item.innerHTML =
            '<div class="bell__icon"></div>' +
            '<div class="bell__content">' +
                '<div class="bell__title"></div>' +
                '<div class="bell__sub"></div>' +
            '</div>' +
            '<time class="bell__time"></time>';
        item.querySelector(".bell__title").textContent = title || "";
        item.querySelector(".bell__sub").textContent = subtitle || "";
        item.querySelector(".bell__time").textContent = formatWhen(when);
        item.querySelector(".bell__icon").textContent = ({
            audit: "▤", shift: "📅", comm: "💬", ping: "•"
        })[kind] || "●";
        bellBody.insertBefore(item, bellBody.firstChild.nextSibling || null);
        if (!silent) bumpUnread();

        // Keep at most 50 items
        var items = bellBody.querySelectorAll(".bell__item");
        if (items.length > 50) items[items.length - 1].remove();
    }

    function formatWhen(when) {
        if (!when) return timeAgo();
        try {
            var d = new Date(when);
            if (isNaN(d.getTime())) return timeAgo();
            var now = new Date();
            var sameDay = d.getFullYear() === now.getFullYear() && d.getMonth() === now.getMonth() && d.getDate() === now.getDate();
            return sameDay
                ? d.toLocaleTimeString("it-IT", { hour: "2-digit", minute: "2-digit" })
                : d.toLocaleDateString("it-IT", { day: "2-digit", month: "short" }) + " · " +
                  d.toLocaleTimeString("it-IT", { hour: "2-digit", minute: "2-digit" });
        } catch (e) { return timeAgo(); }
    }

    var historyLoaded = false;
    function loadHistoryOnce() {
        if (historyLoaded) return;
        historyLoaded = true;
        fetch("/audit/recent.json?limit=20", { credentials: "same-origin" })
            .then(function (r) { return r.ok ? r.json() : []; })
            .then(function (entries) {
                if (!Array.isArray(entries) || entries.length === 0) return;
                // Inserisci dal più vecchio al più recente, in modo che le voci più nuove restino in cima.
                entries.slice().reverse().forEach(function (e) {
                    var title = (e.action || "") + " · " + (e.entityType || "");
                    var sub = e.entityLabel ? (e.entityLabel + " — " + (e.user || "")) : (e.user || "");
                    pushBell(e.kind || "audit", title, sub, e.when, /*silent*/ true);
                });
            })
            .catch(function () { /* il bell continua a funzionare con gli eventi live */ });
    }

    if (bellMark) {
        bellMark.addEventListener("click", function (e) {
            e.preventDefault();
            clearUnread();
        });
    }
    if (bellRoot) {
        bellRoot.addEventListener("toggle", function () {
            if (bellRoot.open) {
                loadHistoryOnce();
                clearUnread();
            }
        });
    }

    var connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/notifications")
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    connection.onreconnecting(function () { setState("connecting"); });
    connection.onreconnected(function () { setState("connected"); });
    connection.onclose(function () { setState("disconnected"); });

    // ── Web Notifications API: chiede il consenso una volta per sessione ──
    function ensureBrowserNotifyPermission() {
        if (!("Notification" in window)) return;
        if (Notification.permission === "default") {
            // Chiede il consenso solo dopo la prima interazione utente per non essere bloccato da Chrome.
            var asked = sessionStorage.getItem("chipdent.notifyAsked");
            if (asked) return;
            document.addEventListener("click", function once() {
                document.removeEventListener("click", once);
                sessionStorage.setItem("chipdent.notifyAsked", "1");
                Notification.requestPermission().catch(function () {});
            }, { once: true });
        }
    }
    function browserNotify(title, body) {
        if (!("Notification" in window) || Notification.permission !== "granted") return;
        if (document.visibilityState === "visible") return; // già visibile, basta il toast
        try { new Notification(title, { body: body, icon: "/img/logo.png", silent: false }); } catch (_) { }
    }
    ensureBrowserNotifyPermission();

    connection.on("activity", function (payload) {
        Chipdent.toast(payload.title || "Aggiornamento", payload.description || "");
        pushBell(payload.kind || "default", payload.title || "Aggiornamento", payload.description || "");
        browserNotify(payload.title || "Aggiornamento", payload.description || "");
        if (!feed) return;
        var li = document.createElement("li");
        li.className = "list__item feed-enter";
        li.innerHTML =
            '<div class="list__icon">●</div>' +
            '<div>' +
                '<div class="list__title"></div>' +
                '<div class="list__meta"></div>' +
            '</div>' +
            '<div class="list__when">ora</div>';
        li.querySelector(".list__title").textContent = payload.title || "Aggiornamento";
        li.querySelector(".list__meta").textContent = payload.description || "";
        feed.insertBefore(li, feed.firstChild);
    });

    connection.on("audit", function (e) {
        // toast is handled inside Audit page; here only feed the bell
        pushBell("audit", (e.action || "") + " · " + (e.entityLabel || ""), "da " + (e.user || ""));
    });

    setState("connecting");
    connection.start()
        .then(function () { setState("connected"); })
        .catch(function (err) {
            console.error("[Chipdent] SignalR start failed", err);
            setState("disconnected");
        });

    window.Chipdent.connection = connection;
})();
