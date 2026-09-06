# Weave

This repository now contains a **first-step Unity architecture scaffold** for the village life-sim concept described in the issue.

The goal of this step is **not** to build the whole game. It is to define a clean foundation for:

- one run = one playable villager
- replaying the same year from different perspectives
- developer canon vs player canon
- decisions separated from consequences
- data-driven tasks, events, locations, and characters
- a small vertical slice built on top of those rules

## Recommended architecture

### 1. Major systems / services

Use a small set of focused systems instead of a giant `GameManager`.

1. **Definition layer**  
   ScriptableObject content for characters, locations, tasks, events, and calendar configuration.

2. **Runtime state layer**  
   Plain runtime data for the active run: calendar progress, character state, location state, world flags, relationships, resources, and travel state.

3. **Simulation services**  
   Stateless or mostly-stateless services that operate on runtime state:
   - task availability
   - travel progression
   - event triggering
   - consequence resolution
   - NPC daily resolution
   - canon resolution

4. **Session / composition root**  
   A thin Unity-facing coordinator that wires definitions, runtime state, and services together for the prototype.

5. **Presentation layer**  
   UI and map visuals that observe runtime state and send player intentions back to the session layer, without owning simulation rules.

6. **Persistence layer**  
   Separate save payloads for:
   - current run state
   - player canon / meta data

### 2. Static definitions vs mutable runtime state

Keep authored content separate from run-specific state.

**Static definitions**
- `CharacterDefinition`
- `LocationDefinition`
- `TaskDefinition`
- `EventDefinition`
- `GameCalendarDefinition`

**Mutable runtime state**
- current day / season / year
- selected playable character
- each character's current location
- each character's resources / relationships
- world flags
- event progress
- travel in progress
- NPC task choices for the current day

**Why**  
This cleanly supports restarting from Day 1, running alternate timelines, and later saving player canon separately from the active run.

### 3. CharacterDefinition vs CharacterState

**CharacterDefinition**
- stable identifier
- display name
- profession
- home location
- starting resources
- developer canon defaults
- visual map color / presentation hints

**CharacterState**
- current location
- inventory / resource totals
- relationship values
- current task
- current travel state
- temporary run-only flags

**Why**  
The definition says who the character is. The state says what is true right now in this run.

### 4. LocationDefinition vs LocationState

**LocationDefinition**
- stable identifier
- display name
- location type
- map position/node position

**LocationState**
- unlocked / blocked flags
- run-time changes specific to that location

For the first prototype, many locations may have little or no mutable state.

**Why**  
Most locations are mostly static at first, but leaving room for `LocationState` prevents future travel/events/upgrades from being hard-coded into scene objects.

### 5. Task representation

Represent tasks as authored definitions with:
- task id
- display name
- required location
- availability conditions
- optional character restrictions
- base resource effects
- optional follow-up event

**Why**  
This keeps `Location != Task`, supports multiple tasks at the same place, and lets professions expose different task sets without rewriting the core loop.

### 6. Required location on a task

A task should directly reference its required `LocationDefinition`.

Example:
- Task: `Mine Iron`
- Required Location: `Eastern Mine`

The simulation can then:
1. validate availability
2. start travel to the required location
3. begin the activity only after arrival

### 7. Travel

Travel should be a simple simulation concept, not a character controller.

For the prototype:
- character has an origin location
- character has a destination location
- character has normalized travel progress
- simulation advances progress
- presentation interpolates between the two map points

**Why**  
This proves the travel pipeline now while leaving room for time cost, injuries, blocked routes, weather, or alternate paths later.

### 8. Visual map observing the simulation

The map should only:
- render location markers
- render character markers
- read their simulated map positions
- show travel movement

It should **not**:
- decide where a character may go
- decide whether a task is legal
- resolve outcomes
- advance time

**Why**  
That separation keeps gameplay rules testable and prevents UI scripts from becoming the real game logic.

### 9. Representing decisions

A decision should be represented as:
- `decision key` (what question is being answered)
- `option id` (which intent the character chooses)

Example:
- decision key: `ALICE_HELP_REQUEST`
- option id: `HELP_ALICE`

### 10. Consequences separate from decisions

Consequences should be evaluated after the decision is known.

Example:
- decision: `HELP_ALICE`
- possible consequences depend on world state:
  - Alice gives axe
  - Alice gives thanks only
  - Alice offers later favour

The stored canon should record the **chosen option**, not the entire resulting world-state mutation.

**Why**  
This is the core rule that lets the same canonical intent produce different results in different runs.

### 11. Events, conditions, and outcomes

Recommended event shape:

- `EventDefinition`
  - event id
  - prompt text
  - actor / decision maker
  - decision key
  - options

- `DecisionOptionDefinition`
  - option id
  - label
  - ordered outcome variants

- `OutcomeVariantDefinition`
  - conditions
  - flag mutations
  - resource changes
  - summary text

For the first slice, simple world-flag conditions are enough.

**Why**  
An ordered list of condition-checked outcome variants is simple, data-driven, and good enough for an early slice without needing a full utility AI or rules engine.

### 12. Developer canon

Store developer canon per character as default mappings:

- character id
- decision key
- default option id

When that character is an NPC and no player canon override exists, use the developer default.

### 13. Player canon

Player canon should be saved separately from the run as:

- character id
- decision key
- canonical option id chosen by the player

At runtime:
1. ask player canon for an override
2. if none exists, fall back to developer canon

**Why**  
This supports alternative village histories without storing full replay files or exact world states.

### 14. NPC daily behaviour without overcomplicated AI

Use a simple resolver for the first slice:

1. gather valid tasks for each NPC
2. apply a short ordered priority list or authored default task list
3. use canon only when an event/decision point occurs
4. resolve the chosen task and resulting event

This is **not** full AI. It is authored simulation with lightweight selection rules.

**Why**  
It is enough to prove the concept while staying compatible with richer priorities later.

### 15. Profession-specific gameplay / minigames

The core simulation should treat profession gameplay as a plug-in module that returns a result payload:

- task completed or failed
- time consumed
- resources gained/lost
- flags changed
- follow-up events triggered

The village simulation should not need to know how mining, farming, or smithing internally works.

**Why**  
This keeps the central loop stable while allowing future profession gameplay to become much richer.

### 16. Day / calendar loop

Recommended loop:

1. start day
2. process scheduled morning events
3. resolve available player tasks
4. player selects task / destination
5. travel
6. resolve task or launch profession module
7. resolve triggered events / dialogue
8. resolve NPC tasks and events
9. finalize world-state changes
10. end day
11. advance calendar

The `GameCalendarDefinition` should define seasons and configurable days-per-season.

### 17. Save separation

Keep these payloads separate:

1. **CurrentRunSaveData**
   - active run state only

2. **PlayerCanonSaveData**
   - saved canonical decisions by character

3. **MetaSaveData**
   - unlocked content, settings, future cross-run data

**Why**  
Starting a new run should reset current run state to Day 1 without wiping player canon.

### 18. ScriptableObjects to use

Recommended ScriptableObjects:

- `CharacterDefinition`
- `LocationDefinition`
- `TaskDefinition`
- `EventDefinition`
- `GameCalendarDefinition`

**Why**  
These are authored, reusable, inspectable content assets that are mostly static.

### 19. What should NOT be ScriptableObjects

Do **not** store changing run state in ScriptableObjects:

- current inventory totals
- relationship values
- day progression
- event completion state
- live travel progress
- current NPC choices

Use runtime data / save DTOs instead.

**Why**  
Changing ScriptableObjects at runtime risks accidental asset mutation and makes multiple runs harder to reason about.

### 20. Suggested Unity folder structure

```text
Assets/
  Scripts/
    Weave/
      Data/
      Runtime/
      Simulation/
      Presentation/
```

This repository includes that starter script layout now.

## Important risks / weaknesses in the current design

1. **Canon granularity**
   - If decision keys are too broad, characters feel robotic.
   - If they are too specific, authoring becomes expensive.

2. **Event authoring load**
   - The concept is strong, but content production could grow quickly.
   - Reuse common event/condition structures where possible.

3. **NPC fairness / visibility**
   - If NPC simulation is opaque, player actions may feel arbitrary.
   - The prototype should surface who did what each day.

4. **Profession module integration**
   - If profession minigames are allowed to mutate world state directly, the architecture will drift.
   - Require them to return structured result payloads.

5. **Scope creep**
   - The idea naturally invites upgrades, routes, seasons, relationships, and town drama.
   - The first slice should only prove the central pipeline.

## Important decisions to make before deeper implementation

1. Will one day always equal one major task, or can a day contain multiple actions later?
2. Should NPC tasks be fully simulated numerically, or sometimes resolved abstractly off-screen?
3. How visible should NPC daily decisions be to the player?
4. Should player canon replace all decisions for a character, or only explicitly saved ones?
5. How should event triggers be authored: calendar-driven, flag-driven, location-driven, or all three?

## Exact scope of the first prototype

Build the smallest playable slice that proves:

- 3 major characters: Farmer, Miner, Lumberjack
- 4 locations: Farm, Mine, Forest, Village Square
- short calendar: 4 seasons x 3 days = 12-day test year
- choose a playable character at run start
- all characters begin at their homes
- player sees available tasks
- each task points to a required location
- choosing a task starts visible travel on the map
- completing the task changes simple resource totals / flags
- NPCs choose a simple default/canonical task each day
- at least one event asks for a decision
- player can choose differently from developer canon
- the same decision can resolve to different outcomes depending on world flags

That is enough to prove:

`WORLD STATE -> TASKS -> DECISION -> TRAVEL -> ACTIVITY -> CONSEQUENCE -> UPDATED WORLD STATE -> NEXT DAY`

## Recommended implementation order

1. calendar definition + runtime calendar state
2. character / location / task definitions
3. run state + character state
4. canon resolver
5. event / decision / consequence definitions
6. task availability + NPC daily resolution
7. travel state + map presentation observer
8. thin prototype session controller
9. basic UI for choosing character, task, and decision
10. save payload split for current run vs player canon

## Starter code included in this repository

The initial code scaffold added in this step provides:

- ScriptableObject definitions for calendar, characters, locations, tasks, and events
- runtime state types for the current run
- canon resolution
- a small day/task/event simulation service
- a thin prototype session entry point
- a map presenter that observes simulation state

This is intentionally a **foundation**, not a full game implementation.