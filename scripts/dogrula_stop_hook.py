#!/usr/bin/env python3
"""Claude Stop hook'u (2026-10-19): kod değişti ama scripts/dogrula.sh geçmediyse "bitti" dedirtmez.

Çalışma ağacının durumu (izlenmeyen dosyalar dahil, .gitignore'a uyarak) bir git tree hash'iyle
ölçülür; dogrula.sh geçince bu hash `<git dizini>/dogrula-ok` dosyasına yazılır (--damgala).
Stop anında commit'lenmemiş kod değişikliği varsa ve hash damgayla aynı değilse cevabı bir kez
durdurur. Yalnız doküman (docs/, *.md) değiştiyse karışmaz. Korumalar: stop_hook_active ise
çalışmaz (döngü yok), her hata sessizce yutulur. Model çağrısı yok.
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile


def git(*args, env=None, ham=False):
    cikti = subprocess.run(["git", *args], capture_output=True, text=True, timeout=30, env=env).stdout
    # Porcelain çıktısında baştaki boşluk durum sütununun parçası; ham=True onu korur.
    return cikti if ham else cikti.strip()


def agac_hash():
    """Çalışma ağacındaki kodun tree hash'i; gerçek index'e dokunmadan geçici bir index kopyasıyla.
    Doküman (docs/, *.md) eklenmez: yalnız rapor yazmak damgayı bozmasın (inceleme bulgusu 3)."""
    with tempfile.TemporaryDirectory() as tmp:
        idx = os.path.join(tmp, "index")
        gercek = git("rev-parse", "--git-path", "index")
        if os.path.exists(gercek):
            shutil.copy(gercek, idx)
        env = dict(os.environ, GIT_INDEX_FILE=idx)
        git("add", "-A", "--", ".", ":(exclude)docs", ":(exclude)*.md", env=env)
        # Kopyalanan index'teki doküman kayıtları da çıkarılır; yalnız `git add` edilmiş rapor da sayılmasın.
        git("rm", "--cached", "-r", "-q", "-f", "--ignore-unmatch", "--", "docs", "*.md", env=env)
        return git("write-tree", env=env)


def damga_yolu():
    return git("rev-parse", "--git-path", "dogrula-ok")


def kod_degisti():
    satirlar = git("-c", "core.quotepath=false", "status", "--porcelain", ham=True).splitlines()
    yollar = [s[3:].split(" -> ")[-1].strip('"') for s in satirlar if s.strip()]
    return [y for y in yollar if not (y.startswith("docs/") or y.endswith(".md"))]


def main():
    if "--hash" in sys.argv:
        print(agac_hash())
        return
    if "--damgala" in sys.argv:
        # dogrula.sh testten önce aldığı hash'i verir; testler sırasında kod değiştiyse damgalamaz.
        bas = sys.argv[sys.argv.index("--damgala") + 1] if len(sys.argv) > sys.argv.index("--damgala") + 1 else ""
        simdi = agac_hash()
        if bas and bas != simdi:
            print("Doğrulama: KALDI (testler sürerken kod değişti; yeniden çalıştır)")
            sys.exit(1)
        with open(damga_yolu(), "w") as f:
            f.write(simdi + "\n")
        return
    try:
        olay = json.load(sys.stdin)
    except ValueError:
        olay = {}
    if olay.get("stop_hook_active"):
        print("{}")
        return
    try:
        os.chdir(git("rev-parse", "--show-toplevel") or ".")
        degisen = kod_degisti()
        damga = open(damga_yolu()).read().strip() if os.path.exists(damga_yolu()) else ""
        if degisen and agac_hash() != damga:
            print(json.dumps({"decision": "block", "reason": (
                f"Kod değişti ({len(degisen)} dosya, ör. {degisen[0]}) ama scripts/dogrula.sh bu haliyle "
                "geçmedi. Çalıştır; geçerse sonucu (GEÇTİ satırı) rapora yaz, kalırsa düzelt. "
                "Kontrol geçmeden 'bitti' deme (AGENTS.md, Doğrulama).")}, ensure_ascii=False))
            return
    except Exception:
        pass
    print("{}")


if __name__ == "__main__":
    main()
