# Звіт WEB ЛР 2

## 1. Ідентифікація

- ПІБ і група: Савицька Даніела Олегівна, КБ-43.
- Спільний baseline 2-A; індивідуального номера немає.
- Заявлений рівень: добрий («Добре»).
- Гілка: `lab/2-input-sqli` (створена від власного тегу `v0.1.0`).
- Квитанція scaffold (`.scaffolds/lab-02.json`): release `lab-02-start-v1`, base_commit `b0c1c769efa414a8727d84f34fdd5a5ca123eadb` (= `v0.1.0`), entry_parent `886317d91c91217959607f9eb4409b354d5859ab` (окремий commit «Update .NET 10 SDK selection policy»).
- Vulnerable commit: `a5cc001a09b117a87005358d6cd7acc39d76c7f8` «Validate create contract; preserve search evidence».
- Fixed commit коду: `64a24cdc3862309654cd21fb6edc7de502ff688b` «Validate incident input and secure search query».
- Фінальний тег `v0.2.0` — на commit із цим звітом (наступний після fixed commit; код не змінює).
- DEL-01: https://github.com/dsavytska/lab1 (гілка `lab/2-input-sqli`, тег `v0.2.0`).

## 2. Контракт POST /api/incidents і CP-01

Обробник у `src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs`. Request DTO `CreateIncidentRequest(Title, Description, Severity, OccurredAtUtc)` — лише чотири поля, усі nullable; обов'язковість перевіряє мій код до створення entity і до звернення до БД.

| Поле | Правило | Помилка |
|---|---|---|
| title | required; ≤ 160 символів (рахую до `Trim()`) | 400, ключ `title` |
| description | required; ≤ 4000 символів (до `Trim()`) | 400, ключ `description` |
| severity | `Enum.TryParse(ignoreCase)` **і** `Enum.IsDefined` | 400, ключ `severity` |
| occurredAtUtc | required; не пізніше `now + 5 хв` (now фіксую один раз на запит) | 400, ключ `occurredAtUtc` |
| cross-field (T-09) | High/Critical → description після `Trim()` ≥ 40 | 400, ключ `description` |
| конфлікт (T-03) | така сама назва після `Trim()`, з урахуванням регістру, у статусі New/Triaged/InProgress/Resolved; Closed не блокує | 409 Problem Details |

Усі помилки полів збираються в один словник і повертаються через `Results.ValidationProblem` (400, `application/problem+json`, поле `errors`). Після перевірок `Title` і `Description` нормалізуються `Trim()` один раз. Сервер сам задає `Id = Guid.NewGuid()`, `OwnerUserId = DbSeeder.AliceId`, `Status = New`, `CreatedAtUtc/UpdatedAtUtc = now`, а дату зберігає як `OccurredAtUtc.ToUniversalTime()` (тому `09:30+03:00` збережено як `06:30+00:00`). Відповідь 201 — окремий response DTO `CreatedIncidentResponse(Id, Title, Severity, Status, OccurredAtUtc, CreatedAtUtc)` із заголовком `Location`.

**Чому TryParse і IsDefined разом.** `Enum.TryParse` перетворює і назви, і числові рядки: `"7"` він приймає як `(IncidentSeverity)7`, якого в enum немає. `Enum.IsDefined` відсікає такі значення. Без нього в БД потрапив би неіснуючий рівень (evidence/t02-severity-7.txt).

**Маршрут JSON → DTO → validation → 400.** JSON-тіло десеріалізується в `CreateIncidentRequest` (імена без урахування регістру), далі мій обробник перевіряє поля і повертає `ValidationProblem` ще до `db.Incidents.Add`. Обмеження `<select>` у браузері нічого не гарантує: запит можна надіслати curl-ом або змінити в DevTools, тому перевірка має бути на сервері.

**Overposting.** Я надіслала коректний запит із зайвими `id`, `status: "Closed"`, `ownerUserId` (Bob) і `createdAtUtc`. Запис отримав новий id, статус `New` і власника «Аліса Коваль» — у request DTO таких властивостей немає, тож зайві поля просто ігноруються (evidence/cp01-overposting.txt).

## 3. Security-сценарій, CP-02 і CP-03

**Контекст і стан до виправлення.** Локальний Development-стенд, PostgreSQL з `infra/compose.yaml`, штучний seed (5 інцидентів після reset), vulnerable commit `a5cc001`.

**Першопричина.** `Lab02Endpoints.cs`, метод `MapLab02Endpoints`, обробник `GET /api/incidents/search`, рядки 19–21: значення `q` із query string конкатенацією вклеюється всередину SQL-літерала (`... ILIKE '%" + (q ?? "") + "%' ...`), а готовий рядок виконується через `db.Incidents.FromSqlRaw(sql)`. Так само `sortBy` з гілки `_ => sortBy` потрапляв у `ORDER BY` без перевірки. Маршрут: query string `q` → параметр лямбди `string? q` → рядок `sql` → `FromSqlRaw` → PostgreSQL.

**Спостереження (лише дозволені read-only запити, після reset):**

| Запит | Status | Результат |
|---|---|---|
| A `q=USB` | 200 | 1 запис «Перевірка USB навчальної мережі» |
| B `q=zz-no-match` | 200 | `[]` |
| C `q=zz-no-match' OR TRUE -- ` | 200 | **5 записів** — уся таблиця (ids …0001–…0005) |
| D `q=O'Brien` | 500 | Problem Details без деталей; у журналі `42601: syntax error at or near "Brien"` |
| D2 `q=комп'ютерного` | 500 | у журналі `42601: syntax error at or near "ютерного"` |

**Пояснення.** У журналі для цих запитів `Parameters=[]` — окремо від тексту SQL не передано жодного значення. У запиті C апостроф закрив рядковий літерал, `OR TRUE` став частиною умови `WHERE` (вона істинна для кожного рядка), а `--` закоментував решту запиту. У D/D2 апостроф так само закриває літерал, але залишок слова ламає синтаксис. Фільтр апострофа чи слова `OR` не усуває механізм: він ламає легітимний пошук (`O'Brien`, `комп'ютерного`) і не змінює того, що дані змішуються з кодом SQL.

**Виправлення (CP-03).** `FromSqlRaw` і склеєний SQL прибрано. Пошук — LINQ:
`Where(item => EF.Functions.ILike(item.Title, pattern, "\\") || EF.Functions.ILike(item.Description, pattern, "\\"))`, де `pattern = "%" + EscapeLike(q ?? "") + "%"`. `EscapeLike` спочатку екранує `\`, потім `%` і `_`, тому вони шукаються буквально. EF Core передає `pattern` у PostgreSQL параметром: у журналі після виправлення `Parameters=[@pattern='?', @pattern0='?', @p='?']` (evidence/fixed-api-log.txt). `sortBy` — allowlist через `switch`: `null/""/createdAtUtc` → `OrderByDescending(CreatedAtUtc)`, `severity` → ранг Critical > High > Medium > Low, `status` → New > Triaged > InProgress > Resolved > Closed; будь-яке інше значення → 400 з ключем `sortBy`. Далі `ThenBy(Id)` і `Take(50)`. Відповідь — `IncidentSearchResponse`. Diff `a5cc001..64a24cd`: 2 файли, +104/−9.

**Повторний і позитивний сценарії після виправлення:** C → 200 `[]`; A → 1 запис; `O'Brien` → 200, 1 запис «O'Brien classroom report»; `комп'ютерного` → 200, 1 запис «Перевірка журналу комп'ютерного класу»; `q=%` і `q=_` → `[]` (шукаються буквально).

## 4. Фактичні перевірки DEL-02

| ID | Сценарій | Передумови | Дія | Очікувано | Фактично | Доказ |
|---|---|---|---|---|---|---|
| T-01 | Коректне створення | reset, API | POST з валідними полями, дата `+03:00` | 201, Location, response DTO | 201, `Location: /api/incidents/6161a045-…`, status New, occurredAtUtc `2026-10-28T06:30:00+00:00` | evidence/t01-create.txt |
| T-02 | Некоректний DTO | те саме | `severity:"7"`; порожній title + без description/дати + `Urgent`; дата 2099; title 161 | 400 problem+json з ключами полів, без внутрішніх деталей | 400 `errors.severity`; 400 з усіма 4 ключами; 400 `errors.occurredAtUtc`; 400 `errors.title` | evidence/t02-*.txt |
| T-02 (авто) | Тест `Create_WithNumericSeverity_Returns400WithSeverityKey` | PostgreSQL, seed | `dotnet test` | 400, problem+json, `severity`, без `Exception` | Passed | evidence/tests-output.txt |
| T-03 | Конфлікт назви | seed «Невідома спроба входу» (InProgress) | POST тієї ж назви; з пробілами на краях; у нижньому регістрі | 409; 409; 201 | 409 «Інцидент уже існує»; 409; 201 | evidence/t03-conflict.txt |
| T-03 (авто) | Тест `Create_SameActiveTitleTwice_Returns409OnSecondRequest` | PostgreSQL | дві однакові POST з унікальною назвою, cleanup у `finally` | 201, потім 409 | Passed | evidence/tests-output.txt |
| T-06 | Неіснуючий id | API | GET `/api/incidents/99999999-…` | 404 problem+json, «Інцидент не знайдено» | 404, title «Інцидент не знайдено», detail без внутрішніх деталей | evidence/t06-404.txt |
| T-09 | Межа cross-field | reset | High + опис 39 / 40 символів після Trim (з пробілами по краях) | 400 `description` / 201 | 400 `errors.description` / 201 | evidence/t09-39-40.txt |
| CP-01 | Overposting | API | POST із зайвими id/status/ownerUserId/createdAtUtc, GET за Location | новий id, New, Аліса | новий id `9d8d75bb-…`, status New, ownerDisplayName «Аліса Коваль» | evidence/cp01-overposting.txt |
| S-01 | Контрольний ввід до fix | vulnerable commit `a5cc001`, reset | GET `search?q=zz-no-match%27%20OR%20TRUE%20--%20` | без дефекту: `[]` | 200, 5 записів (уся таблиця) | evidence/s01-vulnerable-search.txt |
| S-02 | Той самий ввід після fix | fixed commit `64a24cd`, reset | той самий GET | 200 `[]` | 200 `[]` | evidence/s02-t04-fixed-search.txt |
| T-04 | Позитивна регресія | fixed, reset | `q=USB`, `q=O%27Brien`, `q=комп'ютерного` | 200, по 1 запису | 200 / 1 запис у кожному (до fix апострофи давали 500) | evidence/s02-t04-fixed-search.txt |
| T-05 | Невідоме сортування | fixed, reset | GET `search?q=&sortBy=title` | 400, ключ sortBy | 400, `errors.sortBy`: «Допустимі значення: createdAtUtc, severity, status.» | evidence/t05-sortby.txt |
| A-01 | Пошук raw SQL | fixed | `data-access-scan.py .` та `git grep` | класифікувати кожен збіг | 1 збіг — див. нижче | evidence/a01-scan.txt |
| Тести | Build + baseline | fixed, reset | `dotnet build`, `dotnet test -c Release` | усі зелені | Build succeeded; Passed 8 / Failed 0 | evidence/tests-output.txt |

**A-01, класифікація збігів.** Після виправлення сканер знаходить лише `src/SecureLab.Api/Data/DatabaseBootstrap.cs:29`, `ExecuteSqlRawAsync("TRUNCATE TABLE … RESTART IDENTITY CASCADE;")` у reset-гілці. Категорія: статичний службовий SQL. Висновок: безпечно — текст є константою, у нього не потрапляє жодне значення від клієнта, і виконується він лише з `--reset-database` у Development. Колишній збіг `FromSqlRaw` у `Lab02Endpoints.cs` усунено.

Сортування перевірене окремо: `sortBy=severity` → High, Medium, Low, Low, Low (однакові Low упорядковані за Id); `sortBy=status` → New ×3, Triaged, InProgress (evidence/t05-sortby.txt).

## 5. Обмеження і висновок

У роботі я реалізувала серверну перевірку контракту створення інциденту, зберегла й дослідила vulnerable state пошуку, довела SQL-ін'єкцію єдиним дозволеним read-only вводом і усунула першопричину: значення пошуку тепер передається параметром через LINQ/`ILike`, а сортування обирається з allowlist. Усі перевірки виконано на локальному стенді зі штучними даними.

Обмеження: перевірка дубліката через `AnyAsync` перед `SaveChangesAsync` не гарантує інваріант за одночасних запитів — два паралельні POST можуть обидва пройти перевірку. У реальному застосунку потрібен частковий унікальний індекс у БД або транзакція з відповідним рівнем ізоляції; у ЛР 2 migration не додавалась. Помилки читання JSON (зіпсований JSON, число замість рядка) я окремо не відтворювала; вони обробляються конвеєром до виклику обробника, а не моєю валідацією. Числовий рядок, що відповідає визначеному значенню (наприклад `"2"`), `TryParse`+`IsDefined` приймають як `High` — це поведінка, передбачена методичкою.

## 6. Декларація використання ШІ

| Сервіс | Задачі | Авторська адаптація/інтерпретація | Фактична перевірка |
|---|---|---|---|
| Claude (Anthropic), застосунок Claude у режимі Cowork | Розбір методички ЛР 2 і план етапів; генерація коду POST-валідації, `CreatedIncidentResponse`, виправленого пошуку (LINQ/`ILike`, `EscapeLike`, allowlist `sortBy`) і тестів `Lab02ContractTests`; сценаріїв `tests/http/lab-02-checks.http`; PowerShell-скриптів для запуску етапів і скріншотів; витяг доказів із журналів у `docs/evidence/`; чернетка тексту цього звіту та docx-версії | Запускала скрипти на власному стенді й переглядала кожну відповідь і скріншот; звіряла результати з очікуваннями методички; разом із поясненнями розібрала, чому потрібні `TryParse`+`IsDefined`, чому апостроф ламав SQL і чому параметр через `ILike` це усуває; обрала рівень «Добре» та стиль звіту | Збірка без помилок; `dotnet test` Passed 8/8; ручні HTTP-перевірки T-01…T-09, S-01/S-02, T-04, T-05 з фактичними статусами в таблиці розділу 4; журнал API до/після (`Parameters=[]` → `@pattern`) |

## 7. Передавання

- Гілку `lab/2-input-sqli` і тег `v0.2.0` надіслано до https://github.com/dsavytska/lab1 (`git push -u origin lab/2-input-sqli`, `git push origin v0.2.0`).
- У звіті та доказах немає connection string, паролів, токенів, cookies чи дампів БД; повного тексту SQL із журналу не наведено. Git diff перевірено перед commit.
- Звіт (docx) і докази завантажуються в Moodle.
