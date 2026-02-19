# How To Install Skills

This repository stores reusable skills in two places:

- OpenCode-discoverable path: `.opencode/skills/<skill-name>/SKILL.md`
- Documentation/reference path: `docs/skills/<skill-name>/skill.md`

Example included:
- `.opencode/skills/new-feature-skeleton/SKILL.md`
- `docs/skills/new-feature-skeleton/skill.md`

---

## OpenCode

Recommended approach:
- Keep skills versioned in this repo under `.opencode/skills/...`.
- OpenCode auto-discovers `SKILL.md` files in that folder.
- Keep `docs/skills/...` as human-readable reference if needed.

If you want global reuse outside this repo, copy the same file to:
- `~/.config/opencode/skills/<name>/SKILL.md`

Portable option:
- copy `.opencode/skills/new-feature-skeleton/SKILL.md` into another project's `.opencode/skills/new-feature-skeleton/SKILL.md`

---

## Claude Code

Recommended approach:
- Use the same OpenCode skill content as reference.
- In Claude Code, point to `docs/skills/new-feature-skeleton/skill.md` (or to the OpenCode file) and request that it is followed as the task recipe.

Example:
- "Use `docs/skills/new-feature-skeleton/skill.md` as the generation policy for this feature."

If you maintain persistent project instructions, include a short section that references the skill path.

---

## GitHub Copilot (Instructions + CLI)

### Repository instructions

1. Create or update `.github/copilot-instructions.md`.
2. Add a section that references this skill file:

```md
## Skill References

For new use cases, follow:
- docs/skills/new-feature-skeleton/skill.md
```

3. Commit both files so teammates and CI environments get the same behavior.

### Copilot CLI usage

When prompting in CLI, explicitly reference the skill path in the prompt text.

Example prompt:
- "Create Clients/CreateClient using `docs/skills/new-feature-skeleton/skill.md` and validate with build + test."

---

## Team Recommendation

- Treat skills as versioned engineering standards.
- Keep one skill per folder (`docs/skills/<name>/skill.md`).
- Update skills in the same PR where conventions change.
