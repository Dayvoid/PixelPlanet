## Review of the MaCE Proposal

`MaCEPlan.MD` correctly identifies the primary architectural friction in GeneSys: the rigid coupling between **discrete material IDs** and **continuous mass fields**.

### Key Strengths of the MaCE Approach

* **Retaining Locked Lithology**: Keeping bedrock, mantle, and core as static/discrete IDs avoids wasting memory bandwidth on deep geosphere transport.


* **Unified Potential Driving Force ($A$)**: Unifying gravity, pore capacity, hydrostatic head, fluid shear, and biological cohesion into a single affinity field $A$ eliminates sprawling, brittle `if/else` checks.


* **Continuous Erosion Staging**: Feeding mechanical stress into a continuous mobile channel ($\rho_{\text{sediment}}$) before flipping cell occupancy resolves the abrupt identity popping during weathering.



### Critical Shortcomings for Granular "Pixel Sand"

* **Diffusion and Smear**: Pure MaCE redistributes continuous mass across a $3\times 3$ Moore neighborhood via softmax weighting. As noted in the proposal ($\partial_t \rho = \Delta \rho - 2\beta \nabla \cdot (\rho \nabla A)$), lower $\beta$ produces a viscous, milky fluid smear rather than distinct granular particles, while high $\beta$ triggers sharp numerical instability on discrete grids.


* **Gather/Scatter Race Hazards**: Simulating continuous mass transfer over overlapping Moore neighborhoods without atomics requires separate pre-normalization passes ($Z_{ij}$) and ping-pong staging, doubling memory footprint and bandwidth requirements.


* **Lack of Dynamic Friction**: MaCE lacks an explicit mechanism for shear stress thresholds, making it difficult to maintain stable talus slopes at the true angle of repose ($\theta_r \approx 30^\circ\text{--}35^\circ$).

---

## Integrated Architecture: Affinity-Driven Margolus CA (Ad-MaCA)

By adopting a **Margolus Block Cellular Automaton (MaCA)** as the spatial execution substrate, we retain MaCE’s affinity formulation ($A$) while gaining deterministic, conflict-free granular physics.

```
Margolus 2x2 Partitioning (Alternating Ticks):

       Phase 0 (Even Ticks)               Phase 1 (Odd Ticks: Shifted +1, +1)
   +---+---+   +---+---+              +       +---+---+       +
   | 0 | 1 |   | 0 | 1 |              |       | 0 | 1 |       |
   +---+---+   +---+---+              +---+---+---+---+---+---+
   | 2 | 3 |   | 2 | 3 |              | 2 | 3 | 0 | 1 | 2 | 3 |
   +---+---+   +---+---+              +---+---+---+---+---+---+
   | 0 | 1 |   | 0 | 1 |              |       | 2 | 3 |       |
   +---+---+   +---+---+              +       +---+---+       +

```

### Why Margolus Solves Granular Motion

1. **Guaranteed Local Mass Conservation**: The planet is tiled into non-overlapping $2\times 2$ blocks. All exchanges occur strictly within the 4-cell block ($M_{\text{block}} = \sum_{k=0}^3 \rho_k$). No mass can be duplicated or lost.
2. **Zero Race Conditions**: Because neighboring blocks do not share cells within a single sub-step, threads execute with zero write contention. No atomic operations or multi-pass normalizers ($Z_{ij}$) are needed.


3. **Natural Angle of Repose**: Alternating the grid partition offset by $(+1, +1)$ between even and odd ticks provides isotropic diagonal percolation, enabling granular solids to avalanche down slopes naturally without directional sweep artifacts.

---

## Technical Specification: The Ad-MaCA Transport Engine

### 1. Hybrid Cell State & Mobile Channels

Rather than treating cells as purely discrete or purely continuous, each grid location maintains a **structural host** and a **mobile fraction**:

```text
Host Material (uint ID)  -> Bedrock, Soil Matrix, Water, Air, Magma (Static properties & collision)
Mobile Layer (Texture2DArray, R16G16_SFLOAT):
  Channel 0: ρ_sediment  -> Continuous granular mass [0.0, 1.0] (Dry sand, silt, eroded fines)
  Channel 1: ρ_water_mob -> Surface run mass / shallow film [0.0, 1.0]

```

* When $\rho_{\text{sediment}} \ge 1.0$, the cell solidifies into a discrete `MaterialIds.Sediment` or `Soil` host.


* When an exposed `Soil` or `Sediment` cell suffers hydraulic/wind erosion, its structural integrity decreases, shedding mass into $\rho_{\text{sediment}}$. When $\rho_{\text{sediment}} < \varepsilon$, the host transitions to `Air` or `Water`.



### 2. Block Affinity Optimization ($A$)

Inside each $2\times 2$ Margolus block:


$$\mathbf{s} = [c_{00}, c_{10}, c_{01}, c_{11}]$$

For every permissible mass configuration $\mathbf{s}' \in \mathcal{P}(\mathbf{s})$ that conserves block mass, we calculate the configuration affinity score:


$$E(\mathbf{s}') = \sum_{k \in \text{block}} \rho_k \cdot A_k$$

The transition selects the state that maximizes $E(\mathbf{s}')$, damped by a friction/viscosity threshold.

```text
Affinity Terms:
  A_k = - (k_gravity * y_k)                          // Gravitational potential (downward pull)
        + (k_shear * dot(FlowVector_k, d_k))        // Fluid drag / wind entrainment
        - (k_cohesion * Moisture_k)                  // Capillary binding (wet sand sticks)
        - (k_roots * RootMass_k)                     // Vegetation anchoring (turf/trees)
        + (k_buoyancy * (Density_host - Density_k))  // Sinking in water / floating

```

```
Block Avalanche Example (Phase 0, Even Shift):
+---------+---------+                 +---------+---------+
| Sand    | Air     |                 | Air     | Air     |
| A = 1.0 | A = 0.2 |   =======>      | A = 0.2 | A = 0.2 |
+---------+---------+                 +---------+---------+
| Rock    | Air     |                 | Rock    | Sand    |
| Solid   | A = 0.8 |                 | Solid   | A = 0.8 |
+---------+---------+                 +---------+---------+
Initial: E = 1.0                      Final:   E = 0.8 (Sand slides down-right
                                              because lower right has higher
                                              net affinity than top-left).

```

### 3. Coordinate Handling on Polar Cylindrical Grids

GeneSys uses an angular-radial polar grid $(\theta, r)$:

* **Angular Seam ($\theta$)**: Resolution $W$ is guaranteed even ($2^n$ or multiples of 32). Margolus blocks cleanly wrap across $\theta = 0 \leftrightarrow W - 1$ on both even and odd phases.


* **Radial Boundary ($r$)**: Clamped at the mantle floor and atmospheric ceiling. Odd-phase $(+1, +1)$ shifts mask out the top boundary row to prevent vertical leakages.


* **Volume Metric Tensor**: Because cell physical volume expands with radius ($\Delta V \propto r$), gravitational affinity scales with radius:

$$k_{\text{gravity}}(y) = g \cdot \frac{r_y}{R_{\text{surface}}}$$



---

## Integration Tick Order

To integrate with existing systems (`WaterPlan.MD`, `Hydrology.compute`, `Geology.compute`) without creating race conditions or violating latent heat conservation:

```
1. Material Thermodynamics & Latent Phase Changes (MaterialSimulation.compute)
   - Melting, boiling, freezing (preserves energy balance)[cite: 1, 2].

2. Affinity Potential Assembly (AffinityPass.compute) [NEW]
   - Read-only pass: gathers gravity, wind vectors, hydrology flow, root masks[cite: 1, 2].
   - Compiles combined affinity field A(x, y) into a staging buffer[cite: 2].

3. Margolus Granular & Mobile Transport (MacaTransport.compute) [NEW]
   - Evaluates non-overlapping 2x2 blocks[cite: 2].
   - Phase 0 on even ticks (offset 0,0); Phase 1 on odd ticks (offset 1,1).
   - Swaps and redistributes ρ_sediment and surface bedload.

4. Host Identity Reconciliation
   - Sub-cell consolidation: ρ_sediment >= 1.0 -> MaterialIds.Sediment[cite: 2].
   - Vacuum collapse: depleted cells convert to Air or Water[cite: 2].

5. Hydrology & Column Hydrostatics (Hydrology.compute)
   - Infiltration, groundwater percolation, hydrostatic head relaxation[cite: 1].
   - Reads newly settled sediment/soil beds[cite: 1, 2].

6. Mechanical Weathering & Stress Feeds
   - Wind/runoff shear converts structural soil/rock stress (aux.w) into mobile ρ_sediment[cite: 1, 2].

7. Geology, Combustion, and Biology
   - Volcanism, ash settlement, fire spread, grass/tree root updates[cite: 1, 2].

```

---

## Phased Implementation Roadmap

### Phase 1: Core MaCA Solver & Dry Sediment Pilot

* Implement `MacaTransport.compute` with alternating $2\times 2$ block dispatch.
* Support discrete dry granular motion for `MaterialIds.Sediment` and `Soil`.


* **Exit Criteria**: Sediment forms stable talus slopes with angle of repose $\approx 32^\circ$. Pass existing zero-loss conservation tests.



### Phase 2: Continuous Mobile Channels & Erosion Feeds

* Introduce `Resources.MobileMass` (`Texture2DArray` storing $\rho_{\text{sediment}}$).


* Modify `Hydrology.compute`: sustained surface shear transfers mass from host solids to $\rho_{\text{sediment}}$ instead of executing an instant binary ID swap.


* **Exit Criteria**: Weathering a cliff produces smooth sediment avalanches that accumulate at the base without popping artifacts.



### Phase 3: Hydrologic & Rheologic Coupling

* Couple local soil moisture (`aux.y` / `state.z`) to the affinity cohesion term:
* **Dry**: Free-flowing grain behavior ($\theta_r \approx 32^\circ$).
* **Damp**: Cohesive capillary binding ($\theta_r \approx 65^\circ$).
* **Saturated**: Viscous slurry/mudflow ($\theta_r \approx 5^\circ$).


* Add fluid entrainment: high surface water velocity ($\vert{}\mathbf{u}\vert{}$) increases mobile affinity in the direction of flow.


* **Exit Criteria**: Waterfalls and heavy runoff carve riverbeds, carrying suspended sediment out into ocean deltas.



### Phase 4: Biotic Reinforcement & Karst/Ash Integration

* Link grass and tree root network masks (`GrassRead`, `TreeRead`) to the affinity resistance term.


* Route settling volcanic ash through the MaCA solver instead of the discrete claim-winner pass.


* **Exit Criteria**: Rooted vegetation prevents hillside mudslides during heavy precipitation events.



---

## Validation & Test Harness Plan

To match the existing PlayMode testing suite (`Tests/PlayMode/`), add the following automated tests:

| Test Fixture | Purpose | Pass Condition |
| --- | --- | --- |
| `MacaSedimentConservesTotalMass` | Drop 500 sediment particles in an enclosed basin over 500 ticks. | Final mass equals initial mass ($\Delta M = 0.000$).

 |
| `MacaAngleOfReposeConforms` | Deposit continuous sediment from a single-point overhead spout onto a flat bedrock shelf. | Slump angle converges to $32^\circ \pm 3^\circ$. |
| `MacaMoistureIncreasesCriticalAngle` | Compare dry sediment column to wet sediment column under identical lateral disturbing force. | Wet column maintains vertical face longer than dry column. |
| `MacaRootMaskResistsShear` | Apply high lateral wind/water shear to bare soil vs turf-covered soil. | Turf-covered soil sheds $<10\%$ the mobile sediment mass of bare soil.

 |
| `MacaDeterministicReplay` | Run identical world generation and seed with MaCA transport enabled across 100 ticks.

 | Byte-for-byte exact material and mobile mass textures.

 |

Would you like to focus next on the HLSL implementation for the $2\times 2$ Margolus block compute kernel, or on formulating the continuous soil-stress-to-mobile-mass transfer function in `Hydrology.compute`?