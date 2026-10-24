"use strict";

const STORAGE_KEY = "neurolink-fieldnote-demo-cases-v1";
const initialCases = [
  { id: "KJ-026", type: "X-ray", detail: "Chest · 2 views", priority: "Urgent", status: "Needs review", added: "09:42", today: true, notes: "Fictional demo case. Review queue status only; no analysis performed." },
  { id: "KJ-025", type: "Ultrasound", detail: "Abdominal", priority: "Routine", status: "Reviewed", added: "09:18", today: true, notes: "Fictional demo case. Review queue status only; no analysis performed." },
  { id: "KJ-024", type: "X-ray", detail: "Wrist · 3 views", priority: "Time-sensitive", status: "Needs review", added: "08:56", today: true, notes: "Fictional demo case. Review queue status only; no analysis performed." },
  { id: "KJ-023", type: "MRI", detail: "Knee · without contrast", priority: "Routine", status: "Reviewed", added: "Yesterday", today: false, notes: "Fictional demo case. Review queue status only; no analysis performed." },
  { id: "KJ-022", type: "X-ray", detail: "Ankle · 2 views", priority: "Routine", status: "Reviewed", added: "Yesterday", today: false, notes: "Fictional demo case. Review queue status only; no analysis performed." },
  { id: "KJ-021", type: "CT scan", detail: "Head · without contrast", priority: "Urgent", status: "Needs review", added: "Yesterday", today: false, notes: "Fictional demo case. Review queue status only; no analysis performed." }
];

let cases = loadCases();
let activeFilter = "all";
let toastTimer;
let selectedImageUrl = "";
let lastFocusedElement = null;

function loadCases() {
  try {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (!saved) return initialCases.map((item) => ({ ...item }));
    const parsed = JSON.parse(saved);
    if (!Array.isArray(parsed) || !parsed.every(isValidCase)) {
      throw new Error("Saved case data has an unexpected format.");
    }
    return parsed;
  } catch (error) {
    console.error("Unable to read local case records.", error);
    return initialCases.map((item) => ({ ...item }));
  }
}

function isValidCase(item) {
  return item && typeof item.id === "string" && typeof item.type === "string" &&
    typeof item.detail === "string" && typeof item.priority === "string" &&
    typeof item.status === "string" && typeof item.added === "string" &&
    typeof item.notes === "string" && typeof item.today === "boolean";
}

function saveCases() {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(cases));
    return true;
  } catch (error) {
    console.error("Unable to save case records in this browser.", error);
    showToast("Could not save cases. Check browser storage settings.");
    return false;
  }
}

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, (character) => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
  })[character]);
}

function iconForType(type) {
  const symbols = {
    "X-ray": '<path d="M5 3.5h10v13H5z"/><path d="M7.5 7h5M7.5 10h5M7.5 13h3"/>',
    "Ultrasound": '<path d="M5 4h10v9H5z"/><path d="M7 15h6M10 13v2M7 7c.8-1 1.6 1 2.4 0s1.6 1 2.4 0"/>',
    "MRI": '<circle cx="10" cy="10" r="6.5"/><circle cx="10" cy="10" r="3"/><path d="M10 3.5v13"/>',
    "CT scan": '<circle cx="10" cy="10" r="6.5"/><path d="M3.5 10h13M10 3.5v13"/>',
    "Other": '<rect x="4" y="4" width="12" height="12" rx="2"/><path d="M7 10h6M10 7v6"/>'
  };
  return `<span class="case-type-icon" aria-hidden="true"><svg viewBox="0 0 20 20">${symbols[type] || symbols.Other}</svg></span>`;
}

function priorityClass(priority) {
  return `priority-${priority.toLowerCase().replace(/[^a-z]+/g, "-")}`;
}

function renderCaseRow(item) {
  const statusClass = item.status === "Reviewed" ? "review-done" : "review-pending";
  return `<tr>
    <td><div class="case-id-cell">${iconForType(item.type)}<span class="case-id-copy"><strong>${escapeHtml(item.id)}</strong><small>${escapeHtml(item.detail)}</small></span></div></td>
    <td>${escapeHtml(item.type)}</td>
    <td><span class="priority-pill ${priorityClass(item.priority)}">${escapeHtml(item.priority)}</span></td>
    <td><span class="review-pill ${statusClass}">${escapeHtml(item.status)}</span></td>
    <td>${escapeHtml(item.added)}</td>
    <td><button class="row-action" type="button" data-open-case="${escapeHtml(item.id)}" aria-label="Open case ${escapeHtml(item.id)}">↗</button></td>
  </tr>`;
}

function createSvgElement(name, attributes = {}) {
  const element = document.createElementNS("http://www.w3.org/2000/svg", name);
  Object.entries(attributes).forEach(([key, value]) => element.setAttribute(key, String(value)));
  return element;
}

function renderCharts() {
  const typeChart = document.getElementById("case-type-chart");
  const reviewChart = document.getElementById("review-status-chart");
  const reviewLegend = document.getElementById("review-chart-legend");
  const reviewTotal = document.getElementById("review-chart-total");
  if (!typeChart || !reviewChart || !reviewLegend || !reviewTotal) return;

  const categories = ["X-ray", "Ultrasound", "MRI", "CT scan", "Other"];
  const counts = categories.map((type) => cases.filter((item) => item.type === type).length);
  const maxCount = Math.max(...counts);
  typeChart.replaceChildren();

  if (maxCount === 0) {
    typeChart.append(createSvgElement("text", { x: 250, y: 112, "text-anchor": "middle", class: "chart-empty" }));
    typeChart.lastChild.textContent = "No cases to chart";
  } else {
    const left = 36;
    const baseline = 174;
    const plotHeight = 132;
    const interval = 91;
    const barWidth = 42;
    const scaleMax = Math.max(4, Math.ceil(maxCount / 4) * 4);

    for (let index = 0; index <= 4; index += 1) {
      const value = Math.round((scaleMax * index) / 4);
      const y = baseline - (plotHeight * index) / 4;
      typeChart.append(createSvgElement("line", {
        x1: left, x2: 490, y1: y, y2: y, class: index === 0 ? "chart-axis" : "chart-gridline"
      }));
      const tick = createSvgElement("text", { x: 27, y: y + 3, "text-anchor": "end" });
      tick.textContent = String(value);
      typeChart.append(tick);
    }

    categories.forEach((type, index) => {
      const count = counts[index];
      const height = (count / scaleMax) * plotHeight;
      const x = left + 10 + index * interval;
      if (count > 0) {
        typeChart.append(createSvgElement("rect", {
          x, y: baseline - height, width: barWidth, height, rx: 4, class: "chart-bar"
        }));
        const valueLabel = createSvgElement("text", {
          x: x + barWidth / 2, y: baseline - height - 7, "text-anchor": "middle", class: "chart-value"
        });
        valueLabel.textContent = String(count);
        typeChart.append(valueLabel);
      }
      const categoryLabel = createSvgElement("text", {
        x: x + barWidth / 2, y: 196, "text-anchor": "middle"
      });
      categoryLabel.textContent = type;
      typeChart.append(categoryLabel);
    });
  }
  typeChart.setAttribute("aria-label", `Cases by imaging type: ${categories.map((type, index) => `${type} ${counts[index]}`).join(", ")}`);

  const reviewed = cases.filter((item) => item.status === "Reviewed").length;
  const pending = cases.filter((item) => item.status === "Needs review").length;
  const total = reviewed + pending;
  const circumference = 2 * Math.PI * 52;
  const reviewedLength = total ? (reviewed / total) * circumference : 0;
  const pendingLength = total ? (pending / total) * circumference : 0;
  reviewChart.replaceChildren(
    createSvgElement("circle", { cx: 80, cy: 80, r: 52, class: "chart-track" }),
    createSvgElement("circle", {
      cx: 80, cy: 80, r: 52, class: "chart-reviewed",
      "stroke-dasharray": `${reviewedLength} ${circumference}`,
      transform: "rotate(-90 80 80)"
    }),
    createSvgElement("circle", {
      cx: 80, cy: 80, r: 52, class: "chart-pending",
      "stroke-dasharray": `${pendingLength} ${circumference}`,
      "stroke-dashoffset": -reviewedLength,
      transform: "rotate(-90 80 80)"
    })
  );
  const centerValue = createSvgElement("text", { x: 80, y: 80, class: "chart-center-value" });
  centerValue.textContent = String(total);
  reviewChart.append(centerValue);
  reviewChart.setAttribute("aria-label", `Review status: ${reviewed} reviewed, ${pending} need review`);

  reviewTotal.textContent = `${total} ${total === 1 ? "case" : "cases"}`;
  reviewLegend.replaceChildren();
  [
    { label: "Reviewed", count: reviewed, className: "reviewed" },
    { label: "Needs review", count: pending, className: "pending" }
  ].forEach(({ label, count, className }) => {
    const item = document.createElement("div");
    item.className = "chart-legend-item";
    const dot = document.createElement("span");
    dot.className = `chart-legend-dot ${className}`;
    const itemLabel = document.createElement("span");
    itemLabel.textContent = label;
    const itemCount = document.createElement("strong");
    itemCount.textContent = String(count);
    item.append(dot, itemLabel, itemCount);
    reviewLegend.append(item);
  });
}

function render() {
  const recent = [...cases].sort((a, b) => (b.today === a.today ? 0 : b.today ? 1 : -1)).slice(0, 4);
  document.getElementById("recent-cases").innerHTML = recent.map(renderCaseRow).join("");

  const query = document.getElementById("case-search").value.trim().toLowerCase();
  const filtered = cases.filter((item) => {
    const matchesFilter = activeFilter === "all" || item.status === activeFilter;
    const searchable = `${item.id} ${item.type} ${item.detail} ${item.priority} ${item.status}`.toLowerCase();
    return matchesFilter && searchable.includes(query);
  });
  document.getElementById("queue-cases").innerHTML = filtered.map(renderCaseRow).join("");
  document.getElementById("empty-state").classList.toggle("hidden", filtered.length !== 0);
  document.querySelector(".queue-table-card .table-wrap").classList.toggle("hidden", filtered.length === 0);

  const today = cases.filter((item) => item.today).length;
  const pending = cases.filter((item) => item.status === "Needs review").length;
  const reviewed = cases.filter((item) => item.status === "Reviewed" && item.today).length;
  document.getElementById("metric-today").textContent = String(today).padStart(2, "0");
  document.getElementById("metric-review").textContent = String(pending).padStart(2, "0");
  document.getElementById("metric-reviewed").textContent = String(reviewed).padStart(2, "0");
  document.getElementById("nav-count").textContent = String(cases.length).padStart(2, "0");
  document.getElementById("filter-all-count").textContent = cases.length;
  document.getElementById("case-count-label").textContent = `Showing ${recent.length} recent ${recent.length === 1 ? "case" : "cases"}`;
  document.getElementById("queue-count-label").textContent = `${filtered.length} ${filtered.length === 1 ? "case" : "cases"}`;
  updateFilterButtons();
  renderCharts();
}

function updateFilterButtons() {
  document.querySelectorAll(".filter-button").forEach((button) => {
    const isSelected = button.dataset.filter === activeFilter;
    button.classList.toggle("selected", isSelected);
    button.setAttribute("aria-pressed", String(isSelected));
  });
}

function updatePrototypeStatus() {
  const disclaimer = document.getElementById("ai-disclaimer");
  if (disclaimer) {
    disclaimer.textContent = "Browser demo status: local browser storage only. Android app version uses SQLite and optional local AI for software-help topics only.";
  }

  const storageStatus = document.getElementById("metric-storage-status");
  if (storageStatus) {
    storageStatus.textContent = "Browser localStorage only";
  }

  const connectionLabel = document.getElementById("connection-label");
  if (connectionLabel) {
    connectionLabel.textContent = "Browser demo: local storage only";
  }
}

function navigate(view) {
  const panel = document.getElementById(`${view}-view`);
  if (!panel) return;
  document.querySelectorAll(".view-panel").forEach((item) => item.classList.add("hidden"));
  panel.classList.remove("hidden");
  document.querySelectorAll(".nav-link").forEach((item) => item.classList.toggle("active", item.dataset.view === view));
  const label = { overview: "Overview", cases: "Case queue", workflow: "How it works", settings: "Device & privacy" }[view];
  document.getElementById("breadcrumb-current").textContent = label;
  document.getElementById("sidebar").classList.remove("open");
  if (view === "cases") document.getElementById("case-search").focus({ preventScroll: true });
}

function showToast(message) {
  const toast = document.getElementById("toast");
  toast.textContent = message;
  toast.classList.add("visible");
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => toast.classList.remove("visible"), 3000);
}

function openModal(modal) {
  lastFocusedElement = document.activeElement;
  modal.classList.remove("hidden");
  const firstInput = modal.querySelector("input:not([type=file]), button");
  if (firstInput) firstInput.focus();
}

function closeModal(modal) {
  modal.classList.add("hidden");
  if (modal.id === "case-modal") resetImagePreview();
  if (lastFocusedElement && typeof lastFocusedElement.focus === "function") lastFocusedElement.focus();
  lastFocusedElement = null;
}

function openCase(id) {
  const item = cases.find((entry) => entry.id === id);
  if (!item) return;
  const statusClass = item.status === "Reviewed" ? "review-done" : "review-pending";
  document.getElementById("detail-title").textContent = item.id;
  document.getElementById("detail-content").innerHTML = `
    <div class="detail-summary">${iconForType(item.type)}<span class="case-id-copy"><strong>${escapeHtml(item.id)}</strong><small>${escapeHtml(item.detail)}</small></span><span class="review-pill ${statusClass}" style="margin-left:auto">${escapeHtml(item.status)}</span></div>
    <div class="detail-fields">
      <div class="detail-field"><span>Imaging type</span><strong>${escapeHtml(item.type)}</strong></div>
      <div class="detail-field"><span>Priority selected</span><strong>${escapeHtml(item.priority)}</strong></div>
      <div class="detail-field"><span>Added</span><strong>${escapeHtml(item.added)}</strong></div>
      <div class="detail-field"><span>Record location</span><strong>This browser only</strong></div>
    </div>
    <div class="detail-notes"><span>Context for clinician</span><p>${escapeHtml(item.notes || "No additional context provided.")}</p></div>
    <div class="detail-warning">No image analysis or diagnostic recommendation is available. Priority is a staff-entered queue label, not a medical assessment.</div>
    <div class="detail-actions">
      ${item.status === "Needs review" ? '<button class="button button-primary" type="button" id="mark-reviewed">Mark clinician reviewed</button>' : '<button class="button button-secondary" type="button" id="mark-needs-review">Return to review queue</button>'}
    </div>`;
  document.getElementById("mark-reviewed")?.addEventListener("click", () => setReviewStatus(item.id, "Reviewed"));
  document.getElementById("mark-needs-review")?.addEventListener("click", () => setReviewStatus(item.id, "Needs review"));
  openModal(document.getElementById("detail-modal"));
}

function setReviewStatus(id, status) {
  const item = cases.find((entry) => entry.id === id);
  if (!item) return;
  const priorStatus = item.status;
  item.status = status;
  if (!saveCases()) {
    item.status = priorStatus;
    return;
  }
  closeModal(document.getElementById("detail-modal"));
  render();
  showToast(status === "Reviewed" ? "Case marked as clinician reviewed." : "Case returned to the review queue.");
}

function openNewCase() {
  const form = document.getElementById("case-form");
  form.reset();
  resetImagePreview();
  openModal(document.getElementById("case-modal"));
}

function resetImagePreview() {
  if (selectedImageUrl) URL.revokeObjectURL(selectedImageUrl);
  selectedImageUrl = "";
  document.getElementById("image-preview").classList.add("hidden");
  document.getElementById("image-preview").removeAttribute("src");
  document.getElementById("upload-copy-title").textContent = "Choose an image to preview";
}

document.querySelectorAll("[data-view]").forEach((button) => {
  button.addEventListener("click", () => navigate(button.dataset.view));
});

document.getElementById("menu-button").addEventListener("click", () => {
  document.getElementById("sidebar").classList.toggle("open");
});

document.getElementById("new-case-button").addEventListener("click", openNewCase);
document.getElementById("queue-new-case").addEventListener("click", openNewCase);
document.getElementById("close-modal").addEventListener("click", () => closeModal(document.getElementById("case-modal")));
document.getElementById("cancel-modal").addEventListener("click", () => closeModal(document.getElementById("case-modal")));
document.querySelector(".detail-close").addEventListener("click", () => closeModal(document.getElementById("detail-modal")));

document.querySelectorAll(".modal-backdrop").forEach((backdrop) => {
  backdrop.addEventListener("click", (event) => {
    if (event.target === backdrop) closeModal(backdrop);
  });
});

document.addEventListener("keydown", (event) => {
  if (event.key === "Escape") {
    document.querySelectorAll(".modal-backdrop:not(.hidden)").forEach(closeModal);
    document.getElementById("sidebar").classList.remove("open");
  }
  if (event.key === "/" && !event.target.matches("input, textarea, select")) {
    event.preventDefault();
    navigate("cases");
    document.getElementById("case-search").focus();
  }
});

document.getElementById("case-search").addEventListener("input", render);
document.querySelectorAll(".filter-button").forEach((button) => {
  button.addEventListener("click", () => {
    activeFilter = button.dataset.filter;
    render();
  });
});

document.addEventListener("click", (event) => {
  const button = event.target.closest("[data-open-case]");
  if (button) openCase(button.dataset.openCase);
});

document.getElementById("case-image").addEventListener("change", (event) => {
  const file = event.target.files && event.target.files[0];
  if (!file) return;
  if (!file.type.startsWith("image/")) {
    showToast("Please select an image file.");
    event.target.value = "";
    return;
  }
  if (file.size > 12 * 1024 * 1024) {
    showToast("Choose an image smaller than 12 MB.");
    event.target.value = "";
    return;
  }
  if (selectedImageUrl) URL.revokeObjectURL(selectedImageUrl);
  selectedImageUrl = URL.createObjectURL(file);
  const preview = document.getElementById("image-preview");
  preview.src = selectedImageUrl;
  preview.classList.remove("hidden");
  document.getElementById("upload-copy-title").textContent = file.name;
});

document.getElementById("case-form").addEventListener("submit", (event) => {
  event.preventDefault();
  const form = new FormData(event.currentTarget);
  const reference = String(form.get("reference")).trim();
  if (!reference) return;
  if (cases.some((item) => item.id.toLowerCase() === reference.toLowerCase())) {
    showToast("That case reference is already in the queue.");
    document.getElementById("case-reference").focus();
    return;
  }
  const now = new Date();
  const item = {
    id: reference,
    type: String(form.get("type")),
    detail: "Imaging · details not specified",
    priority: String(form.get("priority")),
    status: "Needs review",
    added: `${String(now.getHours()).padStart(2, "0")}:${String(now.getMinutes()).padStart(2, "0")}`,
    today: true,
    notes: String(form.get("notes")).trim()
  };
  cases.unshift(item);
  if (!saveCases()) {
    cases.shift();
    return;
  }
  closeModal(document.getElementById("case-modal"));
  render();
  showToast("Case added to the local review queue.");
  event.currentTarget.reset();
});

document.getElementById("clear-cases-button").addEventListener("click", () => {
  if (!window.confirm("Clear all case records saved in this browser? This cannot be undone.")) return;
  const previousCases = cases;
  cases = [];
  if (!saveCases()) {
    cases = previousCases;
    return;
  }
    render();
    showToast("Local case records cleared.");
  });

  document.getElementById("today-label").textContent = new Intl.DateTimeFormat("en-GB", {
    weekday: "long", day: "numeric", month: "long", year: "numeric"
  }).format(new Date()).toUpperCase();
updatePrototypeStatus();
render();
