// Ana sayfanın (/tr) kendi davranışı. Dış kütüphane yok; scroll-craft motoru (wwwroot/lib/scrollcraft) değiştirilmeden kullanılır.
// Tasarım kaynağı: ~/scrollcraft/builds/briefyn/BRIEF.md (5. bölüm düzen, 7. bölüm teknik kararlar); 2026-11-22'de yolculuk bölümü
// tek bir büyük özet sayfasına indirildi (Ulaş'ın isteği).
//
// İki düzen var:
// - Hareketli ("sc-on", Home.cshtml başındaki satır içi betik koyar): özet bölümü, güven bölümü ve kapanış pinlenir. Özet bölümünde
//   ses dalgasının yatıp sönmesi CSS'te, motorun yayınladığı --sc-p ile; özetin üç parçası motorun ipuçlarıyla (data-sc-cue) gelir.
// - Durağan (hareketi azalt açık, ekran 500 pikselden kısa ya da motor bağlanamadı): motor hiç bağlanmaz; özet ve güven bölümünün
//   çizgileri baştan tam görünür. Kaydırmaya bağlı canlandırma yok.
(() => {
  const doc = document.documentElement;
  const hero = document.querySelector("[data-home-hero]");
  const journey = document.querySelector("[data-home-journey]");
  if (!hero || !journey) return;

  const small = window.matchMedia("(max-width: 860px)");
  const reduce = window.matchMedia("(prefers-reduced-motion: reduce)");
  const clamp01 = x => (x < 0 ? 0 : x > 1 ? 1 : x);

  // ---- Kayan şerit: sabit hızla sağa; durdurma düğmesi (WCAG 2.2.2), üzerine gelince ve odakta da durur (CSS). ----
  const strip = document.querySelector("[data-home-strip]");
  if (strip) {
    const stripTrack = strip.querySelector("[data-strip-track]");
    const toggle = strip.querySelector("[data-strip-toggle]");
    const setSpeed = () => {
      const list = stripTrack.firstElementChild;
      const speed = small.matches ? 28 : 40;
      if (list) stripTrack.style.setProperty("--strip-duration", (list.offsetWidth / speed).toFixed(1) + "s");
    };
    setSpeed();
    small.addEventListener("change", setSpeed);
    if (document.fonts) document.fonts.ready.then(setSpeed);
    toggle.addEventListener("click", () => {
      const paused = strip.classList.toggle("is-paused");
      toggle.setAttribute("aria-label", paused ? "Şeridi oynat" : "Şeridi durdur");
    });
  }

  const motion = doc.classList.contains("sc-on") && !reduce.matches && window.ScrollCraft;
  const fontsReady = document.fonts ? document.fonts.ready : Promise.resolve();

  if (!motion) {
    doc.classList.remove("sc-on");
    // scroll-craft'ın doğrulama aracı sayfanın hazır olduğunu bu sınıftan anlar; motor bağlanmadığı için sayfa kendisi koyar.
    fontsReady.then(() => doc.classList.add("sc-ready"));
    return;
  }

  // ---- Hareketli düzen ----
  hero.querySelector(".hh-panel").setAttribute("data-sc-parallax", small.matches ? "-1.1" : "0.5");
  // Pin payları CSS'teki yüksekliklerle aynı (.sc-on .hj / .ht / .hc): sayfa bağlanırken kaymasın.
  journey.setAttribute("data-sc-act", "pin");
  journey.setAttribute("data-sc-span", small.matches ? "2.2" : "2.4");
  const trust = document.querySelector("[data-home-trust]");
  if (trust) {
    trust.setAttribute("data-sc-act", "pin");
    trust.setAttribute("data-sc-span", "2");
  }
  const closing = document.querySelector("[data-home-closing]");
  if (closing) {
    closing.setAttribute("data-sc-act", "pin");
    closing.setAttribute("data-sc-span", small.matches ? "1.2" : "1.3");
  }

  // Özet sayfası sabitken ekrana sığmalı (iOS Safari'de araç çubuğu yüzünden ekran Chrome'dakinden kısa): taşarsa sayfanın
  // yazısı adım adım küçültülür (içerideki boylar em). Sığan ekranda hiçbir şey değişmez.
  const card = journey.querySelector("[data-hj-card]");
  const fitCard = () => {
    if (!card) return;
    card.style.fontSize = "";
    const base = parseFloat(getComputedStyle(card).fontSize);
    for (let scale = 0.96; card.scrollHeight > card.clientHeight + 1 && scale >= 0.78; scale -= 0.04) {
      card.style.fontSize = (base * scale).toFixed(2) + "px";
    }
  };

  const mount = () => {
    fitCard();
    const main = ScrollCraft.mount(document.querySelector("main"));

    // Kapanış: işaretin çizimi CSS'te (--sc-p). Doğrulama aracı için boyanan değer ve son çeyrekteki yazılmış bekleme yayınlanır.
    if (closing) {
      const mark = closing.querySelector("[data-hc-mark]");
      const cta = closing.querySelector("[data-hc-cta]");
      const publish = () => {
        const p = parseFloat(closing.style.getPropertyValue("--sc-p")) || 0;
        mark.setAttribute("data-sc-verify-state", clamp01(p / 0.75).toFixed(2));
        mark.setAttribute("data-sc-verify-hold", p >= 0.75 ? "true" : "false");
      };
      publish();
      window.addEventListener("scroll", () => requestAnimationFrame(publish), { passive: true });
      // Klavyeyle "Kayıt yükle"ye gelindiğinde pinli bölüm, çağrının görünür olduğu yere getirilir (verify.md: motor pinli bölümde bunu yapamaz).
      cta.addEventListener("focus", () => {
        const cue = cta.closest("[data-sc-cue]");
        if (parseFloat(getComputedStyle(cue).opacity) > 0.85) return;
        const top = closing.getBoundingClientRect().top + window.scrollY;
        window.scrollTo({ top: top + (closing.offsetHeight - window.innerHeight) * 0.5, behavior: "instant" });
        // Motorun kendi focusin işleyicisi bundan sonra çalışır; ipucunun açıldığını görsün diye durum hemen okunur.
        if (main && main.read) main.read();
      });
    }
    // Ekran boyu değişirse (dönme, pencere, telefonda adres çubuğu) özet sayfası yeniden sığdırılır.
    let fitFrame = 0;
    window.addEventListener("resize", () => {
      cancelAnimationFrame(fitFrame);
      fitFrame = requestAnimationFrame(fitCard);
    }, { passive: true });
    small.addEventListener("change", () => {
      fitCard();
      if (main && main.layout) main.layout();
    });

    // Açılış: ince imleçli cihazlarda fotoğraf imlece doğru en fazla 12 piksel kayar (panel ve metin kımıldamaz).
    const fine = window.matchMedia("(hover: hover) and (pointer: fine)");
    const drift = hero.querySelector(".hh-drift");
    if (fine.matches && drift) {
      let tx = 0, ty = 0, cx = 0, cy = 0, moving = 0;
      const step = () => {
        cx += (tx - cx) * 0.08;
        cy += (ty - cy) * 0.08;
        drift.style.transform = "translate3d(" + cx.toFixed(2) + "px," + cy.toFixed(2) + "px,0)";
        moving = Math.abs(tx - cx) + Math.abs(ty - cy) > 0.05 ? requestAnimationFrame(step) : 0;
      };
      hero.addEventListener("pointermove", event => {
        const box = hero.getBoundingClientRect();
        tx = ((event.clientX - box.left) / box.width - 0.5) * 24;
        ty = ((event.clientY - box.top) / box.height - 0.5) * 12;
        if (!moving) moving = requestAnimationFrame(step);
      });
      hero.addEventListener("pointerleave", () => { tx = 0; ty = 0; if (!moving) moving = requestAnimationFrame(step); });
    }
  };
  // Özet sayfası gerçek yazı tipiyle ölçülür; yazı tipi gelmeden yapılan sığdırma yanlış boyda kalırdı.
  fontsReady.then(mount);
})();
