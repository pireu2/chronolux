# Article Outline

**Proposed Titles:**
1. *GPU-Accelerated Texture Space Ray Tracing for Cumulative Light Dosimetry in Heritage Digital Twins*
2. *Interactive Environmental Light Simulation for Cultural Artifact Preservation using DXR*
3. *A Real-Time Metrology Framework for Heritage Conservation via Texture Space Ray Tracing*

**Target Format:** IEEE/ACM Double-Column (6-8 pages)

---

## 1. Introduction (~1 Page)
*   **Context & Problem:** The irreversible nature of photo-degradation in cultural heritage artifacts. The limitations of traditional, offline, non-interactive simulation tools (e.g., Radiance, EnergyPlus) which are slow and inaccessible for rapid iterative planning.
*   **The Proposed Solution:** Introduce **ChronoLux**, a custom, interactive Digital Twin framework.
*   **Key Contributions:** 
    *   Reversing the traditional rendering paradigm: Utilizing Texture Space Ray Tracing to aggregate temporal environmental data (Lux Hours) directly onto artifact surfaces.
    *   Real-time interactivity allowing conservators to test material and positional interventions.
    *   Integration of rigorous scientific models (Perez All-Weather Sky Model) with hardware-accelerated Next Event Estimation (NEE).

## 2. Related Work (~0.5 - 1 Page)
*   **Traditional Simulation in Conservation:** Overview of existing tools and their computational bottlenecks.
*   **Advances in Real-Time Ray Tracing:** How DXR/RTX has revolutionized rendering, and the gap in its application for scientific metrology vs. visual aesthetics.
*   **Digital Twins in Heritage:** Current state of the art and how ChronoLux bridges the gap between high-fidelity 3D scans and environmental physics.

## 3. Methodology & System Architecture (~2 Pages)
*   **The Physics of Light Dosimetry:** 
    *   Defining the mathematical model for Irradiance ($E$) and Total Cumulative Dose ($D_{total}$).
    *   Application of Lambert's Cosine Law and energy conservation principles ($R + T \le 1.0$).
*   **Texture Space Ray Tracing Architecture (Core Innovation):**
    *   Detailed explanation of baking Position and Normal maps.
    *   How the Compute Shader utilizes these maps as ray origins, bypassing screen-space limitations.
    *   Include a high-level system architecture diagram.
*   **Environmental Modeling:**
    *   Integration of the Perez Sky Model for accurate solar telemetry (Altitude/Azimuth) and ambient irradiance based on geolocation and time.
    *   Stochastic hemisphere sampling and NEE implementation.
*   **Statistical Metrology & Data Telemetry:**
    *   Calculation of spatial dose variance to identify high-contrast material stress zones.
    *   Implementation of real-time virtual point-probe sensors for localized delta-dose data extraction.

## 4. Case Studies & Empirical Results (~1.5 - 2 Pages)
*   *Note: Utilizing high-fidelity 3D scans provided by the heritage preservation partner.*
*   **Scenario A: The Baseline Exposure:**
    *   Artifact placed in a standard room (Clear glass, plaster walls, hardwood floor).
    *   Results of a 1-year simulated exposure. 
    *   Heatmap visualization identifying "high-risk" structural zones.
*   **Scenario B: Material Intervention:**
    *   Altering the environment to mitigate damage (e.g., swapping to UV/Tinted glass, changing floor reflectance via carpet).
    *   Comparative analysis of the peak dose reduction (supported by CSV data graphs).
*   **Scenario C: Positional Optimization:**
    *   Baseline environment maintained, but artifact repositioned to rely on diffuse interreflection.
    *   Verification via localized Virtual Lux Sensors and delta-dose tracking, demonstrating adherence to annual conservation limits (e.g., < 50,000 Lux-Hours) at specific high-risk sample points.

## 5. Performance & Validation (~0.5 - 1 Page)
*   **Computational Efficiency:** Benchmarking the GPU-accelerated texture-space approach. Comparing the time taken for a full-year simulation (e.g., 1-hour steps) against traditional offline methods.
*   **Scientific Validation:** Correlating the Virtual Lux Sensor readouts with theoretical mathematical models to prove metrological accuracy.

## 6. Conclusion & Future Work (~0.5 Page)
*   **Summary:** Reiterate how the interactive Digital Twin framework empowers preservationists with accessible, physically accurate data.
*   **Future Directions:** 
    *   Integration of spectral sensitivity curves (CIE $V(\lambda)$) for wavelength-specific damage analysis.
    *   Expanding the dynamic loading of complex architectural environments.