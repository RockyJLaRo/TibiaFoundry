/*
 * Global Tibia search (the header of every page, after tibiadata.js). The types and their data routes come from
 * registry.json "search" (TF_SITE.search):
 *
 * * character and guild need the exact name (TibiaData has no name search); the view shows "not found" otherwise;
 * * world, creature and spell suggest from their full lists (TFData.get(list), loaded on the first focus after the type
 *   is chosen and cached by tibiadata.js); house suggests worlds and opens the houses page with its world and town pickers;
 * * matching ignores case and accents (NFD without combining marks): names starting with the text first, then names
 *   containing it, at most 8; spells match their formula ("exura") too;
 * * the field is an ARIA combobox with a listbox popup (APG pattern, list autocomplete without automatic selection):
 *   arrow keys move the active option (aria-activedescendant), Enter opens it, Escape closes the popup, then clears;
 * * recent searches: localStorage "tf-recent-searches", a JSON array of {type, q} (plus id, the race or spell id, for
 *   creatures and spells), newest first, deduplicated, at most 8; shown when the field is empty and focused, with a
 *   "Clear Recent Searches" button. Every storage access is guarded: with storage blocked they last for the page only.
 *
 * Without JavaScript the form is a plain GET to tibia/?character=<name> (the type picker is hidden, it has no name).
 * window.TFSearch.routeFor(type, item) -> a hash route ("#/creature/demon"; "" when nothing can be opened), for the
 * header, the home page and community shortcuts. Works in Node for the unit tests (module.exports = create).
 */
(function (root, factory) {
  if (typeof module === "object" && module.exports) { module.exports = factory; return; }
  var site = root.TF_SITE || {};
  var api = factory({ search: site.search, defaultWorld: site.defaultWorld, defaultTown: site.defaultTown,
    storage: function () { return root.localStorage; } });
  root.TFSearch = api;
  api.mount(root);
})(typeof self !== "undefined" ? self : this, function create(env) {
  "use strict";
  var KEY = "tf-recent-searches", WORLD_KEY = "tf-world", MAX = 8;
  var entries = env.search || [];
  // A null-prototype map: a type read from storage ("constructor", "__proto__", "toString") is never a search type.
  var byType = Object.create(null);
  entries.forEach(function (e) { if (e && typeof e.type === "string") byType[e.type] = e; });
  function known(type) { return typeof type === "string" && Object.prototype.hasOwnProperty.call(byType, type); }
  var defaultWorld = env.defaultWorld || "Antica", defaultTown = env.defaultTown || "Thais";

  // ------------------------------------------------------------------ storage (every access guarded: it may be blocked)
  function storage() { try { return env.storage ? env.storage() : null; } catch (e) { return null; } }
  function readKey(key) { var s = storage(); if (!s) return null; try { return s.getItem(key); } catch (e) { return null; } }
  function writeKey(key, value) {
    var s = storage();
    if (!s) return false;
    try { if (value == null) s.removeItem(key); else s.setItem(key, value); return true; } catch (e) { return false; }
  }

  // ------------------------------------------------------------------ matching
  var MARKS = /[̀-ͯ]/g;
  function clean(s) { return String(s == null ? "" : s).replace(/\s+/g, " ").trim(); }
  function normalize(s) {
    var x = clean(s);
    if (x.normalize) x = x.normalize("NFD").replace(MARKS, "");
    return x.toLowerCase();
  }
  var keyCache = typeof WeakMap === "function" ? new WeakMap() : null;
  function keysOf(item) {
    var k = keyCache && keyCache.get(item);
    if (!k) {
      k = [item.name, item.words].filter(function (x) { return typeof x === "string" && x; }).map(normalize);
      if (keyCache) keyCache.set(item, k);
    }
    return k;
  }
  /** Items whose name (or spell formula) starts with the text, then those containing it; at most max (8). */
  function filter(items, query, max) {
    var q = normalize(query);
    if (!q) return [];
    var starts = [], contains = [];
    (items || []).forEach(function (item) {
      var k = keysOf(item);
      if (k.some(function (x) { return x.indexOf(q) === 0; })) starts.push(item);
      else if (k.some(function (x) { return x.indexOf(q) !== -1; })) contains.push(item);
    });
    return starts.concat(contains).slice(0, max || MAX);
  }
  /** The item whose name (or formula) is exactly the text, ignoring case and accents. */
  function resolve(items, text) {
    var q = normalize(text);
    if (!q) return null;
    for (var i = 0; i < (items || []).length; i++) if (keysOf(items[i]).indexOf(q) !== -1) return items[i];
    return null;
  }
  function str(v) { return typeof v === "string" && v !== ""; }
  /** The suggestion items of a TibiaData list: {name} for worlds, {name, race} for creatures, {name, id, words} for spells. */
  function itemsFrom(list, data) {
    data = data || {};
    if (list === "worlds") {
      var w = data.worlds || {};
      return (w.regular_worlds || []).concat(w.tournament_worlds || []).filter(function (x) { return x && str(x.name); })
        .map(function (x) { return { name: x.name }; });
    }
    if (list === "creatures") {
      return ((data.creatures || {}).creature_list || []).filter(function (x) { return x && str(x.name) && str(x.race); })
        .map(function (x) { return { name: x.name, race: x.race }; });
    }
    if (list === "spells") {
      return ((data.spells || {}).spell_list || []).filter(function (x) { return x && str(x.name) && str(x.spell_id); })
        .map(function (x) { return { name: x.name, id: x.spell_id, words: str(x.formula) ? x.formula : "" }; });
    }
    return [];
  }

  // ------------------------------------------------------------------ routes
  function rememberedWorld() { return clean(readKey(WORLD_KEY)) || defaultWorld; }
  /**
   * The hash route of a search: item is the typed text or {q|name, race|id, world, town}. A route whose key is missing
   * (a creature typed but not picked) opens the type's list page (browse), or "" when there is none (an empty name).
   */
  function routeFor(type, item) {
    if (!known(type)) return "";
    var entry = byType[type];
    if (item == null || typeof item !== "object") item = { q: item };
    var q = clean(item.q != null ? item.q : item.name);
    var values = { q: q, name: q, race: item.race || item.id, id: item.id, world: clean(item.world) || q || rememberedWorld(), town: clean(item.town) || defaultTown };
    var missing = false;
    var path = entry.route.split("/").map(function (seg) {
      var m = /^\{(\w+)\}$/.exec(seg);
      if (!m) return seg;
      var v = values[m[1]];
      if (v == null || String(v) === "") { missing = true; return ""; }
      return encodeURIComponent(String(v));
    }).join("/");
    if (missing) return entry.browse ? "#/" + entry.browse : "";
    return "#/" + path;
  }

  // ------------------------------------------------------------------ recent searches
  var memory = null;   // set when storage cannot be written: the recent searches then last for this page only
  function valid(r) {
    return !!r && typeof r === "object" && known(r.type) && typeof r.q === "string" && clean(r.q) !== "" && r.q.length <= 100 &&
      (r.id == null || typeof r.id === "string" || typeof r.id === "number");
  }
  function tidy(r) {
    var out = { type: r.type, q: clean(r.q) };
    if (r.id != null && String(r.id) !== "") out.id = String(r.id);
    return out;
  }
  function readRecents() {
    if (memory) return memory.slice();
    var list;
    try { list = JSON.parse(readKey(KEY) || "[]"); } catch (e) { list = []; }
    return Array.isArray(list) ? list.filter(valid).map(tidy).slice(0, MAX) : [];
  }
  function saveRecents(list) { memory = writeKey(KEY, JSON.stringify(list)) ? null : list; }
  function addRecent(r) {
    if (!valid(r)) return readRecents();
    var entry = tidy(r), n = normalize(entry.q);
    var list = [entry].concat(readRecents().filter(function (x) { return !(x.type === entry.type && normalize(x.q) === n); })).slice(0, MAX);
    saveRecents(list);
    return list.slice();
  }
  function clearRecents() { memory = writeKey(KEY, null) ? null : []; }

  // ------------------------------------------------------------------ the header search (browser only)
  function mount(win) {
    var doc = win.document;
    if (!doc) return;
    var TF = win.TF || {};
    var t = TF.t || function (k) { return k; };
    var SITE = win.TF_SITE || {};

    function go(hash) {
      if (win.TFNav && win.TFNav.closeDrawer) win.TFNav.closeDrawer(false);
      var paths = SITE.paths || {};
      if (paths.tibiaHash === "" && doc.body.getAttribute("data-page") === "tibia") {
        if (win.location.hash !== hash) win.location.hash = hash;
      } else win.location.href = (paths.tibiaHash || paths.tibia || "") + hash;
    }
    function remember(type, item) {
      var q = clean(item.name != null ? item.name : item.q);
      if (q) addRecent({ type: type, q: q, id: item.race || item.id });
    }

    // Other search forms (the home hero): data-search-form="<type>", the same routes and recent searches.
    Array.prototype.forEach.call(doc.querySelectorAll("form[data-search-form]"), function (f) {
      f.addEventListener("submit", function (e) {
        e.preventDefault();
        var type = f.getAttribute("data-search-form"), field = f.querySelector("input");
        var hash = clean(field.value) ? routeFor(type, field.value) : "";
        if (!hash) { field.focus(); return; }
        remember(type, { q: field.value });
        go(hash);
      });
    });

    var form = doc.querySelector("form[data-global-search]");
    if (!form) return;
    var select = form.querySelector("[data-search-type]"), input = form.querySelector("[data-search-input]");
    var popup = form.querySelector("[data-search-popup]"), listbox = popup.querySelector("[role=listbox]");
    var title = popup.querySelector("[data-search-title]"), status = popup.querySelector("[data-search-status]");
    var clearBtn = popup.querySelector("[data-search-clear]");
    var lists = {};          // list name -> {items, promise, error}
    var shown = [];          // the options: {type, item, recent}
    var active = -1, open = false, announceTimer = null;

    input.setAttribute("role", "combobox");
    input.setAttribute("aria-autocomplete", "list");
    input.setAttribute("aria-expanded", "false");
    input.setAttribute("aria-controls", listbox.id);

    function entry() { return known(select.value) ? byType[select.value] : entries[0]; }
    function typeLabel(type) { return t("js.searchBox.type." + type); }
    function updateType() {
      var text = t("js.searchBox.placeholder." + entry().type);
      input.placeholder = text;
      input.setAttribute("aria-label", text);
    }
    var ERR = { notfound: "js.errNotFound", invalid: "js.errInvalid", network: "js.errNetwork", server: "js.errServer", ratelimit: "js.errRateLimit", malformed: "js.errMalformed" };

    function ensureList(name) {
      var L = lists[name] || (lists[name] = { items: null, promise: null, error: null });
      if (L.items) return Promise.resolve(L.items);
      if (L.promise) return L.promise;
      if (!win.TFData) return Promise.resolve(null);
      L.error = null;
      L.promise = win.TFData.get(name, []).then(function (r) {
        L.items = itemsFrom(name, r.data);
        L.promise = null;
        render();
        return L.items;
      }, function (e) {
        L.promise = null;
        L.error = e || { kind: "network" };
        render();
        return null;
      });
      return L.promise;
    }

    function announce(text) {
      clearTimeout(announceTimer);
      if (TF.announce && text) announceTimer = setTimeout(function () { TF.announce(text); }, 500);
    }
    function setActive(i) {
      var opts = listbox.querySelectorAll("[role=option]");
      active = opts.length ? (i + opts.length) % opts.length : -1;
      Array.prototype.forEach.call(opts, function (o, j) { o.setAttribute("aria-selected", j === active ? "true" : "false"); });
      if (active >= 0) {
        input.setAttribute("aria-activedescendant", opts[active].id);
        if (opts[active].scrollIntoView) opts[active].scrollIntoView({ block: "nearest" });
      } else input.removeAttribute("aria-activedescendant");
    }
    function close() {
      open = false;
      popup.hidden = true;
      input.setAttribute("aria-expanded", "false");
      setActive(-1);
    }
    function option(o, i) {
      var li = doc.createElement("li");
      li.id = listbox.id + "-" + i;
      li.className = "search-option";
      li.setAttribute("role", "option");
      li.setAttribute("aria-selected", "false");
      var name = doc.createElement("span");
      name.className = "search-option-name";
      name.textContent = o.recent ? o.item.q : o.item.name;
      li.appendChild(name);
      var meta = o.recent ? typeLabel(o.type) : o.item.words;
      if (meta) {
        var m = doc.createElement("span");
        m.className = "search-option-meta";
        m.textContent = meta;
        li.appendChild(m);
      }
      return li;
    }
    /** Rebuilds the popup from the field: recent searches when empty, else suggestions or the list's state. */
    function render() {
      var focused = doc.activeElement === input || popup.contains(doc.activeElement);
      var text = clean(input.value), e = entry(), statusText = "", retry = null, recent = false;
      shown = [];
      if (!text) {
        shown = readRecents().map(function (r) { return { type: r.type, item: r, recent: true }; });
        recent = shown.length > 0;
      } else if (e.list) {
        var L = lists[e.list];
        if (L && L.items) {
          shown = filter(L.items, text).map(function (item) { return { type: e.type, item: item }; });
          if (!shown.length) statusText = t("js.searchBox.noMatches");
          announce(shown.length ? t("js.searchBox.count", { n: shown.length }) : statusText);
        } else if (L && L.error) {
          statusText = t(Object.prototype.hasOwnProperty.call(ERR, L.error.kind) ? ERR[L.error.kind] : "js.errNetwork");
          retry = e.list;
          announce(statusText);
        } else if (L && L.promise) statusText = t("js.loading");
      }
      listbox.textContent = "";
      shown.forEach(function (o, i) { listbox.appendChild(option(o, i)); });
      listbox.hidden = !shown.length;
      listbox.setAttribute("aria-label", recent ? t("js.searchBox.recent") : t("js.searchBox.suggestions"));
      title.hidden = !recent;
      clearBtn.hidden = !recent;
      status.textContent = "";
      status.hidden = !statusText;
      if (statusText) {
        var s = doc.createElement("span");
        s.textContent = statusText;
        status.appendChild(s);
        if (retry) {
          var b = doc.createElement("button");
          b.type = "button";
          b.className = "btn search-retry";
          b.textContent = t("js.retry");
          b.addEventListener("click", function () { input.focus(); ensureList(retry); render(); });
          status.appendChild(b);
        }
      }
      open = focused && (shown.length > 0 || !!statusText);
      popup.hidden = !open;
      input.setAttribute("aria-expanded", open ? "true" : "false");
      active = -1;
      input.removeAttribute("aria-activedescendant");
    }

    function finish(type, item) {
      var hash = routeFor(type, item);
      if (!hash) { input.focus(); return; }
      if (byType[type].browse !== hash.slice(2)) remember(type, item);   // routeFor returned a route: type is known
      close();
      go(hash);
    }
    function choose(i) {
      var o = shown[i];
      if (!o) return;
      if (o.recent) { select.value = o.type; updateType(); }
      input.value = o.recent ? o.item.q : o.item.name;
      finish(o.type, o.item);
    }
    /** Enter without an active option: the exact name; for a list type the exact item, else the first suggestion. */
    function submitTyped() {
      var e = entry(), text = clean(input.value);
      if (e.exact) { if (text) finish(e.type, { q: text }); else input.focus(); return; }
      if (!text) { finish(e.type, {}); return; }
      (e.list ? ensureList(e.list) : Promise.resolve(null)).then(function (items) {
        var item = items && (resolve(items, text) || filter(items, text)[0]);
        finish(e.type, item || { q: text });
      });
    }

    form.addEventListener("submit", function (ev) { ev.preventDefault(); submitTyped(); });
    select.addEventListener("change", function () { updateType(); close(); });
    input.addEventListener("focus", function () { if (entry().list) ensureList(entry().list); render(); });
    input.addEventListener("input", function () { if (entry().list) ensureList(entry().list); render(); });
    input.addEventListener("keydown", function (ev) {
      if (ev.key === "ArrowDown" || ev.key === "ArrowUp") {
        ev.preventDefault();
        if (!open) render();
        if (open) setActive(ev.key === "ArrowDown" ? active + 1 : active < 0 ? -1 : active - 1);
      } else if (ev.key === "Enter") {
        if (open && active >= 0) { ev.preventDefault(); choose(active); }
      } else if (ev.key === "Escape") {
        if (open) { ev.preventDefault(); ev.stopPropagation(); close(); }
        else if (input.value) { ev.preventDefault(); input.value = ""; }
      } else if (ev.key === "Tab") {
        // Tab moves into an open popup's buttons ("Clear Recent Searches", "Try again"; they follow the field in the
        // DOM), so they are reachable by keyboard; otherwise Tab closes the popup.
        if (!(open && !ev.shiftKey && popupButtons().length)) close();
      }
    });
    function popupButtons() {
      return Array.prototype.filter.call(popup.querySelectorAll("button"), function (b) { return !b.disabled && b.getClientRects().length > 0; });
    }
    // Escape on a popup button closes the popup and returns to the field.
    popup.addEventListener("keydown", function (ev) {
      if (ev.key === "Escape") { ev.preventDefault(); ev.stopPropagation(); input.focus(); close(); }
    });
    // A press anywhere in the popup keeps the focus in the field (aria-activedescendant pattern). Safari and Firefox do not
    // focus a clicked button, so without this the field would blur and close the popup before the click arrives.
    popup.addEventListener("mousedown", function (ev) { ev.preventDefault(); });
    listbox.addEventListener("click", function (ev) {
      var li = ev.target.closest && ev.target.closest("[role=option]");
      if (li) choose(Array.prototype.indexOf.call(listbox.children, li));
    });
    clearBtn.addEventListener("click", function () {
      clearRecents();
      input.focus();
      render();
      if (TF.announce) TF.announce(t("js.searchBox.cleared"));
    });
    form.addEventListener("focusout", function (ev) {
      if (!ev.relatedTarget || !popup.contains(ev.relatedTarget)) {
        if (ev.relatedTarget !== input) close();
      }
    });
    doc.addEventListener("click", function (ev) { if (open && !form.contains(ev.target)) close(); });

    // On the Tibia page the header shows the character or guild being viewed, so a misspelt name can be corrected.
    function syncFromRoute() {
      if (doc.activeElement === input) return;
      var m = /^#\/(character|guild)\/([^/]+)\/?$/.exec(win.location.hash);
      if (!m || !known(m[1])) return;
      try { input.value = decodeURIComponent(m[2]); } catch (e) { return; }
      select.value = m[1];
      updateType();
    }
    if (doc.body.getAttribute("data-page") === "tibia") {
      syncFromRoute();
      win.addEventListener("hashchange", syncFromRoute);
    }
    updateType();
  }

  return {
    normalize: normalize, filter: filter, resolve: resolve, itemsFrom: itemsFrom, routeFor: routeFor, types: entries.map(function (e) { return e.type; }),
    recents: { read: readRecents, add: addRecent, clear: clearRecents, key: KEY, max: MAX },
    mount: mount
  };
});
