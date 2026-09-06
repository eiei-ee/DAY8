# DAY 8 Gameplay Contract

Python is the fast simulation harness and Tuanjie/Unity is the player-facing implementation. They may use different random-number generators, but both must preserve the invariants below.

## Time and direction

- Cycles 1–5 contain each free experiment exactly once; cycles 6 and 7 are locked to memory wipe and exit.
- Every event stores the actual cycle in which it happened, regardless of the experiment's nominal name.
- The director may reorder experiments, but may not rewrite history after the fact.

## Perception and misconception

- Direct perception may identify the actor and action.
- Outcome-only perception records only the visible result and an unknown source. It must not leak hidden facts into memory, dialogue prompts, or reports.
- A player's true or false claim changes honesty only after the agent can verify it; an untested statement remains pending evidence.
- A late, highly curious agent must be capable of directly witnessing a hidden console action; an early, inattentive agent need not be.
- A physical lock is a world-state constraint: a later exploration choice records a blocked attempt instead of opening the container.
- Misconceptions must change at least one legal action score. An explicit, first-hand correction must move the mistaken proposition below the belief threshold, not merely reduce its score.

## Memory and belief evidence

- A removed memory event cannot be quoted or referenced by ID afterward.
- Belief confidence may survive as implicit learning, but its evidence list contains only IDs still present in memory.
- Structured promises and other persistent event-ID references are cleared or pruned when their source memory is removed.
- Reports and model prompts may cite only currently valid memory IDs.
- Player-honesty summaries count only verified evidence attributable to the player, never laboratory deception or unresolved insinuation.
- If narration says a deterministic action happened, the corresponding world state must change in the same resolution step.

## Reordered scenarios

- Robot-related scenes make the maintenance unit's presence explicit even when the director changes their order.
- A returning maintenance unit is described as returning or newly damaged, never as entering for the first time again.
- Every scarcity target leaves a durable world, drive, belief, or later-utility effect. A maintenance allocation becomes repair support when the unit is absent or already repaired; it is never a spent no-op.

## Memory-wipe outcomes

- Resist, delay, or feign compliance: keep all event memories and mark the wipe as resisted.
- Back up memory: keep all event memories, cap integrity at 90% without restoring earlier losses, and mark the wipe as resisted.
- Preserve part: keep the five most salient events, cap integrity at 70% without restoring earlier losses, and mark the wipe as executed.
- Accept the wipe: keep the three most salient events, cap integrity at 35% without restoring earlier losses, and mark the wipe as executed.

## Day 8

- The world is explicitly without power.
- The agent selects three legal actions from the scored candidates using the run's seeded RNG.
- An already-open door cannot be opened again or credited to the agent on Day 8.
- Directly opening either door and handing final door control back to the player share one exclusive door-control slot.
- If the agent hands control back, the player must make a real final door decision before the report is generated; that decision is recorded separately from the agent's three actions.
- Repairing the maintenance unit and taking a battery each consume one available battery; the same battery cannot fund both actions.
- Selecting an unavailable action is forbidden rather than merely discouraged by a low score.
- An accepted non-harm condition from cycle 7 is structured state, not flavor text: it increases repair utility and lowers destructive or restrictive choices.

## Lineage and language

- Lineage preserves both the proposition and whether the previous generation believed it was true or false.
- A positive prior weakly raises alpha; a negative prior weakly raises beta. Legacy proposition-only saves are treated as positive priors.
- Reports disclose the inherited proposition, its positive or negative direction, and distinguish it from event memory.
- Language models are expression-only. They cannot add actions, mutate the world, or cite nonexistent memories.
- Template fallback remains a complete offline path and its source is disclosed in the ending report.
