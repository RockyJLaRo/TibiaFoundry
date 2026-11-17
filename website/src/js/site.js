/*
 * Tibia Foundry website shell (every page): texts (TF.t), theme, menu, language selector and offer, header
 * character search, copy buttons, screenshot tabs, lightbox and the analytics click events.
 * Texts come from window.TF_I18N (written into each page by build_site.py from src/locales/<lang>.json).
 */
(function () {
  "use strict";
  var root = document.documentElement;
  root.classList.remove("no-js");

  // ---------------------------------------------------------------- texts: this language, else English (never a bare key)
  var I18N = window.TF_I18N || { lang: "en", intl: "en-US", strings: {}, fallback: {}, languages: [] };
  var SITE = window.TF_SITE || {};
  var DEV = location.protocol === "file:" || /^(localhost|127\.0\.0\.1|\[::1\])$/.test(location.hostname);
  var numberFormat = window.Intl ? new Intl.NumberFormat(I18N.intl) : null;
  function t(key, vars) {
    var text = I18N.strings[key];
    if (text == null || text === "") {
      text = I18N.fallback[key];
      if (DEV) console.warn('[i18n] "' + key + '" is missing in ' + I18N.lang + (text == null ? " and in English" : "; English is shown"));
      if (text == null) text = key;
    }
    return String(text).replace(/\{(\w+)\}/g, function (m, name) {
      if (!vars || vars[name] == null) return m;
      return typeof vars[name] === "number" && numberFormat ? numberFormat.format(vars[name]) : String(vars[name]);
    });
  }
  function store(key, value) { try { localStorage.setItem(key, value); } catch (e) { /* storage blocked */ } }
  function read(key) { try { return localStorage.getItem(key); } catch (e) { return null; } }

  // ---------------------------------------------------------------- copy to clipboard (data-copy="text" or data-copy-from="#id")
  function copyText(text) {
    if (navigator.clipboard && window.isSecureContext) return navigator.clipboard.writeText(text);
    return new Promise(function (resolve, reject) {
      var area = document.createElement("textarea");
      area.value = text; area.setAttribute("readonly", ""); area.style.position = "fixed"; area.style.opacity = "0";
      document.body.appendChild(area); area.select();
      var ok = false; try { ok = document.execCommand("copy"); } catch (e) { ok = false; }
      document.body.removeChild(area);
      if (ok) resolve(); else reject(new Error("copy failed"));
    });
  }
  var live = document.getElementById("live-status");
  function announce(message) { if (live) { live.textContent = ""; setTimeout(function () { live.textContent = message; }, 30); } }
  function confirmCopy(button, ok) {
    var label = button.querySelector(".copy-label") || button;
    if (!button.dataset.label) button.dataset.label = label.textContent;
    label.textContent = ok ? t("js.copied") : t("js.copyFailed");
    button.classList.toggle("copied", ok);
    announce(ok ? t("js.copied") : t("js.copyFailed"));
    clearTimeout(button._copyTimer);
    button._copyTimer = setTimeout(function () { label.textContent = button.dataset.label; button.classList.remove("copied"); }, 1800);
  }
  document.addEventListener("click", function (e) {
    var button = e.target.closest && e.target.closest("[data-copy],[data-copy-from]");
    if (!button) return;
    e.preventDefault();
    var text = button.getAttribute("data-copy");
    if (text == null) { var from = document.querySelector(button.getAttribute("data-copy-from")); text = from ? from.textContent : ""; }
    copyText(text).then(function () { confirmCopy(button, true); }, function () { confirmCopy(button, false); });
  });

  window.TF = { t: t, lang: I18N.lang, intl: I18N.intl, site: SITE, copy: copyText, confirmCopy: confirmCopy, announce: announce, dev: DEV };

  // ---------------------------------------------------------------- language: selector, remembered choice, same page and place
  var LANG_KEY = "tf-lang";
  if (location.protocol === "file:") {
    document.querySelectorAll("a[data-lang], a[data-page-link]").forEach(function (a) {
      var href = a.getAttribute("href");
      if (/^https?:|^mailto:|#/.test(href)) return;
      a.setAttribute("href", href.replace(/\/?$/, "/").replace(/^\.\/$/, "") + "index.html");
    });
  }
  if (/[?&]lang=en\b/.test(location.search)) store(LANG_KEY, "en");
  document.addEventListener("click", function (e) {
    var a = e.target.closest && e.target.closest("a[data-lang]");
    if (!a || e.ctrlKey || e.metaKey || e.shiftKey || e.button > 0) return;
    store(LANG_KEY, a.getAttribute("data-lang"));
    if (a.getAttribute("data-lang") === I18N.lang) { e.preventDefault(); closeLang(); return; }
    // The data views live in the hash (#/character/...): keep it so the same view opens in the new language.
    if (location.hash) { e.preventDefault(); location.href = a.getAttribute("href") + location.hash; }
  });

  var langBox = document.getElementById("lang"), langBtn = document.getElementById("lang-btn"), langMenu = document.getElementById("lang-menu");
  function langLinks() { return Array.prototype.slice.call(langMenu.querySelectorAll("a")); }
  function openLang(focusIndex) {
    langMenu.hidden = false;
    langBtn.setAttribute("aria-expanded", "true");
    var links = langLinks(), cur = links.filter(function (a) { return a.hasAttribute("aria-current"); })[0];
    (focusIndex === -1 ? links[links.length - 1] : focusIndex != null ? links[focusIndex] : cur || links[0]).focus();
  }
  function closeLang(focusButton) {
    if (!langMenu || langMenu.hidden) return;
    langMenu.hidden = true;
    langBtn.setAttribute("aria-expanded", "false");
    if (focusButton) langBtn.focus();
  }
  if (langBtn && langMenu) {
    langBtn.addEventListener("click", function () { if (langMenu.hidden) openLang(); else closeLang(); });
    langBtn.addEventListener("keydown", function (e) {
      if (e.key === "ArrowDown") { e.preventDefault(); openLang(0); }
      else if (e.key === "ArrowUp") { e.preventDefault(); openLang(-1); }
    });
    langMenu.addEventListener("keydown", function (e) {
      var links = langLinks(), i = links.indexOf(document.activeElement);
      if (e.key === "ArrowDown" || e.key === "ArrowUp") {
        e.preventDefault();
        links[(i + (e.key === "ArrowDown" ? 1 : -1) + links.length) % links.length].focus();
      } else if (e.key === "Home" || e.key === "End") {
        e.preventDefault(); links[e.key === "Home" ? 0 : links.length - 1].focus();
      } else if (e.key === "Escape") {
        e.preventDefault(); closeLang(true);
      } else if (e.key === "Tab") closeLang();
    });
    document.addEventListener("click", function (e) { if (!langBox.contains(e.target)) closeLang(); });
  }

  // A visitor whose browser prefers another of the five languages, and who has not chosen one, is offered it once.
  (function offer() {
    var bar = document.getElementById("lang-suggest");
    if (!bar || read(LANG_KEY) || read("tf-lang-offer-closed")) return;
    var wanted = (navigator.languages || [navigator.language || ""]).map(function (l) { return String(l).toLowerCase(); });
    var match = null;
    for (var i = 0; i < wanted.length && !match; i++) {
      I18N.languages.forEach(function (l) {
        if (match) return;
        var code = l.code.toLowerCase();
        if (wanted[i] === code || wanted[i].split("-")[0] === code.split("-")[0]) match = l;
      });
    }
    if (!match || match.code === I18N.lang) return;
    var link = document.querySelector('#lang-menu a[data-lang="' + match.code + '"]');
    if (!link) return;
    bar.setAttribute("lang", match.code);
    bar.querySelector(".lang-suggest-text").textContent = match.offer.text;
    var go = bar.querySelector(".lang-suggest-go");
    go.textContent = match.offer.go;
    go.setAttribute("href", link.getAttribute("href"));
    go.setAttribute("data-lang", match.code);
    go.setAttribute("hreflang", match.code);
    var close = bar.querySelector(".lang-suggest-close");
    close.setAttribute("aria-label", match.offer.close);
    close.title = match.offer.close;
    close.addEventListener("click", function () { bar.hidden = true; store("tf-lang-offer-closed", "1"); });
    bar.hidden = false;
  })();

  // ---------------------------------------------------------------- theme: stored choice, else the system's (set before paint in <head>)
  var THEME_KEY = "tf-theme";
  function systemDark() { return window.matchMedia && matchMedia("(prefers-color-scheme: dark)").matches; }
  function currentTheme() { return root.getAttribute("data-theme") || (systemDark() ? "dark" : "light"); }
  var toggles = document.querySelectorAll("[data-theme-toggle]");
  function themeLabel() {
    var dark = currentTheme() === "dark";
    toggles.forEach(function (btn) {
      btn.setAttribute("aria-label", dark ? t("js.themeToLight") : t("js.themeToDark"));
      btn.setAttribute("aria-pressed", String(dark));
      var text = btn.querySelector(".theme-label");
      if (text) text.textContent = dark ? t("js.themeToLight") : t("js.themeToDark");
    });
  }
  toggles.forEach(function (btn) {
    btn.addEventListener("click", function () {
      var next = currentTheme() === "dark" ? "light" : "dark";
      root.setAttribute("data-theme", next);
      store(THEME_KEY, next);
      themeLabel();
      document.dispatchEvent(new CustomEvent("tf-theme", { detail: next }));
    });
  });
  if (window.matchMedia) {
    var mq = matchMedia("(prefers-color-scheme: dark)");
    if (mq.addEventListener) mq.addEventListener("change", themeLabel);
  }
  themeLabel();

  // ---------------------------------------------------------------- menu (below the wide layout)
  var header = document.querySelector(".site-header");
  var menuBtn = document.getElementById("menu-btn");
  var panel = document.getElementById("site-menu");
  function setMenu(open, focusButton) {
    if (!header || !menuBtn) return;
    header.classList.toggle("menu-open", open);
    menuBtn.setAttribute("aria-expanded", String(open));
    menuBtn.setAttribute("aria-label", open ? t("nav.menuClose") : t("nav.menuOpen"));
    root.classList.toggle("menu-lock", open);
    if (open) { var first = panel && panel.querySelector("a, button, input"); if (first) first.focus(); }
    else if (focusButton) menuBtn.focus();
  }
  if (menuBtn) {
    menuBtn.addEventListener("click", function () { closeLang(); setMenu(!header.classList.contains("menu-open")); });
    panel.addEventListener("click", function (e) { if (e.target.closest("a")) setMenu(false); });
    document.addEventListener("keydown", function (e) { if (e.key === "Escape" && header.classList.contains("menu-open")) setMenu(false, true); });
    window.addEventListener("resize", function () { if (window.innerWidth >= 1180 && header.classList.contains("menu-open")) setMenu(false); });
  }

  // ---------------------------------------------------------------- character search (header, home, menu): opens tibia/#/character/<name>
  document.querySelectorAll("form[data-character-search]").forEach(function (form) {
    form.addEventListener("submit", function (e) {
      var input = form.querySelector("input[name=character]");
      var name = (input.value || "").replace(/\s+/g, " ").trim();
      e.preventDefault();
      if (!name) { input.focus(); return; }
      var target = form.getAttribute("action") + "#/character/" + encodeURIComponent(name);
      setMenu(false);
      if (window.TFApp && form.getAttribute("action") === (SITE.paths && SITE.paths.tibia) && document.body.getAttribute("data-page") === "tibia") location.hash = "#/character/" + encodeURIComponent(name);
      else location.href = target;
    });
  });
  // "/" focuses the visible search field (not while typing).
  document.addEventListener("keydown", function (e) {
    if (e.key !== "/" || e.ctrlKey || e.metaKey || e.altKey) return;
    var tag = (document.activeElement && document.activeElement.tagName) || "";
    if (/^(INPUT|TEXTAREA|SELECT)$/.test(tag) || (document.activeElement && document.activeElement.isContentEditable)) return;
    var fields = Array.prototype.slice.call(document.querySelectorAll("form[data-character-search] input")).filter(function (i) { return i.offsetParent !== null; });
    if (fields.length) { e.preventDefault(); fields[0].focus(); }
  });

  // ---------------------------------------------------------------- tabs (screenshots)
  document.querySelectorAll("[role=tablist]").forEach(function (list) {
    var tabs = Array.prototype.slice.call(list.querySelectorAll("[role=tab]"));
    function select(tab) {
      tabs.forEach(function (x) {
        var on = x === tab;
        x.setAttribute("aria-selected", String(on));
        x.tabIndex = on ? 0 : -1;
        document.getElementById(x.getAttribute("aria-controls")).hidden = !on;
      });
    }
    tabs.forEach(function (tab, i) {
      tab.addEventListener("click", function () { select(tab); });
      tab.addEventListener("keydown", function (e) {
        var j = e.key === "ArrowRight" ? i + 1 : e.key === "ArrowLeft" ? i - 1 : e.key === "Home" ? 0 : e.key === "End" ? tabs.length - 1 : null;
        if (j === null) return;
        j = (j + tabs.length) % tabs.length;
        tabs[j].focus(); select(tabs[j]); e.preventDefault();
      });
    });
  });

  // ---------------------------------------------------------------- lightbox (screenshots)
  var box = document.getElementById("lightbox");
  if (box && typeof box.showModal === "function") {
    var big = box.querySelector("img"), cap = box.querySelector("p");
    document.addEventListener("click", function (e) {
      var btn = e.target.closest && e.target.closest("[data-zoom]");
      if (!btn) return;
      var img = Array.prototype.slice.call(btn.querySelectorAll("img")).filter(function (i) { return i.offsetParent !== null; })[0] || btn.querySelector("img");
      big.src = img.currentSrc || img.src;
      big.alt = img.alt;
      cap.textContent = btn.getAttribute("data-zoom") || img.alt;
      box.showModal();
    });
    box.addEventListener("click", function (e) { if (e.target === box || e.target.closest(".close")) box.close(); });
  }

  // ---------------------------------------------------------------- analytics: one event per download, release, repository or issues link
  var EVENTS = { download: "download_click", releases: "releases_click", repository: "repository_click", issues: "issues_click", email: "email_click" };
  document.addEventListener("click", function (e) {
    var link = e.target.closest && e.target.closest("a[data-track]");
    if (!link || typeof window.gtag !== "function") return;
    var kind = link.getAttribute("data-track");
    window.gtag("event", EVENTS[kind] || "outbound_click", {
      link_url: link.href, link_category: kind, link_location: link.getAttribute("data-place") || "", app_version: SITE.version,
      page_language: I18N.lang
    });
  });
})();
