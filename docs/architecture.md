# Карта архітектури

Карта описує стан після ЛР 1 (варіант 2-A «Трекер інцидентів»): досліджений
details-маршрут і реалізоване наскрізне розширення `severity-summary`.

## Компоненти

| Компонент | Розташування | Відповідальність |
|---|---|---|
| Browser client | `src/SecureLab.Api/Client/` | Надсилає HTTP-запити через `fetch`, безпечно показує відповідь через DOM API |
| ASP.NET Core API (Presentation) | `Presentation/Endpoints`, `Presentation/Contracts` | Маршрутизація за method + URL, серверна перевірка входу, формування HTTP-відповіді та DTO |
| Application | `Application/Incidents/IncidentQueries.cs` | Сценарії читання: список, деталі, підсумок за severity |
| EF Core / Npgsql (Data) | `Data/SecureLabDbContext.cs`, `Data/Entities`, `Data/Migrations` | Відображення entity на таблиці PostgreSQL, migration, seed/reset |
| PostgreSQL у Docker Compose | `infra/compose.yaml` | Зберігає навчальні дані у локальному контейнері на `127.0.0.1:54329` |

## Маршрут ЛР 1: підсумок за severity

```text
кнопка #load-summary-button у Client/index.html
  → loadSeveritySummary у Client/app.js
  → GET /api/incidents/severity-summary (без request body)
  → IncidentEndpoints.GetSeveritySummaryAsync (Presentation/Endpoints/IncidentEndpoints.cs)
  → IncidentQueries.GetSeveritySummaryAsync (Application/Incidents/IncidentQueries.cs)
  → SecureLabDbContext.Incidents → таблиця incidents (AsNoTracking, GroupBy(Severity), Count())
  → IReadOnlyList<IncidentSeveritySummaryResponse> (Presentation/Contracts/IncidentResponses.cs)
  → 200 application/json: [{ "severity": "Low", "count": 1 }, …]
  → textContent у списку #severity-summary-list
```

Контракт:

- поля елемента: лише `severity` і `count`;
- політика нульових груп: **повний перелік рівнів** — після агрегації в БД код
  доповнює відсутні значення `IncidentSeverity` елементами з `count: 0`;
- порядок: **явний порядок критичності** `Low → Medium → High → Critical`
  (порядок enum після матеріалізації агрегату), а не лексикографічний порядок
  текстового стовпця `severity`;
- на відновленому seed: `Low 1, Medium 1, High 1, Critical 0`.

Клієнт має стани «Завантаження…», «Даних немає» (порожній масив або всі
`count = 0`) і фіксоване повідомлення про помилку без внутрішніх деталей.
Query пише структурований log `Severity summary built: {GroupCount} groups …`
без описів інцидентів та конфігурації.

## Досліджений маршрут деталей інциденту

```text
натискання картки інциденту
  → loadIncidentDetails у Client/app.js
  → GET /api/incidents/{id}
  → IncidentEndpoints.GetDetailsAsync (маршрут /{id:guid}; null → 404 Problem Details)
  → IncidentQueries.GetDetailsAsync (AsNoTracking, Where за id, проєкція, SingleOrDefaultAsync)
  → SecureLabDbContext.Incidents → таблиця incidents
  → IncidentDetailsResponse → JSON
  → renderIncidentDetails: textContent і document.createTextNode
```

## Межі довіри

| Межа | Дані, що її перетинають | Чому даним ще не можна довіряти | Де перевіряємо або обмежуємо |
|---|---|---|---|
| Користувач → Browser client | вибір у `<select>`, натискання кнопок | користувач контролює введення | UI лише допомагає сформувати запит, не є контролем |
| Browser client → API | method, URL, path `id`, query `status`, headers | будь-який HTTP-клієнт може надіслати інше значення без форми | маршрутне обмеження `:guid`, `Enum.TryParse` + `Enum.IsDefined` (400), обробка `null` (404) |
| API → PostgreSQL | `id`, умови фільтра, групування | параметр може бути ворожим | параметризовані запити EF Core, `AsNoTracking()`, явна проєкція |
| API → браузер | JSON DTO | право прочитати entity ≠ право отримати всі поля | окремі DTO без `OwnerUserId`, email, внутрішніх коментарів; summary — лише `severity`/`count` |
| Дані response → DOM | текстові поля з JSON | збережений текст (напр. `<script>` у seed) може бути HTML | `textContent`, `document.createTextNode`, створення вузлів; тест забороняє `innerHTML` |

## Конфігураційні входи

- `global.json` — смуга .NET SDK `10.0.3xx`, `rollForward: latestPatch`;
- `src/SecureLab.Api/appsettings.json`, `appsettings.Development.json` — режим міграцій і локальний connection string навчального стенда;
- `infra/compose.yaml` + `infra/.env.example` — образ PostgreSQL, порт `POSTGRES_PORT`, локальні навчальні облікові дані;
- змінна середовища `ConnectionStrings__SecureLab` — перевизначення connection string поза Git (значення не зберігається в репозиторії).

## Повернення до відомого стану

```bash
dotnet run --project src/SecureLab.Api -- --reset-database
```

Команда працює лише в Development, застосовує migrations, очищує відомі навчальні
таблиці та повторно заповнює seed. Після reset: `GET /api/incidents` повертає три
seed-інциденти, `GET /api/incidents/severity-summary` — `Low 1, Medium 1, High 1, Critical 0`.
