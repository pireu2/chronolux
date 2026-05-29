# ChronoLux - Heritage Artifact Environmental Damage Simulator

**Status:** Software Complete (Academic Paper Generation Phase)

## Overview

This project is a scientific visualization tool designed for preventive conservation. It functions as a "Digital Twin" to simulate and calculate cumulative environmental light damage (measured in Lux Hours) on cultural heritage artifacts.

Unlike traditional game renderers that calculate light for visual aesthetics (Screen Space), this project implements a custom, mathematically rigorous **Texture Space Ray Tracer**. It bakes physically accurate light energy data directly into an artifact's UV texture map to create a scientifically queryable heatmap of potential deterioration.

## Technologies Used

- **Engine:** Unity 3D
- **Render Pipeline:** High Definition Render Pipeline (HDRP)
- **API:** DirectX 12 (DirectX Raytracing - DXR)
- **Languages:** C# (Simulation Control), HLSL (Compute Shaders)
- **UI System:** Unity UI Toolkit (UXML/USS)

## Core Features (Completed)

- **Texture Space Path Tracing:** Custom DXR (DirectX Raytracing) kernels that sample irradiance directly into UV-space textures, bypassing screen-space limitations.
- **Perez All-Weather Sky Model:** Mathematically validated solar and skylight distribution using cosine-weighted hemisphere sampling.
- **Material-Aware Simulation:** Physics-driven `SimulationMaterial` component for assigning real-world reflectance and transmittance values to scene geometry.
- **Energy Conservation:** Mathematical clamping and normalization of light transport (Reflectance + Transmittance ≤ 1.0) to ensure thermodynamic validity.
- **Deterministic RTAS Sorting:** Custom renderer sorting logic to ensure stable InstanceID-to-Material mapping across different hardware and sessions.
- **Virtual Lux Sensors:** Real-time metrology probes that provide analytical variance and percentage error against theoretical mathematical baselines.
- **Headless Data Export:** Decoupled background Coroutine that safely generates massive multi-column CSVs of simulation telemetry (AvgDose, MaxDose, Variance) without halting the editor.
- **Lux-to-Dose Conversion:** Translates instantaneous light energy ($E$) into total cumulative dose ($D_{total}$) in Lux Hours.
- **Heatmap Visualization:** 2-pass HDRP shader mapping accumulated exposure data to a Purple-Red-Yellow gradient based on CIE 157:2004 conservation thresholds.
- **Persistent Project Management:** Serialization of artifact transformations, material assignments, and simulation settings directly to JSON.

## Requirements

- **OS:** Windows 10/11 (Required for DX12)
- **GPU:** NVIDIA RTX series (or DXR-compatible GPU) capable of hardware-accelerated ray tracing.
- **Unity Setup:** 
  - HDRP package installed.
  - Graphics API set to `Direct3D12 (Experimental)`.
  - Static Batching disabled.
  - Realtime Ray Tracing explicitly enabled in the HDRP Asset.

## Project Structure

- `/Scripts`: C# controllers for the simulation loop, time management, UI, and texture baking.
- `/Shaders`: HLSL Compute Shaders for the path tracer and the surface shader for the heatmap visualization.
- `/UI`: UXML and USS files that comprise the modern dashboard and material catalog.
- `/Models`: 3D test meshes (ensure meshes have non-overlapping UVs and Read/Write enabled).

---

_Note: The software engineering phase of this academic project is complete. The current focus is running Scenarios A, B, and C to extract data for the resulting CS Diploma thesis paper._
