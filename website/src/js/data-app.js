/*
 * Tibia data views (tibia/ page) and the home page's live sections, built on TFData (tibiadata.js) and TFSanitize.
 * Routes live in the URL hash (#/character/Name, #/world/Antica, ...) so every view survives a reload and works on
 * GitHub Pages without server routing; the route list comes from registry.json (TF_SITE.dataRoutes).
 * Everything from the API is written with textContent / attributes only; news HTML goes through TFSanitize.
 */
(function () {
  "use strict";
  var TF = window.TF, API = window.TFData, SAN = window.TFSanitize;
  if (!TF || !API) return;
  var t = TF.t, SITE = TF.site || {}, LOCALE = TF.intl || "en-US";
  var APIcfg = SITE.api || {};
  var nf = new Intl.NumberFormat(LOCALE);

  // ---------------------------------------------------------------- formatting
  function num(n) { return n == null || n === "" ? "–" : nf.format(n); }
  function parseDate(s) {
    if (!s) return null;
    var d = /^\d{4}-\d{2}-\d{2}$/.test(s) ? new Date(s + "T12:00:00Z") : /^\d{4}-\d{2}$/.test(s) ? new Date(s + "-15T12:00:00Z") : new Date(s);
    return isNaN(d.getTime()) ? null : d;
  }
  function fmtDate(s, withTime) {
    var d = parseDate(s);
    if (!d) return s || "–";
    if (/^\d{4}-\d{2}$/.test(s)) return new Intl.DateTimeFormat(LOCALE, { year: "numeric", month: "long", timeZone: "UTC" }).format(d);
    var o = withTime ? { dateStyle: "medium", timeStyle: "short" } : { dateStyle: "medium", timeZone: "UTC" };
    return new Intl.DateTimeFormat(LOCALE, o).format(d);
  }
  function fmtTime(ms) { return new Intl.DateTimeFormat(LOCALE, { timeStyle: "short" }).format(new Date(ms)); }
  function yesNo(v) { return v ? t("js.yes") : t("js.no"); }
  // A no-break space shown as "Â " by the source (double-encoded) is a plain space.
  function tidy(s) { return String(s == null ? "" : s).replace(/Â /g, " "); }

  // ---------------------------------------------------------------- DOM helpers (text only, never innerHTML with data)
  function h(tag, props) {
    var el = document.createElement(tag);
    if (props) Object.keys(props).forEach(function (k) {
      var v = props[k];
      if (v == null || v === false) return;
      if (k === "class") el.className = v;
      else if (k === "text") el.textContent = v;
      else if (k.slice(0, 2) === "on") el.addEventListener(k.slice(2), v);
      else el.setAttribute(k, v === true ? "" : v);
    });
    for (var i = 2; i < arguments.length; i++) append(el, arguments[i]);
    return el;
  }
  function append(el, c) {
    if (c == null || c === false) return;
    if (Array.isArray(c)) c.forEach(function (x) { append(el, x); });
    else el.appendChild(typeof c === "object" ? c : document.createTextNode(String(c)));
  }
  function route(path) { return "#/" + path.split("/").map(function (p) { return encodeURIComponent(p); }).join("/"); }
  function link(path, text, props) { return h("a", Object.assign({ href: route(path) }, props || {}), text); }
  // A Lucide icon from the inline sprite (#i-<name>); decorative.
  function icon(name, cls) {
    var NS = "http://www.w3.org/2000/svg", svg = document.createElementNS(NS, "svg"), use = document.createElementNS(NS, "use");
    svg.setAttribute("class", "ic" + (cls ? " " + cls : "")); svg.setAttribute("aria-hidden", "true");
    use.setAttribute("href", "#i-" + name);
    svg.appendChild(use);
    return svg;
  }
  function ext(href, text, cls) {
    // Text links get the external-link mark from CSS; buttons carry the icon themselves.
    return h("a", { href: href, target: "_blank", rel: "noopener noreferrer", class: cls || null },
      cls && /btn/.test(cls) ? icon("external-link") : null, text, h("span", { class: "sr-only", text: " " + t("js.newTab") }));
  }
  function img(src, alt, cls, size) {
    if (!src || !/^https:\/\//.test(src)) return null;
    return h("img", { src: src, alt: alt || "", loading: "lazy", decoding: "async", referrerpolicy: "no-referrer", class: cls || null, width: size || null, height: size || null });
  }
  function dl(pairs) {
    return h("dl", { class: "facts" }, pairs.filter(function (p) { return p && p[1] != null && p[1] !== "" && p[1] !== false; }).map(function (p) {
      return h("div", null, h("dt", { text: p[0] }), h("dd", null, p[1]));
    }));
  }
  function section(title, body, id) { return h("section", { class: "data-section", "aria-labelledby": id || null }, h("h2", { id: id || null, text: title }), body); }
  function copyButton(text, label) {
    return h("button", { type: "button", class: "btn btn-small btn-quiet", "data-copy": text }, icon("copy"), h("span", { class: "copy-label", text: label || t("js.copy") }));
  }
  function tibiaUrl(json, fallback) {
    var urls = json && json.information && json.information.tibia_urls;
    return urls && urls.length && /^https:\/\/www\.tibia\.com\//.test(urls[0]) ? urls[0] : fallback;
  }
  function tibiaCharacter(name) { return "https://www.tibia.com/community/?name=" + encodeURIComponent(name).replace(/%20/g, "+"); }

  // ---------------------------------------------------------------- states: loading, error, empty, source line
  function loading(label) {
    var box = h("div", { class: "loading", role: "status" }, h("span", { class: "sr-only", text: label || t("js.loading") }));
    for (var i = 0; i < 6; i++) box.appendChild(h("div", { class: "skeleton", "aria-hidden": "true", style: "--w:" + (60 + (i * 37) % 40) + "%" }));
    return box;
  }
  function errorBox(error, retry) {
    var kind = error && error.kind || "network";
    var msg = { notfound: "js.errNotFound", invalid: "js.errInvalid", network: "js.errNetwork", server: "js.errServer",
                ratelimit: "js.errRateLimit", malformed: "js.errMalformed" }[kind] || "js.errNetwork";
    return h("div", { class: "notice notice-error", role: "alert" },
      h("p", { class: "notice-title", text: t(kind === "notfound" ? "js.errNotFoundTitle" : "js.errTitle") }),
      h("p", { text: t(msg) }),
      retry && kind !== "notfound" && kind !== "invalid" ? h("button", { type: "button", class: "btn btn-small", onclick: retry, text: t("js.retry") }) : null);
  }
  function empty(text) { return h("p", { class: "notice notice-empty", text: text || t("js.empty") }); }
  function sourceLine(result, original) {
    return h("p", { class: "source-line" },
      t("js.dataBy") + " ", ext("https://tibiadata.com/", "TibiaData"), " · ",
      ext(original || "https://www.tibia.com/", t("js.original")), " · ",
      h("span", { text: t("js.updated", { time: fmtTime(result.fetchedAt) }) }),
      result.stale ? h("strong", { class: "stale", text: " " + t("js.stale") }) : null);
  }

  // ---------------------------------------------------------------- sortable, filterable table (stacks into cards on phones)
  function wikiCell(v) { return String(v == null ? "" : v).replace(/\|/g, "{{!}}").replace(/\n/g, " "); }
  function csvCell(v) {
    var s = String(v == null ? "" : v);
    if (/^[=+\-@\t\r]/.test(s)) s = "'" + s;           // no spreadsheet formulas from data
    return /[",\n;]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s;
  }
  // ---------------------------------------------------------------- sorting: one comparator for every table
  // Types: number (also "1,234 gp", "45%"), date (ISO), version ("1.10" after "1.9"), text (locale collation, numeric).
  // Empty values always sort last; ties keep the original order; the data itself is never changed.
  var collator = new Intl.Collator(LOCALE, { numeric: true, sensitivity: "base" });
  function isEmpty(v) { return v == null || v === "" || v === "–" || (typeof v === "number" && isNaN(v)); }
  function toNumber(v) {
    if (typeof v === "number") return v;
    if (typeof v === "boolean") return v ? 1 : 0;
    var m = String(v).replace(/[\s\u00a0\u202f']/g, "").match(/-?\d[\d.,]*/);
    if (!m) return NaN;
    var n = m[0], dec = Math.max(n.lastIndexOf("."), n.lastIndexOf(","));
    if (dec >= 0) {
      var both = n.indexOf(".") >= 0 && n.indexOf(",") >= 0, after = n.length - dec - 1;
      n = both || after !== 3 ? n.slice(0, dec).replace(/[.,]/g, "") + "." + n.slice(dec + 1) : n.replace(/[.,]/g, "");
    }
    return parseFloat(n);
  }
  function versionParts(v) { return String(v).split(/[^\d]+/).filter(Boolean).map(Number); }
  function detectType(values) {
    var list = values.filter(function (v) { return !isEmpty(v); });
    if (!list.length) return "text";
    if (list.every(function (v) { return typeof v === "number"; })) return "number";
    var str = list.map(String);
    if (str.every(function (v) { return /^\d{4}-\d{2}-\d{2}/.test(v); })) return "date";
    if (str.every(function (v) { return /^v?\d+(\.\d+){2,3}$/.test(v); })) return "version";
    if (str.every(function (v) { return /^[^\d-]{0,3}-?\d[\d.,\s\u00a0]*\s*(%|gp|kk|k|tc)?$/i.test(v.trim()); })) return "number";
    return "text";
  }
  function compareValues(x, y, type) {
    if (type === "number") return toNumber(x) - toNumber(y);
    if (type === "date") return (Date.parse(x) || 0) - (Date.parse(y) || 0);
    if (type === "version") {
      var a = versionParts(x), b = versionParts(y);
      for (var i = 0; i < Math.max(a.length, b.length); i++) { var d = (a[i] || 0) - (b[i] || 0); if (d) return d; }
      return 0;
    }
    return collator.compare(String(x), String(y));
  }
  function sortIcon(dir) { return icon(dir > 0 ? "arrow-up" : dir < 0 ? "arrow-down" : "arrow-up-down", "sort-icon"); }
  function announce(text) { var live = document.getElementById("live-status"); if (live) { live.textContent = ""; setTimeout(function () { live.textContent = text; }, 30); } }

  function table(opts) {
    var cols = opts.columns, all = opts.rows || [], pageSize = opts.pageSize || 100, shown = pageSize;
    var sortCol = opts.sort ? cols.findIndex(function (c) { return c.key === opts.sort; }) : -1, sortDir = opts.sortDir || 1;
    var filter = "", extraFilter = null;
    var wrap = h("div", { class: "table-block" });
    var count = h("p", { class: "table-count", "aria-live": "polite" });
    var search = opts.filter === false ? null : h("input", { type: "search", class: "input", placeholder: opts.filterPlaceholder || t("js.filter"), "aria-label": opts.filterPlaceholder || t("js.filter"),
      oninput: function () { filter = search.value.trim().toLowerCase(); shown = pageSize; draw(); } });
    function value(c, r) { return c.get ? c.get(r) : r[c.key]; }
    function sortValue(c, r) { return c.sortValue ? c.sortValue(r) : value(c, r); }
    var types = {};
    function colType(c) {
      if (!types[c.key]) types[c.key] = c.type || (c.num ? "number" : detectType(all.map(function (r) { return sortValue(c, r); })));
      return types[c.key];
    }
    function firstDir(c) { return c.firstDir || (colType(c) === "number" || colType(c) === "date" ? -1 : 1); }
    function rows() {
      var list = all.filter(function (r) {
        if (extraFilter && !extraFilter(r)) return false;
        if (!filter) return true;
        return cols.some(function (c) { var v = value(c, r); return v != null && String(v).toLowerCase().indexOf(filter) >= 0; });
      });
      if (sortCol >= 0) {
        var c = cols[sortCol], type = colType(c);
        list = list.map(function (r, i) { return [r, i]; }).sort(function (A, B) {
          var x = sortValue(c, A[0]), y = sortValue(c, B[0]), ex = isEmpty(x), ey = isEmpty(y);
          if (ex || ey) return ex === ey ? A[1] - B[1] : ex ? 1 : -1;
          return compareValues(x, y, type) * sortDir || A[1] - B[1];
        }).map(function (P) { return P[0]; });
      }
      return list;
    }
    var thead = h("thead"), tbody = h("tbody");
    var tableEl = h("table", { class: "data-table" + (opts.compact ? " compact" : "") }, opts.caption ? h("caption", { class: "sr-only", text: opts.caption }) : null, thead, tbody);
    var more = h("button", { type: "button", class: "btn btn-small more", onclick: function () { shown += pageSize; draw(); } });
    function head() {
      thead.textContent = "";
      var tr = h("tr");
      cols.forEach(function (c, i) {
        var on = i === sortCol;
        var th = h("th", { scope: "col", class: (c.num ? "num " : "") + (on ? "sorted" : ""), "aria-sort": on ? (sortDir > 0 ? "ascending" : "descending") : (c.sort === false ? null : "none") });
        if (c.sort === false) th.textContent = c.label;
        else th.appendChild(h("button", { type: "button", class: "sort", title: t("js.sortBy", { column: c.label }), onclick: function () { sortBy(i, sortCol === i ? -sortDir : firstDir(c)); } },
          h("span", { text: c.label }), sortIcon(on ? sortDir : 0)));
        tr.appendChild(th);
      });
      thead.appendChild(tr);
      syncBar();
    }
    function sortBy(i, dir) {
      sortCol = i; sortDir = dir;
      head(); draw();
      announce(t(dir > 0 ? "js.sortedAsc" : "js.sortedDesc", { column: cols[i].label }));
    }
    // Phones show rows as cards without a header row, so the same sorting is offered as a select + direction button.
    var barSelect = h("select", { class: "input", "aria-label": t("js.sortByLabel"), onchange: function () { var i = +barSelect.value; sortBy(i, firstDir(cols[i])); } },
      [h("option", { value: "-1", text: t("js.sortNone"), disabled: true })].concat(cols.map(function (c, i) { return c.sort === false ? null : h("option", { value: String(i), text: c.label }); })));
    var barDir = h("button", { type: "button", class: "btn btn-small", onclick: function () { if (sortCol >= 0) sortBy(sortCol, -sortDir); } });
    var bar = h("div", { class: "table-sortbar" }, h("label", null, h("span", { class: "field-label", text: t("js.sortByLabel") }), barSelect), barDir);
    function syncBar() {
      barSelect.value = String(sortCol);
      barDir.textContent = "";
      barDir.appendChild(sortIcon(sortDir));
      barDir.appendChild(h("span", { text: t(sortDir > 0 ? "js.ascending" : "js.descending") }));
      barDir.disabled = sortCol < 0;
    }
    function draw() {
      var list = rows();
      tbody.textContent = "";
      list.slice(0, shown).forEach(function (r) {
        var tr = h("tr");
        cols.forEach(function (c, i) {
          var content = c.render ? c.render(r) : value(c, r);
          var td = h(i === 0 ? "th" : "td", { class: c.num ? "num" : null, "data-label": c.label, scope: i === 0 ? "row" : null });
          append(td, content == null || content === "" ? "–" : (typeof content === "number" ? num(content) : content));
          tr.appendChild(td);
        });
        tbody.appendChild(tr);
      });
      count.textContent = t("js.showing", { shown: Math.min(shown, list.length), total: list.length });
      more.hidden = list.length <= shown;
      more.textContent = t("js.showMore", { n: Math.min(pageSize, list.length - shown) });
      if (!list.length) tbody.appendChild(h("tr", null, h("td", { colspan: cols.length, class: "empty-row", text: t("js.noMatches") })));
    }
    function asWiki() {
      var list = rows(), out = ['{| class="wikitable sortable"', "! " + cols.map(function (c) { return wikiCell(c.label); }).join(" !! ")];
      list.forEach(function (r) { out.push("|-"); out.push("| " + cols.map(function (c) { return wikiCell(c.wiki ? c.wiki(r) : value(c, r)); }).join(" || ")); });
      out.push("|}");
      return out.join("\n");
    }
    function asCsv() {
      return [cols.map(function (c) { return csvCell(c.label); }).join(",")].concat(rows().map(function (r) {
        return cols.map(function (c) { return csvCell(c.wiki ? c.wiki(r) : value(c, r)); }).join(",");
      })).join("\r\n");
    }
    function copyAs(fn) {
      return function (e) { var b = e.currentTarget; TF.copy(fn()).then(function () { TF.confirmCopy(b, true); }, function () { TF.confirmCopy(b, false); }); };
    }
    var tools = h("div", { class: "table-tools" }, search,
      h("div", { class: "table-actions" },
        h("button", { type: "button", class: "btn btn-small btn-quiet", onclick: copyAs(asWiki), title: t("js.copyWikiHint") }, icon("copy"), h("span", { class: "copy-label", text: t("js.copyWiki") })),
        h("button", { type: "button", class: "btn btn-small btn-quiet", onclick: copyAs(asCsv) }, icon("copy"), h("span", { class: "copy-label", text: t("js.copyCsv") }))));
    wrap.appendChild(tools);
    if (opts.controls) wrap.appendChild(opts.controls);
    wrap.appendChild(bar);
    wrap.appendChild(count);
    wrap.appendChild(h("div", { class: "table-scroll" + (all.length > 25 ? " tall" : ""), tabindex: "0", role: "region", "aria-label": opts.caption || t("js.table") }, tableEl));
    wrap.appendChild(more);
    head(); draw();
    wrap.setFilter = function (fn) { extraFilter = fn; shown = pageSize; draw(); };
    wrap.asWiki = asWiki;
    return wrap;
  }

  // ---------------------------------------------------------------- shared pieces
  var WORLD_KEY = "tf-world";
  function rememberWorld(w) { try { localStorage.setItem(WORLD_KEY, w); } catch (e) {} }
  function lastWorld() { try { return localStorage.getItem(WORLD_KEY) || (SITE.fixtures && SITE.fixtures.world) || "Antica"; } catch (e) { return "Antica"; } }
  function worldSelect(current, onChange, signal, extra) {
    var sel = h("select", { class: "input", "aria-label": t("js.world"), onchange: function () { onChange(sel.value); } });
    (extra || []).forEach(function (o) { sel.appendChild(h("option", { value: o[0], text: o[1] })); });
    sel.appendChild(h("option", { value: current, text: current, selected: true }));
    API.get("worlds", [], { signal: signal }).then(function (r) {
      var names = (r.data.worlds.regular_worlds || []).concat(r.data.worlds.tournament_worlds || []).map(function (w) { return w.name; }).sort();
      sel.textContent = "";
      (extra || []).forEach(function (o) { sel.appendChild(h("option", { value: o[0], text: o[1], selected: o[0] === current })); });
      names.forEach(function (n) { sel.appendChild(h("option", { value: n, text: n, selected: n === current })); });
    }, function () { /* keep the current world only */ });
    return h("label", { class: "field" }, h("span", { class: "field-label", text: t("js.world") }), sel);
  }
  function selectField(label, options, current, onChange) {
    var sel = h("select", { class: "input", onchange: function () { onChange(sel.value); } },
      options.map(function (o) { return h("option", { value: o[0], text: o[1], selected: String(o[0]) === String(current) }); }));
    return h("label", { class: "field" }, h("span", { class: "field-label", text: label }), sel);
  }
  function vocationLabel(v) { return t("js.voc." + v); }
  function categoryLabel(c) { return t("js.hs." + c); }
  function statusBadge(online) { return h("span", { class: "badge " + (online ? "badge-online" : "badge-offline"), text: online ? t("js.online") : t("js.offline") }); }
  function characterSearch(value, autofocus) {
    var input = h("input", { type: "search", name: "character", class: "input input-large", value: value || "", placeholder: t("js.characterPlaceholder"),
      "aria-label": t("js.characterLabel"), autocomplete: "off", autocapitalize: "words", spellcheck: "false", maxlength: "29", autofocus: autofocus || null });
    return h("form", { class: "search-row", role: "search", onsubmit: function (e) {
      e.preventDefault();
      var name = input.value.replace(/\s+/g, " ").trim();
      if (name) location.hash = route("character/" + name);
      else input.focus();
    } }, input, h("button", { type: "submit", class: "btn btn-primary", text: t("js.search") }));
  }

  // ---------------------------------------------------------------- views
  var V = {};

  V.hub = function (p, ctx) {
    ctx.title("");
    var boosted = h("div", { class: "boosted" }, loading());
    var online = h("p", { class: "lead-number" });
    // Every view, grouped by what a visitor wants to do; each card has the site's pixel icon.
    var groups = [
      ["js.groupPlayers", [["character", "js.toolCharacter", "js.toolCharacterText", "character"], ["guilds/" + lastWorld(), "js.toolGuilds", "js.toolGuildsText", "guild"]]],
      ["js.groupWorlds", [["worlds", "js.toolWorlds", "js.toolWorldsText", "world"], ["highscores/" + lastWorld() + "/experience/all/1", "js.toolHighscores", "js.toolHighscoresText", "highscores"],
        ["killstatistics/" + lastWorld(), "js.toolKills", "js.toolKillsText", "killstatistics"], ["houses/" + lastWorld() + "/Thais", "js.toolHouses", "js.toolHousesText", "houses"]]],
      ["js.groupReference", [["creatures", "js.toolCreatures", "js.toolCreaturesText", "creatures"], ["bosses", "js.toolBosses", "js.toolBossesText", "bosses"],
        ["spells", "js.toolSpells", "js.toolSpellsText", "spells"]]],
      ["js.groupNews", [["news", "js.toolNews", "js.toolNewsText", "news"], ["news/archive/90", "js.toolArchive", "js.toolArchiveText", "news"],
        ["fansites", "js.toolFansites", "js.toolFansitesText", "fansites"]]]
    ];
    var grid = h("div", null, groups.map(function (g, gi) {
      return h("div", { class: "tool-group" }, h("h3", { id: "hub-group-" + gi, text: t(g[0]) }),
        h("ul", { class: "tool-grid", "aria-labelledby": "hub-group-" + gi }, g[1].map(function (x) {
          var a = link(x[0], null, { class: "tool-card" });
          a.appendChild(pixelTile(x[3]));
          a.appendChild(h("span", { class: "tool-title", text: t(x[1]) }));
          a.appendChild(h("span", { class: "tool-text", text: t(x[2]) }));
          return h("li", null, a);
        })));
    }));
    ctx.render([
      section(t("js.characterLabel"), characterSearch("", false), "hub-search"),
      online,
      section(t("js.boostedTitle"), boosted, "hub-boosted"),
      section(t("js.allTools"), grid, "hub-tools")
    ]);
    API.get("worlds", [], { signal: ctx.signal }).then(function (r) {
      online.textContent = t("js.playersOnlineAll", { n: r.data.worlds.players_online });
    }, function () {});
    renderBoosted(boosted, ctx.signal);
  };

  // Lucide icons (inline sprite #i-*) in their tile; decorative, so hidden from assistive technology.
  function pixelTile(name) {
    var ICON = { character: "user", guild: "users", world: "globe", highscores: "trophy", killstatistics: "skull", houses: "house",
      creatures: "paw-print", bosses: "crown", spells: "wand-sparkles", news: "newspaper", fansites: "flag" };
    var tile = h("span", { class: "icon-tile", "aria-hidden": "true" });
    tile.appendChild(icon(ICON[name] || name));
    return tile;
  }

  function renderOnline(target, signal) {
    API.get("worlds", [], { signal: signal }).then(function (r) {
      target.textContent = t("js.playersOnlineAll", { n: r.data.worlds.players_online });
    }, function () { target.textContent = ""; });
  }

  // Today's boosted creature and boss (creatures and boostablebosses answers). An entry counts only with a non-empty name (and,
  // for the creature, the race its page is named by); anything else is treated as "TibiaData cannot tell".
  var BOOSTED = {
    creature: { endpoint: "creatures", label: "js.boostedCreature", pick: function (d) { return d.creatures && d.creatures.boosted; },
                path: function (x) { return "creature/" + x.race; }, valid: function (x) { return typeof x.race === "string" && x.race !== ""; } },
    boss: { endpoint: "boostablebosses", label: "js.boostedBoss", pick: function (d) { return d.boostable_bosses && d.boostable_bosses.boosted; },
            path: function () { return "bosses"; }, valid: function () { return true; } }
  };
  function boostedOf(kind, r) {
    var x = r && r.data ? BOOSTED[kind].pick(r.data) : null;
    return x && typeof x.name === "string" && x.name.trim() !== "" && BOOSTED[kind].valid(x) ? x : null;
  }
  function boostCard(kind, x, size) {
    return h("a", { class: "boost-card", href: (SITE.paths && SITE.paths.tibiaHash || "") + route(BOOSTED[kind].path(x)) },
      img(x.image_url, "", "boost-img", size), h("span", { class: "boost-kind", text: t(BOOSTED[kind].label) }), h("span", { class: "boost-name", text: x.name }));
  }

  // target: the tibia/ hub's box (both cards and the source line, or an error with Retry), or the home page's Today strip, whose
  // .today-cell[data-boosted] cells are filled one by one; a cell whose answer fails or is empty is hidden, never guessed.
  function renderBoosted(target, signal) {
    var cells = target.querySelectorAll(".today-cell[data-boosted]");
    if (cells.length) {
      Array.prototype.forEach.call(cells, function (cell) {
        var kind = cell.getAttribute("data-boosted");
        function hide() { cell.hidden = true; cell.removeAttribute("aria-busy"); }
        if (!BOOSTED.hasOwnProperty(kind)) return hide();
        API.get(BOOSTED[kind].endpoint, [], { signal: signal }).then(function (r) {
          var x = boostedOf(kind, r);
          if (!x) return hide();
          cell.textContent = "";
          cell.appendChild(boostCard(kind, x, 48));
          cell.removeAttribute("aria-busy");
        }, function (e) { if (e.kind !== "aborted") hide(); });
      });
      return;
    }
    Promise.all([API.get("creatures", [], { signal: signal }), API.get("boostablebosses", [], { signal: signal })]).then(function (res) {
      var c = boostedOf("creature", res[0]), b = boostedOf("boss", res[1]);
      target.textContent = "";
      if (c) target.appendChild(boostCard("creature", c, 64));
      if (b) target.appendChild(boostCard("boss", b, 64));
      target.appendChild(sourceLine(res[0], "https://www.tibia.com/library/?subtopic=boostablebosses"));
    }, function (e) { if (e.kind !== "aborted") { target.textContent = ""; target.appendChild(errorBox(e, function () { renderBoosted(target, signal); })); } });
  }

  V["character-search"] = function (p, ctx) {
    ctx.title(t("js.toolCharacter"));
    ctx.render([h("h2", { class: "view-title", text: t("js.toolCharacter") }), h("p", { class: "muted", text: t("js.characterHelp") }), characterSearch("", true)]);
  };

  V.character = function (p, ctx) {
    var name = p.name;
    ctx.title(name);
    return ctx.load("character", [name], function (r) {
      var all = r.data.character, c = all.character;
      ctx.title(c.name);
      var head = h("div", { class: "view-head" },
        h("div", null, h("h2", { class: "view-title", text: c.name }),
          h("p", { class: "subtitle" }, t("js.levelVocation", { level: num(c.level), vocation: c.vocation }), " · ", link("world/" + c.world, c.world))),
        h("div", { class: "head-actions" }, copyButton(c.name, t("js.copyName")),
          copyButton(characterWiki(all), t("js.copyWiki")), ext(tibiaUrl(r.data, tibiaCharacter(c.name)), t("js.openTibia"), "btn btn-small")));
      var info = dl([
        [t("js.name"), c.name], [t("js.formerNames"), (c.former_names || []).join(", ")], [t("js.title"), c.title],
        [t("js.sexLabel"), c.sex ? t("js.sex." + c.sex) : null], [t("js.vocation"), c.vocation], [t("js.level"), num(c.level)],
        [t("js.achievementPoints"), num(c.achievement_points)], [t("js.world"), link("world/" + c.world, c.world)],
        [t("js.formerWorlds"), (c.former_worlds || []).join(", ")], [t("js.residence"), c.residence],
        [t("js.marriedTo"), c.married_to ? link("character/" + c.married_to, c.married_to) : null],
        [t("js.houses"), (c.houses || []).length ? h("ul", { class: "plain" }, c.houses.map(function (x) {
          return h("li", null, link("house/" + c.world + "/" + x.houseid, x.name), " (" + x.town + ", " + t("js.paidUntil", { date: fmtDate(x.paid) }) + ")");
        })) : null],
        [t("js.guild"), c.guild && c.guild.name ? h("span", null, t("js.guildRank", { rank: c.guild.rank }) + " ", link("guild/" + c.guild.name, c.guild.name)) : null],
        [t("js.position"), c.position], [t("js.lastLogin"), c.last_login ? fmtDate(c.last_login, true) : null],
        [t("js.accountStatus"), c.account_status], [t("js.unlockedTitles"), c.unlocked_titles ? num(c.unlocked_titles) : null],
        [t("js.traded"), c.traded ? t("js.yes") : null], [t("js.deletionDate"), c.deletion_date ? fmtDate(c.deletion_date, true) : null]
      ]);
      var parts = [head, h("div", { class: "now-label", text: t("js.currentInfo") }), info];
      if (c.comment) parts.push(section(t("js.comment"), h("p", { class: "pre", text: c.comment })));
      var acc = all.account_information;
      if (acc && (acc.created || acc.loyalty_title || acc.position)) parts.push(section(t("js.accountInfo"), dl([
        [t("js.created"), acc.created ? fmtDate(acc.created, true) : null], [t("js.loyaltyTitle"), acc.loyalty_title], [t("js.position"), acc.position]])));
      var deaths = all.deaths || [];
      parts.push(section(t("js.deaths"), deaths.length ? h("ol", { class: "timeline" }, deaths.map(function (d) {
        return h("li", null, h("time", { datetime: d.time, text: fmtDate(d.time, true) }),
          h("p", null, t("js.diedAtLevel", { level: num(d.level) }) + " ", killerList(d.killers), d.assists && d.assists.length ? h("span", { class: "muted" }, " " + t("js.assistedBy") + " ", killerList(d.assists)) : null));
      })) : empty(t("js.noDeaths")), "deaths"));
      if (all.deaths_truncated) parts.push(h("p", { class: "muted", text: t("js.deathsTruncated") }));
      var ach = all.achievements || [];
      parts.push(section(t("js.achievements"), ach.length ? h("ul", { class: "chips" }, ach.map(function (a) {
        return h("li", { class: "chip" }, a.name, h("span", { class: "grade", "aria-label": t("js.grade", { n: a.grade }), text: " " + "★".repeat(Math.max(0, Math.min(5, a.grade || 0))) }), a.secret ? h("span", { class: "muted", text: " (" + t("js.secret") + ")" }) : null);
      })) : empty(t("js.noAchievements"))));
      var badges = all.account_badges || [];
      if (badges.length) parts.push(section(t("js.badges"), h("ul", { class: "badges" }, badges.map(function (b) {
        return h("li", { title: b.description }, img(b.icon_url, "", null, 32), h("span", null, h("strong", { text: b.name }), h("span", { class: "muted", text: b.description })));
      }))));
      var others = all.other_characters || [];
      if (others.length) parts.push(section(t("js.otherCharacters"), table({
        caption: t("js.otherCharacters"), filter: false, columns: [
          { key: "name", label: t("js.name"), render: function (o) { return link("character/" + o.name, o.name); } },
          { key: "world", label: t("js.world"), render: function (o) { return link("world/" + o.world, o.world); } },
          { key: "status", label: t("js.status"), render: function (o) { return statusBadge(o.status === "online"); } },
          { key: "flags", label: t("js.notes"), get: function (o) { return [o.main ? t("js.main") : "", o.deleted ? t("js.deleted") : "", o.traded ? t("js.traded") : ""].filter(Boolean).join(", "); } }
        ], rows: others })));
      parts.push(sourceLine(r, tibiaUrl(r.data, tibiaCharacter(c.name))));
      return parts;
    });
  };
  function killerList(list) {
    var out = h("span");
    (list || []).forEach(function (k, i) {
      if (i) out.appendChild(document.createTextNode(i === list.length - 1 ? " " + t("js.and") + " " : ", "));
      out.appendChild(k.player ? link("character/" + k.name, k.name) : h("span", { text: k.name }));
      if (k.summon) out.appendChild(document.createTextNode(" (" + t("js.summonOf", { name: k.summon }) + ")"));
    });
    return out;
  }
  function characterWiki(all) {
    var c = all.character;
    return factsWiki([["Name", c.name], ["Level", c.level], ["Vocation", c.vocation], ["World", c.world], ["Residence", c.residence],
      ["Guild", c.guild && c.guild.name ? c.guild.rank + " of [[" + c.guild.name + "]]" : ""], ["Achievement points", c.achievement_points],
      ["Account status", c.account_status], ["Source", "TibiaData / Tibia.com, " + new Date().toISOString().slice(0, 10)]]);
  }

  V.worlds = function (p, ctx) {
    ctx.title(t("js.toolWorlds"));
    return ctx.load("worlds", [], function (r) {
      var w = r.data.worlds;
      var cols = [
        { key: "name", label: t("js.world"), render: function (x) { return link("world/" + x.name, x.name); } },
        { key: "players_online", label: t("js.online"), num: true },
        { key: "location", label: t("js.location") }, { key: "pvp_type", label: t("js.pvpType") },
        { key: "transfer_type", label: t("js.transfer") },
        { key: "premium_only", label: t("js.premium"), get: function (x) { return yesNo(x.premium_only); } },
        { key: "battleye_protected", label: "BattlEye", type: "date", sortValue: function (x) { return x.battleye_protected ? (x.battleye_date || "1970-01-01") : null; }, get: function (x) { return x.battleye_protected ? (x.battleye_date ? fmtDate(x.battleye_date) : t("js.yes")) : t("js.no"); } },
        { key: "game_world_type", label: t("js.worldType") }
      ];
      var parts = [h("h2", { class: "view-title", text: t("js.toolWorlds") }),
        dl([[t("js.playersOnline"), num(w.players_online)], [t("js.record"), t("js.recordOn", { n: num(w.record_players), date: fmtDate(w.record_date, true) })],
            [t("js.worldCount"), num((w.regular_worlds || []).length + (w.tournament_worlds || []).length)]]),
        table({ caption: t("js.toolWorlds"), columns: cols, rows: w.regular_worlds || [], sort: "name", pageSize: 200 })];
      if ((w.tournament_worlds || []).length) parts.push(section(t("js.tournamentWorlds"), table({ caption: t("js.tournamentWorlds"), columns: cols, rows: w.tournament_worlds, filter: false })));
      parts.push(sourceLine(r, tibiaUrl(r.data, "https://www.tibia.com/community/?subtopic=worlds")));
      return parts;
    });
  };

  V.world = function (p, ctx) {
    ctx.title(p.name);
    rememberWorld(p.name);
    return ctx.load("world", [p.name], function (r) {
      var w = r.data.world;
      ctx.title(w.name);
      var players = w.online_players || [];
      return [
        h("div", { class: "view-head" }, h("div", null, h("h2", { class: "view-title", text: w.name }), h("p", { class: "subtitle", text: [w.location, w.pvp_type].filter(Boolean).join(" · ") })),
          h("div", { class: "head-actions" }, copyButton(w.name, t("js.copyName")))),
        h("nav", { class: "chips", "aria-label": t("js.moreAboutWorld") }, [
          link("highscores/" + w.name + "/experience/all/1", t("js.toolHighscores"), { class: "chip" }), link("guilds/" + w.name, t("js.toolGuilds"), { class: "chip" }),
          link("houses/" + w.name + "/Thais", t("js.toolHouses"), { class: "chip" }), link("killstatistics/" + w.name, t("js.toolKills"), { class: "chip" })]),
        dl([[t("js.status"), w.status === "online" ? t("js.online") : t("js.offline")], [t("js.playersOnline"), num(w.players_online)],
          [t("js.record"), w.record_players ? t("js.recordOn", { n: num(w.record_players), date: fmtDate(w.record_date, true) }) : null],
          [t("js.created"), fmtDate(w.creation_date)], [t("js.location"), w.location], [t("js.pvpType"), w.pvp_type], [t("js.transfer"), w.transfer_type],
          [t("js.premium"), yesNo(w.premium_only)], ["BattlEye", w.battleye_protected ? (w.battleye_date ? t("js.since", { date: fmtDate(w.battleye_date) }) : t("js.yes")) : t("js.no")],
          [t("js.worldType"), w.game_world_type], [t("js.questTitles"), (w.world_quest_titles || []).join(", ")]]),
        section(t("js.onlinePlayers"), players.length ? table({ caption: t("js.onlinePlayers"), sort: "level", sortDir: -1, columns: [
          { key: "name", label: t("js.name"), render: function (x) { return link("character/" + x.name, x.name); } },
          { key: "level", label: t("js.level"), num: true }, { key: "vocation", label: t("js.vocation") }], rows: players }) : empty(t("js.nobodyOnline"))),
        sourceLine(r, tibiaUrl(r.data, "https://www.tibia.com/community/?subtopic=worlds&world=" + encodeURIComponent(w.name)))
      ];
    });
  };

  V.guilds = function (p, ctx) {
    ctx.title(t("js.toolGuilds") + " – " + p.world);
    rememberWorld(p.world);
    var controls = h("div", { class: "controls" }, worldSelect(p.world, function (w) { location.hash = route("guilds/" + w); }, ctx.signal));
    return ctx.load("guilds", [p.world], function (r) {
      var g = r.data.guilds;
      function list(items) {
        return table({ caption: t("js.toolGuilds"), sort: "name", columns: [
          { key: "logo_url", label: t("js.logo"), sort: false, filterable: false, render: function (x) { return img(x.logo_url, "", "guild-logo", 32); }, wiki: function () { return ""; } },
          { key: "name", label: t("js.guild"), render: function (x) { return link("guild/" + x.name, x.name); } },
          { key: "description", label: t("js.description"), render: function (x) { return h("span", { class: "clamp", text: x.description || "" }); } }], rows: items || [] });
      }
      return [h("h2", { class: "view-title", text: t("js.guildsOf", { world: g.world }) }), controls,
        section(t("js.activeGuilds", { n: num((g.active || []).length) }), (g.active || []).length ? list(g.active) : empty()),
        (g.formation || []).length ? section(t("js.formingGuilds", { n: num(g.formation.length) }), list(g.formation)) : null,
        sourceLine(r, tibiaUrl(r.data, "https://www.tibia.com/community/?subtopic=guilds&world=" + encodeURIComponent(g.world)))];
    }, controls);
  };

  V.guild = function (p, ctx) {
    ctx.title(p.name);
    return ctx.load("guild", [p.name], function (r) {
      var g = r.data.guild;
      ctx.title(g.name);
      var members = g.members || [];
      var rankOrder = {};   // guild ranks in Tibia.com order (leader first), so "Rank" sorts by hierarchy, not alphabetically
      members.forEach(function (m) { if (!(m.rank in rankOrder)) rankOrder[m.rank] = Object.keys(rankOrder).length; });
      var onlineOnly = h("input", { type: "checkbox", id: "online-only" });
      var memberTable = table({ caption: t("js.members"), sort: "level", sortDir: -1, columns: [
        { key: "rank", label: t("js.rank"), type: "number", firstDir: 1, sortValue: function (x) { return rankOrder[x.rank]; } },
        { key: "name", label: t("js.name"), render: function (x) { return link("character/" + x.name, x.name); } },
        { key: "title", label: t("js.title") }, { key: "vocation", label: t("js.vocation") },
        { key: "level", label: t("js.level"), num: true },
        { key: "status", label: t("js.status"), render: function (x) { return statusBadge(x.status === "online"); } },
        { key: "joined", label: t("js.joined"), type: "date", render: function (x) { return fmtDate(x.joined); } }], rows: members,
        controls: h("label", { class: "check" }, onlineOnly, " " + t("js.onlineOnly")) });
      onlineOnly.addEventListener("change", function () { memberTable.setFilter(onlineOnly.checked ? function (x) { return x.status === "online"; } : null); });
      var homepage = g.homepage && /^https?:\/\//i.test(g.homepage) ? ext(g.homepage, g.homepage) : null;
      return [
        h("div", { class: "view-head" }, h("div", { class: "with-logo" }, img(g.logo_url, "", "guild-logo-large", 64),
          h("div", null, h("h2", { class: "view-title", text: g.name }), h("p", { class: "subtitle" }, link("world/" + g.world, g.world)))),
          h("div", { class: "head-actions" }, copyButton(g.name, t("js.copyName")), ext(tibiaUrl(r.data, "https://www.tibia.com/community/?subtopic=guilds&page=view&GuildName=" + encodeURIComponent(g.name)), t("js.openTibia"), "btn btn-small"))),
        g.description ? h("p", { class: "pre description", text: g.description }) : null,
        dl([[t("js.founded"), fmtDate(g.founded)], [t("js.active"), yesNo(g.active)], [t("js.openApplications"), yesNo(g.open_applications)],
          [t("js.inWar"), yesNo(g.in_war)], [t("js.homepage"), homepage],
          [t("js.membersCount"), t("js.membersOnline", { total: num(g.members_total), online: num(g.players_online) })],
          [t("js.invited"), g.members_invited ? num(g.members_invited) : null],
          [t("js.guildhalls"), (g.guildhalls || []).length ? (g.guildhalls || []).map(function (x) { return x.name + " (" + t("js.paidUntil", { date: fmtDate(x.paid_until) }) + ")"; }).join("; ") : null],
          [t("js.disband"), g.disband_date ? fmtDate(g.disband_date) + (g.disband_condition ? " – " + g.disband_condition : "") : null]]),
        section(t("js.members"), members.length ? memberTable : empty()),
        (g.invites || []).length ? section(t("js.invites"), h("ul", { class: "plain" }, g.invites.map(function (i) { return h("li", null, link("character/" + i.name, i.name), " – " + fmtDate(i.date)); }))) : null,
        sourceLine(r, tibiaUrl(r.data, "https://www.tibia.com/community/?subtopic=guilds"))
      ];
    });
  };

  V.highscores = function (p, ctx) {
    var cats = APIcfg.highscoreCategories || ["experience"], vocs = APIcfg.highscoreVocations || ["all"];
    var world = p.world, category = cats.indexOf(p.category) >= 0 ? p.category : "experience", vocation = vocs.indexOf(p.vocation) >= 0 ? p.vocation : "all";
    var page = Math.max(1, parseInt(p.page, 10) || 1);
    if (world.toLowerCase() !== "all") rememberWorld(world);
    function go(w, c, v, pg) { location.hash = route(["highscores", w, c, v, pg].join("/")); }
    ctx.title(t("js.toolHighscores") + " – " + categoryLabel(category));
    var controls = h("div", { class: "controls" },
      worldSelect(world.toLowerCase() === "all" ? "all" : world, function (w) { go(w, category, vocation, 1); }, ctx.signal, [["all", t("js.allWorlds")]]),
      selectField(t("js.category"), cats.map(function (c) { return [c, categoryLabel(c)]; }), category, function (c) { go(world, c, vocation, 1); }),
      selectField(t("js.vocation"), vocs.map(function (v) { return [v, vocationLabel(v)]; }), vocation, function (v) { go(world, category, v, 1); }));
    return ctx.load("highscores", [world.toLowerCase() === "all" ? "all" : world, category, vocation, page], function (r) {
      var hs = r.data.highscores, list = hs.highscore_list || [], pg = hs.highscore_page || {};
      var cols = [{ key: "rank", label: t("js.rank"), num: true, firstDir: 1 },
        { key: "name", label: t("js.name"), render: function (x) { return link("character/" + x.name, x.name); } },
        { key: "vocation", label: t("js.vocation") }];
      if (world.toLowerCase() === "all") cols.push({ key: "world", label: t("js.world"), render: function (x) { return link("world/" + x.world, x.world); } });
      cols.push({ key: "level", label: t("js.level"), num: true });
      if (list.some(function (x) { return x.title; })) cols.push({ key: "title", label: t("js.title") });
      if (category !== "experience" || true) cols.push({ key: "value", label: category === "experience" ? t("js.experience") : t("js.points"), num: true });
      var total = pg.total_pages || 1;
      var pager = h("nav", { class: "pager", "aria-label": t("js.pages") },
        h("button", { type: "button", class: "btn btn-small", disabled: page <= 1 || null, onclick: function () { go(world, category, vocation, page - 1); } }, icon("arrow-left"), t("js.previous")),
        h("span", { text: t("js.pageOf", { page: num(page), total: num(total) }) }),
        h("button", { type: "button", class: "btn btn-small", disabled: page >= total || null, onclick: function () { go(world, category, vocation, page + 1); } }, t("js.next"), icon("arrow-right")));
      return [h("h2", { class: "view-title", text: t("js.toolHighscores") }), controls,
        h("p", { class: "muted", text: t("js.highscoreAge", { minutes: num(hs.highscore_age || 0) }) }),
        list.length ? table({ caption: t("js.toolHighscores"), columns: cols, rows: list, filter: false, pageSize: 50 }) : empty(),
        pager, sourceLine(r, tibiaUrl(r.data, "https://www.tibia.com/community/?subtopic=highscores"))];
    }, controls);
  };

  V.houses = function (p, ctx) {
    var towns = APIcfg.towns || ["Thais"];
    var world = p.world, town = p.town;
    rememberWorld(world);
    ctx.title(t("js.toolHouses") + " – " + town);
    var controls = h("div", { class: "controls" },
      worldSelect(world, function (w) { location.hash = route("houses/" + w + "/" + town); }, ctx.signal),
      selectField(t("js.town"), towns.map(function (x) { return [x, x]; }), town, function (x) { location.hash = route("houses/" + world + "/" + x); }));
    return ctx.load("houses", [world, town], function (r) {
      var hs = r.data.houses;
      function status(x) { return x.auctioned ? t("js.auctioned") : x.rented ? t("js.rented") : t("js.free"); }
      function list(items) {
        return table({ caption: t("js.toolHouses"), sort: "name", columns: [
          { key: "name", label: t("js.house"), render: function (x) { return link("house/" + world + "/" + x.house_id, x.name); } },
          { key: "size", label: t("js.size"), num: true }, { key: "rent", label: t("js.rent"), num: true },
          { key: "status", label: t("js.status"), get: status },
          { key: "bid", label: t("js.currentBid"), num: true, get: function (x) { return x.auctioned && x.auction ? x.auction.current_bid : null; } },
          { key: "left", label: t("js.timeLeft"), get: function (x) { return x.auctioned && x.auction ? x.auction.time_left : null; } }], rows: items || [] });
      }
      return [h("h2", { class: "view-title", text: t("js.housesIn", { town: hs.town, world: hs.world }) }), controls,
        section(t("js.housesCount", { n: num((hs.house_list || []).length) }), (hs.house_list || []).length ? list(hs.house_list) : empty()),
        section(t("js.guildhallsCount", { n: num((hs.guildhall_list || []).length) }), (hs.guildhall_list || []).length ? list(hs.guildhall_list) : empty()),
        sourceLine(r, tibiaUrl(r.data, "https://www.tibia.com/community/?subtopic=houses&world=" + encodeURIComponent(world)))];
    }, controls);
  };

  V.house = function (p, ctx) {
    ctx.title(t("js.house"));
    return ctx.load("house", [p.world, p.id], function (r) {
      var x = r.data.house, s = x.status || {}, rent = s.rental || {}, auc = s.auction || {};
      ctx.title(x.name);
      return [
        h("div", { class: "view-head" }, h("div", null, h("h2", { class: "view-title", text: x.name }),
          h("p", { class: "subtitle" }, x.town + " · ", link("world/" + x.world, x.world))),
          h("div", { class: "head-actions" }, copyButton(x.name, t("js.copyName")), copyButton(String(x.houseid), t("js.copyId")))),
        h("div", { class: "split" }, img(x.img, t("js.houseImage", { name: x.name }), "house-img"),
          dl([[t("js.houseId"), String(x.houseid)], [t("js.type"), x.type === "guildhall" ? t("js.guildhall") : t("js.house")], [t("js.size"), t("js.sqm", { n: num(x.size) })],
            [t("js.beds"), num(x.beds)], [t("js.rentMonth"), t("js.gold", { n: num(x.rent) })],
            [t("js.status"), s.is_auctioned ? t("js.auctioned") : s.is_rented ? t("js.rented") : t("js.free")],
            [t("js.owner"), rent.owner ? link("character/" + rent.owner, rent.owner) : null], [t("js.paidUntilLabel"), rent.paid_until ? fmtDate(rent.paid_until, true) : null],
            [t("js.movingDate"), rent.moving_date ? fmtDate(rent.moving_date, true) : null],
            [t("js.currentBid"), s.is_auctioned ? t("js.gold", { n: num(auc.current_bid) }) : null], [t("js.bidder"), auc.current_bidder || null],
            [t("js.auctionEnd"), auc.auction_end ? fmtDate(auc.auction_end, true) : null]])),
        s.original ? h("p", { class: "muted", lang: "en", text: s.original }) : null,
        sourceLine(r, tibiaUrl(r.data, "https://www.tibia.com/community/?subtopic=houses&page=view&world=" + encodeURIComponent(x.world) + "&houseid=" + x.houseid))
      ];
    });
  };

  V.killstatistics = function (p, ctx) {
    rememberWorld(p.world);
    ctx.title(t("js.toolKills") + " – " + p.world);
    var controls = h("div", { class: "controls" }, worldSelect(p.world, function (w) { location.hash = route("killstatistics/" + w); }, ctx.signal));
    return ctx.load("killstatistics", [p.world], function (r) {
      var k = r.data.killstatistics, total = k.total || {};
      return [h("h2", { class: "view-title", text: t("js.killsOn", { world: k.world }) }), controls,
        h("p", { class: "muted", text: t("js.killsPeriod") }),
        dl([[t("js.killedLastDay"), num(total.last_day_killed)], [t("js.playersKilledLastDay"), num(total.last_day_players_killed)],
          [t("js.killedLastWeek"), num(total.last_week_killed)], [t("js.playersKilledLastWeek"), num(total.last_week_players_killed)]]),
        table({ caption: t("js.toolKills"), sort: "last_day_killed", sortDir: -1, columns: [
          { key: "race", label: t("js.creature") }, { key: "last_day_killed", label: t("js.killedDay"), num: true },
          { key: "last_day_players_killed", label: t("js.playersKilledDay"), num: true },
          { key: "last_week_killed", label: t("js.killedWeek"), num: true },
          { key: "last_week_players_killed", label: t("js.playersKilledWeek"), num: true }], rows: k.entries || [] }),
        sourceLine(r, tibiaUrl(r.data, "https://www.tibia.com/community/?subtopic=killstatistics&world=" + encodeURIComponent(k.world)))];
    }, controls);
  };

  function cardGrid(items, href, more) {
    var size = 96, shown = more || 96, filter = "";
    var grid = h("ul", { class: "card-grid" });
    var count = h("p", { class: "table-count", "aria-live": "polite" });
    var btn = h("button", { type: "button", class: "btn btn-small more", onclick: function () { shown += size; draw(); } });
    var input = h("input", { type: "search", class: "input", placeholder: t("js.filter"), "aria-label": t("js.filter"), oninput: function () { filter = input.value.trim().toLowerCase(); shown = size; draw(); } });
    function draw() {
      var list = items.filter(function (x) { return !filter || x.name.toLowerCase().indexOf(filter) >= 0; });
      grid.textContent = "";
      list.slice(0, shown).forEach(function (x) {
        var inner = [img(x.image_url, "", "card-img", 64), h("span", { class: "card-name", text: x.name })];
        grid.appendChild(h("li", null, href ? h("a", { class: "mini-card", href: href(x) }, inner) : h("div", { class: "mini-card" }, inner)));
      });
      count.textContent = t("js.showing", { shown: Math.min(shown, list.length), total: list.length });
      btn.hidden = list.length <= shown;
      btn.textContent = t("js.showMore", { n: Math.min(size, list.length - shown) });
      if (!list.length) grid.appendChild(h("li", { class: "empty-row", text: t("js.noMatches") }));
    }
    draw();
    return h("div", null, h("div", { class: "table-tools" }, input), count, grid, btn);
  }

  V.creatures = function (p, ctx) {
    ctx.title(t("js.toolCreatures"));
    return ctx.load("creatures", [], function (r) {
      var c = r.data.creatures;
      return [h("h2", { class: "view-title", text: t("js.toolCreatures") }),
        c.boosted && c.boosted.name ? h("a", { class: "boost-card wide", href: route("creature/" + c.boosted.race) }, img(c.boosted.image_url, "", "boost-img", 64),
          h("span", { class: "boost-kind", text: t("js.boostedCreature") }), h("span", { class: "boost-name", text: c.boosted.name })) : null,
        cardGrid(c.creature_list || [], function (x) { return route("creature/" + x.race); }),
        sourceLine(r, tibiaUrl(r.data, "https://www.tibia.com/library/?subtopic=creatures"))];
    });
  };

  V.creature = function (p, ctx) {
    ctx.title(t("js.creature"));
    return ctx.load("creature", [p.race], function (r) {
      var c = r.data.creature;
      ctx.title(c.name);
      function list(a) { return a && a.length ? a.join(", ") : null; }
      return [
        h("div", { class: "view-head" }, h("div", { class: "with-logo" }, img(c.image_url, "", "creature-img", 64), h("h2", { class: "view-title", text: c.name })),
          h("div", { class: "head-actions" }, copyButton(c.name, t("js.copyName")), copyButton(creatureWiki(c), t("js.copyWiki")))),
        c.featured ? h("p", { class: "badge badge-online", text: t("js.boostedCreature") }) : null,
        dl([[t("js.hitpoints"), num(c.hitpoints)], [t("js.experience"), num(c.experience_points)], [t("js.immune"), list(c.immune)],
          [t("js.strong"), list(c.strong)], [t("js.weak"), list(c.weakness)], [t("js.healed"), list(c.healed)],
          [t("js.paralysable"), yesNo(c.be_paralysed)], [t("js.summonable"), c.be_summoned ? t("js.manaCost", { n: num(c.summoned_mana) }) : t("js.no")],
          [t("js.convincible"), c.be_convinced ? t("js.manaCost", { n: num(c.convinced_mana) }) : t("js.no")], [t("js.seesInvisible"), yesNo(c.see_invisible)],
          [t("js.loot"), c.is_lootable ? list(c.loot_list) : t("js.none")]]),
        c.behaviour ? section(t("js.behaviour"), h("p", { lang: "en", text: tidy(c.behaviour) })) : null,
        c.description ? section(t("js.description"), h("p", { class: "pre", lang: "en", text: tidy(c.description) })) : null,
        sourceLine(r, tibiaUrl(r.data, "https://www.tibia.com/library/?subtopic=creatures&race=" + encodeURIComponent(c.race)))
      ];
    });
  };
  // A plain wikitable of the facts (no template guessing): editors move the values into the template they use.
  function creatureWiki(c) {
    return factsWiki([["Name", c.name], ["Hit points", c.hitpoints], ["Experience", c.experience_points], ["Immune to", (c.immune || []).join(", ")],
      ["Strong against", (c.strong || []).join(", ")], ["Weak against", (c.weakness || []).join(", ")],
      ["Summon", c.be_summoned ? c.summoned_mana : "--"], ["Convince", c.be_convinced ? c.convinced_mana : "--"],
      ["Paralysable", c.be_paralysed ? "yes" : "no"], ["Sees invisible", c.see_invisible ? "yes" : "no"], ["Loot", (c.loot_list || []).join(", ")],
      ["Source", "TibiaData / Tibia.com library, " + new Date().toISOString().slice(0, 10)]]);
  }
  function factsWiki(rows) {
    return ['{| class="wikitable"'].concat(rows.filter(function (r) { return r[1] !== "" && r[1] != null; }).map(function (r) { return "|-\n! " + r[0] + "\n| " + wikiCell(r[1]); })).concat(["|}"]).join("\n");
  }

  V.spells = function (p, ctx) {
    ctx.title(t("js.toolSpells"));
    return ctx.load("spells", [], function (r) {
      var list = r.data.spells.spell_list || [];
      var type = "all";
      function groups(x) { return [x.group_attack && t("js.groupAttack"), x.group_healing && t("js.groupHealing"), x.group_support && t("js.groupSupport")].filter(Boolean).join(", "); }
      var tbl = table({ caption: t("js.toolSpells"), sort: "level", columns: [
        { key: "name", label: t("js.spell"), render: function (x) { return link("spell/" + x.spell_id, x.name); } },
        { key: "formula", label: t("js.formula"), render: function (x) { return h("code", { lang: "la", text: x.formula }); } },
        { key: "level", label: t("js.level"), num: true }, { key: "mana", label: t("js.mana"), num: true },
        { key: "price", label: t("js.price"), num: true },
        { key: "type", label: t("js.type"), get: function (x) { return x.type_rune ? t("js.rune") : t("js.instant"); } },
        { key: "group", label: t("js.group"), get: groups },
        { key: "premium_only", label: t("js.premium"), get: function (x) { return yesNo(x.premium_only); } }], rows: list, pageSize: 250,
        controls: selectField(t("js.type"), [["all", t("js.allTypes")], ["instant", t("js.instant")], ["rune", t("js.rune")], ["premium", t("js.premium")], ["free", t("js.freeAccount")]], type, function (v) {
          tbl.setFilter(v === "all" ? null : function (x) { return v === "rune" ? x.type_rune : v === "instant" ? x.type_instant : v === "premium" ? x.premium_only : !x.premium_only; });
        }) });
      return [h("h2", { class: "view-title", text: t("js.toolSpells") }), h("p", { class: "muted", text: t("js.formulaNote") }), tbl,
        sourceLine(r, tibiaUrl(r.data, "https://www.tibia.com/library/?subtopic=spells"))];
    });
  };

  V.spell = function (p, ctx) {
    ctx.title(t("js.spell"));
    return ctx.load("spell", [p.id], function (r) {
      var s = r.data.spell, i = s.spell_information || {}, rune = s.rune_information || {};
      ctx.title(s.name);
      var parts = [h("div", { class: "view-head" }, h("div", { class: "with-logo" }, img(s.image_url, "", "creature-img", 32), h("h2", { class: "view-title", text: s.name })),
        h("div", { class: "head-actions" }, i.formula ? copyButton(i.formula, t("js.copyFormula")) : null))];
      if (s.description) parts.push(h("p", { lang: "en", text: tidy(s.description) }));
      if (s.has_spell_information) parts.push(section(t("js.spellInfo"), dl([
        [t("js.formula"), i.formula ? h("code", { lang: "la", text: i.formula }) : null], [t("js.vocation"), (i.vocation || []).join(", ")],
        [t("js.type"), i.type_rune ? t("js.rune") : t("js.instant")], [t("js.damageType"), i.damage_type],
        [t("js.group"), [i.group_attack && t("js.groupAttack"), i.group_healing && t("js.groupHealing"), i.group_support && t("js.groupSupport")].filter(Boolean).join(", ")],
        [t("js.level"), num(i.level)], [t("js.mana"), num(i.mana)], [t("js.soul"), i.soul_points ? num(i.soul_points) : null], [t("js.amount"), i.amount ? num(i.amount) : null],
        [t("js.price"), i.price ? t("js.gold", { n: num(i.price) }) : t("js.free")], [t("js.city"), (i.city || []).join ? (i.city || []).join(", ") : i.city],
        [t("js.cooldown"), t("js.seconds", { n: num(i.cooldown_alone) }) + (i.cooldown_group ? " / " + t("js.groupCooldown", { n: num(i.cooldown_group) }) : "")],
        [t("js.premium"), yesNo(i.premium_only)]])));
      if (s.has_rune_information) parts.push(section(t("js.runeInfo"), dl([[t("js.vocation"), (rune.vocation || []).join(", ")],
        [t("js.damageType"), rune.damage_type], [t("js.level"), num(rune.level)], [t("js.magicLevel"), num(rune.magic_level)]])));
      parts.push(sourceLine(r, tibiaUrl(r.data, "https://www.tibia.com/library/?subtopic=spells&spell=" + encodeURIComponent(s.spell_id))));
      return parts;
    });
  };

  V.bosses = function (p, ctx) {
    ctx.title(t("js.toolBosses"));
    return ctx.load("boostablebosses", [], function (r) {
      var b = r.data.boostable_bosses;
      return [h("h2", { class: "view-title", text: t("js.toolBosses") }),
        b.boosted && b.boosted.name ? h("div", { class: "boost-card wide" }, img(b.boosted.image_url, "", "boost-img", 64), h("span", { class: "boost-kind", text: t("js.boostedBoss") }), h("span", { class: "boost-name", text: b.boosted.name })) : null,
        h("p", { class: "muted", text: t("js.bossesNote") }),
        cardGrid(b.boostable_boss_list || [], null, 200),
        sourceLine(r, tibiaUrl(r.data, "https://www.tibia.com/library/?subtopic=boostablebosses"))];
    });
  };

  V.fansites = function (p, ctx) {
    ctx.title(t("js.toolFansites"));
    return ctx.load("fansites", [], function (r) {
      var f = r.data.fansites;
      function cards(list) {
        return h("ul", { class: "fansite-grid" }, (list || []).map(function (x) {
          var types = Object.keys(x.content_type || {}).filter(function (k) { return x.content_type[k]; }).map(function (k) { return t("js.content." + k); });
          var social = Object.keys(x.social_media || {}).filter(function (k) { return x.social_media[k]; });
          return h("li", { class: "fansite" }, img(x.logo_url, "", "fansite-logo"),
            h("h3", null, /^https?:\/\//.test(x.homepage) ? ext(x.homepage, x.name) : x.name),
            dl([[t("js.contact"), x.contact ? ext("https://www.tibia.com/community/?name=" + encodeURIComponent(x.contact).replace(/%20/g, "+"), x.contact) : null], [t("js.contentTypes"), types.join(", ")], [t("js.languages"), (x.languages || []).join(", ").toUpperCase()],
              [t("js.specials"), (x.specials || []).join(", ")], [t("js.socialMedia"), social.join(", ")], [t("js.fansiteItem"), yesNo(x.fansite_item)]]));
        }));
      }
      return [h("h2", { class: "view-title", text: t("js.toolFansites") }), h("p", { class: "muted", text: t("js.fansitesNote") }),
        section(t("js.promoted", { n: num((f.promoted || []).length) }), cards(f.promoted)),
        section(t("js.supported", { n: num((f.supported || []).length) }), cards(f.supported)),
        sourceLine(r, tibiaUrl(r.data, "https://www.tibia.com/community/?subtopic=fansites"))];
    });
  };

  function newsList(items, compact) {
    return h("ul", { class: "news-list" + (compact ? " compact" : "") }, items.map(function (n) {
      var isTicker = n.type === "ticker";
      var headline = tidy(n.news);
      return h("li", { class: "news-item news-" + (n.category || "other") },
        h("p", { class: "news-meta" }, h("time", { datetime: n.date, text: fmtDate(n.date) }), " · ", h("span", { text: t("js.cat." + (n.category || "other")) }), isTicker ? " · " + t("js.ticker") : ""),
        isTicker ? h("p", { class: "news-ticker", lang: "en", text: headline }) :
          h("h3", { class: "news-title", lang: "en" }, link("news/" + n.id, headline, { href: (TF.site.paths && TF.site.paths.tibiaHash || "") + route("news/" + n.id) })),
        h("p", { class: "news-links" }, isTicker ? null : link("news/" + n.id, t("js.read"), { href: (TF.site.paths && TF.site.paths.tibiaHash || "") + route("news/" + n.id), "aria-label": t("js.readNamed", { title: headline }) }),
          isTicker ? null : " · ", n.url ? ext(n.url, t("js.original")) : null));
    }));
  }

  V.news = function (p, ctx) {
    ctx.title(t("js.toolNews"));
    return ctx.load("news", [], function (r) {
      var items = r.data.news || [];
      var ticker = h("div", null, loading());
      API.get("newsticker", [], { signal: ctx.signal }).then(function (tr) {
        ticker.textContent = "";
        ticker.appendChild((tr.data.news || []).length ? newsList(tr.data.news.slice(0, 12), true) : empty());
      }, function (e) { if (e.kind !== "aborted") { ticker.textContent = ""; ticker.appendChild(errorBox(e)); } });
      return [h("h2", { class: "view-title", text: t("js.toolNews") }),
        h("p", null, link("news/archive/90", t("js.toolArchive"), { class: "btn btn-small" })),
        section(t("js.latestNews"), items.length ? newsList(items) : empty(t("js.noNews"))),
        section(t("js.newsTicker"), ticker),
        sourceLine(r, tibiaUrl(r.data, "https://www.tibia.com/news/?subtopic=latestnews"))];
    });
  };

  V["news-archive"] = function (p, ctx) {
    var days = Math.max(1, parseInt(p.days, 10) || 90);
    ctx.title(t("js.toolArchive"));
    var controls = h("div", { class: "controls" }, selectField(t("js.period"), [[30, t("js.days", { n: 30 })], [90, t("js.days", { n: 90 })], [180, t("js.days", { n: 180 })], [365, t("js.days", { n: 365 })]],
      days, function (d) { location.hash = route("news/archive/" + d); }));
    return ctx.load("newsarchive", [days], function (r) {
      var items = r.data.news || [];
      var tbl = table({ caption: t("js.toolArchive"), sort: "date", sortDir: -1, columns: [
        { key: "date", label: t("js.date"), type: "date", render: function (n) { return h("time", { datetime: n.date, text: fmtDate(n.date) }); } },
        { key: "category", label: t("js.category"), get: function (n) { return t("js.cat." + (n.category || "other")); } },
        { key: "type", label: t("js.type"), get: function (n) { return t("js.newsType." + (n.type || "news")); } },
        { key: "news", label: t("js.headline"), render: function (n) { return n.type === "ticker" ? h("span", { lang: "en", text: tidy(n.news) }) : link("news/" + n.id, tidy(n.news), { lang: "en" }); } },
        { key: "url", label: t("js.original"), sort: false, render: function (n) { return n.url ? ext(n.url, "Tibia.com") : null; }, get: function (n) { return n.url; } }], rows: items, pageSize: 100 });
      return [h("h2", { class: "view-title", text: t("js.toolArchive") }), controls, tbl, sourceLine(r, tibiaUrl(r.data, "https://www.tibia.com/news/?subtopic=newsarchive"))];
    }, controls);
  };

  V["news-article"] = function (p, ctx) {
    ctx.title(t("js.toolNews"));
    return ctx.load("newsarticle", [p.id], function (r) {
      var n = r.data.news;
      ctx.title(n.title || t("js.toolNews"));
      var body = h("div", { class: "article-body", lang: "en" });
      body.appendChild(SAN.fragment(tidy(n.content_html || ""), document));
      if (!body.textContent.trim() && n.content) body.appendChild(h("p", { class: "pre", text: tidy(n.content) }));
      return [h("article", { class: "article" },
        h("p", { class: "news-meta" }, h("time", { datetime: n.date, text: fmtDate(n.date) }), " · ", t("js.cat." + (n.category || "other"))),
        h("h2", { class: "view-title", lang: "en", text: tidy(n.title || "") }), body,
        h("p", null, n.url ? ext(n.url, t("js.readOriginal"), "btn btn-small") : null, " ", link("news", t("js.allNews"), { class: "btn btn-small btn-quiet" }))),
        sourceLine(r, n.url || "https://www.tibia.com/news/")];
    });
  };

  // ---------------------------------------------------------------- router
  var routes = (SITE.dataRoutes || []).map(function (rt) {
    var names = [];
    var rx = rt.pattern === "" ? /^$/ : new RegExp("^" + rt.pattern.split("/").map(function (seg) {
      if (seg.charAt(0) === ":") { names.push(seg.slice(1)); return "([^/]+)"; }
      return seg.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    }).join("/") + "/?$");
    return { id: rt.id, rx: rx, names: names };
  });
  // Fixed paths before patterns ("news/archive/90" is not the article "news/:id").
  routes.sort(function (a, b) { return a.names.length - b.names.length; });

  function match(hash) {
    var path = hash.replace(/^#\/?/, "");
    for (var i = 0; i < routes.length; i++) {
      var m = routes[i].rx.exec(path);
      if (!m) continue;
      var params = {};
      try { routes[i].names.forEach(function (n, j) { params[n] = decodeURIComponent(m[j + 1]); }); } catch (e) { return null; }
      return { id: routes[i].id, params: params };
    }
    return null;
  }

  var app = document.getElementById("app");
  var current = null;
  var siteTitle = document.title, baseTitle = t("js.hubTitle") + " – Tibia Foundry";

  function show() {
    if (current) current.abort();
    var controller = new AbortController();
    current = controller;
    var m = match(location.hash);
    var view = m && V[m.id];
    app.setAttribute("aria-busy", "true");
    var ctx = {
      signal: controller.signal,
      title: function (s) {
        document.title = s ? s + " – " + baseTitle : siteTitle;
        // Breadcrumb: Home › Tibia tools › <this view> (the hub itself ends at "Tibia tools").
        var cur = document.querySelector("[data-crumb-current]"), hub = document.querySelector("[data-crumb-hub]");
        if (cur && hub) {
          cur.textContent = s || "";
          cur.hidden = !s;
          var a = hub.querySelector("a");
          if (s) { hub.removeAttribute("aria-current"); a.setAttribute("href", "#/"); }
          else { hub.setAttribute("aria-current", "page"); a.removeAttribute("href"); }
        }
      },
      render: function (nodes) {
        if (controller.signal.aborted) return;
        app.textContent = "";
        append(app, nodes);
        app.removeAttribute("aria-busy");
      },
      load: function (endpoint, args, build, before) {
        ctx.render([before || null, loading()]);
        return API.get(endpoint, args, { signal: controller.signal }).then(function (result) {
          if (controller.signal.aborted) return;
          ctx.render(build(result));
        }, function (error) {
          if (error.kind === "aborted" || controller.signal.aborted) return;
          ctx.render([before || null, h("h2", { class: "view-title", text: t("js.errTitle") }), errorBox(error, show)]);
        });
      }
    };
    document.body.setAttribute("data-route", m ? m.id : "unknown");
    if (!view) {
      ctx.title(t("js.errNotFoundTitle"));
      ctx.render([h("h2", { class: "view-title", text: t("js.errNotFoundTitle") }), h("p", { text: t("js.unknownRoute") }), link("", t("js.hubTitle"), { class: "btn" })]);
      return;
    }
    // Highlight the section in the sub-navigation.
    document.querySelectorAll("[data-route-link]").forEach(function (a) {
      var on = (m.id === a.getAttribute("data-route-link")) || (a.getAttribute("data-route-link") === "news" && m.id.indexOf("news") === 0) ||
        (a.getAttribute("data-route-link") === "worlds" && m.id === "world") || (a.getAttribute("data-route-link") === "character-search" && m.id === "character") ||
        (a.getAttribute("data-route-link") === "creatures" && m.id === "creature") || (a.getAttribute("data-route-link") === "spells" && m.id === "spell") ||
        (a.getAttribute("data-route-link") === "guilds" && m.id === "guild") || (a.getAttribute("data-route-link") === "houses" && m.id === "house");
      if (on) a.setAttribute("aria-current", "page"); else a.removeAttribute("aria-current");
    });
    try { view(m.params, ctx); } catch (e) { ctx.render(errorBox({ kind: "malformed" }, show)); if (TF.dev) console.error(e); }
    var heading = app.querySelector("h2");
    if (show.focusNext && heading) { heading.setAttribute("tabindex", "-1"); heading.focus({ preventScroll: false }); }
    show.focusNext = true;
  }

  if (app) {
    // tibia/?character=Name (search without JavaScript in the form, or shared links) opens the character view.
    var q = /[?&]character=([^&#]*)/.exec(location.search);
    if (q && !location.hash) {
      var nm = decodeURIComponent(q[1].replace(/\+/g, " ")).trim();
      if (nm) history.replaceState(null, "", location.pathname + route("character/" + nm));
    }
    // Sub-navigation links use the default world remembered from the last world view.
    document.querySelectorAll("[data-world-route]").forEach(function (a) {
      a.setAttribute("href", route(a.getAttribute("data-world-route").replace("{world}", lastWorld())));
    });
    window.addEventListener("hashchange", function () { show(); window.scrollTo(0, 0); });
    show();
  }

  // ---------------------------------------------------------------- home page: live news and boosted creatures
  function mountNews(el) {
    var list = el.querySelector("[data-news-list]"), status = el.querySelector("[data-news-status]"), refresh = el.querySelector("[data-news-refresh]");
    var controller = null;
    function load(force) {
      if (controller) controller.abort();
      controller = new AbortController();
      el.setAttribute("aria-busy", "true");
      if (force || !list.children.length) { list.textContent = ""; list.appendChild(loading(t("js.loadingNews"))); }
      API.get("news", [], { signal: controller.signal, force: !!force }).then(function (r) {
        list.textContent = "";
        var items = (r.data.news || []).slice(0, 6);
        list.appendChild(items.length ? newsList(items) : empty(t("js.noNews")));
        status.textContent = "";
        status.appendChild(sourceLine(r, tibiaUrl(r.data, "https://www.tibia.com/news/?subtopic=latestnews")));
        el.removeAttribute("aria-busy");
      }, function (e) {
        if (e.kind === "aborted") return;
        list.textContent = "";
        list.appendChild(errorBox(e, function () { load(true); }));
        el.removeAttribute("aria-busy");
      });
    }
    if (refresh) refresh.addEventListener("click", function () { load(true); });
    load(false);
  }
  document.querySelectorAll("[data-live-news]").forEach(mountNews);
  document.querySelectorAll("[data-live-boosted], [data-today-strip]").forEach(function (el) { renderBoosted(el, null); });
  document.querySelectorAll("[data-live-online]").forEach(function (el) { renderOnline(el, null); });

  window.TFApp = { match: match, views: Object.keys(V), table: table };
})();
