/*
 * Tibia Foundry header menu (every page, after site.js). The links come from registry.json "menu" (build_site.py writes them
 * into the header, the drawer and the footer, so every destination works without JavaScript).
 *
 * * Desktop (1024 px and wider): each group is a disclosure button (aria-expanded) controlling a hidden panel - not an ARIA
 *   menu. Escape, a click outside or focus leaving the group closes it; only one panel is open at a time.
 * * Below 1024 px, and on a wider window whose header does not fit on one line (long labels; the class nav-collapsed), the
 *   menu button opens the drawer: a modal dialog with collapsible groups, the languages and the theme toggle. Tab stays
 *   inside it, Escape and the backdrop close it, and focus returns to the menu button.
 * * On the Tibia page the data view in the address (#/guilds/...) marks its menu link as the current page.
 */
(function () {
  "use strict";
  var root = document.documentElement;
  var t = window.TF && window.TF.t ? window.TF.t : function (key) { return key; };
  var bar = document.querySelector(".header-bar");
  var mainNav = document.querySelector(".main-nav");
  if (!bar || !mainNav) return;
  var wide = window.matchMedia ? window.matchMedia("(min-width: 1024px)") : { matches: window.innerWidth >= 1024 };

  // ---------------------------------------------------------------- desktop: disclosure panels
  var groups = Array.prototype.slice.call(mainNav.querySelectorAll(".nav-group"));
  var openGroup = null;
  function button(group) { return group.querySelector(".nav-button"); }
  function panel(group) { return group.querySelector(".nav-panel"); }
  function closePanel(group, focusButton) {
    if (!group) return;
    button(group).setAttribute("aria-expanded", "false");
    panel(group).hidden = true;
    panel(group).style.left = "";
    if (openGroup === group) openGroup = null;
    if (focusButton) button(group).focus();
  }
  function keepInView(p) {
    // A panel opens under its button; near the right edge it moves left so it stays inside the window.
    var margin = 8, r = p.getBoundingClientRect(), shift = 0;
    if (r.right > window.innerWidth - margin) shift = window.innerWidth - margin - r.right;
    if (r.left + shift < margin) shift = margin - r.left;
    if (shift) p.style.left = shift + "px";
  }
  function openPanel(group) {
    if (openGroup && openGroup !== group) closePanel(openGroup);
    if (window.TF && window.TF.closeLang) window.TF.closeLang();
    button(group).setAttribute("aria-expanded", "true");
    panel(group).hidden = false;
    keepInView(panel(group));
    openGroup = group;
  }
  groups.forEach(function (group) {
    button(group).addEventListener("click", function () { if (openGroup === group) closePanel(group); else openPanel(group); });
    group.addEventListener("focusout", function (e) {
      // Focus moving to something outside the group closes it (a click on nothing focusable is handled below).
      if (openGroup === group && e.relatedTarget && !group.contains(e.relatedTarget)) closePanel(group);
    });
    panel(group).addEventListener("click", function (e) { if (e.target.closest("a")) closePanel(group); });
  });
  document.addEventListener("click", function (e) { if (openGroup && !openGroup.contains(e.target)) closePanel(openGroup); });

  // ---------------------------------------------------------------- drawer (phones, tablets and a header that does not fit)
  var menuBtn = document.getElementById("menu-btn");
  var drawer = document.getElementById("site-menu");
  var drawerPanel = drawer && drawer.querySelector(".drawer-panel");
  var FOCUSABLE = "a[href], button:not([disabled]), summary, input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex='-1'])";
  function drawerOpen() { return !!drawer && !drawer.hidden; }
  function focusables() {
    return Array.prototype.slice.call(drawerPanel.querySelectorAll(FOCUSABLE)).filter(function (el) { return el.getClientRects().length > 0; });
  }
  function openDrawer() {
    if (!drawer || drawerOpen()) return;
    if (window.TF && window.TF.closeLang) window.TF.closeLang();
    closePanel(openGroup);
    drawer.hidden = false;
    menuBtn.setAttribute("aria-expanded", "true");
    menuBtn.setAttribute("aria-label", t("nav.menuClose"));
    root.classList.add("menu-lock");
    var first = focusables()[0];
    if (first) first.focus();
  }
  function closeDrawer(focusButton) {
    if (!drawerOpen()) return;
    drawer.hidden = true;
    menuBtn.setAttribute("aria-expanded", "false");
    menuBtn.setAttribute("aria-label", t("nav.menuOpen"));
    root.classList.remove("menu-lock");
    if (focusButton) menuBtn.focus();
  }
  if (menuBtn && drawer && drawerPanel) {
    menuBtn.addEventListener("click", function () { if (drawerOpen()) closeDrawer(true); else openDrawer(); });
    drawer.addEventListener("click", function (e) {
      if (e.target.closest("[data-drawer-close]")) closeDrawer(true);
      else if (e.target.closest("a[href]") && !e.target.closest("a[data-lang]")) closeDrawer(false);
    });
  }

  document.addEventListener("keydown", function (e) {
    if (drawerOpen()) {
      if (e.key === "Escape") { e.preventDefault(); closeDrawer(true); return; }
      if (e.key !== "Tab") return;
      var items = focusables();
      if (!items.length) return;
      var first = items[0], last = items[items.length - 1], active = document.activeElement;
      if (!drawerPanel.contains(active)) { e.preventDefault(); first.focus(); }
      else if (e.shiftKey && active === first) { e.preventDefault(); last.focus(); }
      else if (!e.shiftKey && active === last) { e.preventDefault(); first.focus(); }
      return;
    }
    if (e.key === "Escape" && openGroup) { e.preventDefault(); closePanel(openGroup, true); }
  });

  // ---------------------------------------------------------------- the desktop header must fit on one line, else the drawer
  function fit() {
    root.classList.remove("nav-collapsed");
    if (wide.matches && bar.scrollWidth > bar.clientWidth + 1) {
      root.classList.add("nav-collapsed");
      closePanel(openGroup);
    } else if (wide.matches) {
      closeDrawer(false);
    }
  }
  var pending = false;
  window.addEventListener("resize", function () {
    if (pending) return;
    pending = true;
    window.requestAnimationFrame(function () { pending = false; fit(); });
  });
  fit();
  if (document.fonts && document.fonts.ready) document.fonts.ready.then(fit);

  // ---------------------------------------------------------------- Tibia page: the data view in the address is the current page
  var ALIAS = { world: "worlds", guild: "guilds", house: "houses", creature: "creatures", spell: "spells" };
  function markRoute() {
    var seg = location.hash.replace(/^#\/?/, "").split("/")[0];
    seg = ALIAS[seg] || seg;
    document.querySelectorAll("a[data-menu-dest^='route:']").forEach(function (a) {
      var mine = a.getAttribute("data-menu-dest").slice(6).split("/")[0];
      if (seg && mine === seg) a.setAttribute("aria-current", "page"); else a.removeAttribute("aria-current");
    });
    document.querySelectorAll(".nav-group, .drawer-group").forEach(function (g) {
      var head = g.querySelector(".nav-button, .drawer-summary");
      if (g.querySelector("a[aria-current='page']")) head.setAttribute("data-current", ""); else head.removeAttribute("data-current");
    });
  }
  if (document.body.getAttribute("data-page") === "tibia") {
    markRoute();
    window.addEventListener("hashchange", markRoute);
  }

  window.TFNav = { openDrawer: openDrawer, closeDrawer: closeDrawer, fit: fit };
})();
