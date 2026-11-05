# Звіт WEB ЛР03

## Відтворення (усі рівні)

- Обраний рівень: достатній («Задовільно»); автор: Савицька Даніела Олегівна, КБ-43; baseline 2-A.
- Base tag `v0.2.0` → `edba946e91665f7a400df90692086da84dff758e`.
- Scaffold: release `lab-03-start-v1`, base_commit `edba946e91665f7a400df90692086da84dff758e` (`.scaffolds/lab-03.json`); встановлено окремим commit `d160e94` «Install lab-03 scaffold».
- Гілка: `lab/3-authentication`. Commit власних змін: `27f8444` «Implement cookie authentication and protected incident creation». Тег `v0.3.0` — на commit зі звітом після повторних build/tests.
- ОС/SDK: Windows, .NET SDK 10.0.401 (`global.json`: 10.0.100, `latestFeature`).
- Команди: `dotnet build SecureLab.sln -c Release`; `docker compose --env-file infra/.env.example -f infra/compose.yaml up -d --wait`; `dotnet run --project src/SecureLab.Api -- --reset-database`; `dotnet test tests/SecureLab.Api.Tests -c Release`. Пароль навчальних акаунтів задавався лише змінною `SeedUsers__Password` у поточному вікні PowerShell.
- Фактичний результат: build без помилок; після scaffold — 9/9 тестів; після моїх змін — 12/12 (evidence-lr03/tests-output.txt).
- Migration/reset: міграція `Lab03Identity` застосовується під час reset; `Reset-Twice.ps1` з комплекту — двічі «Відновлення…/очищено…» і `Passed: 9, Failed: 0`, `$LASTEXITCODE = 0` (evidence-lr03/reset-twice.txt). Після reset T-01 проходить повторно (нове ім'я генерується щоразу).

## Власна реалізація і межі

**DTO boundary.** Register приймає `RegistrationInput(UserName, Email, DisplayName, Password)` і повертає лише `RegisterResponse(Id, UserName, DisplayName)`. Перевірки до `UserManager`: усі поля обов'язкові; userName ≤ 256 і лише `A-Z a-z 0-9 - . _ @ +`; email ≤ 256 і коректний формат (`MailAddress`); displayName ≤ 120 (як у моделі). Нормалізую лише `Trim()` для userName/email/displayName; пароль не обрізаю, не логую й не повертаю. Помилки полів → 400 `ValidationProblem` з ключами; будь-яка відмова Identity (дублікат, слабкий пароль) → однаковий 400 `{"message":"Registration failed"}`. Роль, id, hash у DTO немає — їх задає сервер.

**Звідки береться id.** `GET /api/me` зареєстровано з `.RequireAuthorization()`; метод `GetCurrentUserAsync` читає `ClaimTypes.NameIdentifier` з `HttpContext.User`, перетворює на `Guid` і повертає `CurrentUserResponse(Id, UserName, DisplayName)` цього користувача. Заголовок `X-Demo-UserId` більше не використовується. `POST /api/incidents` (Lab02Endpoints.cs) теж має `.RequireAuthorization()`, а `OwnerUserId` береться з того самого claim; зайве `ownerUserId` у JSON ігнорується, бо його немає в `CreateIncidentRequest`.

**Cookie options (надані scaffold, `AddLab03Identity`).** `HttpOnly = true` — JavaScript не читає cookie; `SameSite = Lax` — браузер не надсилає cookie у міжсайтових POST/fetch; `SecurePolicy = SameAsRequest` у Development і `Always` поза ним. Локально API працює по HTTP, тому cookie без `Secure` — це межа локального стенду: HTTP-трасування не доводить Secure, у production працює `Always` + HTTPS. Строк дії — стандартний для Identity. `OnRedirectToLogin`/`OnRedirectToAccessDenied` повертають 401/403 замість 302 на сторінку входу.

**Login.** Надано `SignInManager.PasswordSignInAsync`; для хибного пароля й невідомого користувача — однаковий 401 Problem Details (перевірено вручну, evidence-lr03/login-uniform-401.txt); я цю поведінку не змінювала.

**Маршрут довіри:** login → `PasswordSignInAsync` (Identity перевіряє пароль із збереженим hash) → `Set-Cookie: .AspNetCore.Identity.Application` (HttpOnly, SameSite=Lax) → браузер/клієнт сам надсилає cookie → cookie authentication handler розшифровує й перевіряє її → claims у `HttpContext.User` → `/api/me` і `OwnerUserId` з principal.

**Перевірка пароля.** Хеш рахує й перевіряє лише Identity `PasswordHasher` (сіль і багато ітерацій), викликаний з `UserManager.CreateAsync` і `PasswordSignInAsync`. У таблиці `identity_users` є стовпець `PasswordHash text`, відкритого пароля немає (T-03). Hash ніколи не повертається, бо його немає в жодному response DTO.

**Чому client UserId не є доказом особи.** Body, header чи query повністю контролює клієнт: до змін `X-Demo-UserId` Bob без cookie повертав профіль Bob. Доказ особи — лише cookie, яку сервер видав після перевірки пароля й сам перевіряє.

**CSRF навчальний борг.** Автентифікація на cookie означає, що браузер додає її автоматично. `SameSite=Lax` блокує міжсайтові POST, але не захищає від запитів із того самого сайту/піддомену; антифорджері-токенів немає — повний CSRF-flow запланований у ЛР 6.

**Канали перебору акаунтів і компроміси.** Login — однаковий 401, канал закритий. Register — відмова однакова для дубліката й слабкого пароля, але при сильному паролі й коректному email 400 майже напевно означає зайняте ім'я/email; це усвідомлений компроміс зручності реєстрації. Обмеження кількості спроб входу не входить до обраного рівня — залишковий ризик підбору пароля.

Logout, A-03/A-04/A-06/A-07, rate limit, події, перевірка flags, браузерний сценарій, duplicate 400/409, поглиблення — не входять до обраного рівня.

## Докази

| ID | Рівень | Передумова/дія | Очікувано | Фактично | Безпечний доказ |
|---|---|---|---|---|---|
| T-01 | достатній | reset; register нового `reg…` з одноразовим паролем | safe 201 | 201, `Location: /api/me`, тіло лише `id`, `userName`, `displayName` | evidence-lr03/t01-register.txt |
| T-02 | достатній | той самий register удруге; некоректні поля | safe rejection | 400 `{"message":"Registration failed"}`; 400 ValidationProblem (userName, email, displayName) | evidence-lr03/t02-register-duplicate-invalid.txt |
| T-03 | достатній | `\d identity_users` + Identity verifier | password не відкрито | є `PasswordHash text`, стовпця пароля немає; значень не виводилось | evidence-lr03/t03-identity-users-schema.txt |
| A-01 | достатній | анонімний `POST /api/incidents` | 401, no redirect | тест `A01_AnonymousCreate_Returns401_WithoutRedirect` Passed (401, без Location, без WWW-Authenticate, problem+json); вручну 401 | tests-output.txt, create-before-after-a05.txt |
| A-02 | достатній | login alice → `/api/me` | principal user | тест `A02_LoginThenMe_ReturnsCurrentPrincipalOnly` Passed; вручну 200 `alice`, «Аліса Коваль» | tests-output.txt, me-before-after.txt |
| A-05 | достатній | Bob створює запис з `ownerUserId` Alice | owner = Bob | тест `A05_BobCreatesWithForgedOwner_OwnerIsBob` Passed (OwnerUserId у БД = BobId); вручну 201 і `ownerDisplayName` «Боб Мельник» | tests-output.txt, create-before-after-a05.txt |
| S-01 | достатній | відповіді, логи, diff | без витоків | у відповідях немає password/hash/stamp/token (A-02 перевіряє); у `Scaffolding` немає логування; значення cookie у доказах приховано; пароль лише в змінній середовища | цей звіт, `git grep` |
| A-03/04/06/07 | добрий | — | — | не входить до обраного рівня | — |

До змін (зафіксовано): `/api/me` без header — 501; з `X-Demo-UserId` Bob без cookie — 200 і профіль Bob; анонімний `POST /api/incidents` — 201. Після змін: усі три — 401.

## Декларація використання ШІ

ШІ використано: Claude (Anthropic), застосунок Claude у режимі Cowork, з доступом до папки з матеріалами. Що адаптовано автором і як перевірено — у журналі. Пароль навчальних акаунтів ШІ не передавався: його вводила я у вікні PowerShell.

| Дата | Сервіс/модель | Завдання | Використана частина відповіді | Авторська адаптація | Перевірка і фактичний результат |
|---|---|---|---|---|---|
| 2026-11-05 | Claude (Anthropic) | розбір методички ЛР03, план рівня «Задовільно» | перелік обов'язкових T/A/S і кроків | обрано рівень «Задовільно» | звірено з таблицею рівнів методички |
| 2026-11-05 | Claude | код register-перевірок, `/api/me` з principal, захист `POST /api/incidents` | `ValidateRegistration`, `GetCurrentUserAsync`, `RequireAuthorization` | розібрано маршрут cookie → `HttpContext.User` → owner | build без помилок; ручні запити до/після змін |
| 2026-11-05 | Claude | автотести A-01, A-02, A-05, оновлення тестів ЛР 2 | `Lab03AuthenticationTests`, вхід у `Lab02ContractTests` | — | `dotnet test`: 12/12 Passed |
| 2026-11-05 | Claude | PowerShell-скрипти етапів, витяг доказів, чернетка звіту | скрипти й текст | пароль вводився вручну; скрипти запускались на власному стенді | переглянуто вивід і скріншоти; у доказах немає паролів і значень cookie |
