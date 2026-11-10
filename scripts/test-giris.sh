#!/bin/sh
# Yerel test hesabıyla giriş yapıp Playwright oturumunu kaydeder.
set -u
umask 077

KOK=$(git rev-parse --show-toplevel 2>/dev/null) || {
  echo "Hata: proje dizini bulunamadı"
  exit 1
}
cd "$KOK" || exit 1

if [ ! -f .env ] || ! command -v python3 >/dev/null 2>&1 || ! command -v playwright-cli >/dev/null 2>&1; then
  echo "Hata: .env, Python veya Playwright CLI eksik"
  exit 1
fi

if [ -L .auth ]; then
  echo "Hata: oturum dizini geçersiz"
  exit 1
fi
mkdir -p -m 700 .auth || {
  echo "Hata: oturum dizini oluşturulamadı"
  exit 1
}
chmod 700 .auth || exit 1
KOD_DOSYASI=$(mktemp .auth/test-giris.XXXXXXXX) || {
  echo "Hata: geçici dosya oluşturulamadı"
  exit 1
}
OTURUM="briefyn-test-giris-$$"
temizle() {
  playwright-cli -s="$OTURUM" close >/dev/null 2>&1 || true
  rm -f "$KOD_DOSYASI"
}
trap temizle EXIT HUP INT TERM

# Kimlik bilgileri yalnızca 600 izinli geçici koda yazılır; CLI argümanlarına geçmez.
if ! python3 - "$KOD_DOSYASI" <<'PY'
import json
import re
import sys
from pathlib import Path

try:
    values = {}
    for line in Path('.env').read_text(encoding='utf-8-sig').splitlines():
        match = re.match(r'^\s*(?:export\s+)?(TEST_USER_EMAIL|TEST_USER_PASSWORD)\s*=\s*(.*)$', line)
        if not match:
            continue
        value = match.group(2).strip()
        if value.startswith(('"', "'")):
            quote = value[0]
            if len(value) < 2 or value[-1] != quote:
                raise ValueError('bad quote')
            value = value[1:-1]
        else:
            value = re.split(r'\s+#', value, maxsplit=1)[0].rstrip()
        values[match.group(1)] = value
    email = values['TEST_USER_EMAIL']
    password = values['TEST_USER_PASSWORD']
    if not email or not password:
        raise ValueError('empty value')
    code = (
        'async page => {\n'
        '  const email = ' + json.dumps(email) + ';\n'
        '  const password = ' + json.dumps(password) + ';\n'
        '  try {\n'
        "    await page.locator('#Input_Email').fill(email);\n"
        "    await page.locator('#Input_Password').fill(password);\n"
        "    await Promise.all([\n"
        "      page.waitForURL(url => url.origin === 'http://127.0.0.1:8080' && !url.pathname.startsWith('/Auth/Login'), { timeout: 10000 }),\n"
        "      page.locator('button[type=submit]').click()\n"
        '    ]);\n'
        '  } finally {\n'
        "    await page.goto('about:blank');\n"
        '  }\n'
        '}\n'
    )
    Path(sys.argv[1]).write_text(code, encoding='utf-8')
except Exception:
    sys.exit(1)
PY
then
  echo "Hata: test hesabı bilgileri okunamadı"
  exit 1
fi

if ! playwright-cli -s="$OTURUM" open http://127.0.0.1:8080/Auth/Login >/dev/null 2>&1; then
  echo "Hata: yerel giriş sayfası açılamadı"
  exit 1
fi
if ! playwright-cli -s="$OTURUM" run-code --filename="$KOD_DOSYASI" >/dev/null 2>&1; then
  echo "Hata: test hesabıyla giriş yapılamadı"
  exit 1
fi
rm -f .auth/test-oturumu.json
if ! playwright-cli -s="$OTURUM" state-save .auth/test-oturumu.json >/dev/null 2>&1; then
  echo "Hata: test oturumu kaydedilemedi"
  exit 1
fi
echo "Test oturumu kaydedildi"
