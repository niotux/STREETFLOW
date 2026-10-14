# Time dependent probabilistic journey model

Status: mathematical design for the proposed traffic model. Parameter values, project-file representation, and implementation are separate later work.

The proposed [version 2 project format](project-format-v2.md) now defines a weight-file representation for review, with schemas and a synthetic example. Contexts are sampled from numerical parameter tensors; the revised format has no named journey catalog. Numerical calibration and runtime implementation remain separate work.

The model describes how traffic enters a road network, which journeys those vehicles intend to make, and how complete routes are sampled from contextual movement probabilities. Demand and movement preferences vary over the day. A vehicle's origin, destination, and journey category give its route a consistent purpose.

This design follows the [traffic route model handoff](traffic-route-model-handoff.md) and the clarified requirement to encode plausible journeys through probabilities. Its scope is independent of the movement simulator. Traffic-light management is a separate mathematical model.

## Network and journey context

Let G be a directed road network. A road is directed, and a movement connects an incoming road to a legal outgoing road. The network supplies entry and exit locations, vehicle permissions, and connectivity constraints.

Let:

| Symbol | Meaning |
| --- | --- |
| o | Journey origin at an entry state |
| d | Intended destination or exit group |
| z | Journey category, such as city-bound, home-bound, local, or through traffic |
| v | Vehicle class, where classes have different permissions or behavior |
| s | Current routing state, including the incoming road |
| a | A possible outgoing movement |
| t | Absolute modeled time |
| theta(t) | Time of day |

Define a journey context k = (o, d, z, v). The context remains fixed while its complete route is generated. The incoming road changes as the route progresses and identifies the direction from which the vehicle approaches each junction.

An approach containing several entries can be represented by several origins and aggregated for analysis. Grouping those origins does not itself determine how demand is divided between their entries. A destination group can contain several acceptable exits, all representing completion of that intended journey.

Journey categories distinguish behavior that an origin and destination alone may not explain. They are optional: if origin and destination already express all relevant intent, the model can use a single category.

The incoming road and persistent journey context summarize the information needed by the initial model. A visited-junction list is not part of this design. Its purpose is to represent coherent journey behavior, rather than enforce a prohibition on revisiting a place.

The routing state must preserve complete movement feasibility. If road adjacency does not determine which later turns are reachable from the entry lane, include the required lane-feasibility information in s. Equivalent lane realizations must not accidentally multiply the probability of a road route.

## Time dependence within one day

Let T_day denote the duration of one day in the chosen time unit. Define

~~~text
theta(t) = t modulo T_day
~~~

All demand and behavioral functions below depend on theta. A repeating daily model has periodic functions, so the same time of day selects the same parameters on each modeled day. These functions describe an authored daily pattern, not measured day-to-day variation.

Time dependence applies independently to:

- The arrival rate at each origin.
- The distribution of destinations and journey categories at that origin.
- The movement preferences for a given journey context and incoming road.

Changing only the total arrival rate would miss changes in journey direction. Changing only turn preferences would miss changes in traffic volume.

A direct mathematical representation uses a partition of the day:

~~~text
0 = b_0 < b_1 < ... < b_m = T_day

I_j = [b_j, b_(j+1))
~~~

Within each interval, rates, journey distributions, and movement scores may be constant. The partition covers the entire day, with no gaps or overlapping intervals. Half-open intervals determine which profile applies exactly at a boundary; midnight returns to the beginning of the next daily cycle.

Piecewise constant profiles are a sufficient initial representation. Continuous functions or interpolation can express gradual changes later, provided rates remain nonnegative and probability distributions remain normalized. Interpolating finite movement scores and then applying softmax preserves positive normalized movement probabilities.

The interval boundaries and the values of every profile are inputs to be chosen later. This design assigns neither clock windows nor numerical traffic strengths.

### Which time applies to a planned route

For a journey with planned departure time t_0, use theta(t_0) to select its journey distribution and movement preferences. Freeze that time context while sampling the complete route.

This is the initial temporal assumption: a route expresses the preferences of the departing journey. New journeys use updated profiles as the clock advances. Vehicles already assigned complete routes keep those routes when a daily profile boundary passes.

Using estimated arrival time at each junction would be a different, more detailed model. It would require a travel-time forecast and time-dependent routing states. That refinement is not required to represent daily changes between generated journeys.

## Journey demand and map population

Let lambda_o(theta) be the arrival intensity at origin o, in vehicles per unit time. Let pi(d, z, v | o, theta) be the joint probability of destination, journey category, and vehicle class for an arrival at that origin.

Require

~~~text
lambda_o(theta) >= 0

pi(d, z, v | o, theta) >= 0

sum_(d,z,v) pi(d, z, v | o, theta) = 1
~~~

The demand for a journey context is

~~~text
Q_(o,d,z,v)(theta) = lambda_o(theta) * pi(d, z, v | o, theta)

Lambda(theta) = sum_o lambda_o(theta)
~~~

Q separates how much traffic is requested from the routes that this traffic prefers. A road's preference score is therefore not also used to set the total number of arrivals.

For example, the qualitative morning observation can be represented by greater probability for city-bound journeys entering from the southern approaches. The evening observation can increase home-bound journeys toward the southern approaches, including arrivals from Mangeron. The Nicolina–Strada Palat corridor can receive stronger movement preference when it serves those journeys. These are relationships to express through the functions, not assigned percentages.

A minimal stochastic arrival model is a nonhomogeneous Poisson process with intensity Lambda(theta(t)). Its expected number of departures in a time interval is

~~~text
E[N(t_1, t_2)] = integral from t_1 to t_2 of Lambda(theta(u)) du
~~~

Generate the next departure by drawing an exponential hazard E with unit rate and solving

~~~text
integral from t_0 to t_next of Lambda(theta(u)) du = E
~~~

For piecewise constant profiles, integrate across each daily boundary until the accumulated hazard reaches E. A zero-rate interval contributes no hazard. The calculation depends on modeled time, not how frequently the generator is polled.

At a sampled departure, select the origin with probability lambda_o / Lambda, then sample (d, z, v) from pi. If Lambda is zero, no departure occurs.

Poisson arrivals are an explicit modeling assumption, not a conclusion about the available traffic data. The demand functions remain useful if a later model introduces correlated arrivals or platoons.

In this model, map population means the distribution of traffic demand and its intended journeys through the map. Exact road occupancy also depends on how vehicles move, wait, and leave.

## Contextual movement scores

For every legal movement a from s, define a finite real score

~~~text
w(a | s, o, d, z, v, theta)
~~~

A score expresses relative preference for that movement in that journey context. Define the base movement probability with softmax:

~~~text
q(a | s, k, theta)
  = exp(w(a | s, k, theta))
    / sum_(legal movements c from s) exp(w(c | s, k, theta))
~~~

Consequently, the legal movement probabilities are positive and sum to one. Adding the same constant to all scores in a row leaves its probabilities unchanged, so only score differences matter.

This is the same score-to-probability operation used in many neural-network outputs. Scores can be authored without training. A later estimator or neural model could supply the scores while preserving the same probability definition; no neural architecture is required by the mathematical design.

If a desired positive probability row is p_a, scores proportional to log(p_a) reproduce it:

~~~text
w_a = log(p_a) + C
~~~

The constant C can be any common row offset. This permits direct probability authoring and score-based authoring to represent the same local preferences.

Finite low scores express unlikely legal choices. Legal reversals and circular routes are not excluded simply because they are undesirable. Network illegality and vehicle incompatibility define feasibility separately.

Origin, destination, category, and time are what make the scores meaningful. Two vehicles at the same junction can have different movement distributions because they are making different journeys. The intended dependency is on journey context, not merely on whether a vehicle has already visited the junction.

## Distribution over complete routes

Local probabilities must be connected into a distribution of complete journeys. Selecting a destination and then making unconditioned local turns could still send a vehicle to another exit.

Fix k and the departure-time context theta_0. Let R_(o,d,v) be all finite legal routes from the chosen entry to the intended destination, satisfying vehicle and lane constraints. A route ends on reaching that destination. Crossing a different external exit ends a different journey and is not a valid completion of this selected context.

The set includes complete routes containing repeated roads or junctions. There is no rule that removes a route solely because it contains a loop.

For route r = (s_0, a_0, s_1, ..., a_(L-1), s_L), define

~~~text
W(r | k, theta_0) = product_(i=0 to L-1) q(a_i | s_i, k, theta_0)

Z(k, theta_0) = sum_(r in R_(o,d,v)) W(r | k, theta_0)

P(r | k, theta_0) = W(r | k, theta_0) / Z(k, theta_0)
~~~

The product captures how the choices fit together under the same journey context. Normalization conditions the distribution on completing the selected journey.

Because q is a transition probability, Z is the probability that the base process reaches the intended destination. It is at most one, even when infinitely many finite cyclic routes exist. A requested context with Z = 0 is infeasible and must be reported, rather than replaced with an unrelated destination.

Every finite feasible complete route with positive local probabilities has positive probability in the conditioned model. A rare circular journey remains possible. Whether it is sufficiently unlikely depends on the scores and the complete-route distribution; parameter selection will be separate work.

### Computing destination conditioning

For a fixed context and departure-time profile, let H(s) be the probability that the base movement process starting at s reaches the selected destination.

Boundary conditions are

~~~text
H(s) = 1 at a selected destination exit
H(s) = 0 at another external exit or a state unable to reach the destination
~~~

For other states,

~~~text
H(s) = sum_(legal movements a) q(a | s, k, theta_0) * H(next(s,a))
~~~

On transient states that can reach the destination, collect base transitions in M and direct destination probabilities in b:

~~~text
H = M H + b
(I - M) H = b
~~~

Keep probabilities of transitions to incompatible exits or unreachable states in the original q normalization. Their successful continuation is zero; do not locally renormalize the base process before solving for H.

For a finite routing-state graph, every retained state has a positive-probability path to the destination. The resulting substochastic transient matrix has spectral radius below one, so the system has a unique solution. Closed components unable to reach the destination have H = 0 and are excluded from this solve.

The actual route-sampling probability is

~~~text
p(a | s, k, theta_0)
  = q(a | s, k, theta_0) * H(next(s,a)) / H(s)
~~~

Use this only at states with H(s) > 0. The probabilities sum to one, and their product along a completed route equals W(r) / Z. Therefore sequential sampling with p generates the defined complete-route distribution without enumerating every route.

The same expression can be written as a softmax over adjusted scores:

~~~text
p(a | s, k, theta_0)
  = softmax_a(w(a | s, k, theta_0) + log H(next(s,a)))
~~~

A zero continuation probability represents inability to complete the selected journey. It is not a loop-avoidance rule.

Authored q rows are base preferences. The final p rows also account for destination completion, so an authored local probability should not be mistaken for a final observed turning percentage. This distinction matters when later populating or fitting the parameters.

## Calculating expected use of the map

Let n_e(r) count how many times route r traverses road e. For journeys departing at time t, the expected demand assigned to road e is

~~~text
D_e(t) = sum_k Q_k(theta(t)) * E[n_e(R) | k, theta(t)]
~~~

The expectation counts repeated traversals. Define an analogous count for each turning movement to calculate expected movement demand.

For a fixed context, let A be the conditioned transient-state matrix derived from p. If u injects journey demand into entry states, expected state visits satisfy

~~~text
y = u + A^T y
(I - A^T) y = u
~~~

Expected road and movement use follow by assigning traversal counts to these visits and transitions. Sum contributions across journey contexts.

These quantities describe road use assigned to departures at t. Physical crossings occur later and may be delayed by queues. They are not instantaneous density measurements.

If a separate movement model supplies the probability K_e(t, tau, k) that a vehicle departing at tau occupies road e at t, expected road population is

~~~text
E[N_e(t)] = sum_k integral over tau <= t
           Q_k(theta(tau)) * K_e(t, tau, k) d tau
~~~

This separates journey demand and route choice from motion and congestion. The route model supplies which journeys use which roads; travel and waiting determine how many vehicles occupy them at a particular time.

## Mathematical objects passed to later work

The proposed model can be summarized as

~~~text
Model = (G, lambda, pi, w)
~~~

G defines feasible movement. The daily functions lambda define arrival volumes, pi defines journey mixtures, and w defines contextual movement preferences. The destination-conditioning equations turn those preferences into complete-route probabilities.

A project-file representation must later preserve these meanings, units, daily time dependence, and context associations. This document does not select field names, a serialization format, interval values, or numerical parameters.

Existing road weights and partial observations may inform later parameter estimation. They do not uniquely identify missing journey distributions. Measured evidence, derived estimates, qualitative observations, and authored assumptions must remain distinguishable when the model is populated.

The route-generation sequence is to sample a departure and its journey context, compute or retrieve the appropriate destination-conditioned probabilities, sample a complete route, and then assign that route to the vehicle before it spawns. Daily profile changes affect subsequent journeys. Previously assigned routes retain their planned context.

The next mathematical task is traffic-light management. That work can use the expected movement demand produced here, together with its own service, queue, and control assumptions.
