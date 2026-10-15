// The app launcher panel: a search box, a PowerToys Run button, and the Start menu as a list.

import { api, type AppItem, type AppsResponse } from "./api";
import { buildSections, launchFailureMessage, powerToysMessage } from "./appsview";
import { icon } from "./icons";

export interface AppsPanelHandlers {
  /** An app was started. The caller closes the panel and says so. */
  onLaunched: (name: string) => void;
  /** Something went wrong; tell the user. */
  onProblem: (message: string) => void;
  /** The PowerToys Run button was pressed and the shortcut sent. */
  onPowerToysOpened: (message: string) => void;
}

export interface AppsPanel {
  element: HTMLElement;
  /** Call each time the panel is opened: starts with an empty search and reads the Start menu again, so new apps show up. */
  open: () => Promise<void>;
}

export function createAppsPanel(handlers: AppsPanelHandlers, showPowerToys: boolean): AppsPanel {
  let data: AppsResponse = { apps: [], recent: [], launcher: { powerToysRunShortcut: "", powerToysDetected: false } };
  let loaded = false;

  const search = document.createElement("input");
  search.type = "search";
  search.placeholder = "Search apps";
  search.autocomplete = "off";
  search.setAttribute("autocapitalize", "off");
  search.setAttribute("autocorrect", "off");
  search.spellcheck = false;
  search.setAttribute("enterkeyhint", "go");
  search.setAttribute("aria-label", "Search apps");

  const head = document.createElement("div");
  head.className = "apps-head";
  head.append(search);

  if (showPowerToys) {
    const powerToys = document.createElement("button");
    powerToys.type = "button";
    powerToys.className = "powertoys";
    powerToys.title = "Open PowerToys Run on the PC";
    powerToys.append(icon("search"), "PowerToys Run");
    powerToys.addEventListener("click", async () => {
      try {
        await api.powerToysRun();
        handlers.onPowerToysOpened(
          powerToysMessage(data.launcher.powerToysRunShortcut || "its shortcut", data.launcher.powerToysDetected),
        );
      } catch (error) {
        handlers.onProblem(launchFailureMessage(error));
      }
    });
    head.append(powerToys);
  }

  const list = document.createElement("div");
  list.className = "app-list";

  const panel = document.createElement("section");
  panel.className = "apps-panel";
  panel.hidden = true;
  panel.append(head, list);

  const launch = async (app: AppItem) => {
    try {
      const result = await api.launchApp(app.id);
      handlers.onLaunched(result.name);
    } catch (error) {
      handlers.onProblem(launchFailureMessage(error, app.name));
      void refresh(); // the list may be out of date
    }
  };

  const toggleFavorite = async (app: AppItem) => {
    const wanted = !app.favorite;
    app.favorite = wanted; // show it at once; undo if the PC says no
    render();
    try {
      await api.setFavorite(app.id, wanted);
    } catch (error) {
      app.favorite = !wanted;
      render();
      handlers.onProblem(launchFailureMessage(error, app.name));
    }
  };

  const row = (app: AppItem): HTMLElement => {
    const wrapper = document.createElement("div");
    wrapper.className = "app-row";

    const open = document.createElement("button");
    open.type = "button";
    open.className = "app-open";
    open.addEventListener("click", () => void launch(app));

    const image = document.createElement("img");
    image.className = "app-icon";
    image.alt = "";
    image.width = 32;
    image.height = 32;
    image.loading = "lazy";
    image.decoding = "async";
    image.src = `/api/apps/${app.id}/icon`;
    // An app without an icon gets its first letter instead of a broken image.
    image.addEventListener("error", () => {
      const letter = document.createElement("span");
      letter.className = "app-icon letter";
      letter.textContent = (app.name.trim()[0] ?? "?").toUpperCase();
      image.replaceWith(letter);
    });

    const name = document.createElement("span");
    name.className = "app-name";
    name.textContent = app.name;
    open.append(image, name);

    const star = document.createElement("button");
    star.type = "button";
    star.className = "app-star" + (app.favorite ? " on" : "");
    star.textContent = app.favorite ? "★" : "☆";
    star.setAttribute("aria-label", app.favorite ? `Remove ${app.name} from favorites` : `Add ${app.name} to favorites`);
    star.setAttribute("aria-pressed", String(app.favorite));
    star.addEventListener("click", () => void toggleFavorite(app));

    wrapper.append(open, star);
    return wrapper;
  };

  function render(): void {
    const sections = buildSections(data.apps, data.recent, search.value);
    const fragment = document.createDocumentFragment();

    if (sections.length === 0) {
      const empty = document.createElement("p");
      empty.className = "app-empty";
      empty.textContent = !loaded ? "Loading…" : search.value.trim() ? "No apps match that." : "No apps found.";
      fragment.append(empty);
    }

    for (const section of sections) {
      const title = document.createElement("h2");
      title.className = "app-section";
      title.textContent = section.title;
      fragment.append(title);
      for (const app of section.apps) fragment.append(row(app));
    }

    list.replaceChildren(fragment);
  }

  async function refresh(): Promise<void> {
    try {
      data = await api.apps();
      loaded = true;
    } catch (error) {
      if (!loaded) handlers.onProblem(launchFailureMessage(error));
    }
    render();
  }

  search.addEventListener("input", () => {
    render();
    list.scrollTop = 0;
  });

  // "Go" on the phone keyboard starts the best match.
  search.addEventListener("keydown", (event) => {
    if (event.key !== "Enter") return;
    event.preventDefault();
    const first = buildSections(data.apps, data.recent, search.value)[0]?.apps[0];
    if (first && search.value.trim() !== "") void launch(first);
  });

  render();
  return {
    element: panel,
    open: () => {
      search.value = "";
      list.scrollTop = 0;
      return refresh();
    },
  };
}
