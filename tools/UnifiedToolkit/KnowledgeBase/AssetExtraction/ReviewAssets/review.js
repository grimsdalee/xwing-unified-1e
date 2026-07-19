(() => {
  "use strict";

  const storageKey = "pilot-token-v2";
  const data = window.pilotTokenReviewData;
  const root = document.getElementById("root");
  const summary = document.getElementById("summary");

  if (!data || !data.plan || !data.sources) {
    root.innerHTML = '<div class="error">Review data could not be loaded. Ensure review-data.js exists beside index.html.</div>';
    return;
  }

  let plan = data.plan;
  const sources = data.sources;

  const escapeHtml = value => String(value ?? "").replace(
    /[&<>"']/g,
    character => ({
      "&": "&amp;",
      "<": "&lt;",
      ">": "&gt;",
      '"': "&quot;",
      "'": "&#39;"
    })[character]);

  const hasCrop = entry =>
    entry.cropX !== null &&
    entry.cropX !== undefined &&
    entry.cropY !== null &&
    entry.cropY !== undefined &&
    Number(entry.cropWidth) > 0 &&
    Number(entry.cropHeight) > 0;

  const clamp = value => Math.max(0, Math.min(1, value));

  function locate(sheetId, entityId) {
    const sheet = plan.sheets.find(item => item.sheetId === sheetId);
    const entry = sheet?.entries.find(item => item.entityId === entityId);
    return { sheet, entry };
  }

  function field(sheet, entry, property, label) {
    const value = entry[property] ?? "";
    return `
      <label>
        ${label}
        <input
          type="number"
          min="0"
          max="1"
          step="0.001"
          value="${escapeHtml(value)}"
          data-action="set-value"
          data-sheet-id="${escapeHtml(sheet.sheetId)}"
          data-entity-id="${escapeHtml(entry.entityId)}"
          data-property="${property}">
      </label>`;
  }

  function entryMarkup(sheet, entry) {
    const hasSuggestion = Number(entry.suggestedCropWidth) > 0;
    const suggestion = hasSuggestion
      ? `<button class="accept suggestion" type="button"
            data-action="accept-suggestion"
            data-sheet-id="${escapeHtml(sheet.sheetId)}"
            data-entity-id="${escapeHtml(entry.entityId)}">
            Accept suggestion ${Math.round((entry.suggestionConfidence || 0) * 100)}% from ${escapeHtml(entry.suggestionSource)}
         </button>`
      : "";

    return `
      <div class="entry">
        <b>${escapeHtml(entry.displayName)}</b><br>
        <small>${escapeHtml(entry.shipId)} · PS ${escapeHtml(entry.pilotSkill)} · ${escapeHtml(entry.squadPointCost)} pts</small>
        ${suggestion}
        <div class="fields">
          ${field(sheet, entry, "cropX", "X")}
          ${field(sheet, entry, "cropY", "Y")}
          ${field(sheet, entry, "cropWidth", "Width")}
          ${field(sheet, entry, "cropHeight", "Height")}
        </div>
        <button class="danger" type="button"
          data-action="clear-crop"
          data-sheet-id="${escapeHtml(sheet.sheetId)}"
          data-entity-id="${escapeHtml(entry.entityId)}">
          Clear crop
        </button>
      </div>`;
  }

  function renderSummary() {
    const entries = plan.sheets.flatMap(sheet => sheet.entries);
    const completeEntries = entries.filter(hasCrop).length;
    const completeSheets = plan.sheets.filter(sheet => sheet.entries.every(hasCrop)).length;

    summary.textContent =
      `${completeSheets}/${plan.sheets.length} sheets complete · ` +
      `${completeEntries}/${entries.length} pilot crops complete`;
  }

  function render() {
    root.innerHTML = "";
    renderSummary();

    plan.sheets.forEach(sheet => {
      const completeCount = sheet.entries.filter(hasCrop).length;
      const section = document.createElement("section");
      section.className = "sheet";
      section.innerHTML = `
        <h2>
          ${escapeHtml(sheet.sheetId)}
          <span class="badge ${sheet.entries.every(hasCrop) ? "complete" : ""}">
            ${completeCount}/${sheet.entries.length}
          </span>
        </h2>
        <div class="work">
          <div>
            <div class="image-wrap" id="image-wrap-${escapeHtml(sheet.sheetId)}">
              <img id="image-${escapeHtml(sheet.sheetId)}"
                   src="${escapeHtml(sources[sheet.sheetId])}"
                   alt="${escapeHtml(sheet.sheetId)} source texture">
            </div>
          </div>
          <div>${sheet.entries.map(entry => entryMarkup(sheet, entry)).join("")}</div>
        </div>`;

      root.appendChild(section);

      const image = document.getElementById(`image-${sheet.sheetId}`);
      image.addEventListener("load", () => draw(sheet));
      image.addEventListener("error", () => {
        image.alt = `Could not load ${sources[sheet.sheetId]}`;
      });

      draw(sheet);
    });
  }

  function draw(sheet) {
    const wrapper = document.getElementById(`image-wrap-${sheet.sheetId}`);
    if (!wrapper) {
      return;
    }

    wrapper.querySelectorAll(".crop-box").forEach(element => element.remove());

    sheet.entries.forEach(entry => {
      if (!hasCrop(entry)) {
        return;
      }

      const box = document.createElement("div");
      box.className = "crop-box";
      box.title = entry.displayName;
      box.style.left = `${Number(entry.cropX) * 100}%`;
      box.style.top = `${Number(entry.cropY) * 100}%`;
      box.style.width = `${Number(entry.cropWidth) * 100}%`;
      box.style.height = `${Number(entry.cropHeight) * 100}%`;

      const label = document.createElement("span");
      label.className = "crop-box-label";
      label.textContent = entry.displayName;
      box.appendChild(label);

      wrapper.appendChild(box);
    });
  }

  function setValue(sheetId, entityId, property, rawValue) {
    const { sheet, entry } = locate(sheetId, entityId);
    if (!sheet || !entry) {
      return;
    }

    entry[property] = rawValue === ""
      ? null
      : clamp(Number.parseFloat(rawValue));

    draw(sheet);
    renderSummary();
  }

  function clearCrop(sheetId, entityId) {
    const { entry } = locate(sheetId, entityId);
    if (!entry) {
      return;
    }

    entry.cropX = null;
    entry.cropY = null;
    entry.cropWidth = null;
    entry.cropHeight = null;
    render();
  }

  function acceptSuggestion(sheetId, entityId) {
    const { entry } = locate(sheetId, entityId);
    if (!entry) {
      return;
    }

    entry.cropX = entry.suggestedCropX;
    entry.cropY = entry.suggestedCropY;
    entry.cropWidth = entry.suggestedCropWidth;
    entry.cropHeight = entry.suggestedCropHeight;
    render();
  }

  function cleanedPlan() {
    const copy = JSON.parse(JSON.stringify(plan));
    copy.generatedUtc = new Date().toISOString();

    copy.sheets.forEach(sheet => {
      sheet.entries.forEach(entry => {
        delete entry.suggestionSource;
        delete entry.suggestionConfidence;
        delete entry.suggestedCropX;
        delete entry.suggestedCropY;
        delete entry.suggestedCropWidth;
        delete entry.suggestedCropHeight;
      });
    });

    return copy;
  }

  function downloadPlan() {
    const blob = new Blob(
      [JSON.stringify(cleanedPlan(), null, 2)],
      { type: "application/json" });

    const link = document.createElement("a");
    link.href = URL.createObjectURL(blob);
    link.download = "pilot-token-extraction-plan.v2.completed.json";
    link.click();
    URL.revokeObjectURL(link.href);
  }

  function saveProgress() {
    localStorage.setItem(storageKey, JSON.stringify(plan));
    window.alert("Browser progress saved.");
  }

  function restoreProgress() {
    const saved = localStorage.getItem(storageKey);
    if (!saved) {
      window.alert("No saved browser progress was found.");
      return;
    }

    const savedPlan = JSON.parse(saved);
    const savedEntries = new Map();

    for (const savedSheet of savedPlan.sheets || []) {
      for (const savedEntry of savedSheet.entries || []) {
        savedEntries.set(savedEntry.entityId, savedEntry);
      }
    }

    for (const currentSheet of plan.sheets) {
      for (const currentEntry of currentSheet.entries) {
        const savedEntry = savedEntries.get(currentEntry.entityId);
        if (!savedEntry) {
          continue;
        }

        currentEntry.cropX = savedEntry.cropX ?? currentEntry.cropX;
        currentEntry.cropY = savedEntry.cropY ?? currentEntry.cropY;
        currentEntry.cropWidth = savedEntry.cropWidth ?? currentEntry.cropWidth;
        currentEntry.cropHeight = savedEntry.cropHeight ?? currentEntry.cropHeight;
      }
    }

    render();
    window.alert("Browser progress merged into the corrected review plan.");
  }

  root.addEventListener("change", event => {
    const element = event.target;
    if (!(element instanceof HTMLInputElement) || element.dataset.action !== "set-value") {
      return;
    }

    setValue(
      element.dataset.sheetId,
      element.dataset.entityId,
      element.dataset.property,
      element.value);
  });

  root.addEventListener("click", event => {
    const button = event.target.closest("button[data-action]");
    if (!button) {
      return;
    }

    if (button.dataset.action === "clear-crop") {
      clearCrop(button.dataset.sheetId, button.dataset.entityId);
    }

    if (button.dataset.action === "accept-suggestion") {
      acceptSuggestion(button.dataset.sheetId, button.dataset.entityId);
    }
  });

  document.getElementById("downloadButton").addEventListener("click", downloadPlan);
  document.getElementById("saveButton").addEventListener("click", saveProgress);
  document.getElementById("restoreButton").addEventListener("click", restoreProgress);

  render();
})();
