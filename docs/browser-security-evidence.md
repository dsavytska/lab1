# Докази безпеки браузера (DEL-01) — Савицька Д. О.

Рівень: **достатній («Задовільно»)**. Секретів (паролів, значень cookie/token, export browser storage) у доказах немає; на одному знімку значення cookie замасковано.

## Стенд і Git-стан

- ОС: Windows; браузер: Microsoft Edge 157 (Chromium); .NET SDK 10.0.401; PostgreSQL у Docker (`infra/compose.yaml`).
- Origins: API і штатний клієнт `http://localhost:5080`; probes `http://localhost:4173` (дозволений) і `http://localhost:4174` (недозволений), запуск: `py -3 -m http.server 4173 --bind localhost --directory demonstrations/cors-probe` і те саме для 4174.
- Receipt `.scaffolds/lab-05.json`: `release` = `lab-05-start-v1`, `inherits` = `v0.4.0`, `base_commit` = `entry_parent` = `5f563031d05789b29dcd1a2932653058d8bfd938` (тег `v0.4.0`; commit політики SDK не знадобився).
- Vulnerable commit: `d71dad85cdb2bc27b5bcb386d327876ff0e30348` — «lab5: install scaffold lab-05-start-v1 (vulnerable state)».
- Fixed commit: `f612c658cfbb8a55801cafa6108efe3690cf6e9a` — «lab5: render incident text as DOM text and add dev-only CORS allowlist».
- `v0.5.0`, `main` і спосіб передавання — у звіті `docs/report-lab-05.md` (DEL-02): тег ставиться на commit із цими доказами, `main` переноситься fast-forward, гілка/тег/main надсилаються в remote `github.com/dsavytska/lab1`.
- Механізм сеансу: cookie `.AspNetCore.Identity.Application`, Domain `localhost`, Path `/`, HttpOnly, SameSite=Lax, Secure — ні (http у Development; `SecurePolicy = SameAsRequest` з ЛР 3). Value не записувалось.

## Гіпотеза й REG-XSS-01

Карта: `docs/source-to-sink.md`. Гіпотеза: значення з поля опису → `incidents.description` → response field `description` → `#description.innerHTML` (HTML-контекст) → браузер виконує обробник `onerror` з даних.

Передумови: commit `d71dad8` (негативний контроль) або `f612c65` (fixed); `dotnet run --project src/SecureLab.Api -- --reset-database`, потім `dotnet run --project src/SecureLab.Api`; пароль лише в `SeedUsers__Password` поточного вікна; вхід через `/auth-lab.html`.
Вхід: однорядковий marker із `resources/lab05-marker.txt` (без змін, без зовнішніх hosts).
Кроки: Alice → `/browser-lab.html` → поле опису ← marker → «Створити й прочитати»; Console: `document.querySelector("#lab05-marker-target").getAttribute("data-lab05-marker")`; Elements: `<div id="description">`. Окремий профіль Edge → Morgan → «Прочитати» той самий Guid. Окремий профіль → Bob → той самий Guid.
Очікування на fixed: marker видно як текст, у `#description` один текстовий вузол, перевірка → `null`, запиту `x` немає; звичайний текст без змін. Негативний контроль на vulnerable: `<img onerror>` у DOM, перевірка → `'executed'`.

Запис PoC: `ecdaa421-af18-4554-b864-04da3994e283`. Записи звичайного тексту (створені `POST /api/incidents` від Alice через curl): `combined` `26f2ed73-64d3-49c7-8cc5-0f6ce98d4dc7`, `htmllike` `5c6d9103-333a-47f2-9ad2-5172ac1c9e4e`, `maxlen` `70be26cf-5e1d-41e6-8532-f5fc597ed613`; seed `…0003`.

| Спостереження | До fix (`d71dad8`) | Після fix (`f612c65`) | Доказ |
|---|---|---|---|
| Значення в API | `description` == marker | `description` == marker; до == після символ у символ (`True`) | `21_stored_value_unchanged.png` |
| DOM основного Description | `<div id="description">` містить `<img src="x" onerror="…">` | один текстовий вузол із рядком marker, `<img>` немає | `12_…`, `23_…` |
| Дія marker | перевірка → `'executed'`, у Network запит `x` | перевірка → `null`, запиту `x` немає | `11_…` (cookie замасковано), `22_…` |
| Лапки/апостроф, `5 < 7 і 9 > 3`, Unicode | — (перевірено після fix) | «Користувач O'Brien написав "терміново": 5 < 7 і 9 > 3, Київ — Ω» без змін | `24_text_combined_after.png` |
| HTML-like рядок `<b>жирний</b> текст` | «жирний» напівжирне, у DOM елемент `<b>` | видно рядок разом із дужками, DOM — текстовий вузол | `08_…`, `25_…` |
| Seed `…0003` з `<script>` у тексті | видно лише «Текст», решта в невиконаному `<script>` | видно все речення | `09_…`, `27_…` |
| Довжина 4000 (межа ЛР 2) | — | `description.Length` = 4000, текст показано, помилок Console немає | `21_…`, `26_…` |
| Вікно Morgan (Analyst) | `GET: 200`, перевірка → `'executed'` — **stored XSS у сеансі іншого користувача** | `GET: 200`, перевірка → `null` | `13_…`, `28_…` |
| Контроль Bob | `GET: 404` (матриця ЛР 4), перевірка → `null` — Bob не є доказом XSS | — | `14_…`, `15_…` |

**Finding.**
- Актив — сеанс браузера та дані, доступні від імені того, хто переглядає інцидент (тут Morgan, Analyst).
- Передумови — локальний акаунт Reporter і контроль над полем опису.
- Першопричина — збережений користувачем текст передавався в HTML sink `innerHTML` у `browser-lab.js` (`load()`), тому браузер розбирав його як розмітку. Класифікація: stored (значення в БД, виконується при пізнішому перегляді) і DOM-based за місцем небезпечного перетворення (клієнтський `innerHTML`).
- Safe DOM diff — `#description.innerHTML` → `.textContent`; `paragraph.innerHTML = comment.text` → `paragraph.textContent` (`19_fix_diff_js.png`). Фільтри `script`/blacklist не додавались, вхідні дані не змінювались.
- Retest — той самий запис і marker: виконання немає, значення не змінилось.
- Залишковий ризик: див. звіт.

## CORS і REG-CORS-01

Прогноз (до зміни policy):

| Сценарій | Чи доходить request до API? | Чи може JavaScript прочитати response? | Чому |
|---|---|---|---|
| same-origin 5080 → API | так | так | той самий origin, CORS не потрібен |
| simple GET 4173 → API | так | ні (до policy) / так (після) | SOP; читання дозволяє лише `Access-Control-Allow-Origin` |
| simple GET 4174 → API | так | ні | 4174 не в allowlist |
| curl → API | так | не застосовується | curl — не browser JavaScript, SOP/CORS не діють |

Policy: `Lab05BrowserProbe` у `Program.cs` — реєструється (`AddCors`) і застосовується (`UseCors` одразу після `UseRouting`, до `UseAuthentication/UseAuthorization`) **лише в Development**; `WithOrigins("http://localhost:4173")`, `WithMethods("GET")`, `WithHeaders("X-Lab-Cors-Probe")`; без `AllowCredentials()`, без `AllowAnyOrigin`. Це навчальний стенд, окремий від штатної same-origin топології `http://localhost:5080`; у Production policy не реєструється.

| Клієнт / простий запит `GET /health` | До policy | Fixed state | Доказ |
|---|---|---|---|
| Same-origin 5080 | `200` (клієнт працює) | `200` | `07_normal_flow.png` |
| Allowed 4173 | `TypeError: Failed to fetch`; Console: «No 'Access-Control-Allow-Origin'…»; Network: GET 200 з `Origin: http://localhost:4173`, без ACAO | `{status: 200, body: '{"status":"ready"}'}`; Response Headers: `Access-Control-Allow-Origin: http://localhost:4173` | `16_…`, `29_…` |
| Disallowed 4174 | `TypeError`, CORS error | `TypeError`, CORS error; запит дійшов до API (200), але ACAO відсутній | `17_…`, `31_…` |
| Direct HTTP / curl | `200` для `/health` з `Origin: 4174`, `401` для `/api/incidents` | те саме: `200`, без ACAO для 4174; `401` для `/api/incidents` | `18_…`, `33_…` |

REG-CORS-01 (HTTP evidence, fixed state у Development): `curl.exe -sS -D - -o NUL -H "Origin: http://localhost:4173" http://localhost:5080/health` → `200`, `Access-Control-Allow-Origin: http://localhost:4173`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`; з `Origin: http://localhost:4174` → `200`, жодного `Access-Control-*` (`32_reg_cors_http.png`, `reg-cors-http.txt`).
Перевірка порядку middleware: зі сторінки 4173 `fetch("http://localhost:5080/api/incidents")` без cookie → `401`, JavaScript прочитав відмову, без «blocked by CORS policy» (`30_cors_4173_order_401.png`).
Пояснення: CORS лише вирішує, чи браузер віддасть відповідь коду сторінки іншого origin. Сам запит доходить до сервера (curl і навіть браузер із 4174 отримали `200`), тому захист даних забезпечують authentication/authorization API (`401` без входу, `404` для чужого інциденту).

## Додатково для Добре та Відмінно
Не виконувалось — обрано рівень «Задовільно».
