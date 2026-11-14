//! InkBoard core: geometry algorithms shared with the Android app over JNI.
//!
//! - `recognize`: turns a hand-drawn stroke into a clean shape
//!   (line, triangle, rectangle/quad, polygon, ellipse/circle).
//! - `simplify`: Ramer–Douglas–Peucker polyline simplification.

pub mod geom;
pub mod recognize;

pub use recognize::{recognize, Shape};

mod ffi {
    use super::geom::{rdp, Pt};
    use super::recognize::{recognize, Shape};
    use jni::objects::{JClass, JFloatArray};
    use jni::sys::{jboolean, jfloat, jfloatArray};
    use jni::JNIEnv;

    fn read_points(env: &mut JNIEnv, arr: &JFloatArray) -> Option<Vec<Pt>> {
        let len = env.get_array_length(arr).ok()? as usize;
        let mut buf = vec![0f32; len];
        env.get_float_array_region(arr, 0, &mut buf).ok()?;
        Some(buf.chunks_exact(2).map(|c| Pt::new(c[0], c[1])).collect())
    }

    fn write_floats(env: &mut JNIEnv, data: &[f32]) -> jfloatArray {
        match env.new_float_array(data.len() as i32) {
            Ok(out) => {
                if env.set_float_array_region(&out, 0, data).is_err() {
                    return std::ptr::null_mut();
                }
                out.into_raw()
            }
            Err(_) => std::ptr::null_mut(),
        }
    }

    /// Output encoding: [] = no shape; [1, x0,y0,x1,y1] = line;
    /// [2, n, x0,y0, ...] = closed polygon; [3, cx,cy,rx,ry,angle] = ellipse.
    pub fn encode(shape: &Option<Shape>) -> Vec<f32> {
        match shape {
            None => vec![],
            Some(Shape::Line(a, b)) => vec![1.0, a.x, a.y, b.x, b.y],
            Some(Shape::Polygon(pts)) => {
                let mut v = vec![2.0, pts.len() as f32];
                for p in pts {
                    v.push(p.x);
                    v.push(p.y);
                }
                v
            }
            Some(Shape::Ellipse { cx, cy, rx, ry, angle }) => vec![3.0, *cx, *cy, *rx, *ry, *angle],
        }
    }

    #[no_mangle]
    pub extern "system" fn Java_com_inkboard_core_InkCore_nativeRecognize<'l>(
        mut env: JNIEnv<'l>,
        _class: JClass<'l>,
        pts: JFloatArray<'l>,
        perfect: jboolean,
    ) -> jfloatArray {
        let Some(points) = read_points(&mut env, &pts) else { return std::ptr::null_mut() };
        let result = std::panic::catch_unwind(|| encode(&recognize(&points, perfect != 0)));
        let data = result.unwrap_or_default();
        write_floats(&mut env, &data)
    }

    #[no_mangle]
    pub extern "system" fn Java_com_inkboard_core_InkCore_nativeSimplify<'l>(
        mut env: JNIEnv<'l>,
        _class: JClass<'l>,
        pts: JFloatArray<'l>,
        epsilon: jfloat,
    ) -> jfloatArray {
        let Some(points) = read_points(&mut env, &pts) else { return std::ptr::null_mut() };
        let result = std::panic::catch_unwind(|| {
            rdp(&points, epsilon).iter().flat_map(|p| [p.x, p.y]).collect::<Vec<f32>>()
        });
        let data = result.unwrap_or_default();
        write_floats(&mut env, &data)
    }
}

pub use ffi::encode;
