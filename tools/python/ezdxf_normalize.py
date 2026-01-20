#!/usr/bin/env python3
"""
ezdxf_normalize.py - improved

Usage:
  python ezdxf_normalize.py input.dxf output.json [--approx-segs N] [--explode-inserts] [--verbose]

Features:
 - Uses ezdxf.recover to read many DXF versions.
 - Emits polylines (approximated arcs/circles/ellipses/splines) and separate circle/arc records.
 - When --explode-inserts is present, expands BLOCK INSERT entities (applies scale/rotation/translation).
 - Writes verbose diagnostics to stdout/stderr (EzDxfWrapper captures these into a temp log).
"""
import sys
import json
import math
import os
import argparse

try:
    import ezdxf
    from ezdxf import recover
except Exception as ex:
    print("ERROR: missing ezdxf module. Install with: pip install ezdxf", file=sys.stderr)
    sys.exit(2)


def rot_point(x, y, ang_deg):
    ang = math.radians(ang_deg)
    ca = math.cos(ang)
    sa = math.sin(ang)
    rx = x * ca - y * sa
    ry = x * sa + y * ca
    return rx, ry


def transform_point(pt, insert, sx, sy, rotation):
    # pt: (x,y)
    # scale (sx, sy), rotate rotation degrees, then translate by insert (ix,iy)
    x = pt[0] * sx
    y = pt[1] * sy
    rx, ry = rot_point(x, y, rotation)
    return [rx + insert[0], ry + insert[1]]


def sample_arc(cx, cy, r, start_deg, end_deg, segments=36):
    start = math.radians(start_deg)
    end = math.radians(end_deg)
    if end < start:
        end += 2 * math.pi
    pts = []
    for i in range(segments + 1):
        t = start + (end - start) * (i / segments)
        pts.append([cx + r * math.cos(t), cy + r * math.sin(t)])
    return pts


def sample_circle(cx, cy, r, segments=48):
    pts = []
    for i in range(segments):
        t = 2 * math.pi * i / segments
        pts.append([cx + r * math.cos(t), cy + r * math.sin(t)])
    if len(pts) > 0:
        pts.append(pts[0])
    return pts


def spline_to_poly(spline, segments=48):
    # Best-effort sampling: prefer fit_points, then control points, otherwise sample via spline.approximation()
    try:
        if hasattr(spline, "fit_points") and spline.fit_points:
            pts = [list(map(float, p)) for p in spline.fit_points]
            return pts
        # ezdxf 1.4 provides spline.eval or spline.control_points; fallback simple sampling of control points
        control = [list(p) for p in getattr(spline, "control_points", [])]
        if control:
            pts = []
            for i in range(segments + 1):
                idx = int(i / segments * (len(control) - 1))
                pts.append(control[idx])
            return pts
    except Exception as ex:
        print("Warning: spline_to_poly failed: " + str(ex), file=sys.stderr)
    return []


def process_entity(e, out, approx_segs, explode_blocks, verbose, doc, parent_transform=None):
    t = e.dxftype()
    if verbose:
        print(f"Processing entity: {t}", file=sys.stderr)

    if t == "LINE":
        try:
            start = [float(e.dxf.start[0]), float(e.dxf.start[1])]
            end = [float(e.dxf.end[0]), float(e.dxf.end[1])]
            if parent_transform:
                start = transform_point(start, *parent_transform)
                end = transform_point(end, *parent_transform)
            out.append({"type": "polyline", "points": [start, end]})
        except Exception as ex:
            print("Warning: LINE processing error: " + str(ex), file=sys.stderr)

    elif t == "LWPOLYLINE":
        try:
            pts_raw = list(e.get_points())
            pts = [[float(x), float(y)] for (x, y, *rest) in pts_raw]
            if parent_transform:
                pts = [transform_point(p, *parent_transform) for p in pts]
            out.append({"type": "polyline", "points": pts})
        except Exception as ex:
            print("Warning: LWPOLYLINE error: " + str(ex), file=sys.stderr)

    elif t == "POLYLINE":
        try:
            pts = []
            for v in e.vertices():
                pts.append([float(v.dxf.x), float(v.dxf.y)])
            if parent_transform:
                pts = [transform_point(p, *parent_transform) for p in pts]
            if pts:
                out.append({"type": "polyline", "points": pts})
        except Exception as ex:
            print("Warning: POLYLINE error: " + str(ex), file=sys.stderr)

    elif t == "CIRCLE":
        try:
            cx = float(e.dxf.center[0]); cy = float(e.dxf.center[1]); r = float(e.dxf.radius)
            if parent_transform:
                # approximate by transforming sampled points
                pts = sample_circle(0, 0, r, segments=approx_segs)
                pts = [transform_point([p[0] + cx, p[1] + cy], *parent_transform) for p in pts]
                out.append({"type": "polyline", "points": pts})
            else:
                out.append({"type": "polyline", "points": sample_circle(cx, cy, r, segments=approx_segs)})
            out.append({"type": "circle", "cx": cx, "cy": cy, "r": r})
        except Exception as ex:
            print("Warning: CIRCLE error: " + str(ex), file=sys.stderr)

    elif t == "ARC":
        try:
            cx = float(e.dxf.center[0]); cy = float(e.dxf.center[1]); r = float(e.dxf.radius)
            sa = float(e.dxf.start_angle); ea = float(e.dxf.end_angle)
            if parent_transform:
                pts = sample_arc(0, 0, r, sa, ea, segments=approx_segs)
                pts = [transform_point([p[0] + cx, p[1] + cy], *parent_transform) for p in pts]
                out.append({"type": "polyline", "points": pts})
            else:
                out.append({"type": "polyline", "points": sample_arc(cx, cy, r, sa, ea, segments=approx_segs)})
            out.append({"type": "arc", "cx": cx, "cy": cy, "r": r, "start": sa, "end": ea})
        except Exception as ex:
            print("Warning: ARC error: " + str(ex), file=sys.stderr)

    elif t == "ELLIPSE":
        try:
            cx = float(e.dxf.center[0]); cy = float(e.dxf.center[1])
            maj = e.dxf.major_axis
            rx = math.hypot(maj[0], maj[1])
            ratio = float(e.dxf.ratio)
            ry = rx * ratio
            pts = []
            for i in range(approx_segs):
                a = 2 * math.pi * i / approx_segs
                pts.append([cx + rx * math.cos(a), cy + ry * math.sin(a)])
            if parent_transform:
                pts = [transform_point(p, *parent_transform) for p in pts]
            out.append({"type": "polyline", "points": pts})
        except Exception as ex:
            print("Warning: ELLIPSE error: " + str(ex), file=sys.stderr)

    elif t == "SPLINE":
        try:
            pts = spline_to_poly(e, segments=approx_segs)
            if parent_transform:
                pts = [transform_point(p, *parent_transform) for p in pts]
            if pts:
                out.append({"type": "polyline", "points": pts})
        except Exception as ex:
            print("Warning: SPLINE error: " + str(ex), file=sys.stderr)

    elif t == "INSERT" and explode_blocks:
        # Expand the block definition (INSERT)
        try:
            name = e.dxf.name
            insert_pt = [float(e.dxf.insert[0]), float(e.dxf.insert[1])]
            sx = float(getattr(e.dxf, "xscale", 1.0))
            sy = float(getattr(e.dxf, "yscale", 1.0))
            rotation = float(getattr(e.dxf, "rotation", 0.0))
            if verbose:
                print(f"Expanding INSERT '{name}' at {insert_pt} sx={sx} sy={sy} rot={rotation}", file=sys.stderr)
            # find block and iterate block entities
            blk = doc.blocks.get(name)
            if blk is None:
                print(f"Warning: Block '{name}' not found.", file=sys.stderr)
            else:
                for be in blk:
                    # process each entity in block with transform
                    process_entity(be, out, approx_segs, explode_blocks, verbose, doc, parent_transform=(insert_pt, sx, sy, rotation))
        except Exception as ex:
            print("Warning: INSERT expansion failed: " + str(ex), file=sys.stderr)

    else:
        # ignore unsupported or non-geometry types (HATCH, TEXT, ATTRIB, etc.)
        if verbose:
            print(f"Ignored entity type: {t}", file=sys.stderr)


def main():
    parser = argparse.ArgumentParser(description="Normalize DXF using ezdxf and export JSON geometry.")
    parser.add_argument("input", help="Input DXF file")
    parser.add_argument("output", help="Output JSON file")
    parser.add_argument("--approx-segs", type=int, default=36, help="Segments for arc/circle approximation")
    parser.add_argument("--explode-inserts", action="store_true", help="Explode INSERT/BLOCKs into geometry")
    parser.add_argument("--verbose", action="store_true", help="Verbose output to stderr")
    args = parser.parse_args()

    if not os.path.exists(args.input):
        print("Input not found: " + args.input, file=sys.stderr)
        sys.exit(3)

    try:
        doc, auditor = recover.readfile(args.input)
        if doc is None:
            print("ezdxf failed to read file. Auditor: " + str(auditor), file=sys.stderr)
            sys.exit(4)
    except Exception as ex:
        print("ezdxf failed: " + str(ex), file=sys.stderr)
        sys.exit(5)

    modelspace = doc.modelspace()
    out = []

    for e in modelspace:
        try:
            process_entity(e, out, args.approx_segs, args.explode_inserts, args.verbose, doc)
        except Exception as ex:
            print(f"Warning: failed to process top-level entity {e.dxftype()}: {ex}", file=sys.stderr)

    # write JSON
    try:
        with open(args.output, "w", encoding="utf-8") as f:
            json.dump(out, f, indent=2)
        print(args.output)
        sys.exit(0)
    except Exception as ex:
        print("Failed to write JSON: " + str(ex), file=sys.stderr)
        sys.exit(6)


if __name__ == "__main__":
    main()