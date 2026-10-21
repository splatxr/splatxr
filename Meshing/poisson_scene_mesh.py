#!/usr/bin/env python3
"""
Mesh a Gaussian splat scene PLY with Poisson surface reconstruction.
Usage:
    python poisson_scene_mesh.py scene.ply --mesh_output_path output
"""

import argparse
import math
import sys
from pathlib import Path

import numpy as np
import open3d as o3d

PLY_TYPES = {
    "char": "i1", "int8": "i1", "uchar": "u1", "uint8": "u1",
    "short": "i2", "int16": "i2", "ushort": "u2", "uint16": "u2",
    "int": "i4", "int32": "i4", "uint": "u4", "uint32": "u4",
    "float": "f4", "float32": "f4", "double": "f8", "float64": "f8",
}


def read_ply_vertices(path) -> dict[str, np.ndarray]:
    """Read the vertex element of a binary PLY as {property_name: array}.

    Other elements are skipped, e.g. the extrinsic/intrinsic/version metadata
    that Apple's splat exporter writes before the vertices.
    """
    with open(path, "rb") as f:
        if f.readline().strip() != b"ply":
            raise ValueError(f"{path} is not a PLY file.")

        endian = "<"
        elements = []
        while True:
            line = f.readline()
            if not line:
                raise ValueError(f"Unexpected end of file in PLY header of {path}.")
            parts = line.decode("ascii", errors="replace").split()
            if not parts:
                continue
            if parts[0] == "end_header":
                break
            if parts[0] == "format":
                if parts[1] == "binary_big_endian":
                    endian = ">"
                elif parts[1] != "binary_little_endian":
                    raise ValueError(f"Only binary PLY files are supported, got '{parts[1]}'.")
            elif parts[0] == "element":
                elements.append((parts[1], int(parts[2]), []))
            elif parts[0] == "property":
                if parts[1] == "list":
                    raise ValueError(f"List properties are not supported: {line!r}")
                elements[-1][2].append((parts[2], endian + PLY_TYPES[parts[1]]))

        for name, count, props in elements:
            dtype = np.dtype(props)
            if name != "vertex":
                f.seek(dtype.itemsize * count, 1)
                continue
            raw = f.read(dtype.itemsize * count)
            if len(raw) != dtype.itemsize * count:
                raise ValueError(f"{path} ended before all vertices were read.")
            data = np.frombuffer(raw, dtype=dtype)
            return {p: data[p] for p, _ in props}

    raise ValueError(f"{path} has no vertex element.")


def get_mesh_output_path(splat_path, mesh_output_dir) -> Path:
    return Path(mesh_output_dir) / f"{Path(splat_path).stem}_mesh.obj"


def poisson_reconstruct(points: np.ndarray, depth: int = 8, density_trim: float = 0.1) -> o3d.geometry.TriangleMesh:
    pcd = o3d.geometry.PointCloud()
    pcd.points = o3d.utility.Vector3dVector(points.astype(np.float64))

    pcd, _ = pcd.remove_statistical_outlier(nb_neighbors=20, std_ratio=2.0)

    pcd.estimate_normals(search_param=o3d.geometry.KDTreeSearchParamHybrid(radius=0.1, max_nn=20))
    pcd.orient_normals_consistent_tangent_plane(50)

    mesh, densities = o3d.geometry.TriangleMesh.create_from_point_cloud_poisson(
        pcd, depth=depth, width=0, scale=0.9, linear_fit=False
    )

    densities = np.asarray(densities)
    mesh.remove_vertices_by_mask(densities < np.quantile(densities, density_trim))
    mesh = mesh.crop(pcd.get_axis_aligned_bounding_box())

    print(f"Poisson mesh: {len(mesh.vertices)} vertices, {len(mesh.triangles)} faces")
    return mesh


def simplify_mesh(mesh: o3d.geometry.TriangleMesh, target_faces: int, voxel_size: float = 0.01) -> o3d.geometry.TriangleMesh:
    simplified = mesh.simplify_quadric_decimation(target_faces)
    simplified.remove_duplicated_vertices()
    simplified.remove_duplicated_triangles()
    simplified.remove_unreferenced_vertices()

    if not simplified.has_vertices():
        print("Simplification failed, falling back to voxel downsampling")
        pcd = mesh.sample_points_uniformly(number_of_points=len(mesh.vertices))
        pcd_down = pcd.voxel_down_sample(voxel_size=voxel_size)
        simplified = o3d.geometry.TriangleMesh.create_from_point_cloud_alpha_shape(pcd_down, alpha=0.1)

    simplified.compute_vertex_normals()
    print(f"Simplified mesh: {len(simplified.vertices)} vertices, {len(simplified.triangles)} faces")
    return simplified


def scene_target_faces(current_faces: int, min_faces: int, keep_ratio: float) -> int:
    """Faces to keep: keep_ratio grows with sqrt(mesh size) so larger scenes keep more detail."""
    if keep_ratio <= 0.0:
        return min_faces
    scaled_ratio = min(keep_ratio * math.sqrt(current_faces / 100_000), 0.80)
    target = max(min_faces, int(current_faces * scaled_ratio))
    print(f"Scene adaptive target: {target} ({scaled_ratio * 100:.0f}% of {current_faces})")
    return target


def mesh_scene(splat_path, mesh_output_path, mesh_face_count: int = 7000, mesh_scene_keep_ratio: float = 0.15) -> Path:
    """Mesh a splat scene PLY and write <mesh_output_path>/<name>_mesh.obj. Returns the OBJ path."""
    if not mesh_output_path:
        raise ValueError("mesh_output_path is required.")
    output_path = get_mesh_output_path(splat_path, mesh_output_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    print(f"Poisson scene meshing: {splat_path} -> {output_path}")
    vertices = read_ply_vertices(splat_path)
    points = np.column_stack([vertices["x"], vertices["y"], vertices["z"]])

    mesh = poisson_reconstruct(points)
    if not mesh.has_triangles():
        raise RuntimeError("Poisson reconstruction produced an empty mesh.")
    target = scene_target_faces(len(mesh.triangles), mesh_face_count, mesh_scene_keep_ratio)
    mesh = simplify_mesh(mesh, target)

    o3d.io.write_triangle_mesh(str(output_path), mesh)
    print(f"Saved mesh to {output_path}")
    return output_path.resolve()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description='Mesh a splat scene PLY with Poisson reconstruction')
    parser.add_argument('splat_path', type=str, help='Path to the input splat PLY file')
    parser.add_argument('--mesh_output_path', type=str, required=True, help='Directory for the output OBJ file')
    parser.add_argument('--mesh_face_count', type=int, help='Minimum face count of downsampled final mesh', default=7000)
    parser.add_argument(
        '--mesh_scene_keep_ratio',
        type=float,
        default=0.15,
        help='Scene Mesh: Base ratio of faces to keep; scales up with mesh size via sqrt (default 0.15).',
    )
    args = parser.parse_args()

    try:
        mesh_scene(args.splat_path, args.mesh_output_path, args.mesh_face_count, args.mesh_scene_keep_ratio)
        sys.exit(0)
    except Exception as exc:
        print(f"Error: {exc}", file=sys.stderr, flush=True)
        sys.exit(1)
