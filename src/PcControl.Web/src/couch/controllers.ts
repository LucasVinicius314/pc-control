// The controllers shown in the top bar: one chip each, with a battery when the controller has one.

import type { ControllerItem } from "../api";
import { icon } from "../icons";
import { el } from "./ui";

const LEVELS = ["empty", "low", "medium", "full"] as const;

/** How many of the battery's three bars are lit: none when empty, all three when full. */
export function barsFor(level: ControllerItem["level"]): number {
  const index = LEVELS.findIndex((l) => l === level);
  return index < 0 ? 0 : index;
}

/** What the chip says when pointed at, and what a screen reader reads. */
export function describeController(c: ControllerItem, position: number): string {
  const name = `Controller ${position}`;
  if (c.power === "wired") return `${name}: wired`;
  const charging = c.charging ? ", charging" : "";
  if (c.power === "battery" && typeof c.percent === "number") return `${name}: battery ${c.percent}%${charging}`;
  if (c.power === "battery" && c.level) return `${name}: battery ${c.level === "medium" ? "half" : c.level}${charging}`;
  return `${name}: connected`;
}

/** Shows the controllers, or nothing at all when none are connected. */
export function renderControllers(box: HTMLElement, controllers: readonly ControllerItem[]): void {
  box.replaceChildren(
    ...controllers.map((c, index) => {
      const chip = el("div", "pad-chip");
      chip.title = describeController(c, index + 1);
      chip.setAttribute("aria-label", chip.title);
      chip.append(icon("gamepad"));
      if (c.power === "wired") {
        chip.append(el("span", "pad-wired", "Wired"));
      } else if (c.power === "battery" && c.level) {
        chip.classList.add(`level-${c.level}`);
        const battery = el("span", "battery");
        const lit = barsFor(c.level);
        for (let i = 0; i < 3; i++) battery.append(el("i", i < lit ? "on" : ""));
        chip.append(battery);
        if (typeof c.percent === "number") chip.append(el("span", "pad-percent", `${c.percent}%`));
        if (c.charging) chip.classList.add("charging");
      }
      return chip;
    }),
  );
}
