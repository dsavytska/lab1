# Звіт ЛР04 — авторизація, володіння ресурсом та IDOR/BOLA

**Студентка:** Савицька Даніела Олегівна, група КБ-43
**Викладач:** Бабенко Ю. М.
**Репозиторій:** `dsavytska/lab1`, гілка `lab/4-authorization`

## Контекст і межа виконання
- Рівень: достатній («Задовільно»). Я виконую обов'язкові кроки 1–8 і 13 для однієї головної action; кроки 9–12 (privileged transition, audit), list-scoping і child endpoint належать до доброго рівня.
- Власний baseline 2-A, base tag `v0.3.0` → `d79f39f`. Гілку `lab/4-authorization` створено від цього тегу.
- Головна action — **details `GET /api/incidents/{id}`** (read). Політика відмови для чужого ресурсу — **`404`**, однаково з неіснуючим id. Маршрути мого рівня: `GET /api/incidents/{id}` (рішення про ресурс) і вся група `/api/incidents` (вимагає входу). Створення `POST /api/incidents` успадковане з ЛР 3.
- Після тегу `v0.3.0` ЛР 3 не виправлялась; тег не пересувався.

| Стан | Commit |
|---|---|
| Base (ЛР 3) | `v0.3.0` → `d79f39f` |
| Vulnerable state (PoC, матриця, початок звіту) | `e779e1b3881b1053d8ca57098f606bacd2ca3715` |
| Fixed state (код, тести) = тег **`v0.4.0`** | `5f563031d05789b29dcd1a2932653058d8bfd938` |
| Цей звіт і докази | наступний commit «Record lab 4 hashes» після тегу |

## Матриця і server-side рішення
Матриця — `docs/access-matrix.md`. Правило головної action:

| Суб'єкт | `GET /api/incidents/{id}` |
|---|---|
| Анонім | deny — `401` Problem Details, без redirect |
| Reporter — власник | allow — `200` |
| Reporter — не власник | deny — `404` «Інцидент не знайдено» (як для неіснуючого id) |
| Analyst (Morgan) | allow — `200`, лише читання для аналізу; права змінювати чужий content немає |
| Administrator | окремого дозволу в предметному контракті немає → як не власник, `404` |

Де реалізовано рішення:
- `IncidentEndpoints.MapIncidentEndpoints` — `.RequireAuthorization()` на групі `/api/incidents` (автентифікацію перевіряю до будь-якого запиту в БД).
- `IncidentEndpoints.GetDetailsAsync` — ідентифікатор користувача беру з `ClaimTypes.NameIdentifier` перевіреної cookie, роль — `User.IsInRole("Analyst")`. Нічого з URL, query чи тіла запиту не використовується як доказ права.
- `IncidentQueries.GetDetailsAsync(id, currentUserId, canReadAll)` — **query scoping**: умова `canReadAll || incident.OwnerUserId == currentUserId` стоїть у самому LINQ-запиті, тобто в `WHERE` SQL. Чужий рядок з БД не завантажується взагалі.
- Відповідь формує один helper `IncidentNotFound()`; `detail` не містить id, тому чужий і неіснуючий інцидент не відрізнити.
- DTO `IncidentDetailsResponse` не містить `ownerUserId`/email (перевіряє `GetDetails_DoesNotExposeInternalOwnerFields`); internal comments відфільтровано ще з ЛР 1 (`!comment.IsInternal`). Request DTO для read немає, server-managed поля не змінюються.

## Вразливий стан і PoC
Vulnerable commit: `e779e1b`. Код details після ЛР 3 я не змінювала, тому vulnerable commit містить лише сценарій `tests/http/lab-04-access.http`, матрицю і початок звіту. Відтворення — наданим `login-and-read.py` (cookie лише в пам'яті процесу) і `curl.exe`, на синтетичних seed-даних після `--reset-database`.

| Суб'єкт | Дія | Очікувано за матрицею | Фактично до виправлення | Доказ |
|---|---|---|---|---|
| Alice | details власного `…0001` | `200` | `login_status=204`, `read_status=200`, «Підозрілий лист із вкладенням», «Аліса Коваль» | `evidence-lr04/04_before_alice_own.png` |
| Alice (та сама cookie) | details Bob `…0002` | `404` | **`read_status=200`**, «Невідома спроба входу», ownerDisplayName «Боб Мельник» — **IDOR/BOLA** | `evidence-lr04/05_before_alice_bob_idor.png` |
| Анонім | details `…0001` без cookie | `401` | **`200`** і повний JSON інциденту Alice — маршрут ще не вимагав автентифікації (крок 4.5) | `evidence-lr04/06_before_anonymous_details.png` |
| Morgan (Analyst) | details Alice | `200` | `200` | `evidence-lr04/07_before_morgan_alice.png` |

Повний текстовий витяг — `docs/evidence-lr04/http-before-after.txt` (пароль і значення cookie не виводились).

## Виправлення та retest
Fixed commit: `5f56303` (тег `v0.4.0`). Ті самі запити після виправлення:

| Суб'єкт | Дія | Фактично після виправлення | Доказ |
|---|---|---|---|
| Alice | details власного | `200`, id `…0001` | `13_after_alice_own.png` |
| Alice | details Bob | `404`, `{"title":"Інцидент не знайдено","status":404,…}` — даних Bob немає | `14_after_alice_bob_404.png` |
| Alice | Bob `…0002` і неіснуючий `…00ff` | обидва `404`, `application/problem+json`, однакові type/title/status/detail, різниться лише `traceId` | `15_after_foreign_vs_unknown.png` |
| Morgan (Analyst) | details Alice | `200` | `16_after_morgan_alice.png` |
| Анонім | details Alice | `401` Problem Details, без redirect | `17_after_anonymous_401.png` |

Для read стан ресурсу не змінюється в жодному сценарії; mutation-маршрутів на моєму рівні не створювалось.

## Доказова таблиця

| ID | Рівень | Сценарій | Передумови | Дія | Очікувано | Фактично | Доказ |
|---|---|---|---|---|---|---|---|
| A-01 | добрий | Анонімний доступ | — | — | — | не виконувалось як обов'язковий — рівень «Добре»; додатково є тест `Details_AsAnonymous_Returns401_WithoutRedirect` (Passed) | `11_tests_lab04.png` |
| A-02 | достатній | Власний ресурс | Alice, seed `…0001` | `GET /api/incidents/{aliceId}` | allow | `200`, id і ownerDisplayName Alice; тест `A02_Details_AsOwnerAlice_ReturnsOwnIncident` Passed | автоматичний HTTP test; `13_after_alice_own.png` |
| A-03 | достатній | Чужий ресурс | Alice, Bob `…0002` | той самий GET з підміненим id | `404` | `404`, тіло = тілу для неіснуючого id без `traceId`; немає id, title і імені Bob; тест `A03_Details_AsNonOwnerAlice_ReturnsSame404AsUnknownId_WithoutBobData` Passed | автоматичний HTTP test; `14_…`, `15_…` |
| A-03R | достатній, entry ЛР 5 | Аналітичний read | Morgan (Analyst), ресурс Alice | `GET /api/incidents/{aliceId}` | allow для аналізу | `200`; тест `A03R_Details_AsAnalystMorgan_ReturnsAliceIncidentForAnalysis` Passed + `.http` п. 8 | test; `16_after_morgan_alice.png` |
| A-04–A-09, A-12 | добрий | — | — | — | — | не виконувалось — рівень «Добре» | — |
| A-10 | добрий | — | — | — | — | окремо не оформлювався (рівень «Добре»), але нерозрізнюваність перевіряє A-03 | — |
| A-11 | відмінний | — | — | — | — | не виконувалось — рівень «Відмінно» | — |

Тести: `dotnet test` після reset — **16/16 Passed** (було 12, додано 4 у `Lab04AuthorizationTests`). Три успадковані тести `IncidentEndpointTests` (list, unknown 404, details без owner fields) і тест severity-summary переведено на клієнт після login Alice, бо група `/api/incidents` тепер вимагає входу. Очікування не послаблювались. Тест `GetList_ReturnsSeededIncidents` очікує обидва інциденти, бо список на моєму рівні не обмежувався. Доказ — `11_tests_lab04.png` (9/9 у фільтрі Lab04 + IncidentEndpointTests), `12_tests_all.png` (16/16).

## Основний finding

1. **Контекст.** Ресурс — Incident; action — перегляд деталей `GET /api/incidents/{id}`. Дозволені суб'єкти: власник-Reporter і Analyst (лише читання).
2. **Гіпотеза.** Alice, увійшовши під своїм обліковим записом, прочитає інцидент Bob, якщо підставить його id в URL.
3. **Стан до.** Alice → Bob: `read_status=200`, «Невідома спроба входу», «Боб Мельник»; анонімно — теж `200`. Vulnerable commit `e779e1b`.
4. **Першопричина.** `IncidentQueries.GetDetailsAsync` шукав рядок лише за `incident.Id == id` і не отримував поточного користувача; endpoint не мав ні `.RequireAuthorization()`, ні порівняння `OwnerUserId` з principal. Сам id в URL — не дефект: дефект у тому, що сервер не перевіряв право на конкретний ресурс.
5. **Виправлення.** `.RequireAuthorization()` на групі `/api/incidents`; query scoping `canReadAll || OwnerUserId == currentUserId`, де `currentUserId` — з `NameIdentifier`, а `canReadAll` — з ролі Analyst у principal; єдиний helper `IncidentNotFound()` для чужого й неіснуючого. Межі DTO: відповідь не містить `ownerUserId`/email та internal comments.
6. **Retest.** Той самий запит Alice → Bob дає `404` без даних Bob; відповідь збігається з неіснуючим id, крім `traceId`. Анонім отримує `401`.
7. **Позитивна регресія.** Alice читає власний інцидент (`200`), Morgan/Analyst читає інцидент Alice (`200`), створення інциденту з ЛР 3 працює (тести ЛР 2/3 Passed).
8. **Залишковий ризик.**
   - Перевірено лише details. Список `GET /api/incidents`, `search` і `severity-summary` усе ще повертають усі інциденти будь-якому автентифікованому користувачу: заголовки та severity чужих записів видно (рівні «Добре»/«Відмінно»). Update/delete/triage і child endpoint на моєму рівні не існують і не перевірялись.
   - CSRF для cookie-authenticated mutating endpoint (`POST /api/incidents`) до ЛР 6 лишається задокументованим навчальним боргом ЛР 3.
   - Роль зчитується з cookie під час login: зміна ролі набуде чинності лише після повторного входу.

Схема рішення:

```
Запит Alice з id → cookie → перевірений principal (NameIdentifier, роль)
 → .RequireAuthorization() (анонім → 401)
 → SELECT … WHERE id = @id AND (@canReadAll OR owner_user_id = @currentUserId)
 → рядок є → 200 DTO | рядка немає (чужий або неіснуючий) → 404 «Інцидент не знайдено»
```

## Browser flow
Після виправлення я увійшла як alice на `http://localhost:5080/auth-lab.html`, потім у тій самій вкладці відкрила `http://localhost:5080`.
- Клік по «Підозрілий лист із вкладенням» показує деталі власного інциденту, автор «Аліса Коваль» — `evidence-lr04/18_browser_alice_own.png`.
- Клік по «Невідома спроба входу» (Bob; список на достатньому рівні не обмежено) показує «Помилка: Інцидент не знайдено» — `evidence-lr04/19_browser_bob_denied.png`.

`src/SecureLab.Api/Client/app.js` я не змінювала: дані виводяться через `createTextElement(...)`, а помилка — через `detailsElement.textContent = \`Помилка: ${error.message}\``; `innerHTML` відсутній (`git grep`, рядки 94 і 105, і тест `ClientScript_DoesNotUseDangerousInnerHtmlSink`). Клієнт лише показує рішення сервера. На знімках немає вкладок DevTools, пароля і cookie.

## Перевірки, відтворення і подання
- ОС/SDK: Windows, .NET SDK 10.0.401; PostgreSQL у Docker (`infra/compose.yaml`).
- Команди: `git switch -c lab/4-authorization v0.3.0`; `docker compose --env-file infra/.env.example -f infra/compose.yaml up -d --wait`; `dotnet run --project src/SecureLab.Api -- --reset-database`; `dotnet test tests/SecureLab.Api.Tests -c Release`; `py -3 …/login-and-read.py --user alice --incident bob`. Пароль навчальних акаунтів задавався лише змінною `SeedUsers__Password` у поточному вікні PowerShell (перевірка `--check`: `{"password_present": true, "json_valid": true}`).
- Перед змінами: 12/12 Passed. Після виправлення і reset: 16/16 Passed.
- Тег: `git rev-parse "v0.4.0^{commit}"` → `5f563031d05789b29dcd1a2932653058d8bfd938` (= fixed commit). Hash записано в цьому наступному commit, тег не пересувався.
- Remote: `https://github.com/dsavytska/lab1` (гілка `lab/4-authorization`, тег `v0.4.0`). Методичка рекомендує тримати vulnerable commit лише в приватному repository; мій навчальний репозиторій публічний, як і в ЛР 1–3. Vulnerable commit не містить нового вразливого коду: це стан ЛР 3 плюс PoC на синтетичних seed-даних, без секретів і реальних даних. Його не використано для deployment, і тег `v0.4.0` стоїть лише на fixed commit. Відхилення від рекомендації фіксую явно.
- Не виконувалось (поза рівнем «Задовільно»): A-01 як обов'язковий, A-04–A-12, обмеження list/search/summary, triage, audit, child endpoint.

## Використання ШІ

ШІ використано: Claude (Anthropic), застосунок Claude у режимі Cowork, з доступом до папки з матеріалами. Пароль навчальних акаунтів ШІ не передавався: його вводила я у вікні PowerShell. Скрипти запускала я на власному стенді.

| Дата | Сервіс/модель | Завдання | Використана частина відповіді | Авторська адаптація | Перевірка і фактичний результат |
|---|---|---|---|---|---|
| 2026-11-19 | Claude (Anthropic) | розбір методички ЛР04, вибір рівня й головної action | план кроків 1–8, 13 для details | обрано рівень «Задовільно», action read, політику 404 | звірено з таблицею рівнів і CP-01 методички |
| 2026-11-19 | Claude | матриця доступу, сценарій `.http` | `docs/access-matrix.md`, `tests/http/lab-04-access.http` | обґрунтування 404, рядки вищих рівнів позначено | PoC Alice → Bob відтворено: 200 до, 404 після |
| 2026-11-19 | Claude | код виправлення | `RequireAuthorization` на групі, query scoping, `IncidentNotFound()` | розібрано шлях cookie → principal → WHERE | build без помилок; ручний retest |
| 2026-11-19 | Claude | тести A-02, A-03, A-03R, переведення старих тестів на login | `Lab04AuthorizationTests`, зміни `IncidentEndpointTests` | — | `dotnet test`: 16/16 Passed |
| 2026-11-19 | Claude | PowerShell-скрипт етапу, обрізання знімків, чернетка звіту | скрипт і текст | browser flow виконано вручну | переглянуто вивід і знімки: паролів і значень cookie немає |
