// Arşive sor sohbeti (Ask.cshtml). Dış kütüphane yok; betik olmadan form tek soruyu yine sorar.
// Sohbet yalnız bu sayfanın belleğinde durur: toplantı içeriği tarayıcıya kaydedilmez.
(() => {
  const root = document.querySelector("[data-ask]");
  if (!root) return;

  const find = name => root.querySelector(`[data-ask-${name}]`);
  const form = find("form");
  const input = find("input");
  const thread = find("thread");
  const send = find("send");
  const count = find("count");
  const status = find("status");
  const scope = find("scope");
  const scopeLabel = find("scope-label");
  const targets = [...root.querySelectorAll("[data-ask-target]")];
  const turnTemplate = document.getElementById("ask-turn-template");
  const problemTemplate = document.getElementById("ask-problem-template");
  const limit = input.maxLength;
  const still = window.matchMedia("(prefers-reduced-motion: reduce)");
  // Telefonda Enter yeni satır açar; gönderme düğmeyle.
  const touch = window.matchMedia("(pointer: coarse)");
  const blank = thread.childElementCount === 0;
  let running = null;

  // ---- Kapsam ----
  const scopeValue = () => form.elements.Scope.value;
  const targetSelect = () => form.querySelector(`[data-ask-target="${scopeValue()}"] select`);
  const day = value => value.split("-").reverse().join(".");

  const describeScope = () => {
    const option = targetSelect()?.selectedOptions[0];
    const place = scopeValue() === "Archive" ? "Tüm arşiv"
      : option?.dataset.label ?? (scopeValue() === "Series" ? "Bir seri" : "Bir toplantı");
    const from = form.elements.From.value;
    const to = form.elements.To.value;
    if (from && to) return `${place}, ${day(from)} ile ${day(to)} arası`;
    if (from) return `${place}, ${day(from)} ve sonrası`;
    if (to) return `${place}, ${day(to)} ve öncesi`;
    return place;
  };

  const isNarrowed = () => scopeValue() !== "Archive" || form.elements.From.value !== "" || form.elements.To.value !== "";

  const syncScope = () => {
    targets.forEach(target => { target.hidden = target.dataset.askTarget !== scopeValue(); });
    scopeLabel.textContent = describeScope();
    scope.classList.toggle("is-narrowed", isNarrowed());
    find("scope-clear").hidden = !isNarrowed();
  };

  const setScope = (value, target) => {
    form.elements.Scope.value = value;
    targets.forEach(item => {
      item.querySelector("select").value = item.dataset.askTarget === value && target ? target : "";
    });
    form.elements.From.value = "";
    form.elements.To.value = "";
    syncScope();
  };

  const closeScope = returnFocus => {
    if (!scope.open) return;
    scope.open = false;
    if (returnFocus) scope.querySelector("summary").focus();
  };

  scope.addEventListener("change", syncScope);
  find("scope-done").hidden = false;
  find("scope-done").addEventListener("click", () => { closeScope(false); input.focus(); });
  find("scope-clear").addEventListener("click", () => { setScope("Archive"); closeScope(false); input.focus(); });
  document.addEventListener("click", event => { if (!scope.contains(event.target)) closeScope(false); });
  document.addEventListener("keydown", event => {
    if (event.key !== "Escape" || !scope.open) return;
    event.preventDefault();
    closeScope(true);
  });

  // ---- Yazma alanı ----
  const syncInput = () => {
    input.style.height = "auto";
    input.style.height = `${input.scrollHeight}px`;
    const left = limit - input.value.length;
    count.hidden = left > 100;
    count.textContent = `${left} karakter kaldı`;
    send.classList.toggle("is-idle", !running && input.value.trim() === "");
  };

  const syncBusy = () => {
    root.classList.toggle("is-busy", running !== null);
    send.setAttribute("aria-label", running ? "Durdur" : "Sor");
    syncInput();
  };

  input.addEventListener("input", syncInput);
  input.addEventListener("keydown", event => {
    if (event.key !== "Enter" || event.shiftKey || event.isComposing || touch.matches) return;
    event.preventDefault();
    if (!running) form.requestSubmit();
  });

  // ---- Cevap ----
  // Metin hemen yerleşir (kaymaz), kelimeler sırayla görünür; bitince düz metne döner.
  const reveal = answer => {
    const text = answer.querySelector("[data-ask-text]");
    const full = text.textContent;
    const words = full.split(/(\s+)/);
    if (still.matches || full.trim() === "") return;
    const step = Math.min(34, 1600 / words.length);
    text.replaceChildren(...words.map((word, index) => {
      if (word.trim() === "") return document.createTextNode(word);
      const span = document.createElement("span");
      span.className = "ask-word";
      span.style.animationDelay = `${Math.round(index * step)}ms`;
      span.textContent = word;
      return span;
    }));
    const total = words.length * step;
    answer.style.setProperty("--ask-reveal", `${Math.round(total)}ms`);
    answer.classList.add("is-revealing");
    window.setTimeout(() => {
      text.textContent = full;
      answer.classList.remove("is-revealing");
    }, total + 700);
  };

  const announce = text => { status.textContent = text; };

  const problem = (message, link) => {
    const node = problemTemplate.content.firstElementChild.cloneNode(true);
    node.querySelector("[data-ask-text]").textContent = message;
    if (link) {
      const anchor = node.querySelector('[data-slot="link"]');
      anchor.href = link.href;
      anchor.textContent = link.text;
      anchor.hidden = false;
      node.querySelector("[data-ask-retry]").remove();
    }
    return node;
  };

  const wireAnswer = (answer, ask) => {
    const text = answer.querySelector("[data-ask-text]").textContent;
    const copy = answer.querySelector("[data-ask-copy]");
    if (copy && navigator.clipboard) {
      const label = copy.querySelector("[data-ask-copy-label]");
      copy.hidden = false;
      copy.addEventListener("click", async () => {
        try {
          await navigator.clipboard.writeText(text);
          label.textContent = "Kopyalandı";
        } catch {
          label.textContent = "Kopyalanamadı";
        }
        window.setTimeout(() => { label.textContent = "Cevabı kopyala"; }, 2000);
      });
    }
    if (!ask) return;
    const again = (button, data) => {
      if (!button) return;
      button.hidden = false;
      button.addEventListener("click", () => {
        if (running) return;
        if (data !== ask) setScope("Archive");
        run(data.question, data.fields, data.label);
      });
    };
    again(answer.querySelector("[data-ask-retry]"), ask);
    again(answer.querySelector("[data-ask-widen]"), {
      question: ask.question,
      fields: { Scope: "Archive", SeriesId: "", MeetingId: "", From: "", To: "" },
      label: ""
    });
  };

  // Sunucunun cevabı Razor'dan gelir (kaçışlı HTML); model metni burada hiç HTML olarak yorumlanmaz.
  const read = async response => {
    if (response.status === 401 || response.redirected)
      return problem("Oturumunuz kapanmış. Giriş yapıp tekrar sorun.", { href: "/Auth/Login?returnUrl=%2FAsk", text: "Giriş yap" });
    if (response.status === 400)
      return problem("Sayfa uzun süre açık kalmış. Yenileyip tekrar sorun.", { href: "/Ask", text: "Sayfayı yenile" });
    if (!response.ok) return problem("Cevap alınamadı. Biraz sonra tekrar sorun.");
    const parsed = new DOMParser().parseFromString(await response.text(), "text/html");
    return parsed.querySelector("[data-ask-answer]") ?? problem("Cevap alınamadı. Biraz sonra tekrar sorun.");
  };

  async function run(question, fields, label) {
    const turn = turnTemplate.content.firstElementChild.cloneNode(true);
    const slot = name => turn.querySelector(`[data-slot="${name}"]`);
    const reply = turn.querySelector(".ask-reply");
    const waiting = slot("waiting");
    const body = slot("answer");
    const ask = { question, fields, label };
    slot("question").textContent = question;
    if (label) slot("scope").textContent = label;
    else slot("scope").remove();

    root.dataset.state = "chat";
    thread.querySelector(".is-last")?.classList.remove("is-last");
    turn.classList.add("is-last");
    thread.append(turn);

    const say = text => {
      waiting.textContent = text;
      announce(text);
    };
    say("Toplantılarınıza bakıyorum");
    const timers = [
      window.setTimeout(() => say("Cevabı hazırlıyorum"), 5000),
      window.setTimeout(() => say("Biraz uzun sürdü, hâlâ hazırlıyorum"), 20000)
    ];
    running = new AbortController();
    syncBusy();
    turn.scrollIntoView({ block: "start", behavior: still.matches ? "auto" : "smooth" });

    const data = new FormData();
    data.set("__RequestVerificationToken", form.elements.__RequestVerificationToken.value);
    data.set("Question", question);
    Object.entries(fields).forEach(([name, value]) => data.set(name, value));

    let answer;
    try {
      const response = await fetch(form.action, {
        method: "POST",
        body: data,
        headers: { "X-Requested-With": "XMLHttpRequest" },
        signal: running.signal
      });
      answer = await read(response);
    } catch (error) {
      answer = problem(error.name === "AbortError" ? "Durdurdum." : "Bağlantı kurulamadı. İnternetinizi kontrol edip tekrar sorun.");
    }
    timers.forEach(window.clearTimeout);
    running = null;
    announce(answer.querySelector("[data-ask-text]").textContent);
    wireAnswer(answer, ask);
    body.replaceChildren(answer);
    reply.classList.remove("is-thinking");
    reply.classList.add("is-done");
    reveal(answer);
    syncBusy();
  }

  form.addEventListener("submit", event => {
    event.preventDefault();
    if (running) {
      running.abort();
      return;
    }
    const question = input.value.trim();
    if (question === "") {
      input.focus();
      return;
    }
    const select = targetSelect();
    if (select && select.value === "") {
      scope.open = true;
      select.focus();
      announce(scopeValue() === "Series" ? "Lütfen bir seri seçin." : "Lütfen bir toplantı seçin.");
      return;
    }
    const fields = Object.fromEntries(["Scope", "SeriesId", "MeetingId", "From", "To"].map(name => [name, form.elements[name].value]));
    const label = isNarrowed() ? describeScope() : "";
    closeScope(false);
    input.value = "";
    run(question, fields, label);
    if (!touch.matches) input.focus();
  });

  root.querySelectorAll("[data-ask-starter]").forEach(button => {
    button.hidden = false;
    button.addEventListener("click", () => {
      if (running) return;
      setScope(button.dataset.scope, button.dataset.target);
      input.value = button.textContent.trim();
      form.requestSubmit();
      // Kapsam yalnız bu soru için: sonraki soru yine tüm arşive sorulur.
      setScope("Archive");
    });
  });

  // Yeni sohbet: sayfa yeniden yüklenmeden başa döner.
  find("new").addEventListener("click", event => {
    event.preventDefault();
    running?.abort();
    thread.replaceChildren();
    root.dataset.state = "empty";
    announce("");
    input.value = "";
    setScope("Archive");
    syncInput();
    window.scrollTo(0, 0);
    input.focus();
  });

  thread.querySelectorAll("[data-ask-answer]").forEach(answer => wireAnswer(answer, null));
  syncScope();
  syncInput();
  if (blank && !touch.matches) input.focus();
})();
