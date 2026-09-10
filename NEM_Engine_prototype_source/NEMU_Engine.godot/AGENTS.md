# AGENTS.md

This workspace contains multiple experimental game and simulation projects, with the primary active project being the Godot-based NEMU engine under `NEMU/NEMU_Engine.godot`.

## Project context
- Primary engine: Godot 4.7 project named `NEMU Engine`
- Main project folder: `NEMU/NEMU_Engine.godot`
- Primary language: C# for gameplay/simulation logic, with Godot scenes and shader resources
- Secondary research folders: `Neverending Galaxy/`, `GodotTesting/`, `Example Projects/`, and `SPGL/`

## Operating rules for agents
1. Prefer surgical, minimal changes that align with the existing architecture.
2. Keep modifications compatible with Godot 4.7 and .NET 6 settings already defined in the project.
3. Respect the existing scene and script naming conventions in `res/`.
4. When working in C#, favor readable, deterministic logic over clever abstractions.
5. Do not introduce extraneous dependencies or broad refactors unless explicitly requested.
6. If a task requires a broader design change, explain the tradeoff before changing multiple systems.

## Suggested workflow
- Start by locating the relevant scene, node, or script before editing.
- Prefer reading the exact files tied to the bug or feature.
- Use small, targeted edits and validate with the smallest relevant command or Godot check available.
- Keep editor/project files consistent with the current Godot configuration.

## Relevant directories
- `NEMU/NEMU_Engine.godot/project.godot` — project entry configuration
- `NEMU/NEMU_Engine.godot/res/` — core scripts, shader files, and scenes
- `NEMU/NEMU_Engine.godot/res/scenes/` — scene files (including `Main.tscn`)

## Validation
- Prefer the smallest check that verifies the modified behavior.
- For C# scripts, use `dotnet build` if a project/solution is available for the relevant code.
- For Godot-specific work, validate scene/script integrity and ensure no obvious editor errors introduced.

## Scope discipline
- Do not rewrite unrelated research notes or archived files under `Neverending Galaxy/` unless the task explicitly requires it.
- Treat legacy and experimental files as reference material, not as the primary target for routine changes.

## Expectations for future agents
- Keep task planning explicit and concise.
- Summarize what changed, what was validated, and any known limitations before concluding.
- If a requirement is ambiguous, ask for clarification rather than making assumptions.
