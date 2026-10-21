# SplatXR Toolkit

**Version:** 0.1.0  
**Unity Version:** 2022.3 or higher (tested in Unity 6.2)  
**Author:** Princeton's Situated Interactions Lab  
**Documentation:** [splatxr.github.io/docs](https://splatxr.github.io/docs/index.html)  
**Paper:** [SplatXR, UIST 2026](https://splatxr.github.io/static/paper/splatxr_uist26_paper.pdf)

Unity toolkit for building XR (AR/VR/MR) applications with 3D Gaussian Splatting. See the [documentation](https://splatxr.github.io/docs/index.html) for full guides on every building block.

## Installation

1. Open your Unity project and go to **Window > Package Manager** (**Window > Package Management > Package Manager** in Unity 6).
2. Click the **+** button in the top-left corner and select **Add package from disk...** (**Install package from disk...** in Unity 6).
3. Select the `package.json` file in this folder.
4. (Optional, required for the Splat Physics block) The SplatXR meshing tool is included in the release zip. If you installed from the GitHub source, download it from the [Releases page](https://github.com/splatxr/splatxr/releases) and place it in this package's `Runtime/SplatXRBuildingBlocks/Physics` folder or your project's `Assets` folder, keeping its file name (`splatxr_meshing.exe` on Windows, `splatxr_meshing_mac` on macOS).

See the [Installation guide](https://splatxr.github.io/docs/installation.html) for more detail.

## Quick Start

1. Open **SplatXR > Building Blocks Panel** from the Unity menu.
2. Select the **Splat Renderer** building block.
3. Enter an **Object Name** and keep **Renderer** set to **Base Renderer**.
4. Click **Browse** next to **Input Splat File (PLY)** and select your `.ply` file.
5. Click **Add to Scene**.

## Building Blocks

| Block | What it does |
| ----- | ------------ |
| [Splat Renderer](https://splatxr.github.io/docs/building-blocks/renderer.html) | On-device rendering of Gaussian splats, with Base and LoD renderers. |
| [Splat Physics](https://splatxr.github.io/docs/building-blocks/physics.html) | Collision meshes from splats for physics interactions and shadow casting. |
| [Splat Effects](https://splatxr.github.io/docs/building-blocks/effects.html) | Shader effects such as Disintegrate, Radial Reveal, and Digital Rain, plus custom effects. |
| [Splat Blending](https://splatxr.github.io/docs/building-blocks/blending.html) | Portal windows that blend splat spaces with passthrough reality. |

Known issues and performance tips are in the [FAQ](https://splatxr.github.io/docs/faq.html).

## License

The SplatXR Toolkit is released under the [MIT License](LICENSE.md).
