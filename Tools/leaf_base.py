"""Where a leaf sprite's leaf meets the plant: the tip of its petiole (or, for a blade drawn without one, the
end of the blade it grew from). Used by Tools/attach_plant_parts.py.

A petiole is the one extremity of the leaf outline that stays thin for a while: around its tip only a sliver of
the disc is leaf, while around the blade's own tip (or a lobe) a wedge of it is. So of the outline's extreme
points (its convex hull), the base is the one with the least leaf inside a disc of 12 % of the leaf's length.
Blades without a petiole (the Farm crops' cards) return the hull point at the wider end of the blade's long axis:
leaves taper to their tip, and their base is the broad end.
"""
import numpy as np
from PIL import Image
from scipy.spatial import ConvexHull

ALPHA_CUT = 0.45


def alpha_of(png):
    """Alpha in UV orientation: row 0 is v = 0 (the bottom of the image)."""
    return np.asarray(Image.open(png).convert("RGBA"))[::-1, :, 3].astype(np.float32) / 255.0


def base_in_cell(alpha, x0, y0, x1, y1):
    """(u, v, kind) of the base of the leaf drawn in alpha[y0:y1, x0:x1]; kind is "petiole" or "blade"."""
    cell = alpha[y0:y1, x0:x1] > ALPHA_CUT
    ys, xs = np.nonzero(cell)
    if len(xs) < 20:
        return None
    points = np.stack([xs, ys], 1).astype(np.float64)
    centre = points.mean(0)
    _, vectors = np.linalg.eigh(np.cov((points - centre).T))
    length = float(np.ptp((points - centre) @ vectors[:, 1]))
    hull = points[ConvexHull(points).vertices]
    radius = 0.12 * length
    disc = np.pi * radius * radius
    fill = []
    for h in hull:
        inside = np.sum(np.sum((points - h) ** 2, 1) <= radius * radius)
        fill.append(inside / disc)
    fill = np.array(fill)
    # Only true extremities: hull points far out along the leaf (not the flanks of a broad blade).
    reach = np.linalg.norm(hull - centre, axis=1)
    far = reach >= 0.35 * length
    candidates = np.where(far)[0]
    best = candidates[np.argmin(fill[candidates])]
    if fill[best] < 0.16:
        kind, tip = "petiole", hull[best]
    else:
        # No stalk drawn: the broad end of the long axis.
        axis = vectors[:, 1]
        along = (points - centre) @ axis
        ends = []
        for sign in (1.0, -1.0):
            band = points[sign * along > 0.30 * length]
            ends.append((len(band), sign))
        sign = max(ends)[1]
        end = points[np.argmax(sign * along)]
        kind, tip = "blade", end
    h, w = alpha.shape
    return ((x0 + tip[0] + 0.5) / w, (y0 + tip[1] + 0.5) / h, kind)


def sprite_base(png):
    alpha = alpha_of(png)
    return base_in_cell(alpha, 0, 0, alpha.shape[1], alpha.shape[0])