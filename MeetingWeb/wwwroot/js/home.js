// Ana sayfanın (/tr) kendi davranışı. Dış kütüphane yok; scroll-craft motoru (wwwroot/lib/scrollcraft) değiştirilmeden kullanılır.
// Tasarım kaynağı: ~/scrollcraft/builds/briefyn/BRIEF.md (5. bölüm düzen, 6. bölüm zirve senaryosu, 7. bölüm teknik kararlar).
//
// İki düzen var, ikisi de aynı örnek veriden ve aynı state(p) fonksiyonundan çıkar:
// - Hareketli ("sc-on", Home.cshtml başındaki satır içi betik koyar): yolculuk pinlenir, imza hareketi "dalga satıra yatar"
//   motorun yayınladığı --sc-p ile sürülür.
// - Durağan (hareketi azalt açık, ekran 500 pikselden kısa ya da motor bağlanamadı): motor hiç bağlanmaz; her adımın yanında
//   sahnenin o adımın sonundaki hâli bir kez çizilir. Kaydırmaya bağlı canlandırma yok.
(() => {
  const doc = document.documentElement;
  const hero = document.querySelector("[data-home-hero]");
  const journey = document.querySelector("[data-home-journey]");
  if (!hero || !journey) return;

  const small = window.matchMedia("(max-width: 860px)");
  const reduce = window.matchMedia("(prefers-reduced-motion: reduce)");

  // ---- Örnek toplantı (tek kaynak). Gerçek kişi ya da müşteri değil; sahnenin üstünde "Örnek toplantı" yazar. ----
  // keep: özette hangi satıra dönüştüğü. wide: yalnız geniş ekranda gösterilen satır (telefonda ilk altı satır).
  const MEETING = {
    file: "haftalik-urun.m4a",
    speakers: {
      1: { label: "Konuşmacı 1", name: "Elif", tone: "#0A6F94" },
      2: { label: "Konuşmacı 2", name: "Burak", tone: "#2F5BEA" },
      3: { label: "Konuşmacı 3", name: "Mert", tone: "#0E4458" }
    },
    lines: [
      { speaker: 1, text: "Lansmanı ayın sonuna mı alıyoruz, yoksa bekliyor muyuz?", keep: "topic" },
      { speaker: 2, text: "Ödeme ekranı bitmeden lansman olmaz bence." },
      { speaker: 2, text: "Geçen haftaki geri bildirimler de hep ödeme ekranıyla ilgiliydi.", wide: true },
      { speaker: 3, text: "En çok kart bilgisi adımı soruldu.", wide: true },
      { speaker: 1, text: "Tamam, lansman ödeme ekranı bitince.", keep: "decision" },
      { speaker: 3, text: "Ödeme ekranını cumaya bitiririm.", keep: "todo0" },
      { speaker: 2, text: "Ben de müşterilere duyuru metnini hazırlarım.", keep: "todo1" },
      { speaker: 1, text: "Fiyat konusunu gelecek hafta konuşalım." },
      { speaker: 1, text: "Başka konu yoksa burada bitirelim.", wide: true }
    ],
    summary: [
      { heading: "Özet" },
      { key: "topic", text: "Lansman tarihi ve ödeme ekranı konuşuldu." },
      { heading: "Kararlar" },
      { key: "decision", text: "Lansman, ödeme ekranı bitince yapılacak." },
      { heading: "Yapılacaklar" },
      { key: "todo0", text: "Ödeme ekranını bitirmek (Mert, cuma)" },
      { key: "todo1", text: "Duyuru metnini hazırlamak (Burak)" }
    ],
    question: "Lansman tarihini hangi toplantıda konuştuk?",
    answer: "Lansman, ödeme ekranı bitince yapılacak.",
    source: "Kaynak: Haftalık ürün toplantısı",
    invite: { title: "Ödeme ekranını bitirmek", meta: "Mert, cuma", label: "Takvim daveti" },
    mail: "Özet e-postayla gönderildi: ekip"
  };
  const BAR_HEIGHTS = [0.16, 0.34, 0.24, 0.52, 0.78, 0.44, 1, 0.62, 0.9, 0.7, 0.4, 0.86, 0.58, 0.98, 0.76, 0.48, 0.92, 0.6, 0.32, 0.74, 0.5, 0.26, 0.38, 0.2];

  const clamp01 = x => (x < 0 ? 0 : x > 1 ? 1 : x);
  const ramp = (p, from, length) => clamp01((p - from) / length);
  const ease = t => t * t * (3 - 2 * t);
  const mix = (a, b, t) => a + (b - a) * t;
  const SVG = "http://www.w3.org/2000/svg";

  const el = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text) node.textContent = text;
    return node;
  };
  const svgEl = (tag, attrs) => {
    const node = document.createElementNS(SVG, tag);
    Object.entries(attrs || {}).forEach(([key, value]) => node.setAttribute(key, value));
    return node;
  };
  const icon = (paths, className) => {
    const svg = svgEl("svg", { viewBox: "0 0 20 20", class: className, focusable: "false" });
    svg.innerHTML = paths;
    return svg;
  };

  // Konum, dönüşümlerden etkilenmesin diye offset zinciriyle ölçülür (sahnenin kendisine göre).
  const offsetIn = (node, root) => {
    let x = 0, y = 0;
    for (let n = node; n && n !== root; n = n.offsetParent) { x += n.offsetLeft; y += n.offsetTop; }
    return { x, y, w: node.offsetWidth, h: node.offsetHeight };
  };

  // ---- Sahne: örnek toplantının levhası. Metin hep HTML; SVG yalnız çubuk ve çizgi. ----
  function buildScene(host, opts) {
    const lines = MEETING.lines.filter(line => opts.wide || !line.wide);
    // Pinli sahne kısa ekranda bütün satırları alamaz: özete dönüşmeyen satırlar sondan başlayarak çıkarılır (en az dört kalır).
    if (opts.fit) {
      const rowHeight = opts.wide ? 41 : 48;
      const room = Math.floor((host.clientHeight - (opts.wide ? 330 : 170)) / rowHeight);
      for (let i = lines.length - 1; i >= 0 && lines.length > Math.max(room, 4); i--) {
        if (!lines[i].keep) lines.splice(i, 1);
      }
    }
    const scene = el("div", "hs" + (opts.wide ? " hs--wide" : " hs--narrow") + (opts.mini ? " hs--mini" : ""));
    const file = el("div", "hs-file");
    file.append(icon('<path d="M5 2.75h6.5L15 6.25v11H5z" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round"/><path d="M8 10.5v3M10 9v6M12 11v2" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/>', "hs-icon"), el("span", null, MEETING.file));

    const page = el("div", "hs-doc");
    const rowsBox = el("div", "hs-rows");
    const rows = lines.map(line => {
      const row = el("div", "hs-row");
      const tag = el("span", "hs-tag");
      const tagA = el("span", "hs-tag-a", MEETING.speakers[line.speaker].label);
      const tagB = el("span", "hs-tag-b", MEETING.speakers[line.speaker].name);
      tagB.style.color = MEETING.speakers[line.speaker].tone;
      tag.append(tagA, tagB);
      const text = el("span", "hs-text", line.text);
      row.append(tag, text);
      rowsBox.append(row);
      return { line, row, tagA, tagB, text };
    });
    const sumBox = el("div", "hs-sum");
    const sumItems = {};
    const sumNodes = MEETING.summary.map(item => {
      const node = item.heading ? el("p", "hs-h", item.heading) : el("p", "hs-item", item.text);
      if (item.key) sumItems[item.key] = node;
      sumBox.append(node);
      return node;
    });
    page.append(rowsBox, sumBox);

    const tools = el("div", "hs-tools");
    const ask = el("div", "hs-ask");
    const question = el("div", "hs-q");
    question.append(icon('<circle cx="8.5" cy="8.5" r="5" fill="none" stroke="currentColor" stroke-width="1.8"/><path d="m12.3 12.3 4.2 4.2" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/>', "hs-icon"));
    const questionText = el("span", "hs-q-text");
    question.append(questionText);
    const chars = [...MEETING.question].map(ch => {
      const span = el("span", "hs-ch", ch);
      questionText.append(span);
      return span;
    });
    const answer = el("div", "hs-a");
    const answerText = el("p", "hs-a-text", MEETING.answer);
    const source = el("p", "hs-src", MEETING.source);
    answer.append(answerText, source);
    ask.append(question, answer);

    const invite = el("div", "hs-invite");
    const inviteBody = el("div", "hs-invite-body");
    inviteBody.append(el("span", "hs-invite-label", MEETING.invite.label), el("strong", null, MEETING.invite.title), el("span", "hs-invite-meta", MEETING.invite.meta));
    invite.append(icon('<rect x="3" y="4.5" width="14" height="12.5" rx="2" fill="none" stroke="currentColor" stroke-width="1.6"/><path d="M3 8.5h14M7 2.75v3.5M13 2.75v3.5" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/>', "hs-icon hs-icon-lg"), inviteBody);
    const mail = el("div", "hs-mail");
    mail.append(icon('<rect x="2.75" y="4.75" width="14.5" height="10.5" rx="1.75" fill="none" stroke="currentColor" stroke-width="1.6"/><path d="m3.5 5.75 6.5 5 6.5-5" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round"/>', "hs-icon"), el("span", null, MEETING.mail));
    tools.append(ask, invite, mail);

    const ink = svgEl("svg", { class: "hs-ink", focusable: "false" });
    const defs = svgEl("defs");
    const grad = svgEl("linearGradient", { id: opts.id + "-ink", gradientUnits: "userSpaceOnUse", x1: "0", y1: "0", x2: "1", y2: "0" });
    grad.append(svgEl("stop", { offset: "0", "stop-color": "#0697B4" }), svgEl("stop", { offset: "1", "stop-color": "#2F6BF2" }));
    defs.append(grad);
    ink.append(defs);

    scene.append(file, page, tools, ink);
    host.replaceChildren(scene);

    // Çubuklar: her satır bir çubuktan doğar; kalan çubuklar yalnız sesin kendisi, satıra dönüşmez, söner.
    const count = opts.wide ? 24 : 12;
    const speakerOf = new Map();
    rows.forEach((row, j) => speakerOf.set(Math.round(((j + 0.5) * count) / rows.length - 0.5), j));
    const bars = Array.from({ length: count }, (_, i) => {
      const g = svgEl("g", { class: "hs-bar" });
      const a = svgEl("path", { class: "hs-bar-a", stroke: "url(#" + opts.id + "-ink)" });
      g.append(a);
      let b = null;
      const row = speakerOf.has(i) ? rows[speakerOf.get(i)] : null;
      if (row) {
        b = svgEl("path", { class: "hs-bar-b", stroke: MEETING.speakers[row.line.speaker].tone });
        g.append(b);
      }
      ink.append(g);
      return { i, g, a, b, row, rowIndex: row ? speakerOf.get(i) : -1, height: BAR_HEIGHTS[Math.round((i * (BAR_HEIGHTS.length - 1)) / Math.max(count - 1, 1))] };
    });

    const state = { scene, file, page, rowsBox, rows, sumBox, sumNodes, sumItems, tools, ask, chars, answerText, source, invite, mail, ink, grad, bars, opts, geo: null, p: 0 };
    layoutScene(state);
    return state;
  }

  // Geometri: çubukların dalgadaki yeri ve satırların, özet satırlarının çizgi başlangıçları ölçülür.
  function layoutScene(s) {
    const stroke = s.opts.wide ? 6 : 5;
    const k = s.opts.wide ? 10 : 8;
    const W = s.scene.clientWidth;
    const H = s.scene.clientHeight;
    s.ink.setAttribute("viewBox", "0 0 " + W + " " + H);
    s.ink.setAttribute("width", W);
    s.ink.setAttribute("height", H);
    s.grad.setAttribute("x2", String(W));

    // Dalga, satırların altındaki boş yerde durur; çubuklar oradan yukarı, kendi satırlarına yatar.
    const page = offsetIn(s.page, s.scene);
    const rowsBottom = page.y + s.rowsBox.offsetHeight;
    const room = page.y + page.h - rowsBottom;
    const limit = s.opts.wide ? 150 : 84;
    const fitsBelow = room >= 56;
    const hMax = fitsBelow ? Math.max(Math.min(room - 20, limit), 40) : Math.min(page.h * 0.5, limit);
    const midY = fitsBelow ? rowsBottom + room / 2 : page.y + page.h / 2;
    const padX = s.opts.wide ? 26 : 14;
    const step = (page.w - padX * 2) / Math.max(s.bars.length - 1, 1);
    const sumPos = {};
    Object.entries(s.sumItems).forEach(([key, node]) => { sumPos[key] = offsetIn(node, s.scene); });

    s.bars.forEach(bar => {
      const h = Math.max(hMax * bar.height, k * 2 + 4);
      const bx = page.x + padX + bar.i * step;
      const geo = { h, bx, k, v: h - k, stroke };
      if (bar.row) {
        const row = offsetIn(bar.row.row, s.scene);
        const text = offsetIn(bar.row.text, s.scene);
        const lineY = text.y + text.h + 3 + stroke / 2;
        // Çizginin görünen başı (kıvrımdan sonra) metnin sol kenarına hizalanır.
        const x0 = text.x - k + stroke / 2;
        let L = row.x + row.w - x0;
        const keep = bar.row.line.keep;
        if (keep && sumPos[keep]) {
          // Satır, metni özetteki metnin tam yerine gelecek kadar taşınır; çizgisi de aynı kadar.
          const item = sumPos[keep];
          geo.to = { x: x0 + item.x - text.x, y: lineY + item.y - text.y };
          L = Math.min(L, item.w);
        }
        geo.L = Math.max(L, h + k);
        geo.line = { x: x0, y: lineY };
        // Yol çizginin sağ ucundan başlar, kıvrımdan (logodaki gibi) geçer, çubuğun tepesinde biter.
        const d = "M" + geo.L + " 0H" + k + "Q0 0 0 " + -k + "V" + -h;
        bar.a.setAttribute("d", d);
        bar.b.setAttribute("d", d);
        geo.T = bar.a.getTotalLength();
        geo.window = geo.L - k;
        const dash = geo.window + " " + (geo.T + geo.window + 10);
        [bar.a, bar.b].forEach(path => { path.setAttribute("stroke-dasharray", dash); path.setAttribute("stroke-width", stroke); });
      } else {
        bar.a.setAttribute("d", "M0 0V" + -h);
        geo.T = h;
        geo.v = h;
        geo.window = h;
        bar.a.setAttribute("stroke-dasharray", h + " " + (h * 2 + 10));
        bar.a.setAttribute("stroke-width", stroke);
      }
      geo.midY = midY;
      bar.geo = geo;
    });

    // Davet kartı, "Ödeme ekranını bitirmek" satırından ayrılır.
    const todo = sumPos.todo0;
    const card = offsetIn(s.invite, s.scene);
    s.inviteFrom = todo ? { x: todo.x - card.x, y: todo.y - card.y } : { x: 0, y: 0 };
    apply(s, s.p);
  }

  // Sahnenin p anındaki hâli (p: yolculuğun kaydırma payı, 0-1). Yalnız transform, opacity ve stroke-dashoffset yazılır.
  function apply(s, p) {
    s.p = p;
    const wide = s.opts.wide;
    const n = s.rows.length;

    // Yükleme: dosya etiketi levhaya kayar.
    const fileIn = ease(ramp(p, 0.08, 0.05));
    s.file.style.opacity = fileIn.toFixed(3);
    s.file.style.transform = "translate3d(" + ((1 - fileIn) * -24).toFixed(1) + "px,0,0)";

    // Konuşmacılar ve özet zamanlaması.
    const nameSwap = ease(ramp(p, 0.475, 0.045));
    const tagIn = ease(ramp(p, 0.425, 0.03));
    const move = ease(ramp(p, 0.565, 0.07));
    // Taşınan satırın konuşma hâli, yerine varınca özetteki cümlesine dönüşür.
    const keepOut = ease(ramp(p, 0.622, 0.014));
    const dim = ease(ramp(p, 0.72, 0.04)) * (1 - ease(ramp(p, 0.86, 0.03)));

    let laid = 0;
    s.bars.forEach(bar => {
      const g = bar.geo;
      if (!g) return;
      // Ses gelir: çubuk noktadan tam boyuna uzar, ortası dalganın orta çizgisinde kalır.
      const grow = ease(ramp(p, 0.10 + (0.08 * bar.i) / s.bars.length, 0.045));
      const x = mix(1, g.v, grow);
      if (!bar.row) {
        const out = ease(ramp(p, 0.22 + (0.13 * bar.i) / s.bars.length, 0.045));
        bar.g.setAttribute("transform", "translate(" + g.bx.toFixed(1) + " " + (g.midY + g.h - x / 2).toFixed(1) + ")");
        bar.a.setAttribute("stroke-dashoffset", (-(g.T - x)).toFixed(2));
        bar.g.style.opacity = (1 - out).toFixed(3);
        return;
      }
      // İmza hareketi ("dalga satıra yatar"): sırası gelen çubuk önce dik hâliyle satırının başına iner,
      // sonra logodaki kıvrımla yatıp sağa doğru satır çizgisine uzar.
      const j = bar.rowIndex;
      const lay = ramp(p, 0.22 + (j * 0.115) / Math.max(n - 1, 1), 0.065);
      const travel = ease(ramp(lay, 0, 0.5));
      const flow = ease(ramp(lay, 0.4, 0.6));
      const origin0 = { x: g.bx, y: g.midY + g.h - x / 2 };
      let ox = mix(origin0.x, g.line.x, travel);
      let oy = mix(origin0.y, g.line.y, travel);
      laid += flow / n;
      const offset = mix(g.T - x, 0, flow);
      let opacity = 1;
      let shiftX = 0, shiftY = 0;
      const keep = bar.row.line.keep;
      if (keep && g.to) {
        // Özete kalan satır, metniyle birlikte özetteki yerine taşınır.
        shiftX = (g.to.x - g.line.x) * move;
        shiftY = (g.to.y - g.line.y) * move;
        ox += shiftX;
        oy += shiftY;
        if (keep !== "decision") opacity = 1 - 0.38 * dim;
      } else {
        opacity = 1 - ease(ramp(p, 0.54 + (0.025 * j) / n, 0.035));
      }
      bar.g.setAttribute("transform", "translate(" + ox.toFixed(1) + " " + oy.toFixed(1) + ")");
      bar.a.setAttribute("stroke-dashoffset", (-offset).toFixed(2));
      bar.b.setAttribute("stroke-dashoffset", (-offset).toFixed(2));
      bar.b.style.opacity = (nameSwap * (1 - move)).toFixed(3);
      bar.g.style.opacity = opacity.toFixed(3);

      // Satırın metni, çizgisi yerine oturunca belirir; özete geçerken söner.
      const row = bar.row;
      const textIn = ramp(flow, 0.5, 0.45);
      const out = keep ? keepOut : 1 - opacity;
      row.text.style.opacity = (textIn * (1 - out)).toFixed(3);
      row.text.style.transform = "translate3d(0," + ((1 - textIn) * 6).toFixed(1) + "px,0)";
      row.row.style.transform = keep ? "translate3d(" + shiftX.toFixed(1) + "px," + shiftY.toFixed(1) + "px,0)" : "";
      const tagOut = keep ? ease(ramp(p, 0.555, 0.03)) : out;
      row.tagA.style.opacity = (tagIn * (1 - nameSwap) * (1 - tagOut)).toFixed(3);
      row.tagB.style.opacity = (nameSwap * (1 - tagOut)).toFixed(3);
    });

    // Özet: üç başlık ve altında kalan satırlar.
    s.sumNodes.forEach((node, index) => {
      const shown = node.classList.contains("hs-h") ? ease(ramp(p, 0.59 + index * 0.006, 0.04)) : ease(ramp(p, 0.636, 0.016));
      const isDecision = node === s.sumItems.decision;
      // Soru sorulurken cevabın geldiği satır öne çıkar; ötekiler hafifçe geri çekilir ama 4,5:1'in altına inmez (en az 0,62 opaklık).
      const dimmed = isDecision || node.classList.contains("hs-h") ? 0 : 0.38 * dim;
      node.style.opacity = (shown * (1 - dimmed)).toFixed(3);
      node.style.transform = "translate3d(0," + ((1 - shown) * 6).toFixed(1) + "px,0)";
    });
    if (s.sumItems.decision) s.sumItems.decision.classList.toggle("is-found", dim > 0.5);

    // Arşive sor: soru kaydırdıkça harf harf yazılır, cevap kaynağıyla gelir.
    const askIn = ease(ramp(p, 0.70, 0.03)) * (1 - ease(ramp(p, 0.86, 0.03)));
    s.ask.style.opacity = askIn.toFixed(3);
    s.ask.style.transform = "translate3d(0," + ((1 - ease(ramp(p, 0.70, 0.03))) * 10).toFixed(1) + "px,0)";
    const typed = ramp(p, 0.72, 0.075) * s.chars.length;
    s.chars.forEach((ch, index) => { ch.style.opacity = index < typed ? "1" : "0"; });
    const answerIn = ease(ramp(p, 0.80, 0.03));
    s.answerText.style.opacity = answerIn.toFixed(3);
    s.source.style.opacity = ease(ramp(p, 0.82, 0.03)).toFixed(3);

    // Takvim ve paylaşma: iş satırı ayrılıp davet kartı olur; yanında e-posta etiketi.
    const travel = ease(ramp(p, 0.88, 0.07));
    const from = wide ? s.inviteFrom : { x: 0, y: 48 };
    s.invite.style.opacity = ease(ramp(p, 0.88, 0.025)).toFixed(3);
    s.invite.style.transform = "translate3d(" + mix(from.x, 0, travel).toFixed(1) + "px," + mix(from.y, 0, travel).toFixed(1) + "px,0)";
    const mailIn = ease(ramp(p, 0.93, 0.03));
    s.mail.style.opacity = mailIn.toFixed(3);
    s.mail.style.transform = "translate3d(" + ((1 - mailIn) * (wide ? -12 : 0)).toFixed(1) + "px," + ((1 - mailIn) * (wide ? 0 : 8)).toFixed(1) + "px,0)";

    // Doğrulama aracı (shoot.mjs) için: sahnede gerçekten boyanan değerlerin kısa imzası.
    s.scene.setAttribute("data-sc-verify-state", [fileIn, laid, nameSwap, move, askIn, travel, mailIn].map(v => v.toFixed(2)).join(" "));
  }

  const scenes = [];
  const relayout = () => scenes.forEach(layoutScene);
  let resizeFrame = 0;
  const observer = "ResizeObserver" in window ? new ResizeObserver(() => {
    cancelAnimationFrame(resizeFrame);
    resizeFrame = requestAnimationFrame(relayout);
  }) : null;
  const track = scene => { scenes.push(scene); if (observer) observer.observe(scene.scene); };

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
    // Durağan düzen: her adımın yanında sahnenin o adımın sonundaki hâli. Kaydırmaya bağlı hiçbir şey yok.
    fontsReady.then(() => {
      journey.querySelectorAll("[data-hj-mini]").forEach((host, index) => {
        const end = parseFloat(host.getAttribute("data-hj-mini")) || 1;
        const scene = buildScene(host, { wide: false, mini: true, id: "hj-mini-" + index });
        // Soru ve davet alanı yalnız o adımlarda yer kaplar.
        scene.tools.hidden = end < 0.68;
        apply(scene, end);
        track(scene);
      });
      // scroll-craft'ın doğrulama aracı sayfanın hazır olduğunu bu sınıftan anlar; motor bağlanmadığı için sayfa kendisi koyar.
      doc.classList.add("sc-ready");
    });
    return;
  }

  // ---- Hareketli düzen ----
  hero.querySelector(".hh-panel").setAttribute("data-sc-parallax", small.matches ? "-1.1" : "0.5");
  journey.setAttribute("data-sc-act", "pin");
  journey.setAttribute("data-sc-span", small.matches ? "4.4" : "5");
  const closing = document.querySelector("[data-home-closing]");
  if (closing) {
    closing.setAttribute("data-sc-act", "pin");
    closing.setAttribute("data-sc-span", small.matches ? "1.2" : "1.3");
  }

  const sceneHost = journey.querySelector("[data-hj-scene]");
  const mount = () => {
    const main = ScrollCraft.mount(document.querySelector("main"));
    const scene = buildScene(sceneHost, { wide: !small.matches, fit: true, id: "hj-scene" });
    track(scene);
    let frame = 0;
    const update = () => {
      frame = 0;
      const p = parseFloat(journey.style.getPropertyValue("--sc-p")) || 0;
      if (Math.abs(p - scene.p) > 0.0004) apply(scene, p);
    };
    const request = () => { if (!frame) frame = requestAnimationFrame(update); };
    window.addEventListener("scroll", request, { passive: true });
    window.addEventListener("resize", request, { passive: true });
    update();

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
    // Pencere telefon ve masaüstü genişliği arasında geçerse sahne o düzene göre yeniden kurulur.
    small.addEventListener("change", () => {
      scenes.splice(scenes.indexOf(scene), 1);
      const next = buildScene(sceneHost, { wide: !small.matches, fit: true, id: "hj-scene" });
      Object.assign(scene, next);
      apply(scene, parseFloat(journey.style.getPropertyValue("--sc-p")) || 0);
      track(scene);
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
  // Satırlar gerçek yazı tipiyle ölçülür; yazı tipi gelmeden çizilen çizgiler yanlış uzunlukta kalırdı.
  fontsReady.then(mount);
})();
