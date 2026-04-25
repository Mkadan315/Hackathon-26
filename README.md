# Campus Dash Godot

A one-input Godot C# endless runner prototype for your hackathon.

## Controls

- Left/right arrow keys: move between lanes
- Left mouse click or tap: move one lane to the right
- `P`: pause or resume
- `R`: restart after game over
- `Esc`: quit

## How to open

1. Install **Godot .NET**, not the regular Godot build.
2. Install the **.NET 8 SDK** from Microsoft.
3. Open Godot.
4. Click **Import**.
5. Select `CampusDashGodot/project.godot`.
6. Open the project.
7. Let Godot finish building C# scripts.
8. Press the play button.

If you see only a gray screen, open the bottom **Output** or **Debugger** panel. It usually means the C# script did not compile or the project was opened in regular Godot instead of Godot .NET.

The game creates the student, third-person camera, lanes, obstacles, buffs, debuffs, and scoreboard from one C# script.

## Visual style

The prototype uses simple low-poly 3D pieces built in code: a student with a backpack, a campus hallway, classroom doors, lockers, realistic desks, teaching assistants, professors, and 3D sprite icons for pickups.

## Third-party assets

- `ThirdParty/Kenney/AnimatedCharacters3`: Kenney Animated Characters 3, CC0. Used as an optional player model if Godot imports the FBX successfully. Keep `License.txt` with the asset files.
- `ThirdParty/Quaternius/HoodieCharacter`: Quaternius Hoodie Character from Poly Pizza, CC0. Kept as an experimental/reference model, but not loaded by default.

## Game concept

You are a student running through campus. Dodge desks, teaching assistants, and professors. Collect energy drinks and snacks. Avoid homework and projects. The game saves best score and best survival time locally.

## Buffs and debuffs

- Energy drink: gives a score boost and a 4-second shield.
- Snack: gives points and a short 2-second shield.
- Homework: subtracts points and slows score gain for 4 seconds.
- Project: subtracts more points and slows score gain for 6 seconds.

## Easy hackathon upgrades

- Replace colored shapes with simple 3D models.
- Add sound effects.
- Add a title screen.
- Add a local top-five leaderboard.
- Add campus background props like lockers, signs, and classroom doors.
