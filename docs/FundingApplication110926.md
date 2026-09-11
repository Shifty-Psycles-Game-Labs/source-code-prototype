# SCOTTISH ENTERPRISE RESEARCH AND INNOVATION GRANT APPLICATION

Project title
NEM-U: A Layered Simulation Engine for Consistent Physical, Cognitive, and Narrative Worlds

Applicant
Organisation: Shifty Psycles Game Labs
Lead applicant: B.P. Law
Contact: <shiftypsycles@gmail.com>
Location: Edinburgh, Scotland
Funding requested: GBP 30,000-50,000, subject to the eligible-cost assessment
Project duration: 12 months
Proposed start date: [Insert confirmed start date]

EXECUTIVE SUMMARY

Shifty Psycles Game Labs is developing NEM-U, a general-purpose simulation engine for building internally consistent fictional, educational, and research environments. The project addresses a practical gap between conventional physics engines, which are powerful but difficult to extend beyond established physical behaviours, and narrative tools, which are flexible but provide few guarantees that a world remains coherent as it grows.

NEM-U introduces a layered architecture that keeps different kinds of claims and behaviour explicitly separate:

- Tier 1: physical and mathematical simulation, including discrete differential operators, incidence matrices, and harmonic field evolution.
- Tier 2: structured cognitive and behavioural modelling, represented as bounded tensor-like state variables and gradient-inspired update rules.
- Tier 3: designed narrative mechanics, such as fictional powers or world rules, explicitly identified as authored mechanics rather than physical facts.

A governing meta-layer records whether rules are derived, modelled, or deliberately designed. This gives creators and researchers a transparent way to extend a simulated world without silently mixing physical claims with speculative or narrative behaviour.

The provided sampleis an archived Godot 4.8 and C# prototype with documented discrete-operator concepts, incidence-matrix construction, field data structures, player look and WASD movement, a procedural city node, and early harmonic simulation components. Grant support would reactivate and consolidate this prototype into a tested vertical slice, make five identified MVP design decisions, document the operator interfaces, and benchmark the engine on affordable reference hardware.

The requested support would fund a 12-month research and development programme with four milestone-gated outputs: an authoritative technical specification, a working vertical-slice demonstration, an operator-level technical manual, and a performance and validation report. The result would be a demonstrable Scottish technology asset with potential applications in game development, procedural generation, interactive education, simulation research, and tools for designing complex fictional systems.

THE PROBLEM AND OPPORTUNITY

Teams building complex simulated or fictional worlds currently face three problems.

First, conventional engines generally provide rendering, physics, and scripting, but they do not provide a reusable framework for distinguishing established physical behaviour from conjectural models and authored world rules. Consistency must be maintained manually by each project team.

Second, research prototypes often contain mathematically interesting components without a practical authoring environment, visual feedback, or a pathway to an accessible demonstration. This makes it difficult for collaborators, educators, and potential customers to inspect or extend the work.

Third, narrative systems and procedural-generation tools can become difficult to audit as their rule sets expand. A change made for storytelling can unintentionally affect a physical simulation, a behavioural model, or a procedural generator.

NEM-U addresses this opportunity by treating the relationships between layers as explicit, typed, bounded interfaces. The engine is intended to let a user see what is physically simulated, what is a structured hypothesis, and what is an authored rule. This approach supports both creative freedom and technical accountability.

PROPOSED INNOVATION

The core innovation is not a claim that one mathematical model explains every domain. It is an engineering architecture for composing different domains without confusing their evidential status.

NEM-U combines:

- Discrete differential operators and graph or simplicial-complex representations.
- Sparse field updates suitable for CPU and future GPU execution.
- Harmonic and Laplacian-inspired propagation for testable field experiments.
- Bounded cognitive state models with Knowledge, Emotion, Intent, and Action dimensions.
- Procedural geometry driven by explicit field values rather than opaque randomness alone.
- A narrative or metaphysical rule layer that remains visibly distinct from physical simulation.
- Instrumentation for field values, entropy-like measures, coupling strength, and operator traces.

This creates a reusable substrate for experiments where the user can change a rule, observe its effect, and identify which layer produced the result. The architecture is designed to remain useful even if a proposed theory is later rejected or revised: the simulation layer, validation tools, and authoring interfaces remain valuable independently.

CURRENT STATE OF DEVELOPMENT

The archived prototype documentation describes:

- A Godot 4.8 and C# engine codebase.
- A scene-based architecture for world orchestration and visualisation.
- Discrete operator and field components, including incidence-matrix and Laplacian-related code.
- Early Maxwell-like and metric or curvature-oriented engine components.
- Cognitive state and attention-matrix prototypes.
- Player look and WASD movement in the prototype scene.
- A procedural city node and wireframe visualisation experiments.
- An incomplete debug HUD identified as a next development task.
- Documentation describing the intended operator pipeline, public-safe interfaces, and proposed research roadmap.

The source README identifies this as an archived snapshot that is not actively maintained. It is an experimental system, not a completed commercial product. Several claims in the working documentation are aspirational and require measurement. The proposed project is deliberately structured to test, qualify, or reject those claims rather than present them as established results.

SOURCE SCOPE AND REPRESENTATION

This application is a concise funding summary based on the public-safe content of NEM-U Prototype: Operations Manifold Omega_(M_4), including its prototype status, operator taxonomy, admissibility conditions, discrete implementation mapping, Godot architecture, cognitive-field notes, risks, roadmap, and evaluation criteria. It is not a complete reproduction of the source archive or a claim that every proposed subsystem is implemented.

The source describes three operator classes: physical operators acting on tensor fields and differential forms; cognitive operators acting on a cognitive tensor; and metaphysical or magical operators acting on probability measures. This application uses the terms physical, cognitive, and narrative layers as a product and communication framing. The narrative layer is an authored software layer and should not be read as a claim that metaphysical mechanisms have been scientifically validated.

RESEARCH AND DEVELOPMENT OBJECTIVES

1. Establish one authoritative specification. Make and document the five MVP design decisions identified in the source roadmap: the progression operator, collapse operator, entropy-field definition, discrete manifold topology, and update orchestration rules.

2. Build a reproducible vertical slice. Demonstrate a user-authored narrative rule operating on a Tier 1 field substrate, with the boundary between physical, cognitive, and narrative layers visible in the interface.

3. Validate numerical behaviour. Test field updates, Laplacian application, smoothing or collapse operations, bounded coupling, and cognitive updates using repeatable scenarios and recorded metrics.

4. Create developer documentation. Produce an operator-level manual covering domains, inputs, outputs, invariants, admissibility checks, extension points, and known limitations.

5. Benchmark accessible hardware. Measure startup time, frame rate, memory use, field-update cost, and scaling behaviour on a defined reference machine. The current target is modest hardware rather than a high-end workstation.

6. Prepare commercial and research pathways. Package the demonstrator and documentation for discussions with Scottish, UK, and international partners in games, education, simulation, procedural generation, and research tooling.

TECHNICAL APPROACH

The engine represents a simulation domain as a graph or simplicial-complex-like structure containing vertices, edges, and, where applicable, faces. Incidence relationships are used to construct discrete operators. A simplified public description of the operator pipeline is:

1. Construct or load the discrete domain.
2. Apply an incidence-based Laplacian or other selected operator to a field.
3. Derive field magnitude and diagnostic values.
4. Apply bounded progression, smoothing, or collapse updates.
5. Feed selected outputs into geometry, cognition, or narrative layers through explicit interfaces.
6. Record the update, invariants, and validation metrics.

Representative public-safe forms include:

- Discrete Laplacian: Delta = D1^T D1 + D2 D2^T
- Field update: H_next = H - step * Delta(H)
- Magnitude diagnostic: S(x) = |H(x)|
- Gradient-informed movement: x_next = x + alpha * grad(S)
- Cognitive relaxation: H_next = H - step * grad(Phi(H))

These expressions are prototypes and test cases, not claims that the underlying research questions are solved. The implementation will include parameter bounds, deterministic test modes, and explicit diagnostics so that stability and failure cases can be measured.

The Godot scene tree will remain the presentation and orchestration layer. C# components will own numerical data structures, update rules, validation checks, and logging. This separation supports later replacement of CPU implementations with sparse or GPU-backed routines without changing the authoring model.

WORK PACKAGES AND MILESTONES

Work package 1: Specification and risk closure, months 1-3

- Review the current operator and architecture documents.
- Produce one authoritative terminology and interface specification.
- Resolve or formally defer the five MVP design decisions identified in the source roadmap.
- Define acceptance tests for physical, cognitive, and narrative layer boundaries.
- Establish version control, issue tracking, and reproducible experiment records.

Milestone 1: Approved technical specification and test plan.

Work package 2: Engine vertical slice, months 4-6

- Consolidate the discrete domain and field update path.
- Implement explicit interfaces between physical, cognitive, and authored narrative layers.
- Add one user-authored narrative mechanic with explicit provenance and permissions.
- Add visual diagnostics for field magnitude, coupling, and operator traces.
- Demonstrate the workflow in a playable or interactive Godot scene.

Milestone 2: Working vertical-slice demonstration on the reference hardware.

Work package 3: Validation and documentation, months 7-9

- Run repeatable harmonicity, boundedness, smoothing, and cognitive-update tests.
- Compare expected and observed numerical behaviour.
- Record failure cases and revise the implementation where required.
- Draft the operator-level technical manual under appropriate confidentiality controls.

Milestone 3: Technical manual, draft 1, and validation report.

Work package 4: Benchmarking and commercial preparation, months 10-12

- Benchmark CPU time, memory, frame rate, and scaling behaviour.
- Package the demonstrator and example scenarios.
- Prepare licensing, collaboration, and partner-facing materials.
- Identify the next development stage and a route to a paid pilot or research partnership.

Milestone 4: Technical manual v1.0, benchmark report, and partner-ready demonstrator.

EXPECTED OUTCOMES AND BENEFITS

The project will deliver:

- A validated NEM-U vertical slice rather than a documentation-only concept.
- A clear, auditable separation between physical simulation, cognitive modelling, and authored narrative rules.
- A reusable operator and field framework for future simulation projects.
- A technical manual that enables external review and controlled extension.
- Benchmark evidence showing the hardware requirements and practical limits of the approach.
- Public-safe examples suitable for demonstrations, education, and research discussion.
- A defined commercialisation route through licensing, technical partnerships, commissioned prototypes, or a productised developer tool.

For Scottish Enterprise, the opportunity is an ambitious software R&D project with potential to create intellectual property, technical capability, skilled development work, and exportable tools. The project is also suitable for collaboration with universities, creative technology organisations, and small studios that need procedural or simulation technology without the cost of developing a foundational engine from scratch.

MARKET AND COMMERCIALISATION

Initial target users are:

- Independent and mid-sized game studios building complex narrative worlds.
- Procedural-generation developers and technical artists.
- Researchers and educators demonstrating computational models interactively.
- Simulation and visualisation teams requiring transparent, extensible field systems.
- Organisations exploring cognitive or agent-based behaviour in controlled environments.

The first commercial route will be a demonstrator-led partnership model. Potential partners would receive a controlled technical demonstration and, where appropriate, access to documentation or source components under licence or NDA. Longer-term options include a developer toolkit, commissioned integrations, educational licensing, and a research platform subscription.

No unvalidated total-addressable-market figure is included in this application. Market sizing will be completed during Work Package 4 using customer interviews, competitor analysis, and evidence from pilot discussions.

INTELLECTUAL PROPERTY AND CONFIDENTIALITY

The project will protect the specific cross-layer operator algebra, internal Tier 3 cost and reweighting mechanisms, and full manifold construction details as confidential intellectual property where appropriate. Public-facing documentation will describe interfaces, validation methods, and safe examples without disclosing proprietary implementation details.

The project will maintain a clear distinction between:

- Established third-party mathematics and openly documented algorithms.
- Shifty Psycles Game Labs' original software, specifications, and operator interfaces.
- Hypotheses or research questions that remain unvalidated.
- Deliberate fictional or narrative mechanics.

Any external academic references will be cited accurately and will not be presented as endorsements, partnerships, or proof of NEM-U claims.

RISKS AND MITIGATION

Mathematical and specification risk: Some proposed operators and cross-layer rules remain unresolved. The project addresses this through an authoritative specification, explicit issue ownership, acceptance tests, and formal recording of decisions.

Numerical stability risk: Discrete updates may diverge for unsuitable step sizes or coupling strengths. The implementation will use bounded parameters, deterministic test cases, monitoring, and failure reports. Stability will be reported as measured behaviour, not assumed from terminology.

Scope risk: The project could expand into an unbounded theory programme. The grant scope is limited to one vertical slice, a defined operator set, the five MVP design decisions identified in the source roadmap, and measurable benchmarks.

Commercial risk: Demand for a new simulation category is not yet proven. The team will test demand through partner interviews and demonstrations before committing to a larger product build.

Capability and delivery risk: The project is being developed by a small team. Milestone-gated delivery, modular work packages, external technical review where affordable, and early hardware testing will reduce this risk.

Communication risk: Terms such as metaphysics, magic, or unified theory may be misunderstood. Commercial and grant communications will lead with software functionality, layer separation, reproducibility, and measurable engineering outcomes.

FUNDING REQUEST AND USE OF FUNDS

The requested grant contribution is GBP 30,000-50,000 for a 12-month R&D programme. The final figure and eligible-cost breakdown will be aligned with Scottish Enterprise guidance.

Indicative allocation:

- Software engineering and numerical implementation.
- Research, specification, and technical validation.
- Documentation, user testing, and partner engagement.
- Hardware, software, testing, and delivery overheads.

A final costed budget will be supplied in the format required by Scottish Enterprise. Funding should be released against the four milestones above. This structure protects public value by linking expenditure to inspectable outputs and allows the project to stop, redirect, or narrow work if validation results do not support the original hypothesis.

TEAM AND CAPABILITY

Shifty Psycles Game Labs has an existing Godot and C# prototype, technical documentation, and a defined research direction. The project lead, B.P. Law, will lead the technical specification, prototype development, documentation, and partner engagement. Additional support will be sought where needed for numerical methods, independent review, commercial research, and user testing.

The team will not present unverified qualifications, partnerships, performance results, or academic endorsements as established facts. Evidence supplied with the application will include the current codebase, prototype demonstrations, technical documents, and milestone records.

SUCCESS CRITERIA

The project will be considered successful when:

1. The five MVP design decisions identified in the source roadmap have been resolved, formally deferred with rationale, or replaced by tested alternatives.
2. A user can run the vertical slice and identify which outputs originate in the physical, cognitive, and narrative layers.
3. The selected numerical tests run reproducibly and produce recorded pass, fail, or limitation results.
4. The demonstrator runs on the agreed reference hardware within the agreed performance envelope.
5. The technical manual and benchmark report are complete and suitable for external technical review.
6. At least three potential partners or customers have reviewed the demonstrator and provided documented feedback.
7. The next commercial or research stage has a defined scope, cost, and route to market.

CONCLUSION

NEM-U is a high-risk, high-potential software R&D project. Its value lies in turning a difficult conceptual problem into a practical engineering system: a simulation environment where different kinds of rules can coexist while remaining identifiable, testable, and replaceable.

The prototype provides a starting point, but the proposed grant is not based on treating the prototype's strongest claims as already proven. It is based on a focused programme to resolve known uncertainties, build a working vertical slice, measure performance and stability, and produce documentation that external reviewers and commercial partners can assess.

Scottish Enterprise support would help Shifty Psycles Game Labs convert an experimental codebase into a credible, demonstrable, and potentially exportable simulation technology platform developed in Scotland.

#### `https://shifty-psy.github.io/SPGL/`

#### `shiftypsycles@gmail.com`
