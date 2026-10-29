<p align="center">
    <!-- doc badges -->
    <a href="https://splatxr.github.io/docs/index.html">
        <img alt="Documentation" src="https://img.shields.io/badge/docs-splatxr.github.io-ff6a3d.svg"></a>
    <!-- license badge -->
    <a href="LICENSE">
        <img alt="License" src="https://img.shields.io/badge/License-MIT-blue.svg"></a>
</p>

<h1 align="center">SplatXR</h1>

<p align="center"> Unity toolkit for AR/VR 3D Gaussian Splatting </p>

<p align="center">
    <a href="https://splatxr.github.io"><b>Project Page</b></a> ·
    <a href="https://splatxr.github.io/docs/index.html"><b>Documentation</b></a> ·
    <a href="https://arxiv.org/abs/2610.26338"><b>Paper</b></a> ·
    <a href="https://github.com/splatxr/splatxr/releases"><b>Releases</b></a>
</p>

<p align="center">
    <img alt="SplatXR demo" src="https://splatxr.github.io/static/images/SplatXR_gif_optimized.gif" width="350">
</p>

# About

SplatXR is an open-source Unity toolkit for building XR (AR/VR/MR) applications with 3D Gaussian Splatting (3DGS). This toolkit is described in our UIST 2026 paper, [*SplatXR: A Toolkit for Multi-Layered Extended Reality via On-Headset 3D Gaussian Splatting*](https://arxiv.org/abs/2610.26338).

The toolkit is structured around a **modular building block system**:

- **Rendering**: optimized, standalone on-headset visualization of Gaussian splats.
- **Physics**: collision detection and physical interaction through mesh-augmented splats.
- **Effects**: shader-based effects such as disintegration, radial reveal, and digital rain.
- **Blending**: portal-like compositing between virtual splat scenes and real passthrough.

Building blocks are added from the **SplatXR Building Blocks Panel** in the Unity editor and can be applied to splat GameObjects.

SplatXR was developed at Princeton University's Situated Interactions Lab (PSI Lab).

### Platform Support

| Platform        | Status                                                                 |
| --------------- | ---------------------------------------------------------------------- |
| Meta Quest 3    | Fully supported                                     |
| Apple Vision Pro | Mostly supported for rendering in VR and AR      |
| Other Unity XR headsets | Likely to work; contributions welcome                          |

# Quickstart

## 1. Installation

### Prerequisites

- Unity **2022.3** or newer (tested on Unity 6.2)
- An XR project set up for your target headset (e.g. the Meta XR SDK for Quest 3)

### Install the Unity package

1. Download the latest release from the [Releases page](https://github.com/splatxr/splatxr/releases) and unzip it (or clone this repository).
2. Open your Unity project and go to **Window > Package Manager**.
3. Click the **+** button in the top-left corner and select **Add package from disk...**.
4. Select the `package.json` file inside the `SplatXRToolkit` folder.
5. The package appears in the Package Manager under **In Project**.
6. Open **SplatXR > Building Blocks Panel** from the Unity menu.

### Install the meshing tool (optional, needed for Splat Physics)

The [Splat Physics](https://splatxr.github.io/docs/building-blocks/physics.html) block uses a separate meshing tool to generate collision meshes. The release may already include the meshing tool in `SplatXRToolkit/Runtime/SplatXRBuildingBlocks/Physics/`. If it isn't there, download it for your platform:

- **Windows:** [splatxr_meshing.exe](https://github.com/splatxr/splatxr/releases)
- **macOS:** [splatxr_meshing_mac](https://github.com/splatxr/splatxr/releases)

Place it in `SplatXRToolkit/Runtime/SplatXRBuildingBlocks/Physics/` (or your project's `Assets` folder) without renaming it. On macOS, the first run may be blocked; allow it under **System Settings > Privacy & Security > Open Anyway**.

See the [Installation guide](https://splatxr.github.io/docs/installation.html) for more detail.

## 2. Add your first splat

Don't have a splat yet? [Download a sample splat](https://drive.google.com/file/d/1jSminyShIB1PNanPwkbY0LOUug2h0wm9/view?usp=sharing).

1. Open **SplatXR > Building Blocks Panel**.
2. Select the **Splat Renderer** building block.
3. Enter an **Object Name** and keep **Renderer** set to **Base Renderer**.
4. Click **Browse** next to **Input Splat File (PLY)** and choose your `.ply` file.
5. Click **Add to Scene**.

Your splat is now rendered in the scene and ready for the other building blocks: physics, effects, and blending.

## 3. Build to your headset

Build and deploy as you normally would for your XR platform. For best performance, keep visible splats in a scene under roughly **200k** (see the [FAQ](https://splatxr.github.io/docs/faq.html)).

# Building Blocks

| Block | What it does | Docs |
| ----- | ------------ | ---- |
| **Splat Renderer** | On-device rendering of Gaussian splats from `.ply` files. **Base Renderer** for a single splat, **LoD Renderer** for distance-based detail levels in larger scenes. | [Renderer](https://splatxr.github.io/docs/building-blocks/renderer.html) |
| **Splat Physics** | Generates collision meshes from splats for rigid body dynamics and shadow casting. **Object** mode for detailed objects, **Space** mode for rooms and environments. | [Physics](https://splatxr.github.io/docs/building-blocks/physics.html) |
| **Splat Effects** | Shader-based effects (Disintegrate, Radial Reveal, Digital Rain), plus a template for writing your own custom effect shaders. | [Effects](https://splatxr.github.io/docs/building-blocks/effects.html) |
| **Splat Blending** | Portal windows that show a splat space blended with passthrough reality, with adjustable edge softness. | [Blending](https://splatxr.github.io/docs/building-blocks/blending.html) |


# Repository Structure

- [`SplatXRToolkit/`](SplatXRToolkit): the Unity package. Install it through the Package Manager using its `package.json`. See its [README](SplatXRToolkit/README.md) for a short overview, and the [documentation](https://splatxr.github.io/docs/index.html) for the full reference of each building block.
- [`Meshing/`](Meshing): the Python source for the splat-to-mesh meshing tool used by the Splat Physics block. These scripts are what the bundled meshing tool (`splatxr_meshing.exe` / `splatxr_meshing_mac`) is packaged from as a standalone executable.

# Learn More

| Section | Description |
| ------- | ----------- |
| [Project Page](https://splatxr.github.io) | Overview, videos, and paper |
| [Documentation](https://splatxr.github.io/docs/index.html) | Guides for every building block |
| **Getting Started** | |
| [Installation](https://splatxr.github.io/docs/installation.html) | Install the package and meshing tool |
| [Quickstart](https://splatxr.github.io/docs/quickstart.html) | Import your first Gaussian splat in a few minutes |
| [FAQ / Known Issues](https://splatxr.github.io/docs/faq.html) | Splat count limits, editor performance, and more |
| **Paper** | |
| [Paper](https://arxiv.org/abs/2610.26338) | *SplatXR*, UIST 2026 |

# Contributing

We welcome contributions from the community! Technical support and maintenance from the original authors will be limited, so we encourage you to:

- Contribute features, fixes, or support for new XR platforms via pull requests.
- Improve or add to the documentation.
- Answer questions and help triage issues.

Please open an issue or pull request on [GitHub](https://github.com/splatxr/SplatXR).

# Citation

If you use SplatXR in your research, please consider citing our [paper](https://arxiv.org/abs/2610.26338):

```bibtex
@inproceedings{vachha2026splatxr,
author = {Vachha, Cyrus and Kari, Mohamed and Abtahi, Parastoo},
title = {SplatXR: A Toolkit for Multi-Layered Extended Reality via On-Headset 3D Gaussian Splatting},
year = {2026},
publisher = {Association for Computing Machinery},
url = {https://doi.org/10.1145/3830398.3830681},
doi = {10.1145/3830398.3830681},
booktitle = {Proceedings of the 39th Annual ACM Symposium on User Interface Software and Technology},
series = {UIST '26}
}
```

# Authors and Acknowledgements

SplatXR is developed by [Cyrus Vachha](https://cvachha.github.io/research-profile.html), [Mohamed Kari](https://mkari.de/), and [Parastoo Abtahi](https://parastooabtahi.com/) at Princeton University. Corresponding author: Cyrus Vachha (cvachha[at]princeton.edu).

We thank [Szymon Rusinkiewicz](https://www.cs.princeton.edu/~smr/) for advice and assistance with the object meshing algorithm, Simon Bethke for providing the [botanical garden dataset](https://www.kaggle.com/datasets/simonbethke/botanical-garden-america), and members of the PSI Lab for their advice. This research was supported by the Princeton NextG Innovation Grant.

The splat rendering shaders are adapted from [GaussianSplats3D](https://github.com/mkkellogg/GaussianSplats3D) by Mark Kellogg (MIT License). See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

# License

SplatXR is released under the [MIT License](LICENSE).
