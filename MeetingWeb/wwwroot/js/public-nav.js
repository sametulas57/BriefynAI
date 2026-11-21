// Herkese açık sitenin üst menüsü (_PublicNav.cshtml). Dış kütüphane yok.
// Açılır menüler <details>: betik olmadan da açılıp kapanır. Bu betik şunları ekler:
// aria-expanded, Esc ile kapatıp odağı tetikleyiciye döndürme, dışarı tıklayınca ya da Tab ile çıkınca kapatma,
// telefonda (lg altı) düğmeyle açılan panel: açıkken arka plan kaymaz ve klavyeyle ulaşılamaz.
(() => {
  const header = document.querySelector("[data-public-nav]");
  if (!header) return;

  const toggle = header.querySelector("[data-nav-toggle]");
  const panel = document.getElementById(toggle.getAttribute("aria-controls"));
  const menus = [...header.querySelectorAll("details[data-nav-menu]")];
  const desktop = window.matchMedia("(min-width: 64rem)");
  const behind = () => document.querySelectorAll("body > main, body > footer");

  const summaryOf = menu => menu.querySelector("summary");
  const syncMenu = menu => summaryOf(menu).setAttribute("aria-expanded", String(menu.open));
  const closeMenu = (menu, returnFocus) => {
    menu.open = false;
    syncMenu(menu);
    if (returnFocus) summaryOf(menu).focus();
  };

  const panelOpen = () => toggle.getAttribute("aria-expanded") === "true";
  const setPanel = open => {
    toggle.setAttribute("aria-expanded", String(open));
    panel.classList.toggle("is-open", open);
    document.documentElement.classList.toggle("pn-locked", open);
    behind().forEach(element => { element.inert = open; });
  };

  menus.forEach(menu => {
    syncMenu(menu);
    menu.addEventListener("toggle", () => {
      syncMenu(menu);
      // "name" özelliğini tanımayan tarayıcılarda da aynı anda tek menü açık kalsın.
      if (menu.open) menus.forEach(other => { if (other !== menu && other.open) closeMenu(other, false); });
    });
    // Masaüstünde Tab ile menünün dışına çıkınca kapanır. Fareyle tıklamada relatedTarget boş gelir; onu dışarı tıklama yakalar.
    menu.addEventListener("focusout", event => {
      if (desktop.matches && menu.open && event.relatedTarget && !menu.contains(event.relatedTarget)) closeMenu(menu, false);
    });
  });

  document.addEventListener("click", event => {
    if (!desktop.matches) return;
    menus.forEach(menu => { if (menu.open && !menu.contains(event.target)) closeMenu(menu, false); });
  });

  document.addEventListener("keydown", event => {
    if (event.key !== "Escape") return;
    const focused = document.activeElement;
    const menu = menus.find(item => item.open && (item.contains(focused) || desktop.matches));
    if (menu) {
      event.preventDefault();
      closeMenu(menu, menu.contains(focused) || focused === document.body);
      return;
    }
    if (panelOpen()) {
      event.preventDefault();
      setPanel(false);
      toggle.focus();
    }
  });

  toggle.addEventListener("click", () => setPanel(!panelOpen()));

  // Pencere masaüstü genişliğine geçerse panel ve kilit bırakılır.
  desktop.addEventListener("change", () => {
    if (desktop.matches && panelOpen()) setPanel(false);
  });

  // Geri tuşuyla önbellekten dönülünce menü kapalı başlasın.
  window.addEventListener("pageshow", event => {
    if (!event.persisted) return;
    setPanel(false);
    menus.forEach(menu => closeMenu(menu, false));
  });
})();
