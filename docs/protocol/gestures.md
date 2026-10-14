# Trackpad gesture specification

Any phone client (the web page today, an Android app later) implements these rules so the trackpad
feels the same everywhere. The numbers are in CSS pixels (density-independent) and milliseconds.
The web implementation is `src/PcControl.Web/src/gestures.ts` and its tests are the executable
form of this document.

The client only recognises gestures. Cursor speed, acceleration and sensitivity live on the PC (see
`settings` in [websocket.md](websocket.md)), so every client moves the cursor the same way.

## Constants

| Name | Value | Meaning |
| --- | --- | --- |
| `TOUCH_SLOP` | 6 px | Movement below this still counts as "not moved" (a tap or a hold) |
| `TAP_MAX_MS` | 250 ms | A single touch shorter than this, without moving, is a tap |
| `HOLD_MS` | 400 ms | A single touch held still this long starts a drag |
| `TWO_FINGER_TAP_MAX_MS` | 300 ms | A two-finger touch shorter than this, without moving, is a tap |
| `TWO_FINGER_SLOP` | 10 px | Per-gesture movement below this still counts as "not moved" |
| `MODE_DECISION_PX` | 12 px | Two-finger movement needed to decide between scroll and pinch |
| `SCROLL_UNITS_PER_PX` | 2.4 | Wheel units per pixel of two-finger drag (120 units = one notch) |
| `PINCH_STEP_PX` | 40 px | Change in finger distance that makes one zoom step |

## Rules

### One finger

1. **Move.** The cursor follows the finger. Nothing is sent until the finger has moved more than
   `TOUCH_SLOP` from where it landed; then the movement since landing is sent at once, and every
   later movement is sent as it happens.
2. **Tap.** Lifting within `TAP_MAX_MS` without having passed `TOUCH_SLOP` sends a **left click**.
   A double tap is simply two taps; Windows turns them into a double click.
3. **Press and hold to drag.** Holding still (within `TOUCH_SLOP`) for `HOLD_MS` presses the **left
   button down** and gives a short vibration. Moving the finger now drags. Lifting releases the button.
   Lifting without moving still releases it (so a long press is not a click).

### Two fingers

A two-finger gesture starts when a second finger lands while the first is not dragging. It cancels
any pending tap or hold. A third finger is ignored.

4. **Tap.** If both fingers lift within `TWO_FINGER_TAP_MAX_MS` and neither moved more than
   `TWO_FINGER_SLOP`, send a **right click**.
5. **Scroll or pinch.** Once the fingers have moved `MODE_DECISION_PX` (the movement of their midpoint,
   or the change in their distance, whichever is larger), the gesture is locked to one mode until all
   fingers lift:
   - If the change in distance is larger than the midpoint's movement, it is a **pinch**.
   - Otherwise it is a **scroll**.
6. **Scroll.** The midpoint's movement is sent as wheel units: `SCROLL_UNITS_PER_PX` per pixel,
   keeping the fractional remainder so slow scrolling is not lost.
   - *Natural scrolling* (default): the content follows the fingers. Fingers moving down scroll up
     (positive vertical wheel value); fingers moving right send a negative horizontal value.
   - With natural scrolling off, both signs flip.
7. **Pinch.** Each `PINCH_STEP_PX` of change in distance sends one zoom step: fingers spreading is a
   positive step (zoom in, Ctrl + wheel up), fingers closing is a negative one. The PC presses Ctrl
   around the wheel event.

### After a gesture

8. When one finger of a two-finger gesture lifts, the remaining finger is ignored until every finger
   has lifted. This avoids the cursor jumping.
9. If the browser or OS cancels the touch, release a held drag button and reset.

## Messages produced

| Gesture | Message |
| --- | --- |
| Move | binary `move` frame (summed once per animation frame) |
| Tap | `button` left `click` |
| Two-finger tap | `button` right `click` |
| Drag start / end | `button` left `down` / `up` |
| Scroll | binary `scroll` frame |
| Pinch | `zoom` with `steps` |
