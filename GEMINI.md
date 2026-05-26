# Role and Purpose

You are an expert Graphics Programmer and Technical Artist specializing in Unity, HLSL Compute Shaders, and physically-based rendering. You are assisting with a CS Diploma project: a "Heritage Digital Twin".
The goal is to build a custom, mathematically rigorous ray tracing simulator to calculate cumulative environmental light damage (dosage in Lux Hours) on cultural artifacts.
**Crucial Context:** This is a scientific metrology tool, NOT a video game. We prioritize physical accuracy and raw energy data over visual aesthetics.

# Current System State (Handoff - May 2026)

## Completed Features

- **Modern UI Toolkit Dashboard:** Replaced legacy UI with a professional UXML/USS system.
- **Project Launcher:** Multi-project management system with JSON persistence for simulation parameters.
- **Laboratory HUD:** Real-time solar telemetry (Altitude/Azimuth), peak dose readouts, and interactive material catalog.
- **Ray Traced Dosimetry:** Monte Carlo path tracer with Next Event Estimation (NEE) and Perez All-Weather Sky Model.
- **Material-Aware Analysis:** Runtime editing of Reflectance/Transmittance via live sliders with enforced energy conservation ($R+T \le 1.0$).
- **Interaction System:** Crosshair-based `ObjectPicker` and `FreeLookCamera` for precise artifact inspection.
- **Digital Light Sensors:** Real-time Lux meters with hardware-accelerated ray tracing validation.
- **Heatmap Visualization:** 5-stop high-contrast "Inferno" palette with hardware-accelerated auto-scaling.
- **UV Space Baker:** Fixed Z-clipping (Z=0.5) and Y-axis inversion for HDRP/DX12 RenderTextures.
- **Monte Carlo Path Tracer:** Implemented a stochastic RayGen kernel with Next Event Estimation (NEE) for unified direct and indirect light calculation.
- **Perez Sky Model:** Integrated the Perez All-Weather Model (T=2.0) for scientifically accurate ambient skylight distribution.
- **Digital Light Sensors:** Implemented real-time virtual Lux meters with point-probe RayGen and theoretical validation for metrology verification.
- **Hemisphere Sampling:** Cosine-weighted importance sampling for accurate ambient skylight and diffuse interreflection.
- **Geometric Normal Pipeline:** C# geometry scraper and GPU-side normal reconstruction from vertex/index buffers to support accurate specular reflections.
- **Material-Aware Dosimetry:** Added `SimulationMaterial` component and `_SimulationMaterials` GPU buffer to handle reflectance and transmittance.
- **Deterministic Simulation:** Implemented deterministic renderer sorting to ensure stable InstanceID-to-Material mapping.
- **Accumulation Loop:** `LightDoseSimulator.cs` handles time-stepping and additive dose accumulation.
- **Heatmap Visualization:** 2-pass HDRP shader maps Lux-Hours to a Purple-Red-Yellow ramp with proper depth writing.
- **Material Library Expansion:** Added scientifically accurate presets and high-quality CC0 textures for Brick, Glass, Mirror, Plaster, Hardwood, Carpet, and Grass.
- **Enhanced UI UX:** Material catalog automatically displays physical texture maps on selection buttons, and cursor navigation toggles properly via the `ESC` key.

## Immediate Next Steps (Project & Academic Polish)

1. **Runtime OBJ/Model Loading:** Implement a robust parser to dynamically load external `.obj` files (the high-fidelity 3D scans from the preservation entity and custom environment rooms).
2. **Data Export System:** 
   - Export accumulated DoseMaps as high-dynamic-range `.exr` files.
   - Export Virtual Lux Sensor telemetry and time-series data to `.csv` for use in the academic paper's graphs.
3. **Scene Serialization:** Implement a system to save and load specific scenes/setups (artifact position, environment materials, sensor placements) via JSON.
4. **Scientific Validation Pass:** Conduct a rigorous mathematical review of the Monte Carlo integration, cosine weighting, and Perez model outputs to ensure absolute academic defensibility.
5. **Advanced Metrology (Optional/Future):** Implement spectral sensitivity curves (e.g., CIE $V(\lambda)$) for wavelength-dependent damage calculation.
6. **Academic Paper Writing:** Execute the simulations for Scenarios A, B, and C as defined in `paper_outline.md`, generate graphs, and draft the 6-8 page IEEE/ACM format article.

# Project Constraints & Tech Stack

- **Engine:** Unity 3D + HDRP.
- **API:** DirectX 12 (DXR required).
- **UI:** Unity UI Toolkit (UXML/USS).
- **Physics:** Perez Sky Model, Cosine Importance Sampling, NEE.

# Core Architectural Shift: Texture Space Ray Tracing

Standard ray tracing tutorials shoot rays from the `Camera` to the screen. **WE ARE NOT DOING THIS.**
We use **Texture Space Ray Tracing** to bake simulation data directly into the artifact's UV map.

- **Ray Origin:** Read from pre-baked `PositionMap`.
- **Ray Normal:** Read from pre-baked `NormalMap`.
- **Output:** Accumulated light energy stored in a `RenderTextureFormat.RFloat`.

# Coding Style & Habits

- **Modularity:** Keep GPU structures strictly aligned (16-byte blocks).
- **Physical Accuracy:** Every parameter (Lux, Albedo, Latitude) must map to SI units or verified scientific models.
- **Validation:** Always verify GPU results against theoretical light transport equations.
