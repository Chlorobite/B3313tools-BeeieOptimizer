# B3313 Dev Tools - BeeieOptimizer
BeeieOptimizer is a tool that interfaces with SM64Lib from SM64 Rom Manager to perform various optimizations, including:
* Fast3D optimization
  * optimization of geometry (gSPVertex - gSP1Triangle) calls, including degenerate (0-area) triangle elimination
  * elimination of duplicate RCP state setup
  * gSPCullDisplayList usage, per-material, several times depending on the geometry complexity
* Collision optimization, using a Blender 4 script
* MIO0 compression

The only B3313-specific function is Painting64 awareness. The optimizations might otherwise be applicable to other SM64 Rom Manager hacks as well, provided:
* the romhack is patched to allow decompressing MIO0 areas, if MIO0 is used
* any new functions the hack might perform in the Fast3D space are accounted for.

[Join the B33h1v3 Discord](https://discord.gg/n8PsDgVkBr) for discussion!

## Usage
1. Obtain the dependencies:
  * This software was developed and tested on Linux only; Windows users must run through WSL, or patch the process calls accordingly.
  * Blender 4 is required to be installed on the system for the collision optimization process.
  * A [Painting64](https://github.com/Chlorobite/B3313tools-Painting64) binary is required to run the optimization process, unless Painting64-specific logic is manually removed from the source code; __this will break B3313__, but should work for other hacks.
  * You must source your own target ROM & .config file pair, as well as `paintingcfg.txt`. We will not provide instructions on how to obtain this material.
2. Run `dotnet build`
3. Copy the files located in `place in bin directory` over to the executable's directory.
4. Run `./BeeieOptimizer` in a terminal for usage instructions.

## Licensing
BeeieOptimizer — the logic that interfaces with SM64Lib to optimize B3313 — is released under the BSD-3-Clause license. See LICENSE_BeeieOptimizer.md for more details.

SM64Lib is released under the MIT license. See LICENSE_SM64Lib.md for more details.

Neither Super Mario 64 hacks, or 'SM64Lib' are affiliated with or endorsed by Nintendo. A legal copy of Super Mario 64 is required in order to use this repository.
