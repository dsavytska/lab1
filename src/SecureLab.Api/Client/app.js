const listElement = document.querySelector("#incident-list");
const listStatusElement = document.querySelector("#list-status");
const detailsElement = document.querySelector("#incident-details");
const filterForm = document.querySelector("#filter-form");
const summaryButton = document.querySelector("#load-summary-button");
const summaryStatusElement = document.querySelector("#summary-status");
const summaryListElement = document.querySelector("#severity-summary-list");

async function apiFetch(path, options = {}) {
  const response = await fetch(path, {
    headers: { Accept: "application/json", ...options.headers },
    ...options,
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(problem?.title ?? `HTTP ${response.status}`);
  }

  return response.json();
}

function createTextElement(tagName, text, className) {
  const element = document.createElement(tagName);
  element.textContent = text;
  if (className) {
    element.className = className;
  }
  return element;
}

function renderIncidentList(incidents) {
  listElement.replaceChildren();

  if (incidents.length === 0) {
    listStatusElement.textContent = "За заданим фільтром інцидентів немає.";
    return;
  }

  listStatusElement.textContent = `Знайдено: ${incidents.length}`;
  for (const incident of incidents) {
    const item = document.createElement("li");
    const button = document.createElement("button");
    button.type = "button";
    button.className = "incident-card";
    button.append(
      createTextElement("strong", incident.title),
      createTextElement("span", `${incident.severity} · ${incident.status}`, "metadata"),
    );
    button.addEventListener("click", () => loadIncidentDetails(incident.id));
    item.append(button);
    listElement.append(item);
  }
}

function renderIncidentDetails(incident) {
  const heading = createTextElement("h3", incident.title);
  const metadata = createTextElement(
    "p",
    `${incident.severity} · ${incident.status} · автор: ${incident.ownerDisplayName}`,
    "metadata",
  );
  const description = createTextElement("p", incident.description);
  const commentsHeading = createTextElement("h4", "Коментарі");
  const comments = document.createElement("ul");

  for (const comment of incident.comments) {
    const item = document.createElement("li");
    item.append(
      createTextElement("strong", `${comment.authorDisplayName}: `),
      document.createTextNode(comment.text),
    );
    comments.append(item);
  }

  if (incident.comments.length === 0) {
    comments.append(createTextElement("li", "Коментарів немає."));
  }

  detailsElement.className = "";
  detailsElement.replaceChildren(heading, metadata, description, commentsHeading, comments);
}

async function loadIncidents() {
  listStatusElement.textContent = "Завантаження…";
  listElement.replaceChildren();

  const status = new FormData(filterForm).get("status");
  const query = status ? `?status=${encodeURIComponent(status)}` : "";

  try {
    renderIncidentList(await apiFetch(`/api/incidents${query}`));
  } catch (error) {
    listStatusElement.textContent = `Помилка: ${error.message}`;
  }
}

async function loadIncidentDetails(id) {
  detailsElement.className = "details-placeholder";
  detailsElement.textContent = "Завантаження…";

  try {
    renderIncidentDetails(await apiFetch(`/api/incidents/${encodeURIComponent(id)}`));
  } catch (error) {
    detailsElement.textContent = `Помилка: ${error.message}`;
  }
}

// ЛР 1: підсумок кількості інцидентів за severity.
// Дані API записуються лише через textContent; HTML-розмітка з даних не формується.
async function loadSeveritySummary() {
  summaryStatusElement.textContent = "Завантаження…";
  summaryListElement.replaceChildren();
  summaryButton.disabled = true;

  try {
    const summary = await apiFetch("/api/incidents/severity-summary");

    // Порожній стан: API повернув [] або всі рівні мають count = 0
    // (наприклад, таблиця incidents порожня після очищення навчальної БД).
    if (summary.length === 0 || summary.every((item) => item.count === 0)) {
      summaryStatusElement.textContent = "Даних немає";
      return;
    }

    for (const item of summary) {
      const element = document.createElement("li");
      element.textContent = `${item.severity}: ${item.count}`;
      summaryListElement.append(element);
    }

    summaryStatusElement.textContent = `Рівнів severity: ${summary.length}`;
  } catch {
    // Коротке фіксоване повідомлення без stack trace, SQL або внутрішніх деталей.
    summaryStatusElement.textContent = "Не вдалося завантажити підсумок. Спробуйте пізніше.";
  } finally {
    summaryButton.disabled = false;
  }
}

summaryButton.addEventListener("click", loadSeveritySummary);

filterForm.addEventListener("submit", (event) => {
  event.preventDefault();
  loadIncidents();
});

loadIncidents();
