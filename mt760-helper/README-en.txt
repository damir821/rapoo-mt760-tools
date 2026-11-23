MT760 Helper — switch windows with the side wheel of the Rapoo MT760 Mini
=========================================================================

What it does
------------
Roll the side wheel and the Windows window list (Alt+Tab) opens — and it does
NOT disappear right away. Keep rolling to choose a window. When you stop rolling
the wheel and moving the mouse, the selected window opens after 4.5 seconds
(adjustable in the settings). A countdown is shown at the bottom of the screen.

While the window list is open:
  • side or main wheel — choose a window;
  • left button — open the selected window;
  • right button — cancel, stay in the current window;
  • wheel click — open the selected window;
  • Enter — open, Esc — cancel.
What each mouse button does can be changed in the settings ("Mouse buttons
while the window list is open"): "Open selected window", "Cancel switching"
or "Do nothing".

Clicking a window thumbnail opens that very window, but only when Windows
reports where the thumbnails are. Windows 11 does not always do that; when it
doesn't, the click works like a click outside the windows (opens the selection).
Clicks made while the list is open never reach other programs (taskbar, a
second monitor).

Shift + side wheel — normal horizontal scrolling (timelines, wide tables).

The program works offline, sends nothing anywhere and does not touch A HUB's
files. Laptop touchpads and other mice scroll as usual: the helper reacts only
to the Rapoo mouse wheel.

The interface language follows Windows (Russian or English) and can be changed
in the settings ("Язык / Language").


Step 1. Remove the fast Alt+Tab macro in A HUB (required)
---------------------------------------------------------
If the side wheel is bound to an Alt+Tab macro in A HUB, the mouse itself
presses Alt+Tab and releases Alt after 0.1 s, so windows flash past.

1. Open A HUB → Remap.
2. Select the side wheel and give it back its default action:
   Roller left / Roller right. If you can't find it, reset the button mapping
   to factory defaults.
3. Save / apply.

Check: in the MT760 Helper window, "Wheel check" box, roll the side wheel.
You should see "Wheel left/right · Rapoo mouse · switching windows".
A red warning about Alt+Tab means the macro is still assigned.


Step 2. Start
-------------
1. Double-click MT760Helper.exe. The settings window opens and a blue icon
   appears in the tray (next to the clock).
2. Tick "Start with Windows" so you don't have to start it by hand.
3. You can close the settings window: the program keeps running in the tray.
   Click the tray icon to open the settings again; right-click it to disable
   the helper or exit.


A HUB doesn't need to stay open
-------------------------------
A HUB writes DPI and button settings into the mouse itself. After setting the
mouse up you can close A HUB completely: right-click its tray icon → exit.
The X button in its window only minimizes A HUB to the tray, where it keeps
running.


Settings
--------
• Keep the window list open — how many seconds the list waits after your last
  action.
• Wheel notches per window — set 2 if the selection jumps over windows.
• Reverse side wheel direction — if the wheel scrolls the wrong way.
• Keep the list open while I move the mouse — handy if you want to click a window.
• Rapoo mouse only — untick to use another mouse's tilt/side wheel the same way.
• Mouse buttons while the window list is open — what the left button (outside
  windows), the right button and the wheel click do.

Button test: tests\Запустить тест кнопок.cmd — moves the cursor and clicks by
itself for about a minute and writes tests\result.txt. Don't touch the mouse
meanwhile.

Settings are stored in %APPDATA%\MT760Helper\settings.ini; errors (if any) go to
%APPDATA%\MT760Helper\error.log.


Troubleshooting
---------------
• Nothing happens. Look at "Wheel check" in the settings:
  – empty — the wheel sends nothing: in A HUB it is set to "Disable key" or a
    macro (see step 1);
  – "other device" — the mouse is not recognized as Rapoo (untick
    "Rapoo mouse only").
• It doesn't switch away from a window running as administrator (Task Manager,
  admin console). Windows doesn't let a normal program control such windows.
  You can run MT760Helper.exe as administrator.
• Alt is "stuck" (letters open menus). Press and release Alt on the keyboard.
  The helper releases Alt itself, but can't if its process is killed.


Building from source
--------------------
Sources are in the src folder. Run build.cmd: it uses the C# compiler that
ships with Windows (.NET Framework 4), nothing to download.
