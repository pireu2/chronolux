# ChronoLux: Digital Twin for Environmental Light Dosimetry

[![Unity](https://img.shields.io/badge/Unity-2022.3+-black?logo=unity)](https://unity.com/)
[![Render Pipeline](https://img.shields.io/badge/Pipeline-HDRP-blue)](#)
[![API](https://img.shields.io/badge/API-DirectX_12_DXR-green)](#)
[![Status](https://img.shields.io/badge/Status-Software_Complete-success)](#)

**ChronoLux** is a scientifically rigorous metrology and visualization tool designed for the preventive conservation of cultural heritage artifacts. Built as a CS Diploma Project, it functions as a "Digital Twin," simulating long-term solar radiation exposure and calculating cumulative environmental light damage (measured in **Lux-Hours**) on 3D artifacts.

Unlike traditional game renderers that prioritize aesthetic screen-space approximations, ChronoLux employs a custom **Texture-Space Monte Carlo Path Tracer** to mathematically simulate thermodynamic light transport directly onto an artifact's surface over extended temporal ranges.

---

## 🔬 Scientific Methodology

### 1. Texture-Space Ray Tracing
Standard ray tracers shoot rays from a virtual camera to evaluate pixels on a screen. ChronoLux inverts this paradigm. Using DirectX Raytracing (DXR), it launches rays directly from the unwrapped UV coordinates of the 3D artifact (via pre-baked `PositionMap` and `NormalMap` buffers). This ensures that light energy is calculated persistently across the entire surface of the object, regardless of camera occlusion or screen resolution.

### 2. Perez All-Weather Sky Model
To ensure physically accurate solar illumination, ChronoLux integrates the **Perez Sky Model**. It calculates the precise astronomical altitude and azimuth of the sun based on geolocation (Latitude/Longitude), UTC offset, and time of year. Diffuse skylight is evaluated using **Cosine-Weighted Hemisphere Importance Sampling** to accurately simulate ambient scattering.

### 3. Next Event Estimation (NEE)
The custom stochastic RayGen kernel utilizes Next Event Estimation to explicitly sample direct sunlight alongside recursive indirect multi-bounce reflections, unifying the direct and indirect light calculation for maximum statistical convergence.

### 4. Energy Conservation
All scene geometry is bound by strict thermodynamic laws. The custom `SimulationMaterial` component allows real-time configuration of physical Reflectance ($R$) and Transmittance ($T$) values, algorithmically clamping the system to ensure $R + T \le 1.0$.

---

## ⚙️ Core Capabilities

- **Time-Stepping Dosimetry:** Accumulates instantaneous illuminance ($E$) in Lux over configurable chronological steps (e.g., hourly intervals across 365 days) into a cumulative dose ($D_{total}$) in Lux-Hours.
- **Virtual Lux Sensors:** Deployable 3D probes that act as digital light meters. They calculate expected theoretical irradiance ($E = E_{beam} \cdot \cos(\theta) + E_{diffuse} \cdot 0.5$) and validate the Monte Carlo raytracer's output, actively tracking percentage error margins.
- **False-Color Heatmap Visualization:** High-performance HDRP surface shader maps the raw Float32 Lux-Hour data into an "Inferno" (Purple-Red-Yellow) gradient. This enables curators to instantly identify microscopic high-risk exposure hotspots.
- **Deterministic RTAS:** Enforces absolute strict sorting of Renderer Instance IDs during Acceleration Structure generation to guarantee repeatable scientific data across different hardware executions.
- **Headless Data Pipeline:** Asynchronous Coroutines safely read back GPU memory to generate massive multi-column CSV datasets (tracking `DeltaMaxDose`, `DoseVariance`, `Coverage`, and telemetry) without stalling the main simulation thread.

---

## 💻 System Requirements

Because ChronoLux relies on raw hardware-accelerated ray tracing to calculate millions of paths, it requires modern hardware:

*   **OS:** Windows 10 / Windows 11 (Strictly required for DirectX 12)
*   **GPU:** NVIDIA RTX 20-series / 30-series / 40-series, or AMD equivalent (Must support `DXR 1.0` or higher)
*   **Engine:** Unity (HDRP)
*   **Project Settings:** 
    *   Graphics API: `Direct3D12 (Experimental)`
    *   Static Batching: **Disabled** (Required for DXR sorting)
    *   Realtime Ray Tracing: **Enabled** in the active HDRP Asset

---

## 📊 Data Output & Metrology

ChronoLux provides highly granular export mechanisms for external data analysis (e.g., Python, MATLAB, Excel):

1.  **SimulationMetrics.csv:** A comprehensive time-series dataset generated asynchronously. It tracks Hourly Time, Sun Altitude/Azimuth, Exterior Beam/Diffuse Lux, Artifact Delta Max/Avg Dose, Cumulative Dose, Hourly Variance, Surface Coverage (%), and Virtual Sensor Error tracking.
2.  **Daily Dose EXR Snapshots:** Exports full 32-bit floating-point High Dynamic Range `.exr` textures for every day of the simulation. This allows exact surface irradiance maps to be analyzed externally or re-imported into ChronoLux's UI timeline slider.

---

## 📂 Project Architecture

*   `/Scripts`: C# simulation orchestration, orbital mechanics (SunCalculator), Async GPU Readback logic, and CSV writing pipelines.
*   `/Shaders`: HLSL Compute kernels (`IrradianceBaker.compute`) containing the core mathematical ray tracer and BRDF evaluation, plus the visual `HeatmapShader.shader`.
*   `/UI`: Modern Unity UI Toolkit architecture (`.uxml` and `.uss`) comprising the laboratory dashboard, settings launcher, and material catalog.
*   `/Models`: Sandbox testing environments (Note: All imported target artifacts must have non-overlapping UVs and `Read/Write` enabled in import settings).

---

> **Academic Note:** The software engineering phase of this CS Diploma project is complete. Current usage is focused on executing experimental scenarios to extract empirical data for the resulting academic paper.