# Autoregressive Route Generation: Design Notes

## Goal

Generate each car's route one move at a time, where every move is probabilistic and depends on the route so far. The car starts at an entry (A) with no destination chosen. The route emerges from repeated sampling until the model samples "exit".

This replaces the current `contextual_journey_v1` flow (pick destination first, then derive the route) with a step-by-step walk. It is the same loop an LLM uses to produce words.

## The generation loop

```
route = [entry_link]
while True:
    options = legal_next_moves(route.last)        # from the map (the mask)
    if can_exit_here(route.last): options += [EXIT]
    scores  = score(options, route, time_of_day)   # the model
    probs   = softmax(scores)
    choice  = sample(probs)
    if choice == EXIT or len(route) >= MAX_LEN: break
    route.append(choice)
return route                                       # handed to SUMO at insertion
```

The route is computed fully at spawn time and given to SUMO before the car is inserted. The walk is simulated offline, so SUMO's side of the architecture does not change.

## Four separate layers

Keep these apart so any one can be swapped without touching the others.

### 1. Map (static facts, no probabilities)

- **Nodes** and **directed links** (from, to).
- **Legal movements**: for each link, which links can follow. Illegal moves never receive a score.
- **Entry points**: where cars spawn.
- **Exit points**: where a trip is allowed to end (this is where EXIT may appear as an option).

### 2. Features (what the scorer can "see")

Per movement:
- turn type: straight / left / right / U-turn
- turn angle
- `reverses_previous_link`: true if it goes back along the link you just came from

Per link:
- length, speed limit, road class (main vs. side street), bearing
- zone or region it leads toward (optional)

Per node:
- number of options, node type (junction, dead end, boundary)

### 3. Model (the tunable part)

Score for each candidate move, computed from features and history:

```
score(move) = w_turn[turn_type]
            + w_reverse * reverses_previous_link
            + w_class[road_class]
            + w_context · context_features(route)
            + time_bias[period][move_feature]
```

Context features of the route so far (examples):
- number of left turns in the last N moves
- previous turn type (turning twice in a row)
- trip length so far
- has this link or node already been visited (loop signal)
- distance to the nearest exit

Exit score:
```
score(EXIT) = w_exit_base + w_exit_len * trip_length + w_exit_boundary * at_boundary
```

Example dials (values are starting guesses to tune, not measured):
- U-turn: -6 (strongly unlikely, not forbidden)
- straight: +1
- revisiting a node: -3
- exit weight grows with trip length so trips end

### 4. Demand (independent of routing)

- Arrival rates per entry per time period (already in `arrival_rates`).
- Vehicle types and categories.

## Guarantees vs. tendencies

| Behavior | How it is achieved | Certain? |
| --- | --- | --- |
| Only legal turns | Map mask removes illegal moves | Yes |
| Routes end | `MAX_LEN` cap | Yes |
| No U-turns | Mask them out, or use a large negative weight | Mask: yes. Weight: very unlikely |
| Few loops | Negative weight on revisits + rising exit score | Unlikely, not impossible |
| Realistic turn patterns | Tuned weights | Only as good as the tuning |

If you need a hard no-loop rule, mask revisited nodes. If you want loops to be rare but possible, use the negative weight.

## Time dependence

Two places, both optional:
- Arrival rates per period (demand).
- Score multipliers or biases per period (for example, stronger preference for inbound roads in the morning).

## A note on traffic waves

This model controls **what routes cars take and when they appear**. It does not simulate traffic dynamics. Stop-and-go waves come from SUMO (car-following, signals, road capacity) once enough cars are on the road. What this model can do:
- Stop cars from circling, which removes a source of artificial congestion.
- Create rush-hour demand surges through period-dependent arrival rates and directional biases, which is what typically triggers congestion in SUMO.

Whether a wave actually forms depends on SUMO settings and the network, and needs to be checked in simulation.

## Suggested file layout

```
map.json        nodes, links, legal movements, entries, exits
features.json   per-link and per-movement features
model.json      feature weights, exit weights, time biases, MAX_LEN
demand.json     arrival rates, vehicle types
```

`model.json` is the only file that changes when you tune behavior. If you later replace the scorer with a trained network, `map.json` and `demand.json` stay the same.

## Implementation checklist

1. Build the map layer and the legal-move mask from the SUMO network.
2. Compute movement and link features (turn type, reverse flag, angle).
3. Implement the loop above with a hand-written `score()`.
4. Add an exit option and `MAX_LEN`.
5. Generate a few thousand routes and check: route length distribution, loop frequency, U-turn frequency, share of trips per entry/exit.
6. Tune weights until those statistics look right.
7. Feed routes to SUMO at insertion and watch for congestion behavior.

## Later: swapping in a trained model

If hand-tuning becomes unwieldy, or real trajectory data is available:
1. Use the hand-tuned loop to generate a large set of routes (or use real data).
2. Train a small GRU or transformer to predict the next move given the route so far.
3. Replace `score()` with the network. The loop, map, and demand layers stay unchanged.
4. Add learned node/link embeddings in place of hand-written features.

## Open points

- This design was written without seeing `prepare.py` or the generator code, so the integration point with the existing generator is not verified.
- Starting weight values are guesses and need tuning against generated statistics.
- On a single four-way junction there are very few real decisions, so a larger network is needed to see the benefit.
