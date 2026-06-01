# Contributing to ChronoLux

Thank you for your interest in contributing to **ChronoLux**! This project started as a CS Diploma thesis to solve a very specific problem in the scientific metrology of photodegradation in cultural heritage artifacts. 

As an academic and scientific tool, we welcome contributions that improve the physical accuracy, computational efficiency, or usability of the Digital Twin.

## How to Contribute

### 1. Reporting Bugs
If you find a bug in the simulation data, UI, or rendering pipeline, please open an issue in the GitHub repository. Be sure to include:
- Your operating system and GPU model.
- Steps to reproduce the bug.
- Expected behavior vs. actual behavior.
- Any relevant screenshots or error logs.

### 2. Suggesting Enhancements
We are always looking for ways to improve the tool. If you have an idea for a new feature (e.g., adding spectral sensitivity curves, new sensor types, or VR support), please open an issue describing your proposal before writing any code. This ensures your idea aligns with the project's scientific goals.

### 3. Submitting Pull Requests
If you want to contribute code:
1. Fork the repository.
2. Create a new branch for your feature or bug fix (`git checkout -b feature/your-feature-name`).
3. Make your changes, ensuring they adhere to the project's coding standards and physical accuracy constraints.
4. Test your changes thoroughly, especially the GPU compute shaders.
5. Commit your changes with clear, descriptive commit messages.
6. Push your branch to your fork.
7. Open a Pull Request (PR) against the `main` branch of this repository.

## Coding Guidelines
- **Modularity:** Keep GPU structures strictly aligned (16-byte blocks).
- **Physical Accuracy:** Every parameter (Lux, Albedo, Latitude) must map to SI units or verified scientific models.
- **Validation:** Always verify GPU results against theoretical light transport equations.

Thank you for helping make ChronoLux a better tool for the preservation of cultural heritage!
