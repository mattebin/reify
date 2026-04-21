# Name: reify

## Why reify

**reify** (verb, /ˈreɪ.ɪ.faɪ/) — to make something abstract concrete; to turn an
idea into a thing you can inspect and manipulate.

That is exactly what this plugin does. Unity's runtime state — which shader
keyword is live on which renderer, which volume override is active at a world
position, which animator transition is eligible right now, what the mesh's
native bounds are before any Transform scale — is *abstract* from an LLM's
perspective. It's hidden inside the engine, visible only as pixels in a
screenshot or as clicks through the Inspector. Reify makes that state
*concrete*: structured JSON an LLM can read, diff, grep, and reason about.

Additional reasons:

- **Programming heritage.** "Reification" is a well-known term in PL theory
  (reified types, reified generics, reifying a continuation). The audience
  for this tool is people who already know the word.
- **No ecosystem collision.** No meaningful Unity asset, MCP server, or .NET
  package currently uses the name. Free to own.
- **Short and memorable.** Five letters. Pronounces unambiguously. Types
  fast on the CLI. Reads as one beat in prose.
- **Captures the philosophy directly.** `PHILOSOPHY.md` is 200 lines of
  elaboration on "stop guessing from screenshots, read the state." The name
  is the one-word version.

## Brand direction

- **Lowercase in code and CLI:** `reify`, `reify-server`, `reify.unity`,
  `com.reify.unity`. Namespaces: `Reify.Editor`, `Reify.Server`,
  `Reify.Shared`. (PascalCase where .NET / Unity conventions demand it;
  lowercase everywhere else.)
- **Title case in prose:** "Reify exposes structured state..." in READMEs,
  docs, marketing (eventually).
- **No stylization.** No all-caps (REIFY), no forced lowercase sigil
  (reify™), no dot-separated wordmark. Just the word.

## Tagline

> **Structured state for Unity, for LLMs that reason.**

Ships on the README headline and the repo description. The tagline is load-
bearing: it declares the audience (LLMs, not humans), the medium (structured
state, not screenshots), and the domain (Unity) in nine words.
