//! Small 2D geometry toolkit.

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Pt {
    pub x: f32,
    pub y: f32,
}

impl Pt {
    pub fn new(x: f32, y: f32) -> Self {
        Pt { x, y }
    }
    pub fn dist(self, o: Pt) -> f32 {
        ((self.x - o.x).powi(2) + (self.y - o.y).powi(2)).sqrt()
    }
    pub fn sub(self, o: Pt) -> Pt {
        Pt::new(self.x - o.x, self.y - o.y)
    }
    pub fn add(self, o: Pt) -> Pt {
        Pt::new(self.x + o.x, self.y + o.y)
    }
    pub fn scale(self, s: f32) -> Pt {
        Pt::new(self.x * s, self.y * s)
    }
    pub fn len(self) -> f32 {
        (self.x * self.x + self.y * self.y).sqrt()
    }
    pub fn dot(self, o: Pt) -> f32 {
        self.x * o.x + self.y * o.y
    }
}

/// Distance from `p` to segment `a`-`b`.
pub fn dist_to_segment(p: Pt, a: Pt, b: Pt) -> f32 {
    let ab = b.sub(a);
    let l2 = ab.dot(ab);
    if l2 <= f32::EPSILON {
        return p.dist(a);
    }
    let t = (p.sub(a).dot(ab) / l2).clamp(0.0, 1.0);
    p.dist(a.add(ab.scale(t)))
}

pub fn path_length(pts: &[Pt]) -> f32 {
    pts.windows(2).map(|w| w[0].dist(w[1])).sum()
}

/// Axis-aligned bounding box: (min, max).
pub fn bbox(pts: &[Pt]) -> (Pt, Pt) {
    let mut mn = Pt::new(f32::MAX, f32::MAX);
    let mut mx = Pt::new(f32::MIN, f32::MIN);
    for p in pts {
        mn.x = mn.x.min(p.x);
        mn.y = mn.y.min(p.y);
        mx.x = mx.x.max(p.x);
        mx.y = mx.y.max(p.y);
    }
    (mn, mx)
}

/// Resample a polyline to `n` points evenly spaced along its length.
pub fn resample(pts: &[Pt], n: usize) -> Vec<Pt> {
    if pts.len() < 2 || n < 2 {
        return pts.to_vec();
    }
    let total = path_length(pts);
    if total <= f32::EPSILON {
        return vec![pts[0]; n];
    }
    let step = total / (n - 1) as f32;
    let mut out = Vec::with_capacity(n);
    out.push(pts[0]);
    let mut acc = 0.0f32;
    let mut prev = pts[0];
    let mut i = 1;
    while i < pts.len() && out.len() < n {
        let cur = pts[i];
        let d = prev.dist(cur);
        if acc + d >= step && d > 0.0 {
            let t = (step - acc) / d;
            let q = prev.add(cur.sub(prev).scale(t));
            out.push(q);
            prev = q;
            acc = 0.0;
        } else {
            acc += d;
            prev = cur;
            i += 1;
        }
    }
    while out.len() < n {
        out.push(*pts.last().unwrap());
    }
    out
}

/// Ramer–Douglas–Peucker simplification (iterative, no recursion depth risk).
pub fn rdp(pts: &[Pt], eps: f32) -> Vec<Pt> {
    if pts.len() < 3 {
        return pts.to_vec();
    }
    let mut keep = vec![false; pts.len()];
    keep[0] = true;
    keep[pts.len() - 1] = true;
    let mut stack = vec![(0usize, pts.len() - 1)];
    while let Some((s, e)) = stack.pop() {
        if e <= s + 1 {
            continue;
        }
        let (mut idx, mut dmax) = (s, 0.0f32);
        for i in s + 1..e {
            let d = dist_to_segment(pts[i], pts[s], pts[e]);
            if d > dmax {
                dmax = d;
                idx = i;
            }
        }
        if dmax > eps {
            keep[idx] = true;
            stack.push((s, idx));
            stack.push((idx, e));
        }
    }
    pts.iter().zip(keep).filter(|(_, k)| *k).map(|(p, _)| *p).collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rdp_collapses_straight_line() {
        let pts: Vec<Pt> = (0..100).map(|i| Pt::new(i as f32, 0.01 * (i % 2) as f32)).collect();
        let s = rdp(&pts, 0.5);
        assert_eq!(s.len(), 2);
    }

    #[test]
    fn rdp_keeps_corner() {
        let mut pts: Vec<Pt> = (0..50).map(|i| Pt::new(i as f32, 0.0)).collect();
        pts.extend((1..50).map(|i| Pt::new(49.0, i as f32)));
        let s = rdp(&pts, 0.5);
        assert_eq!(s.len(), 3);
    }

    #[test]
    fn resample_count_and_spacing() {
        let pts = vec![Pt::new(0.0, 0.0), Pt::new(100.0, 0.0)];
        let r = resample(&pts, 11);
        assert_eq!(r.len(), 11);
        assert!((r[5].x - 50.0).abs() < 0.01);
    }
}
