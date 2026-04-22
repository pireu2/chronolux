# ChronoLux - Heritage Artifact Environmental Damage Simulator

**Status:** Work in Progress (Diploma Project)

## Overview

This project is a scientific visualization tool designed for preventive conservation. It functions as a "Digital Twin" to simulate and calculate cumulative environmental light damage (measured in Lux Hours) on cultural heritage artifacts.

Unlike traditional game renderers that calculate light for visual aesthetics (Screen Space), this project implements a custom, mathematically rigorous **Texture Space Ray Tracer**. It bakes physically accurate light energy data directly into an artifact's UV texture map to create a scientifically queryable heatmap of potential deterioration.

## Technologies Used

- **Engine:** Unity 3D
- **Render Pipeline:** High Definition Render Pipeline (HDRP)
- **API:** DirectX 12 (DirectX Raytracing - DXR)
- **Languages:** C# (Simulation Control), HLSL (Compute Shaders)

## Current Scope (WIP)

- **Texture Space Path Tracing:** Custom DXR (DirectX Raytracing) kernels that sample irradiance directly into UV-space textures, bypassing screen-space limitations.
- **Material-Aware Simulation:** Physics-driven `SimulationMaterial` component for assigning real-world reflectance and transmittance values to scene geometry.
- **Energy Conservation:** Mathematical clamping and normalization of light transport (Reflectance + Transmittance ≤ 1.0) to ensure thermodynamic validity.
- **Deterministic RTAS Sorting:** Custom renderer sorting logic to ensure stable InstanceID-to-Material mapping across different hardware and sessions.
- **Progressive Light Accumulation:** C# simulation loop that steps through time (Sunrise to Sunset), accumulating light samples to prevent GPU timeouts.
- **Lux-to-Dose Conversion:** Translates instantaneous light energy ($E$) into total cumulative dose ($D_{total}$) in Lux Hours.
- **Heatmap Visualization:** 2-pass HDRP shader mapping accumulated exposure data to a Purple-Red-Yellow gradient based on CIE 157:2004 conservation thresholds.


## Requirements

- **OS:** Windows 10/11 (Required for DX12)
- **GPU:** NVIDIA RTX series (or DXR-compatible GPU) capable of hardware-accelerated ray tracing.
- **Unity Setup:** \* HDRP package installed.
  - Graphics API set to `Direct3D12 (Experimental)`.
  - Static Batching disabled.
  - Realtime Ray Tracing explicitly enabled in the HDRP Asset.

## Project Structure

- `/Scripts`: C# controllers for the simulation loop, time management, and texture baking.
- `/Shaders`: HLSL Compute Shaders for the path tracer and the surface shader for the heatmap visualization.
- `/Models`: 3D test meshes (ensure meshes have non-overlapping UVs and Read/Write enabled).

---

_Note: This is an active academic project and is currently focused strictly on the core physics simulation and data accumulation engine._
