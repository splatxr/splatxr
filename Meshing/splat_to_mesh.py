#!/usr/bin/env python3
"""
Convert a Gaussian splat PLY into an OBJ mesh.
Usage:
    python splat_to_mesh.py scene.ply --mesh_output_path output
    python splat_to_mesh.py object.ply --mesh_output_path output --mesh_is_object True
"""

import argparse
import sys
from pathlib import Path

from marching_cubes_object_mesh import mesh_object
from poisson_scene_mesh import mesh_scene

def process_ply(
    splat_path: str,
    mesh_output_path: str,
    mesh_is_object: bool = False,
    mesh_threshold: float = 0.15,
    mesh_splatscale: float = 1.5,
    mesh_grid_size: int = 256,
    mesh_flatten_base: bool = False,
    mesh_flatten_percentage: float = 5.0,
    mesh_flatten_axis: int = 4,
    mesh_face_count: int = 7000,
    mesh_scene_keep_ratio: float = 0.15,
) -> str:
    """
    Process a splat PLY file to generate a mesh.

    Args:
        splat_path: Path to the input splat PLY file
        mesh_output_path: Directory for the output OBJ file
        mesh_is_object: If True, use the marching cubes object pipeline; if False, use the Poisson scene pipeline
        mesh_threshold: Threshold for object mesh generation
        mesh_splatscale: Splat scale for object mesh generation
        mesh_grid_size: Grid resolution for object mesh generation
        mesh_flatten_base: If True, flatten the base of the object mesh
        mesh_flatten_percentage: Percentage of mesh base to flatten
        mesh_flatten_axis: Axis to flatten (0:+X 1:+Y 2:+Z 3:-X 4:-Y 5:-Z)
        mesh_face_count: Target face count after downsampling
        mesh_scene_keep_ratio: Base ratio of faces to keep when downsampling scene meshes

    Returns:
        Absolute path of the written OBJ file.
    """

    if mesh_is_object:
        output_mesh = mesh_object(
            splat_path,
            mesh_output_path,
            mesh_threshold,
            mesh_splatscale,
            mesh_grid_size,
            mesh_flatten_base,
            mesh_flatten_percentage,
            mesh_flatten_axis,
            mesh_face_count,
        )
    else:
        output_mesh = mesh_scene(splat_path, mesh_output_path, mesh_face_count, mesh_scene_keep_ratio)
    return str(Path(output_mesh).resolve())


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description='Process PLY splat file to generate a mesh')
    parser.add_argument('splat_path', type=str, help='Path to the input splat PLY file')
    parser.add_argument('--mesh_output_path', type=str, help='Path to the output OBJ file location', default=None)

    parser.add_argument('--mesh_is_object', type=str, help='Mesh: True if the splat is an object, else it is a scene', default='False')

    parser.add_argument('--mesh_threshold', type=float, help='Object Mesh: Threshold for mesh generation', default=0.15)
    parser.add_argument('--mesh_splatscale', type=float, help='Object Mesh: Splat Scale for mesh generation', default=1.5)
    parser.add_argument('--mesh_grid_size', type=int, help='Object Mesh: Size of splat grid for detail of reconstruction', default=256)

    parser.add_argument('--mesh_flatten_base', type=str, help='Object Mesh: True to flatten the base of the mesh', default='False')
    parser.add_argument('--mesh_flatten_percentage', type=float, help='Object Mesh: Percentage of the mesh base to flatten', default=5.0)
    parser.add_argument('--mesh_flatten_axis', type=int, help='Object Mesh: Axis of mesh to flatten (0: +X, 1: +Y, 2: +Z, 3: -X, 4: -Y, 5: -Z)', default=4)

    parser.add_argument('--mesh_face_count', type=int, help='Face count of downsampled final mesh', default=7000)

    parser.add_argument(
        '--mesh_scene_keep_ratio',
        type=float,
        default=0.15,
        help='Scene Mesh: Base ratio of faces to keep; scales up with mesh size via sqrt (default 0.15).',
    )

    args = parser.parse_args()

    mesh_is_object = args.mesh_is_object.lower() in ('true', '1', 'yes')
    mesh_flatten_base = args.mesh_flatten_base.lower() in ('true', '1', 'yes')

    try:
        output_mesh_path = process_ply(
            args.splat_path,
            args.mesh_output_path,
            mesh_is_object,
            args.mesh_threshold,
            args.mesh_splatscale,
            args.mesh_grid_size,
            mesh_flatten_base,
            args.mesh_flatten_percentage,
            args.mesh_flatten_axis,
            args.mesh_face_count,
            mesh_scene_keep_ratio=args.mesh_scene_keep_ratio,
        )
        sys.exit(0)
    except Exception as exc:
        print(f"Error: {exc}", file=sys.stderr, flush=True)
        sys.exit(1)
