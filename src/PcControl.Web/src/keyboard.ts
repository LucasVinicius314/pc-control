// Types on the PC from the phone's on-screen keyboard.
//
// Android keyboards do not send reliable key events. The classic trap is Backspace in an empty
// field: there is nothing to delete, so no event arrives. The hidden input therefore always holds
// one invisible character (the sentinel). Deleting it fires an input event we turn into Backspace,
// and we put the sentinel back. Words being composed (suggestions, autocorrect) are sent live as
// small edits, see textdiff.ts.

import type { InputChannel } from "./input";
import { charToCode, type Modifiers } from "./modifiers";
import { diffComposition } from "./textdiff";

const SENTINEL = "​";

// Physical-keyboard keys (and desktop browsers) arrive as keydown events with a real key name.
const KEYDOWN_KEYS = new Set([
  "Enter",
  "Backspace",
  "Delete",
  "Tab",
  "Escape",
  "ArrowLeft",
  "ArrowRight",
  "ArrowUp",
  "ArrowDown",
  "Home",
  "End",
  "PageUp",
  "PageDown",
]);

export class KeyboardInput {
  readonly element: HTMLInputElement;
  private composing = false;
  private composed = "";

  constructor(
    private readonly input: InputChannel,
    private readonly mods: Modifiers,
    onFocusChange: (focused: boolean) => void,
  ) {
    const el = document.createElement("input");
    el.type = "text";
    el.className = "keyboard-capture";
    el.setAttribute("aria-label", "Keyboard input for the PC");
    el.autocapitalize = "off";
    el.autocomplete = "off";
    el.spellcheck = false;
    el.setAttribute("autocorrect", "off");
    el.setAttribute("enterkeyhint", "enter");
    el.value = SENTINEL;
    this.element = el;

    el.addEventListener("keydown", (e) => this.onKeyDown(e));
    el.addEventListener("compositionstart", () => this.onCompositionStart());
    el.addEventListener("compositionend", (e) => this.onCompositionEnd(e));
    el.addEventListener("input", (e) => this.onInput(e as InputEvent));
    el.addEventListener("focus", () => {
      this.resetValue();
      onFocusChange(true);
    });
    el.addEventListener("blur", () => onFocusChange(false));
  }

  get focused(): boolean {
    return document.activeElement === this.element;
  }

  show(): void {
    this.element.focus();
  }

  hide(): void {
    this.element.blur();
  }

  /** Presses a key by its code (for example from the on-screen special-key row). */
  pressKey(code: string): void {
    this.input.key(code, "press");
    this.mods.consumeOnce();
  }

  private onKeyDown(e: KeyboardEvent): void {
    // Keyboards that compose text report keyCode 229 and a useless key name; those go through input events.
    if (e.isComposing || e.keyCode === 229 || !KEYDOWN_KEYS.has(e.key)) return;
    e.preventDefault();
    this.pressKey(e.key); // these key names are also the codes the PC expects
  }

  private onCompositionStart(): void {
    this.composing = true;
    this.composed = "";
  }

  private onCompositionEnd(e: CompositionEvent): void {
    this.applyComposition(e.data ?? this.stripped());
    this.composing = false;
    this.composed = "";
    this.resetValue();
  }

  private onInput(e: InputEvent): void {
    if (this.composing || e.isComposing) {
      this.applyComposition(this.stripped());
      return;
    }

    switch (e.inputType) {
      case "insertText":
      case "insertFromPaste":
      case "insertFromDrop":
      case "insertReplacementText":
        this.typed(e.data ?? this.stripped());
        break;
      case "deleteContentBackward":
      case "deleteWordBackward":
      case "deleteSoftLineBackward":
        this.pressKey("Backspace");
        break;
      case "deleteContentForward":
        this.pressKey("Delete");
        break;
      case "insertLineBreak":
      case "insertParagraph":
        this.pressKey("Enter");
        break;
      default:
        // insertCompositionText after compositionend was already sent by the composition handlers.
        break;
    }

    this.resetValue();
  }

  /** Sends only what changed in the word being composed. */
  private applyComposition(next: string): void {
    const edit = diffComposition(this.composed, next);
    for (let i = 0; i < edit.backspaces; i++) this.input.key("Backspace", "press");
    this.input.text(edit.insert);
    this.composed = next;
  }

  /** Typed text. With a shortcut modifier held, a single letter becomes a real key press (Ctrl+C). */
  private typed(text: string): void {
    const chars = Array.from(text);
    if (this.mods.anyHeld && chars.length === 1) {
      const code = charToCode(chars[0] ?? "");
      if (code) {
        this.input.key(code, "press");
        this.mods.consumeOnce();
        return;
      }
    }

    this.input.text(text);
    this.mods.consumeOnce();
  }

  private stripped(): string {
    return this.element.value.split(SENTINEL).join("");
  }

  private resetValue(): void {
    if (this.element.value !== SENTINEL) this.element.value = SENTINEL;
    try {
      this.element.setSelectionRange(SENTINEL.length, SENTINEL.length);
    } catch {
      // Some input types refuse selection; text inputs do not, but never fail typing over it.
    }
  }
}
