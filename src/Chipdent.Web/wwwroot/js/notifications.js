// Chipdent — SignalR notifications client

(function () {
    "use strict";

    if (typeof signalR === "undefined") {
        console.warn("[Chipdent] SignalR not loaded");
        return;
    }

    var indicator = document.querySelector("[data-conn]");
    var feed = document.querySelector("[data-feed]");

    function setState(state) {
        if (!indicator) return;
        indicator.setAttribute("data-state", state);
        var labels = { connected: "live", connecting: "connessione…", disconnected: "offline" };
        var label = indicator.querySelector(".label");
        if (label) label.textContent = labels[state] || state;
    }

    var connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/notifications")
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    connection.onreconnecting(function () { setState("connecting"); });
    connection.onreconnected(function () { setState("connected"); });
    connection.onclose(function () { setState("disconnected"); });

    connection.on("activity", function (payload) {
        Chipdent.toast(payload.title || "Aggiornamento", payload.description || "");
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

    setState("connecting");
    connection.start()
        .then(function () { setState("connected"); })
        .catch(function (err) {
            console.error("[Chipdent] SignalR start failed", err);
            setState("disconnected");
        });

    window.Chipdent.connection = connection;
})();
