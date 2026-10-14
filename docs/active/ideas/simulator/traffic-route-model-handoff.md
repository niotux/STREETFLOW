# Traffic route model handoff

This document briefs a fresh instance on the next traffic-model discussion. It records the context, the user's intent, and the scope. It deliberately leaves the mathematics, algorithms, and data format undecided. No new model has been implemented by this handoff.

The subsequent [mathematical design](traffic-route-model-proposal.md) records the clarified idea: daily journey demand and contextual movement probabilities describe coherent complete journeys. Project-file format and numerical parameter selection are separate later tasks; visited-junction memory is not the model's premise.

## What we are building

STREETFLOW has a SUMO-based traffic simulator. A project contains a road network and traffic-generation inputs. The application generates vehicles and their routes; SUMO handles their movement, lane changes, junction interactions, and departures from the simulated area.

Podu Roș in Iași is the current working intersection. The goal is traffic whose volume, direction, and journeys make sense at different times of day. The user wants vehicles to have coherent journeys, rather than merely populate roads that have large weights.

## What we have been doing so far

The current generator uses time-dependent weights assigned to roads. Those weights influence the overall arrival rate and the selection of complete legal routes. Routes are calculated before vehicles spawn. Generation is seeded so a run can be reproduced.

These road weights do not explicitly describe why a vehicle entered the map, which destination it is heading toward, or how earlier route choices should influence later choices. They are the starting point that the proposed model should replace or enhance.

The available evidence is incomplete. The original Podu Roș inputs use selected trajectories from a limited evening camera window, with estimates for roads outside that coverage. They do not provide measured full-day traffic for every road, morning directional counts, or a complete record of turning choices. An aggregate hourly traffic shape from an Iași report also informs the prepared inputs, but it does not supply those missing local details. Existing weights should not be presented as measured road density.

A separate `podu-ros-commute` scenario adds the user's qualitative observation: morning traffic generally travels from the south/Nicolina toward the city and Palas; evening traffic generally travels back toward the southern approaches. Its time windows and directional strengths are documented estimates. This changes the generated direction of traffic, but still expresses commute behavior through road weights rather than explicit journey intent.

We also investigated vehicles getting stuck near Nicolina. Generated routes were taking a side-street detour, making a U-turn, and returning to Nicolina. These routes contributed to blockage. The current generator excludes immediate U-turn connections from its route searches. That fixes a documented routing problem; it does not establish that all remaining congestion is realistic or that demand is calibrated.

Traffic lights are visible on the map. New city runs start with lights off and use priority rules until an external API command changes them. The user requested API control for a future controlling server, and explicitly rejected a traffic-light control panel in the map UI. Signal control and other server behavior belong to separate work.

## The user's general idea

Road density or road preference alone seems insufficient. A vehicle's choices should fit together as a journey, and a later choice should be influenced by the choices already made in that journey.

The Nicolina–Strada Palat corridor is the main route into and out of the city in the user's description. In the morning, journeys should reflect travel toward the city. Later in the day, journeys should reflect people returning home. For example, a vehicle entering from Bulevardul Profesor Dimitrie Mangeron during the homeward period should commonly head toward the southern boulevard/Nicolina corridor, rather than be given the same preferences as a city-bound morning journey. This is a qualitative expectation, not a measured turning percentage or a prohibition on other journeys.

The user initially considered a neural network because the map is a graph and route choices appear connected. For this next step, the user wants a probabilistic model that can be authored without training data. The model and its probability data must live in the project, so its assumptions are inspectable and can be changed deliberately.

## Requirements carried into the next discussion

- Describe coherent complete journeys, with route choices depending on earlier choices.
- Calculate a vehicle's complete route before spawning it; it then follows that route.
- Store the model's probability data and assumptions in the project.
- Replace or enhance the current method of choosing routes from road weights.
- Express different morning and evening travel patterns, including the main corridor's role.
- Distinguish measured evidence from user observations and authored estimates.
- Work without requiring a trained neural network or data that we do not have.

How to satisfy these requirements is the subject of the next discussion. This handoff does not choose a mathematical model, a memory representation, a sampling method, or a file schema.

## Where to find the existing context

- [Current project format](../../../components/simulator/experiment-project-format.md): the accepted inputs and generation behavior. A future proposal must be distinguished from this implemented format.
- [Current demand generator](../../../../components/simulator/Streetflow.Simulator/Generation/CityDemandGenerator.cs): how road weights currently affect arrivals and route selection.
- [Candidate route generation](../../../../components/simulator/Streetflow.Simulator/Generation/CandidateRoutes.cs): existing construction of legal complete routes.
- [Original Podu Roș scenario](../../../../experiments/drafts/podu-ros/README.md): traffic evidence and its limits.
- [Commute scenario](../../../../experiments/drafts/podu-ros-commute/README.md): directional estimates and the Nicolina blockage investigation.

## Starting brief for a fresh instance

> Help me develop the mathematics of a probabilistic traffic-route model for this project. First read this handoff and establish what the current road-weight approach can and cannot express. I want coherent vehicle journeys whose later route choices depend on earlier choices, with a complete route calculated before spawning. The probability data must be stored in the project. We lack sufficient training data, so a trained neural network is not the starting point. Morning traffic generally heads from Nicolina toward the city/Palas, and evening traffic generally returns south; the Nicolina–Strada Palat corridor is the main route. These are qualitative observations, not measured turning counts. Work through the model with me before implementing it. Keep this discussion focused on mathematics and traffic assumptions; leave server, API, Docker, UI, and signal-control work outside its scope. This handoff supplies the premise, not a chosen solution.
