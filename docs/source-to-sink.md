# Карта source-to-sink — Incident.Description (Савицька Д. О.)

Поле: `Incident.Description`. Стан: vulnerable `d71dad8` (scaffold `lab-05-start-v1`) → fixed `f612c65`.
Запис для PoC: `ecdaa421-af18-4554-b864-04da3994e283` (автор Alice, опис — канонічний marker із `resources/lab05-marker.txt`).

| Крок | Файл та функція/DTO | Конкретне поле/значення | Доказ і довіра |
|---|---|---|---|
| Source | `Client/browser-lab.html`, форма `#edit`, `<textarea name="description">`; обробник `submit` у `browser-lab.js` | `event.currentTarget.elements.description.value` = marker | контрольоване користувачем (Alice вводить довільний текст) |
| Request | `browser-lab.js`, `fetch("/api/incidents", { method: "POST", … JSON.stringify({ …, description }) })` | JSON field `description` у request payload | Network: `POST /api/incidents` → `201`; контрольоване користувачем |
| Validation/mapping | `Scaffolding/Lab02Endpoints.cs`, `MapPost("/api/incidents")`: обов'язкове, ≤ 4000, `Trim()`, правило High/Critical ≥ 40; `CreateIncidentRequest.Description` → `Incident.Description` | marker проходить без змін (кутові дужки в звичайному тексті дозволені) | validation перевіряє формат і довжину, але не робить значення «безпечним HTML» — і не повинна |
| Storage | `Data/Entities/Incident.cs`, PostgreSQL `incidents.description` | той самий рядок, символ у символ | збережене значення користувача; до і після fix однакове (`21_stored_value_unchanged.png`) |
| Response | `GET /api/incidents/{id}` → `IncidentQueries.GetDetailsAsync` (з query scoping ЛР 4) → `IncidentDetailsResponse.Description` | JSON field `description` | Network → Response; `200` для Alice і Morgan (Analyst), `404` для Bob; недовірене для відображення |
| Client transform | `browser-lab.js`, `load()`: `current = await response.json()` | `current.description` | значення не змінюється; JSON-екранування не захищає HTML sink |
| Sink/context | **до:** `document.querySelector("#description").innerHTML = current.description` — контекст HTML parser; **після:** `.textContent = current.description` — текстовий вузол | до: у `#description` елемент `<img onerror>`, marker → `executed`; після: один `#text`, marker → `null` | `12_marker_alice_elements.png`, `23_marker_after_elements.png` |

## Інвентаризація DOM sinks (власний клієнтський код)

| Місце | Дані | Категорія даних | Контекст | Рішення |
|---|---|---|---|---|
| `browser-lab.js` `#title.textContent = current.title` | назва інциденту | збережене значення користувача | текстовий вузол | безпечно, без змін |
| `browser-lab.js` `#description.innerHTML = current.description` | опис | збережене значення користувача | HTML parser | **небезпечно → замінено на `textContent`** |
| `browser-lab.js` цикл коментарів `paragraph.innerHTML = comment.text` | `comments[].text` | збережене значення користувача (автор коментаря) | HTML parser | **небезпечно → замінено на `textContent`** (без окремого seed-досліду — рівень «Задовільно») |
| `browser-lab.js` `status.textContent = \`GET: ${response.status}\`` | HTTP status | server-generated | текстовий вузол | безпечно |
| `browser-lab.js` `elements.description.value = current.description` | опис у textarea | збережене значення користувача | властивість `value` поля форми | безпечно: значення не розбирається як HTML |
| `app.js` `createTextElement(...)`, `textContent` | дані штатного клієнта | збережене значення користувача | текстовий вузол | безпечно (тест `ClientScript_DoesNotUseDangerousInnerHtmlSink`) |
| `auth-lab.js` `result.textContent` | статус login / відповідь `/api/me` | server-generated | текстовий вузол | безпечно |

`eval`, `new Function`, inline event handlers і динамічних URL у власному клієнтському коді немає. Сторонніх бібліотек у дослідженому flow немає.

## Гіпотеза
Значення з поля «Опис нового інциденту» зберігається в `incidents.description`, повертається через response field `description` і передається у `#description.innerHTML` як HTML-контекст; тому, якщо опис містить розмітку з обробником подій, браузер інтерпретує дані не як звичайний текст, а як HTML і виконує обробник у сеансі будь-кого, хто відкриє цей інцидент.
