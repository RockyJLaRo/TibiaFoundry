/*
 * "Today in Tibia": Rashid's town and the next server save (home page and calculators hub). Pure client-side calculation with
 * the registry's functions rashid and nextServerSave (formulas.js, rules and sources in src/formulas.json); no request is made
 * and nothing is stored. The values are refreshed at every full minute while the page is visible (TF.everyMinute) and when the
 * tab becomes visible again. The ticking countdown is deliberately not a live region, so assistive technology reads it only
 * when the user moves to it.
 *
 * Markup (build_site.today_server_html): [data-today-server] containing [data-today-rashid], [data-today-countdown], [data-today-clock].
 */
(function () {
  "use strict";
  var F = window.TFFormulas, TF = window.TF, I18N = window.TF_I18N || {};
  var root = document.querySelector("[data-today-server]");
  if (!F || !TF || !root) return;
  // In-game English town names in every language (the site's other pages write them the same way).
  var TOWNS = { svargrond: "Svargrond", libertybay: "Liberty Bay", porthope: "Port Hope", ankrahmun: "Ankrahmun", darashia: "Darashia", edron: "Edron", carlin: "Carlin" };
  var rashid = root.querySelector("[data-today-rashid]");
  var countdown = root.querySelector("[data-today-countdown]");
  var clock = root.querySelector("[data-today-clock]");
  var clockFormat = window.Intl ? new Intl.DateTimeFormat(I18N.intl || "en-US", { weekday: "short", hour: "2-digit", minute: "2-digit" }) : null;

  function render() {
    var now = Date.now();
    var at = F.FORMULAS.nextServerSave({ now: now }).at;
    var minutes = Math.ceil((at - now) / 60000);   // whole minutes left, counted up so that 0:00 means "now"
    rashid.textContent = TOWNS[F.FORMULAS.rashid({ now: now }).town];
    var time = document.createElement("time");
    time.setAttribute("datetime", "PT" + Math.floor(minutes / 60) + "H" + (minutes % 60) + "M");
    time.textContent = TF.t("home.today.countdown", { hm: Math.floor(minutes / 60) + ":" + ("0" + (minutes % 60)).slice(-2) });
    countdown.textContent = "";
    countdown.appendChild(time);
    var local = document.createElement("time");
    local.setAttribute("datetime", new Date(at).toISOString());
    local.textContent = TF.t("home.today.local", { time: clockFormat ? clockFormat.format(new Date(at)) : new Date(at).toLocaleString() });
    clock.textContent = "";
    clock.appendChild(local);
    clock.setAttribute("data-at", String(at));
  }

  render();
  TF.everyMinute(render);
})();
