#!/usr/bin/env python3
"""
Mesh a Gaussian splat object PLY with marching cubes.
Usage:
    python marching_cubes_object_mesh.py object.ply --mesh_output_path output
"""

import argparse
import math
import sys
from pathlib import Path

import numpy as np
import open3d as o3d
from skimage.measure import marching_cubes

PLY_TYPES = {
    "char": "i1", "int8": "i1", "uchar": "u1", "uint8": "u1",
    "short": "i2", "int16": "i2", "ushort": "u2", "uint16": "u2",
    "int": "i4", "int32": "i4", "uint": "u4", "uint32": "u4",
    "float": "f4", "float32": "f4", "double": "f8", "float64": "f8",
}

FLATTEN_AXES = {0: '+X', 1: '+Y', 2: '+Z', 3: '-X', 4: '-Y', 5: '-Z'}


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



def load_splats(path):
    """Return (pos, opacity, scale, rot) as float32 arrays; rot is a unit (w, x, y, z) quaternion."""
    vertices = read_ply_vertices(path)
    n = len(vertices["x"])

    def cols(names, default):
        return np.stack(
            [vertices[name] if name in vertices else np.full(n, default) for name in names], axis=1
        ).astype(np.float32)

    pos = cols(("x", "y", "z"), 0.0)
    opacity_raw = vertices["opacity"] if "opacity" in vertices else np.ones(n)
    opacity = (1.0 / (1.0 + np.exp(-opacity_raw.astype(np.float32)))).astype(np.float32)
    scale = np.exp(cols(("scale_0", "scale_1", "scale_2"), 0.0))
    rot = cols(("rot_0", "rot_1", "rot_2", "rot_3"), 0.0)
    if "rot_3" not in vertices:
        rot[:, 0] = 1.0
    return pos, opacity, scale, normalize_quats(rot)


def normalize_quats(rot: np.ndarray) -> np.ndarray:
    """3DGS PLYs store raw, unnormalized quaternions; normalize them (zero quaternions become identity)."""
    norm = np.linalg.norm(rot.astype(np.float64), axis=1, keepdims=True)
    identity = np.array([1.0, 0.0, 0.0, 0.0])
    unit = np.where(norm > 0.0, rot / np.where(norm > 0.0, norm, 1.0), identity)
    return unit.astype(np.float32)


def quat_wxyz_to_matrix(q: np.ndarray) -> np.ndarray:
    """Rotation matrices for unit (w, x, y, z) quaternions of shape (..., 4); returns shape (..., 3, 3)."""
    q = np.asarray(q, dtype=np.float64)
    w, x, y, z = q[..., 0], q[..., 1], q[..., 2], q[..., 3]
    xx, yy, zz = x * x, y * y, z * z
    xy, xz, yz = x * y, x * z, y * z
    xw, yw, zw = x * w, y * w, z * w
    m = np.empty(q.shape[:-1] + (3, 3), dtype=np.float32)
    m[..., 0, 0] = 1.0 - 2.0 * (yy + zz)
    m[..., 0, 1] = 2.0 * (xy - zw)
    m[..., 0, 2] = 2.0 * (xz + yw)
    m[..., 1, 0] = 2.0 * (xy + zw)
    m[..., 1, 1] = 1.0 - 2.0 * (xx + zz)
    m[..., 1, 2] = 2.0 * (yz - xw)
    m[..., 2, 0] = 2.0 * (xz - yw)
    m[..., 2, 1] = 2.0 * (yz + xw)
    m[..., 2, 2] = 1.0 - 2.0 * (xx + yy)
    return m


def splat_func(d2: np.ndarray) -> np.ndarray:
    out = np.zeros_like(d2, dtype=np.float32)
    mask = d2 < 4.0
    t = 1.0 - 0.25 * d2[mask]
    out[mask] = t * t
    return out


def build_field(pos, opacity, scale, rot, thresh: float, splat_scale: float, grid_size: int,
                max_batch_voxels: int = 1 << 21):
    """Rasterize splats into a grid; the surface is the field's zero level set.

    Splats are rasterized in vectorized batches of splats that share the same voxel footprint;
    max_batch_voxels bounds the temporary memory of one batch.
    """
    splat_support = 2.0
    if pos.shape[0] == 0:
        raise ValueError("The splat PLY contains no splats.")

    rotations = quat_wxyz_to_matrix(rot)
    a = rotations * (splat_scale * scale)[:, None, :]
    extent = splat_support * np.sqrt(np.sum(a * a, axis=2))
    bmin = np.min(pos - extent, axis=0)
    bmax = np.max(pos + extent, axis=0)

    bbox_size = bmax - bmin
    bbox_center = 0.5 * (bmax + bmin)
    bbox_size_max = float(np.max(bbox_size))
    if bbox_size_max <= 0.0:
        raise ValueError("Degenerate splat cloud bounding box.")

    padding = int(math.ceil(1.0 + 2.0 * splat_scale))
    grid_scale = bbox_size_max / float(grid_size - padding)
    ni, nj, nk = (
        max(int(math.ceil(float(bbox_size[d]) / bbox_size_max * (grid_size - padding))), 2) + padding
        for d in range(3)
    )
    offset = (bbox_center - 0.5 * (np.array([ni, nj, nk]) - 1) * grid_scale).astype(np.float32)

    field = np.full((nk, nj, ni), float(thresh), dtype=np.float32)
    inv_grid_scale = 1.0 / grid_scale
    dims = np.array([ni, nj, nk])

    sc = np.maximum(scale, grid_scale) * splat_scale
    a = rotations * sc[:, None, :]
    extent = splat_support * np.sqrt(np.sum(a * a, axis=2))
    inv_a = np.linalg.inv(a)

    offset64 = offset.astype(np.float64)
    start = np.maximum(np.floor(((pos - extent).astype(np.float64) - offset64) * inv_grid_scale).astype(np.int64), 0)
    end = np.minimum(np.ceil(((pos + extent).astype(np.float64) - offset64) * inv_grid_scale).astype(np.int64), dims - 1)
    footprint = end - start + 1
    inside = np.flatnonzero(np.all(footprint > 0, axis=1))
    if inside.size == 0:
        return field, grid_scale, offset

    shapes, shape_of = np.unique(footprint[inside], axis=0, return_inverse=True)
    shape_of = shape_of.reshape(-1)
    by_shape = inside[np.argsort(shape_of, kind="stable")]
    group_starts = np.searchsorted(np.sort(shape_of, kind="stable"), np.arange(len(shapes) + 1))

    field_flat = field.reshape(-1)
    for g, (bx, by, bz) in enumerate(shapes):
        members = by_shape[group_starts[g]:group_starts[g + 1]]
        batch = max(1, max_batch_voxels // int(bx * by * bz))
        for b in range(0, len(members), batch):
            idx = members[b:b + batch]
            ii = start[idx, 0:1] + np.arange(bx)
            jj = start[idx, 1:2] + np.arange(by)
            kk = start[idx, 2:3] + np.arange(bz)
            rel = (
                offset[0] + grid_scale * ii.astype(np.float32) - pos[idx, 0:1],
                offset[1] + grid_scale * jj.astype(np.float32) - pos[idx, 1:2],
                offset[2] + grid_scale * kk.astype(np.float32) - pos[idx, 2:3],
            )

            # local = inv(a) @ rel, evaluated on the separable (k, j, i) box of each splat.
            d2 = np.zeros((len(idx), bz, by, bx), dtype=np.float32)
            local = np.empty_like(d2)
            for c in range(3):
                np.add(
                    (inv_a[idx, c, 0, None] * rel[0])[:, None, None, :]
                    + (inv_a[idx, c, 1, None] * rel[1])[:, None, :, None],
                    (inv_a[idx, c, 2, None] * rel[2])[:, :, None, None],
                    out=local,
                )
                np.multiply(local, local, out=local)
                d2 += local

            hit = d2 < 4.0
            weight = splat_func(d2[hit]) * np.broadcast_to(opacity[idx, None, None, None], d2.shape)[hit]
            voxel = (kk[:, :, None, None] * nj + jj[:, None, :, None]) * ni + ii[:, None, None, :]
            np.add.at(field_flat, voxel[hit], -weight)

    return field, grid_scale, offset


def marching_cubes_mesh(splat_path, thresh: float, splat_scale: float, grid_size: int) -> o3d.geometry.TriangleMesh:
    if thresh <= 0.0:
        raise ValueError("mesh_threshold must be positive.")
    if splat_scale <= 0.0:
        raise ValueError("mesh_splatscale must be positive.")
    if grid_size <= 1:
        raise ValueError("mesh_grid_size must be > 1.")

    pos, opacity, scale, rot = load_splats(splat_path)
    field, grid_scale, offset = build_field(pos, opacity, scale, rot, thresh, splat_scale, grid_size)

    verts_zyx, faces, _, _ = marching_cubes(field, level=0.0, spacing=(grid_scale,) * 3)
    verts = verts_zyx[:, ::-1] + offset

    mesh = o3d.geometry.TriangleMesh(
        o3d.utility.Vector3dVector(verts.astype(np.float64)),
        o3d.utility.Vector3iVector(faces.astype(np.int32)),
    )
    print(f"Marching cubes mesh: {len(mesh.vertices)} vertices, {len(mesh.triangles)} faces")
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


def flatten_base(mesh: o3d.geometry.TriangleMesh, flatten_percentage: float = 5.0, axis_selection: int = 4) -> None:

    if not 0.0 < flatten_percentage < 100.0:
        raise ValueError("mesh_flatten_percentage must be between 0 and 100.")
    if axis_selection not in FLATTEN_AXES:
        raise ValueError("mesh_flatten_axis must be 0-5.")

    axis = axis_selection % 3
    vertices = np.asarray(mesh.vertices).copy()
    lo, hi = vertices[:, axis].min(), vertices[:, axis].max()
    band = (hi - lo) * flatten_percentage / 100.0
    if band == 0:
        print(f"Mesh has zero extent along {FLATTEN_AXES[axis_selection]}; not flattening.")
        return

    if axis_selection < 3:
        mask = vertices[:, axis] < lo + band
        target = lo
    else:
        mask = vertices[:, axis] > hi - band
        target = hi

    vertices[mask, axis] = target
    mesh.vertices = o3d.utility.Vector3dVector(vertices)
    mesh.compute_vertex_normals()
    print(f"Flattened {mask.sum()} of {len(vertices)} vertices along {FLATTEN_AXES[axis_selection]}")


def mesh_object(
    splat_path,
    mesh_output_path,
    mesh_threshold: float = 0.15,
    mesh_splatscale: float = 1.5,
    mesh_grid_size: int = 256,
    mesh_flatten_base: bool = False,
    mesh_flatten_percentage: float = 5.0,
    mesh_flatten_axis: int = 4,
    mesh_face_count: int = 7000,
) -> Path:
    """Mesh a splat object PLY and write <mesh_output_path>/<name>_mesh.obj. Returns the OBJ path."""
    if not mesh_output_path:
        raise ValueError("mesh_output_path is required.")
    output_path = get_mesh_output_path(splat_path, mesh_output_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    print(f"Marching cubes object meshing: {splat_path} -> {output_path}")
    mesh = marching_cubes_mesh(splat_path, mesh_threshold, mesh_splatscale, mesh_grid_size)
    mesh = simplify_mesh(mesh, mesh_face_count)
    if mesh_flatten_base:
        flatten_base(mesh, mesh_flatten_percentage, mesh_flatten_axis)

    o3d.io.write_triangle_mesh(str(output_path), mesh)
    print(f"Saved mesh to {output_path}")
    return output_path.resolve()


def str_to_bool(value: str) -> bool:
    return value.lower() in ('true', '1', 'yes')


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description='Mesh a splat object PLY with marching cubes')
    parser.add_argument('splat_path', type=str, help='Path to the input splat PLY file')
    parser.add_argument('--mesh_output_path', type=str, required=True, help='Directory for the output OBJ file')
    parser.add_argument('--mesh_threshold', type=float, help='Object Mesh: Threshold for mesh generation', default=0.15)
    parser.add_argument('--mesh_splatscale', type=float, help='Object Mesh: Splat Scale for mesh generation', default=1.5)
    parser.add_argument('--mesh_grid_size', type=int, help='Object Mesh: Size of splat grid for detail of reconstruction', default=256)

    parser.add_argument('--mesh_flatten_base', type=str_to_bool, help='Object Mesh: True to flatten the base of the mesh', default=False)
    parser.add_argument('--mesh_flatten_percentage', type=float, help='Object Mesh: Percentage of the mesh base to flatten', default=5.0)
    parser.add_argument('--mesh_flatten_axis', type=int, help='Object Mesh: Axis of mesh to flatten (0: +X, 1: +Y, 2: +Z, 3: -X, 4: -Y, 5: -Z)', default=4)

    parser.add_argument('--mesh_face_count', type=int, help='Face count of downsampled final mesh', default=7000)
    args = parser.parse_args()

    try:
        mesh_object(
            args.splat_path,
            args.mesh_output_path,
            args.mesh_threshold,
            args.mesh_splatscale,
            args.mesh_grid_size,
            args.mesh_flatten_base,
            args.mesh_flatten_percentage,
            args.mesh_flatten_axis,
            args.mesh_face_count,
        )
        sys.exit(0)
    except Exception as exc:
        print(f"Error: {exc}", file=sys.stderr, flush=True)
        sys.exit(1)
