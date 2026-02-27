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

- **Texture Space Path Tracing:** Custom HLSL Compute Shader that fires rays from the artifact's surface normals to sample incoming environmental light (Irradiance).
- **Progressive Light Accumulation:** C# simulation loop that steps through time, accumulating light samples frame-by-frame to prevent GPU timeouts.
- **Lux-to-Dose Conversion:** Translates instantaneous light energy ($E$) into total cumulative dose ($D_{total}$).
- **Heatmap Visualization:** Custom surface shader that reads the accumulated exposure data and maps it to a color gradient (Safe vs. Danger) based on museum conservation thresholds.

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
