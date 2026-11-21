/*
 * TibiaData API client (https://tibiadata.com/, API v5): the only place the website talks to the API.
 *
 * - one request per URL at a time (callers asking for the same URL share it);
 * - every caller can cancel (AbortSignal); the network request is cancelled when nobody waits for it any more;
 * - responses are cached per endpoint for at least TibiaData's own cache time (registry.json api.cacheSeconds),
 *   in memory and in sessionStorage; the boosted creature and boss (config.expireAtServerSave) also expire at the next server save; after a failure the last good response is returned marked as stale;
 * - one retry for network errors, 429 and 5xx (Retry-After respected, at most 5 s); never for 4xx;
 * - every response is checked against the fields the site uses before it is handed out (schema check);
 * - errors are TFDataError objects with a kind: notfound, invalid, network, server, ratelimit, malformed, aborted.
 *
 * Works in the browser (window.TFData) and in Node for the unit tests (module.exports = create).
 */
(function (root, factory) {
  if (typeof module === "object" && module.exports) module.exports = factory;
  else root.TFData = factory({ config: root.TF_SITE && root.TF_SITE.api, fetch: root.fetch.bind(root), storage: safeStorage(root),
    nextServerSave: root.TFFormulas ? function (ms) { return root.TFFormulas.FORMULAS.nextServerSave({ now: ms }).at; } : null });

  function safeStorage(w) {
    try { var s = w.sessionStorage, k = "__tf"; s.setItem(k, "1"); s.removeItem(k); return s; } catch (e) { return null; }
  }
})(typeof self !== "undefined" ? self : this, function create(env) {
  "use strict";
  var config = env.config || {};
  var base = config.base || "https://api.tibiadata.com/v5/";
  var ttl = config.cacheSeconds || {};
  var fetchFn = env.fetch;
  var storage = env.storage || null;
  var expireAtSave = config.expireAtServerSave || [];
  var nextServerSave = env.nextServerSave || null;
  var now = env.now || function () { return Date.now(); };
  var sleep = env.sleep || function (ms, signal) {
    return new Promise(function (resolve, reject) {
      var id = setTimeout(resolve, ms);
      if (signal) signal.addEventListener("abort", function () { clearTimeout(id); reject(abortError()); }, { once: true });
    });
  };
  var memory = new Map();
  var inflight = new Map();
  var stats = { network: 0, cache: 0, shared: 0 };
  var PREFIX = "tf-api:";

  function TFDataError(kind, message, status) {
    var e = new Error(message || kind);
    e.name = "TFDataError"; e.kind = kind; e.status = status || 0;
    return e;
  }
  function abortError() { return TFDataError("aborted", "The request was cancelled."); }

  // ------------------------------------------------------------------ schema checks (the fields the views rely on)
  function isStr(v) { return typeof v === "string"; }
  function isNum(v) { return typeof v === "number" && isFinite(v); }
  function isArr(v) { return Array.isArray(v); }
  function obj(v) { return v !== null && typeof v === "object" && !isArr(v); }
  function listOrNull(v) { return v == null || isArr(v); }

  var schemas = {
    character: function (j) { return obj(j.character) && obj(j.character.character) && isStr(j.character.character.name) && isNum(j.character.character.level); },
    worlds: function (j) { return obj(j.worlds) && isArr(j.worlds.regular_worlds); },
    world: function (j) { return obj(j.world) && isStr(j.world.name) && listOrNull(j.world.online_players); },
    guilds: function (j) { return obj(j.guilds) && listOrNull(j.guilds.active) && listOrNull(j.guilds.formation); },
    guild: function (j) { return obj(j.guild) && isStr(j.guild.name) && listOrNull(j.guild.members); },
    highscores: function (j) { return obj(j.highscores) && listOrNull(j.highscores.highscore_list); },
    houses: function (j) { return obj(j.houses) && listOrNull(j.houses.house_list) && listOrNull(j.houses.guildhall_list); },
    house: function (j) { return obj(j.house) && isStr(j.house.name) && obj(j.house.status); },
    killstatistics: function (j) { return obj(j.killstatistics) && listOrNull(j.killstatistics.entries); },
    creatures: function (j) { return obj(j.creatures) && listOrNull(j.creatures.creature_list); },
    creature: function (j) { return obj(j.creature) && isStr(j.creature.name); },
    spells: function (j) { return obj(j.spells) && listOrNull(j.spells.spell_list); },
    spell: function (j) { return obj(j.spell) && isStr(j.spell.name); },
    boostablebosses: function (j) { return obj(j.boostable_bosses) && listOrNull(j.boostable_bosses.boostable_boss_list); },
    fansites: function (j) { return obj(j.fansites) && listOrNull(j.fansites.promoted) && listOrNull(j.fansites.supported); },
    news: function (j) { return listOrNull(j.news); },
    newsticker: function (j) { return listOrNull(j.news); },
    newsarchive: function (j) { return listOrNull(j.news); },
    newsarticle: function (j) { return obj(j.news) && isNum(j.news.id); }
  };

  function enc(s) { return encodeURIComponent(String(s).trim()); }

  /** Endpoint path and cache kind of each request the site makes. */
  var endpoints = {
    character: function (name) { return ["character/" + enc(name), "character"]; },
    worlds: function () { return ["worlds", "worlds"]; },
    world: function (name) { return ["world/" + enc(name), "world"]; },
    guilds: function (world) { return ["guilds/" + enc(world), "guilds"]; },
    guild: function (name) { return ["guild/" + enc(name), "guild"]; },
    highscores: function (world, category, vocation, page) {
      return ["highscores/" + enc(world || "all") + "/" + enc(category || "experience") + "/" + enc(vocation || "all") + "/" + Math.max(1, parseInt(page, 10) || 1), "highscores"];
    },
    houses: function (world, town) { return ["houses/" + enc(world) + "/" + enc(town), "houses"]; },
    house: function (world, id) { return ["house/" + enc(world) + "/" + (parseInt(id, 10) || 0), "house"]; },
    killstatistics: function (world) { return ["killstatistics/" + enc(world), "killstatistics"]; },
    creatures: function () { return ["creatures", "creatures"]; },
    creature: function (race) { return ["creature/" + enc(race), "creature"]; },
    spells: function () { return ["spells", "spells"]; },
    spell: function (id) { return ["spell/" + enc(id), "spell"]; },
    boostablebosses: function () { return ["boostablebosses", "boostablebosses"]; },
    fansites: function () { return ["fansites", "fansites"]; },
    news: function () { return ["news/latest", "news"]; },
    newsticker: function () { return ["news/newsticker", "newsticker"]; },
    newsarchive: function (days) { return ["news/archive/" + Math.min(3650, Math.max(1, parseInt(days, 10) || 90)), "newsarchive"]; },
    newsarticle: function (id) { return ["news/id/" + (parseInt(id, 10) || 0), "newsarticle"]; }
  };

  // ------------------------------------------------------------------ cache
  function readCache(url) {
    var hit = memory.get(url);
    if (hit) return hit;
    if (!storage) return null;
    try {
      var raw = storage.getItem(PREFIX + url);
      if (!raw) return null;
      hit = JSON.parse(raw);
      if (hit && typeof hit.t === "number" && "data" in hit) { memory.set(url, hit); return hit; }
    } catch (e) { /* unreadable entry: ignore */ }
    return null;
  }
  function writeCache(url, data) {
    var entry = { t: now(), data: data };
    memory.set(url, entry);
    if (storage) {
      try { storage.setItem(PREFIX + url, JSON.stringify(entry)); }
      catch (e) { try { clearStorage(); storage.setItem(PREFIX + url, JSON.stringify(entry)); } catch (e2) { /* full: memory only */ } }
    }
    return entry;
  }
  function clearStorage() {
    if (!storage) return;
    var keys = [];
    for (var i = 0; i < storage.length; i++) { var k = storage.key(i); if (k && k.indexOf(PREFIX) === 0) keys.push(k); }
    keys.forEach(function (k) { storage.removeItem(k); });
  }

  // ------------------------------------------------------------------ network
  function classify(status, body) {
    var code = body && body.information && body.information.status;
    var message = code && code.message;
    if (status === 404) return TFDataError("notfound", message || "Not found", status);
    if (status === 429) return TFDataError("ratelimit", message || "Too many requests", status);
    if (status >= 500) return TFDataError("server", message || "Server error", status);
    if (status >= 400) return TFDataError("invalid", message || "Invalid request", status);
    return null;
  }

  function fetchOnce(url, signal) {
    stats.network++;
    return fetchFn(url, { signal: signal, headers: { Accept: "application/json" }, credentials: "omit", referrerPolicy: "strict-origin-when-cross-origin" })
      .then(function (response) {
        return response.text().then(function (text) {
          var body = null;
          try { body = text ? JSON.parse(text) : null; } catch (e) { body = null; }
          var err = classify(response.status, body);
          if (err) { err.retryAfter = parseFloat(response.headers && response.headers.get && response.headers.get("Retry-After")) || 0; throw err; }
          if (!body) throw TFDataError("malformed", "The response is not valid JSON.", response.status);
          // TibiaData reports some failures inside a 200 response.
          var inner = body.information && body.information.status;
          if (inner && inner.http_code && inner.http_code !== 200) {
            var e2 = classify(inner.http_code, body);
            if (e2) throw e2;
          }
          return body;
        });
      }, function (e) {
        if (signal && signal.aborted) throw abortError();
        throw TFDataError("network", e && e.message ? e.message : "Network error");
      });
  }

  function fetchWithRetry(url, signal) {
    return fetchOnce(url, signal).catch(function (e) {
      if (e.kind === "network" || e.kind === "server" || e.kind === "ratelimit") {
        var wait = Math.min(5000, Math.max(800, (e.retryAfter || 0) * 1000));
        return sleep(wait, signal).then(function () { return fetchOnce(url, signal); });
      }
      throw e;
    });
  }

  /** Within its TTL and, for data that changes at server save, still before the first save after it was fetched. */
  function fresh(entry, kind, maxAge) {
    var t = now();
    if (t - entry.t >= maxAge) return false;
    return !(nextServerSave && expireAtSave.indexOf(kind) !== -1 && t >= nextServerSave(entry.t));
  }

  /**
   * get(name, args, {signal, force}) -> Promise<{data, fetchedAt, fromCache, stale, error}>
   * Rejects with a TFDataError when there is no usable data (stale data is returned instead of failing).
   */
  function get(name, args, options) {
    options = options || {};
    var spec = endpoints[name];
    if (!spec) return Promise.reject(TFDataError("invalid", "Unknown endpoint " + name));
    var parts = spec.apply(null, args || []);
    var url = base + parts[0], kind = parts[1];
    var maxAge = (ttl[kind] || 300) * 1000;
    var cached = readCache(url);
    if (!options.force && cached && fresh(cached, kind, maxAge)) {
      stats.cache++;
      return Promise.resolve({ data: cached.data, fetchedAt: cached.t, fromCache: true, stale: false, url: url });
    }
    if (options.signal && options.signal.aborted) return Promise.reject(abortError());

    var entry = inflight.get(url);
    if (entry) stats.shared++;
    else {
      var controller = typeof AbortController !== "undefined" ? new AbortController() : null;
      entry = { controller: controller, waiting: 0, promise: null };
      entry.promise = fetchWithRetry(url, controller && controller.signal).then(function (body) {
        if (!schemas[kind](body)) throw TFDataError("malformed", "The response does not have the expected fields.");
        return writeCache(url, body);
      });
      entry.promise.then(function () { inflight.delete(url); }, function () { inflight.delete(url); });
      inflight.set(url, entry);
    }
    entry.waiting++;

    var done = false;
    return new Promise(function (resolve, reject) {
      function release() {
        if (done) return;
        done = true;
        entry.waiting--;
        if (entry.waiting <= 0 && entry.controller && inflight.get(url) === entry) { inflight.delete(url); entry.controller.abort(); }
      }
      if (options.signal) options.signal.addEventListener("abort", function () { if (!done) { release(); reject(abortError()); } }, { once: true });
      entry.promise.then(function (fresh) {
        if (done) return;
        done = true; entry.waiting--;
        resolve({ data: fresh.data, fetchedAt: fresh.t, fromCache: false, stale: false, url: url });
      }, function (error) {
        if (done) return;
        done = true; entry.waiting--;
        if (cached && error.kind !== "notfound" && error.kind !== "invalid" && error.kind !== "aborted") {
          resolve({ data: cached.data, fetchedAt: cached.t, fromCache: true, stale: true, error: error, url: url });
        } else reject(error);
      });
    });
  }

  return {
    get: get,
    endpoints: endpoints,
    schemas: schemas,
    stats: stats,
    clear: function () { memory.clear(); clearStorage(); },
    Error: TFDataError,
    base: base
  };
});
