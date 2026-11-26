# Rapoo MT760 Mini tools: hold-to-choose Alt+Tab on the side wheel + Russian UI for A HUB

> **Interface: English and Russian.** MT760 Helper follows the Windows language and can be switched in its settings. / **Интерфейс на английском и русском.**

Two small, offline Windows tools for the **Rapoo MT760 Mini** mouse and its **A HUB** driver app:

1. **MT760 Helper** — the side (thumb) wheel opens the Windows Alt+Tab switcher and **keeps it open** while you pick a window, instead of the instant flash you get from an A HUB Alt+Tab macro.
2. **A HUB Russian translation** — a complete `ru_RU.qm` for A HUB, whose shipped Russian file is empty.

Keywords: Rapoo MT760, MT760 Mini, Rapoo A HUB, thumb wheel, side scroll wheel, horizontal scroll, Alt+Tab, task switcher, window switcher, Windows 11, mouse remap, русский перевод A HUB, Qt .qm translation.

Русское описание — ниже.

---

## MT760 Helper

The A HUB app can only bind the side wheel to a firmware macro (Alt down → Tab → Alt up in ~0.1 s), so windows flash past. MT760 Helper replaces that:

- Roll the side wheel: Alt+Tab opens and Alt stays held. Keep rolling to move through windows.
- The selected window opens when you stop scrolling and moving the mouse for a set time (default 4.5 s), with a countdown at the bottom of the screen.
- While the list is open: click a thumbnail to open it, click its X to close that window, left click on empty space = open the selection, right click = cancel, wheel click = open, Enter / Esc. Each mouse button is configurable (open / cancel / do nothing). Clicks never leak into other apps (taskbar, second monitor).
- **Shift + side wheel** keeps normal horizontal scrolling (timelines, wide tables).
- Reacts **only to the Rapoo mouse** (raw-input device check), so laptop touchpads and other mice scroll as usual.
- Tray icon, settings window in English or Russian (follows the Windows language, switchable), optional autostart. No network, no admin rights.

**Setup**
1. In A HUB, set the side wheel back to its default action (Roller left / Roller right) so the mouse sends ordinary horizontal scroll instead of a macro.
2. Download `MT760Helper.exe` from [Releases](../../releases) (or build it, see below) and run it. Tick "Start with Windows" for autostart. Full instructions: `mt760-helper/README-en.txt`.

The settings window has a live "wheel check" that shows what the side wheel sends and warns if A HUB's Alt+Tab macro is still assigned.

**Build**: `mt760-helper\build.cmd` uses the C# compiler that ships with Windows (.NET Framework 4.x). Nothing to download.

**Test**: `mt760-helper\tests\Запустить тест кнопок.cmd` runs an end-to-end test (moves the mouse by itself for about a minute; don't touch it) and writes `result.txt`.

## A HUB Russian translation

A HUB (Qt 5.15) loads `translations\<Language>.qm` from its install folder; the shipped `ru_RU.qm` is a 462-byte stub. `ahub-russian\dist\ru_RU.qm` holds all 1635 strings found in A HUB's other translations plus 22 strings present in its QML but translated in no language, in plain, clear Russian.

Install: start A HUB, then run `ahub-russian\Установить перевод.cmd` (copies the file, sets `"Language": "ru_RU"` in `%LOCALAPPDATA%\A HUB\configs\config.json`, restarts A HUB). `Вернуть английский.cmd` restores the original. A HUB itself is not modified; an A HUB update may reset the file — just install again.

---

## Notes for AI agents and developers

Layout:
- `mt760-helper/src` — C# 5 (built with the Windows-bundled `csc.exe` 4.x, so no `$""`, `?.`, etc.).
  - `Engine.cs` — low-level mouse hook + raw input, holds Alt, sends Tab/Shift+Tab, handles clicks/keys while switching.
  - `Lang.cs` — interface language; texts are written in place as `Lang.T("русский", "English")`.
  - `Settings.cs` (`%APPDATA%\MT760Helper\settings.ini`, HKCU Run autostart), `SettingsForm.cs`, `Overlay.cs` (countdown), `TrayApp.cs`, `Program.cs` (single instance, error log, optional trace when `%APPDATA%\MT760Helper\trace.on` exists).
- `mt760-helper/tests/ClickTest.cs` — end-to-end test with synthetic input (`RapooOnly=0` so injected wheel events count).
- `ahub-russian/tools/QmTool.cs` — reads/writes Qt `.qm` (`dump`, `build`, `check`); messages with an empty context are written without a context tag so they match any context.
- `ahub-russian/tools/ExtractQml.cs` — pulls plain and zlib-compressed QML out of the Qt resources in `A HUB.exe` and lists every `qsTr` string.

Findings that cost time:
- **Device identification**: when `WH_MOUSE_LL` is called, the matching `WM_INPUT` is already queued, so the hook can `PeekMessage` it and read the device (VID `24AE`) before deciding to swallow the event. Blocking in the LL hook does not suppress `WM_INPUT`.
- **Windows 11 Alt+Tab** is one full-screen window (`XamlExplorerHostIslandWindow`, explorer.exe), so `WindowFromPoint` can't tell a thumbnail from empty space. UI Automation exposes the thumbnails (`ListItem`s) only sometimes.
- **A click inside the Win11 switcher makes it stick**: after a click on empty space (or on a thumbnail's X) releasing Alt, Enter and Space do nothing; Esc still cancels. Pressing **Tab** un-sticks it, but focus restarts at the first window (Tab → index 0, Shift+Tab → last). So the helper lets clicks through to Windows (thumbnail opens, X closes), tracks the net wheel steps, and rebuilds the selection with Tabs before opening — lazily, on the next wheel step or on commit, with ~150 ms before releasing Alt. A click is treated as an X-close when an Alt+Tab-eligible window (visible, titled, unowned, not a tool window, not cloaked) disappeared.
- Hovering a thumbnail in Win11 Alt+Tab does **not** move the selection.
- Before releasing Alt the helper taps an unassigned key (VK `0xE8`) so menus don't open.
- A HUB stores key/DPI settings in the mouse; the side-wheel key IDs in `%LOCALAPPDATA%\A HUB\configs\MT760 Mini\*.json` are 7 and 8 (`"HorizontalRoller": 255` = left, `1` = right). A HUB macro keys come from the mouse's own HID keyboard collection.
- A HUB's home-screen battery percent appears only intermittently after a restart, in any language; it is not caused by translations.
- A HUB (Qt 5.15) ignores posted mouse messages and exposes no UIA names, so its screens can't be automated without moving the real cursor.

License: MIT. Rapoo and A HUB are trademarks of their owners; this project is not affiliated with Rapoo and ships none of A HUB's files.

---

## По-русски

**MT760 Helper** — переключение окон боковым колесом мыши Rapoo MT760 Mini. Колесо открывает список окон (Alt+Tab) и держит его, пока вы выбираете. Окно откроется, когда вы перестанете крутить колесо (время настраивается), внизу идёт отсчёт. Пока список открыт: левая кнопка — открыть выбранное, правая — отмена, нажатие колеса — открыть; действия кнопок меняются в настройках. Shift + боковое колесо — обычная горизонтальная прокрутка. Тачпад и другие мыши не затрагиваются. Работает без интернета.

Установка: в A HUB верните боковому колесу стандартное действие («Колесо влево / вправо»), скачайте `MT760Helper.exe` из [Releases](../../releases) и запустите. Подробности — в `mt760-helper/README.txt`.

**Русский перевод A HUB** — полный файл `ru_RU.qm` (в A HUB русский файл пустой). Запустите A HUB и дважды щёлкните `ahub-russian\Установить перевод.cmd`. Вернуть как было — `Вернуть английский.cmd`. Подробности — в `ahub-russian/README.txt`.
