//! Hand-drawn stroke → clean shape recognition.

use crate::geom::{bbox, dist_to_segment, path_length, rdp, resample, Pt};
use std::f32::consts::PI;

#[derive(Clone, Debug, PartialEq)]
pub enum Shape {
    Line(Pt, Pt),
    /// Closed polygon (vertices in drawing order, not repeated at the end).
    Polygon(Vec<Pt>),
    Ellipse { cx: f32, cy: f32, rx: f32, ry: f32, angle: f32 },
}

const SAMPLES: usize = 96;

/// Recognize a stroke. `perfect` forces regular forms (square, circle).
pub fn recognize(raw: &[Pt], perfect: bool) -> Option<Shape> {
    if raw.len() < 3 {
        return None;
    }
    let (mn, mx) = bbox(raw);
    let diag = mn.dist(mx);
    if diag < 8.0 {
        return None;
    }
    let pts = resample(raw, SAMPLES);
    let start = pts[0];
    let end = *pts.last().unwrap();
    let length = path_length(&pts);
    let gap = start.dist(end);

    // Open strokes: straight line only.
    if gap > 0.22 * diag.max(length * 0.25) {
        return recognize_line(&pts, start, end);
    }
    let ellipse = fit_ellipse(&pts);
    let poly = fit_polygon(&pts, diag);

    let e_err = ellipse.as_ref().map(|(_, e)| *e).unwrap_or(f32::MAX);
    let p_err = poly.as_ref().map(|(v, e)| (v.len(), *e));

    // Prefer polygon when it has few vertices and fits clearly better.
    if let Some((n, perr)) = p_err {
        if (3..=4).contains(&n) && perr < 0.035 && perr < e_err * 0.85 {
            let verts = poly.unwrap().0;
            return Some(finish_polygon(verts, perfect));
        }
    }
    if e_err < 0.09 {
        let (mut sh, _) = ellipse.unwrap();
        if let Shape::Ellipse { ref mut rx, ref mut ry, ref mut angle, .. } = sh {
            let ratio = rx.min(*ry) / rx.max(*ry);
            if perfect || ratio > 0.86 {
                let r = (*rx + *ry) / 2.0;
                *rx = r;
                *ry = r;
                *angle = 0.0;
            } else {
                *angle = snap_axis_angle(*angle, rx, ry);
            }
        }
        return Some(sh);
    }
    if let Some((n, perr)) = p_err {
        if (3..=6).contains(&n) && perr < 0.05 {
            return Some(finish_polygon(poly.unwrap().0, perfect));
        }
    }
    None
}

fn recognize_line(pts: &[Pt], start: Pt, end: Pt) -> Option<Shape> {
    let chord = start.dist(end);
    if chord < 8.0 {
        return None;
    }
    let max_dev = pts.iter().map(|p| dist_to_segment(*p, start, end)).fold(0.0f32, f32::max);
    if max_dev <= 0.06 * chord + 2.0 {
        Some(Shape::Line(start, end))
    } else {
        None
    }
}

/// PCA ellipse fit. Returns shape and mean normalized radial error.
fn fit_ellipse(pts: &[Pt]) -> Option<(Shape, f32)> {
    let n = pts.len() as f32;
    let cx = pts.iter().map(|p| p.x).sum::<f32>() / n;
    let cy = pts.iter().map(|p| p.y).sum::<f32>() / n;
    let (mut sxx, mut syy, mut sxy) = (0f32, 0f32, 0f32);
    for p in pts {
        let dx = p.x - cx;
        let dy = p.y - cy;
        sxx += dx * dx;
        syy += dy * dy;
        sxy += dx * dy;
    }
    let angle = 0.5 * (2.0 * sxy).atan2(sxx - syy);
    let (c, s) = (angle.cos(), angle.sin());
    let (mut umin, mut umax, mut vmin, mut vmax) = (f32::MAX, f32::MIN, f32::MAX, f32::MIN);
    for p in pts {
        let dx = p.x - cx;
        let dy = p.y - cy;
        let u = dx * c + dy * s;
        let v = -dx * s + dy * c;
        umin = umin.min(u);
        umax = umax.max(u);
        vmin = vmin.min(v);
        vmax = vmax.max(v);
    }
    let rx = (umax - umin) / 2.0;
    let ry = (vmax - vmin) / 2.0;
    if rx < 3.0 || ry < 3.0 {
        return None;
    }
    // Recentre on the bounding box in the rotated frame.
    let uc = (umax + umin) / 2.0;
    let vc = (vmax + vmin) / 2.0;
    let ccx = cx + uc * c - vc * s;
    let ccy = cy + uc * s + vc * c;
    let mut err = 0f32;
    for p in pts {
        let dx = p.x - ccx;
        let dy = p.y - ccy;
        let u = dx * c + dy * s;
        let v = -dx * s + dy * c;
        let r = ((u / rx).powi(2) + (v / ry).powi(2)).sqrt();
        err += (r - 1.0).abs();
    }
    err /= n;
    Some((Shape::Ellipse { cx: ccx, cy: ccy, rx, ry, angle }, err))
}

/// Polygon fit via RDP on the closed stroke. Returns vertices and the mean
/// distance of samples to the polygon, normalized by the bbox diagonal.
fn fit_polygon(pts: &[Pt], diag: f32) -> Option<(Vec<Pt>, f32)> {
    let mut closed = pts.to_vec();
    closed.push(pts[0]);
    let mut verts = rdp(&closed, 0.07 * diag);
    verts.pop(); // last == first (closing point)
    // The start point is often mid-edge: drop vertices whose interior angle is ~180°.
    loop {
        if verts.len() < 3 {
            return None;
        }
        let n = verts.len();
        let mut removed = false;
        for i in 0..n {
            let a = verts[(i + n - 1) % n];
            let b = verts[i];
            let c = verts[(i + 1) % n];
            let ang = turn_angle(a, b, c);
            if ang.abs() < 0.35 {
                verts.remove(i);
                removed = true;
                break;
            }
        }
        if !removed {
            break;
        }
    }
    // Merge vertices that are too close together.
    let min_edge = 0.08 * diag;
    let mut merged: Vec<Pt> = Vec::new();
    for v in verts {
        if merged.last().map(|l: &Pt| l.dist(v) < min_edge).unwrap_or(false) {
            continue;
        }
        merged.push(v);
    }
    if merged.len() > 3 && merged[0].dist(*merged.last().unwrap()) < min_edge {
        merged.pop();
    }
    if merged.len() < 3 {
        return None;
    }
    let n = merged.len();
    let mut err = 0f32;
    for p in pts {
        let mut best = f32::MAX;
        for i in 0..n {
            best = best.min(dist_to_segment(*p, merged[i], merged[(i + 1) % n]));
        }
        err += best;
    }
    err /= pts.len() as f32 * diag;
    Some((merged, err))
}

/// Signed turning angle at b (radians, 0 = straight).
fn turn_angle(a: Pt, b: Pt, c: Pt) -> f32 {
    let u = b.sub(a);
    let v = c.sub(b);
    let cross = u.x * v.y - u.y * v.x;
    cross.atan2(u.dot(v))
}

fn interior_angle(a: Pt, b: Pt, c: Pt) -> f32 {
    let u = a.sub(b);
    let v = c.sub(b);
    let d = (u.len() * v.len()).max(1e-6);
    (u.dot(v) / d).clamp(-1.0, 1.0).acos()
}

fn finish_polygon(verts: Vec<Pt>, perfect: bool) -> Shape {
    if verts.len() == 4 {
        let n = 4;
        let right = (0..n).all(|i| {
            let ang = interior_angle(verts[(i + n - 1) % n], verts[i], verts[(i + 1) % n]);
            (ang - PI / 2.0).abs() < 0.30
        });
        if right {
            return Shape::Polygon(rectify(&verts, perfect));
        }
    }
    Shape::Polygon(verts)
}

/// Fit a (possibly rotated) rectangle to 4 roughly-right-angled vertices.
fn rectify(v: &[Pt], perfect: bool) -> Vec<Pt> {
    // Orientation from the longest edge.
    let mut best = (0usize, 0f32);
    for i in 0..4 {
        let l = v[i].dist(v[(i + 1) % 4]);
        if l > best.1 {
            best = (i, l);
        }
    }
    let e = v[(best.0 + 1) % 4].sub(v[best.0]);
    let mut angle = e.y.atan2(e.x);
    // Snap to axis if close.
    let snapped = (angle / (PI / 2.0)).round() * (PI / 2.0);
    if (angle - snapped).abs() < 0.14 {
        angle = snapped;
    }
    let (c, s) = (angle.cos(), angle.sin());
    let cx = v.iter().map(|p| p.x).sum::<f32>() / 4.0;
    let cy = v.iter().map(|p| p.y).sum::<f32>() / 4.0;
    let (mut umin, mut umax, mut vmin, mut vmax) = (f32::MAX, f32::MIN, f32::MAX, f32::MIN);
    for p in v {
        let dx = p.x - cx;
        let dy = p.y - cy;
        let u = dx * c + dy * s;
        let w = -dx * s + dy * c;
        umin = umin.min(u);
        umax = umax.max(u);
        vmin = vmin.min(w);
        vmax = vmax.max(w);
    }
    // Average opposite edges rather than using extremes.
    let hu0 = (umax - umin) / 2.0;
    let hv0 = (vmax - vmin) / 2.0;
    let (mut hu, mut hv) = (hu0 * 0.92, hv0 * 0.92);
    if perfect {
        let h = (hu + hv) / 2.0;
        hu = h;
        hv = h;
    }
    let uc = (umax + umin) / 2.0;
    let vc = (vmax + vmin) / 2.0;
    let corners = [(-hu, -hv), (hu, -hv), (hu, hv), (-hu, hv)];
    corners
        .iter()
        .map(|(u, w)| {
            let uu = uc + u;
            let ww = vc + w;
            Pt::new(cx + uu * c - ww * s, cy + uu * s + ww * c)
        })
        .collect()
}

fn snap_axis_angle(angle: f32, rx: &mut f32, ry: &mut f32) -> f32 {
    let q = (angle / (PI / 2.0)).round();
    let snapped = q * (PI / 2.0);
    if (angle - snapped).abs() < 0.17 {
        if (q as i32).rem_euclid(2) == 1 {
            std::mem::swap(rx, ry);
        }
        0.0
    } else {
        angle
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn jitter(i: usize) -> f32 {
        // Deterministic pseudo-noise in [-1, 1].
        let x = (i as f32 * 12.9898).sin() * 43758.547;
        (x - x.floor()) * 2.0 - 1.0
    }

    fn circle(cx: f32, cy: f32, rx: f32, ry: f32, n: usize, noise: f32) -> Vec<Pt> {
        (0..=n)
            .map(|i| {
                let t = i as f32 / n as f32 * 2.0 * PI * 1.03;
                Pt::new(cx + rx * t.cos() + noise * jitter(i), cy + ry * t.sin() + noise * jitter(i + 7))
            })
            .collect()
    }

    fn polyline(corners: &[Pt], per_edge: usize, noise: f32, close: bool) -> Vec<Pt> {
        let mut out = vec![];
        let n = corners.len();
        let edges = if close { n } else { n - 1 };
        let mut k = 0;
        for i in 0..edges {
            let a = corners[i];
            let b = corners[(i + 1) % n];
            for j in 0..per_edge {
                let t = j as f32 / per_edge as f32;
                out.push(Pt::new(a.x + (b.x - a.x) * t + noise * jitter(k), a.y + (b.y - a.y) * t + noise * jitter(k + 3)));
                k += 1;
            }
        }
        if close {
            out.push(Pt::new(corners[0].x + 2.0, corners[0].y + 1.0));
        } else {
            out.push(*corners.last().unwrap());
        }
        out
    }

    #[test]
    fn detects_line() {
        let pts = polyline(&[Pt::new(0.0, 0.0), Pt::new(300.0, 120.0)], 60, 2.0, false);
        assert!(matches!(recognize(&pts, false), Some(Shape::Line(_, _))));
    }

    #[test]
    fn rejects_scribble() {
        let pts: Vec<Pt> = (0..200)
            .map(|i| Pt::new(i as f32 * 2.0, ((i as f32) * 0.7).sin() * 40.0))
            .collect();
        assert_eq!(recognize(&pts, false), None);
    }

    #[test]
    fn detects_circle() {
        let pts = circle(100.0, 100.0, 80.0, 78.0, 120, 2.0);
        match recognize(&pts, false) {
            Some(Shape::Ellipse { rx, ry, .. }) => {
                assert!((rx - ry).abs() < 0.01, "circle should be regular");
                assert!((rx - 80.0).abs() < 8.0);
            }
            other => panic!("expected ellipse, got {other:?}"),
        }
    }

    #[test]
    fn detects_ellipse_axis_aligned() {
        let pts = circle(0.0, 0.0, 150.0, 60.0, 120, 2.0);
        match recognize(&pts, false) {
            Some(Shape::Ellipse { rx, ry, angle, .. }) => {
                assert!(angle.abs() < 1e-3);
                assert!(rx > ry * 2.0);
            }
            other => panic!("expected ellipse, got {other:?}"),
        }
    }

    #[test]
    fn detects_rectangle() {
        let c = [Pt::new(0.0, 0.0), Pt::new(300.0, 4.0), Pt::new(296.0, 180.0), Pt::new(-3.0, 176.0)];
        let pts = polyline(&c, 40, 2.0, true);
        match recognize(&pts, false) {
            Some(Shape::Polygon(v)) => {
                assert_eq!(v.len(), 4);
                // axis-snapped: two distinct y values
                let ys: Vec<i32> = v.iter().map(|p| p.y.round() as i32).collect();
                assert!(ys[0] == ys[1] || ys[1] == ys[2]);
            }
            other => panic!("expected rectangle, got {other:?}"),
        }
    }

    #[test]
    fn perfect_rectangle_is_square() {
        let c = [Pt::new(0.0, 0.0), Pt::new(200.0, 0.0), Pt::new(200.0, 160.0), Pt::new(0.0, 160.0)];
        let pts = polyline(&c, 40, 1.0, true);
        match recognize(&pts, true) {
            Some(Shape::Polygon(v)) => {
                let w = v[0].dist(v[1]);
                let h = v[1].dist(v[2]);
                assert!((w - h).abs() < 0.5);
            }
            other => panic!("expected square, got {other:?}"),
        }
    }

    #[test]
    fn detects_triangle() {
        let c = [Pt::new(0.0, 200.0), Pt::new(120.0, 0.0), Pt::new(240.0, 200.0)];
        let pts = polyline(&c, 50, 2.0, true);
        match recognize(&pts, false) {
            Some(Shape::Polygon(v)) => assert_eq!(v.len(), 3),
            other => panic!("expected triangle, got {other:?}"),
        }
    }

    #[test]
    fn detects_diamond() {
        let c = [Pt::new(100.0, 0.0), Pt::new(200.0, 100.0), Pt::new(100.0, 200.0), Pt::new(0.0, 100.0)];
        let pts = polyline(&c, 40, 1.5, true);
        match recognize(&pts, false) {
            Some(Shape::Polygon(v)) => assert_eq!(v.len(), 4),
            other => panic!("expected diamond, got {other:?}"),
        }
    }

    #[test]
    fn tiny_input_is_ignored() {
        assert_eq!(recognize(&[Pt::new(0.0, 0.0), Pt::new(1.0, 1.0), Pt::new(2.0, 2.0)], false), None);
    }

    #[test]
    fn encode_formats() {
        use crate::encode;
        assert_eq!(encode(&None), Vec::<f32>::new());
        assert_eq!(encode(&Some(Shape::Line(Pt::new(1.0, 2.0), Pt::new(3.0, 4.0)))), vec![1.0, 1.0, 2.0, 3.0, 4.0]);
        let e = encode(&Some(Shape::Polygon(vec![Pt::new(0.0, 0.0), Pt::new(1.0, 0.0), Pt::new(0.0, 1.0)])));
        assert_eq!(e[0], 2.0);
        assert_eq!(e[1], 3.0);
        assert_eq!(e.len(), 8);
    }
}
