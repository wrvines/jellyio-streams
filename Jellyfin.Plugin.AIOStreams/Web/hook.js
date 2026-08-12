(function () {
    "use strict";
    var BUTTON_CLASS = "jellyio-hook-search";
    var attempts = 0;

    function status() {
        return window.ApiClient && window.ApiClient.getJSON
            ? window.ApiClient.getJSON(window.ApiClient.getUrl("AIOStreams/Status"))
            : Promise.resolve(null);
    }

    function streamLibraryId() {
        return status().then(function (st) {
            if (!st || st.FolderState !== "Ok") { return null; }
            return window.ApiClient.getJSON(window.ApiClient.getUrl("Library/VirtualFolders")).then(function (folders) {
                var found = null;
                (folders || []).forEach(function (f) {
                    var loc = f.Location || [];
                    if (loc.indexOf(st.StreamRoot) >= 0 || loc.indexOf(st.StreamRoot + "/") === 0) {
                        found = f.ItemId;
                    }
                });
                return found;
            }).catch(function () { return null; });
        }).catch(function () { return null; });
    }

    function currentParentId() {
        var m = (window.location.hash || "").match(/parentId=([^&]+)/);
        return m ? decodeURIComponent(m[1]) : null;
    }

    function ensureButton(parentId) {
        if (!parentId || document.querySelector("." + BUTTON_CLASS)) { return; }
        streamLibraryId().then(function (libId) {
            if (!libId || libId !== parentId) { return; }
            var toolbar = document.querySelector(".pageLibraryPage .header, .libraryPage .header, .header");
            if (!toolbar) { return; }
            var btn = document.createElement("button");
            btn.className = BUTTON_CLASS + " button-link emby-button";
            btn.style.cssText = "margin-left:1em;";
            btn.textContent = "Search AIOStreams";
            btn.addEventListener("click", function () {
                if (window.Dashboard && Dashboard.navigate) {
                    Dashboard.navigate("pluginpage?name=JellyioStreamsSearch");
                }
            });
            toolbar.appendChild(btn);
        });
    }

    function ensureSearchCard() {
        var hash = window.location.hash || "";
        if (hash.indexOf("#/search.html") !== 0) { return; }
        var term = decodeURIComponent((hash.match(/query=([^&]*)/) || [])[1] || "");
        if (!term || document.querySelector(".jellyio-hook-searchcard")) { return; }
        var container = document.querySelector(".searchResults, #searchPage .itemsContainer");
        if (!container) { return; }
        var empty = !container.querySelector(".card");
        if (!empty) { return; }
        var card = document.createElement("div");
        card.className = "jellyio-hook-searchcard card";
        card.style.cssText = "padding:1.5em;text-align:center;";
        var p = document.createElement("p");
        p.textContent = "Not in your library? Find \"" + term + "\" on AIOStreams.";
        var btn = document.createElement("button");
        btn.textContent = "Search AIOStreams";
        btn.addEventListener("click", function () {
            if (window.Dashboard && Dashboard.navigate) {
                Dashboard.navigate("pluginpage?name=JellyioStreamsSearch&query=" + encodeURIComponent(term) + "&type=movie");
            }
        });
        card.appendChild(p);
        card.appendChild(btn);
        container.appendChild(card);
    }

    function tick() {
        try {
            ensureButton(currentParentId());
            ensureSearchCard();
        } catch (e) { /* never throw */ }
        attempts++;
        if (attempts < 120) { setTimeout(tick, 2000); }
    }

    setTimeout(tick, 3000);
})();
