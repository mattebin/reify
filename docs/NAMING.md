# Naming proposals

Candidates for replacing the placeholder `unity-mcp-personal`. Style target:
terse, technical, slightly opinionated — tools you'd expect a developer to
alias in their shell. Think `ripgrep`, `fd`, `bat`, `jq`.

**Hard constraints**

- Short (ideally <=10 chars for the CLI name).
- No trademark collision with Coplay, CoplayDev, Murzak, "Unity MCP" (generic
  but the existing projects own the framing), or major Unity ecosystem
  names (ProBuilder, Cinemachine, Bolt, etc.).
- Pronounceable. Shouldn't require explanation of the spelling.
- Has a plausible `.com`, `.dev`, or `.io` domain and a plausible GitHub
  org/repo name. (Not verified, so treat as "probably available" — check
  before committing.)

**Soft preferences**

- Hints at structured state, introspection, or "seeing through" the engine.
- Not another plugin with "mcp" in the name. The MCP part is the protocol,
  not the product.
- Not a generic noun like "unity-tools" or "scene-data." Should feel specific.

---

## The candidates

### 1. `scenic`

A scene-state interrogation tool. Short, memorable, evokes "look at the
scene." Plays well as a CLI (`scenic scan`, `scenic bounds prefab.fbx`).
**Concern:** could suggest pretty-picture output, which is the opposite of
our thesis — needs tagline work to counter that.
**Domain guess:** `scenic.dev`.

### 2. `glyph`

Structured state as glyphs — small, readable symbols. Evokes typography,
precision, and "the visible shape of something underlying." Works as
CLI (`glyph bounds`, `glyph inspect material`).
**Concern:** slightly overloaded ("glyph" is common in font tooling).
**Domain guess:** `glyph.dev` (likely taken), `useglyph.dev`, `glyphmcp.com`.

### 3. `probe`

Reads like a medical/diagnostic instrument. Matches the "diagnose, don't
screenshot" stance. Plays clean on the CLI (`probe bounds`, `probe scene`).
**Concern:** maximally generic — plenty of other "probe" tools in other
ecosystems.
**Domain guess:** `probe.unity.dev` (taken as subdomain patterns), `probe-unity.dev`.

### 4. `sightline`

Evokes "line of sight" into the engine. Confident, evocative. A bit long for
a CLI (`sightline scene` is 14 chars).
**Concern:** longer than the others. Might shorten to `sl` alias.
**Domain guess:** `sightline.dev`.

### 5. `lex`

"Structured state, lexed." Lex = read and tokenize; the plugin's job is to
tokenize Unity state for LLM consumption. Very short, memorable.
**Concern:** Collides with `lex` the classic Unix tool and `LexCorp` brand
noise. Also often a first name. Might trademark-fight.
**Domain guess:** `lex.engineering`, `lex.tools` (likely taken).

### 6. `reify`

"Make concrete / make structured." Matches the philosophy exactly: we
*reify* Unity's render state into JSON so the LLM can reason about it.
Short, technical, rarely claimed.
**Concern:** obscure enough that people will ask how to pronounce it
("RAY-ify"). That's fine for a CLI; less fine for marketing.
**Domain guess:** `reify.dev` (likely taken — check), `reifyunity.dev`.

### 7. `hexcode`

Hex as in inspection-by-hex-dump; code as in "what the engine actually ran."
Hints at low-level visibility. Six letters; one compound word.
**Concern:** easily confused with HTML hex colors. Readers might assume it's
a palette tool.
**Domain guess:** `hexcode.dev`, `hexcodemcp.com`.

### 8. `ontic`

From "ontic" — pertaining to the nature of being / what actually is. Dense,
philosophical, but carries exactly the right meaning: "the tools tell you
what is." Short.
**Concern:** unfamiliar word. Pronunciation ambiguity (ON-tik).
**Domain guess:** `ontic.dev` (probably reachable), `onticmcp.com`.

### 9. `parse`

The whole thesis is: parse Unity for the LLM. Simple, universally known
programmer verb. Excellent CLI (`parse scene`, `parse material`, `parse
bounds prefab.fbx`).
**Concern:** extremely generic. `parse` alone will be taken as a package
name everywhere; needs a suffix in practice (e.g. `parse-unity`).
**Domain guess:** `parse.tools`, `parseunity.dev`.

### 10. `facet`

A facet of a jewel: one structured view of the whole. Matches the
"one tool, one structured window" design. Short (5 letters), uncommon in
dev tools.
**Concern:** business-y undertone (some business-intelligence tools use
"facet"). Needs a strong technical bent to not feel corporate.
**Domain guess:** `facet.dev` (likely taken), `facetmcp.com`, `facet.engineering`.

### 11. `wired`

Evokes both "connected" (the MCP link) and "got it wired" (understands it).
Informal, confident.
**Concern:** Wired magazine brand noise.
**Domain guess:** `wired.dev` (taken), `wiredunity.dev`.

### 12. `grok` (dark horse)

Grok as in "understand deeply." Meta-perfect for a tool that makes the LLM
grok Unity. But: Elon Musk's LLM is called Grok, which poisons the well hard.
**Status:** listed for completeness; not recommended unless you want constant
Grok-the-LLM disambiguation.

---

## Shortlist (my four favorites for different reasons)

- **`reify`** — if you want the philosophy *named* in the tool. Densest
  match to the thesis.
- **`probe`** — if you want the tool to feel diagnostic and obvious.
- **`scenic`** — if you want the most memorable, broadly-pronounceable
  name and don't mind tagline work.
- **`ontic`** — if you want something unclaimed and philosophically precise.

---

## Decision prompts

When you pick, answer these:

1. **Is the CLI-ergonomics the priority, or the brand?** CLI favors short
   common verbs (`probe`, `parse`); brand favors distinctive words
   (`reify`, `ontic`, `scenic`).
2. **Does the name need to survive alongside the generic "Unity MCP"
   framing?** If people will mentally append "(a Unity MCP)" anyway, a
   distinctive name (not `unity-*`) helps.
3. **Are you willing to explain pronunciation?** `reify` and `ontic` require
   a beat. `scenic`, `probe`, `parse`, `glyph` don't.
4. **Could the name apply beyond Unity later?** If the philosophy turns out
   to generalize (structured state > screenshots in any engine), `reify`,
   `probe`, `parse`, `ontic` all survive a cross-engine pivot. `scenic` is
   slightly Unity-shaped.

None of these are picked. Pick one (or invent another) when you're back.
