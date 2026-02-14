using System;

namespace UnityEngine
{
    public struct Quaternion : IEquatable<Quaternion>
    {
        public float x, y, z, w;

        public Quaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public static Quaternion identity => new(0, 0, 0, 1);

        public Vector3 eulerAngles
        {
            get
            {
                float sinr_cosp = 2 * (w * x + y * z);
                float cosr_cosp = 1 - 2 * (x * x + y * y);
                float roll = MathF.Atan2(sinr_cosp, cosr_cosp);

                float sinp = 2 * (w * y - z * x);
                float pitch = MathF.Abs(sinp) >= 1
                    ? MathF.CopySign(MathF.PI / 2, sinp)
                    : MathF.Asin(sinp);

                float siny_cosp = 2 * (w * z + x * y);
                float cosy_cosp = 1 - 2 * (y * y + z * z);
                float yaw = MathF.Atan2(siny_cosp, cosy_cosp);

                const float rad2deg = 180f / MathF.PI;
                return new Vector3(roll * rad2deg, pitch * rad2deg, yaw * rad2deg);
            }
        }

        public static Quaternion Euler(float x, float y, float z)
        {
            const float deg2rad = MathF.PI / 180f;
            x *= deg2rad * 0.5f;
            y *= deg2rad * 0.5f;
            z *= deg2rad * 0.5f;

            float cx = MathF.Cos(x), sx = MathF.Sin(x);
            float cy = MathF.Cos(y), sy = MathF.Sin(y);
            float cz = MathF.Cos(z), sz = MathF.Sin(z);

            return new Quaternion(
                sx * cy * cz - cx * sy * sz,
                cx * sy * cz + sx * cy * sz,
                cx * cy * sz - sx * sy * cz,
                cx * cy * cz + sx * sy * sz
            );
        }

        public static Quaternion Euler(Vector3 euler) => Euler(euler.x, euler.y, euler.z);

        public static Quaternion AngleAxis(float angle, Vector3 axis)
        {
            float rad = angle * MathF.PI / 180f * 0.5f;
            axis = axis.normalized;
            float s = MathF.Sin(rad);
            return new Quaternion(axis.x * s, axis.y * s, axis.z * s, MathF.Cos(rad));
        }

        public static Quaternion operator *(Quaternion a, Quaternion b) => new(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z
        );

        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            float qx2 = q.x * 2f, qy2 = q.y * 2f, qz2 = q.z * 2f;
            float qxx = q.x * qx2, qyy = q.y * qy2, qzz = q.z * qz2;
            float qxy = q.x * qy2, qxz = q.x * qz2, qyz = q.y * qz2;
            float qwx = q.w * qx2, qwy = q.w * qy2, qwz = q.w * qz2;

            return new Vector3(
                (1f - (qyy + qzz)) * v.x + (qxy - qwz) * v.y + (qxz + qwy) * v.z,
                (qxy + qwz) * v.x + (1f - (qxx + qzz)) * v.y + (qyz - qwx) * v.z,
                (qxz - qwy) * v.x + (qyz + qwx) * v.y + (1f - (qxx + qyy)) * v.z
            );
        }

        public static bool operator ==(Quaternion a, Quaternion b) =>
            MathF.Abs(Dot(a, b)) > 1f - 1e-6f;
        public static bool operator !=(Quaternion a, Quaternion b) => !(a == b);

        public static float Dot(Quaternion a, Quaternion b) =>
            a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;

        public bool Equals(Quaternion other) => this == other;
        public override bool Equals(object? obj) => obj is Quaternion q && this == q;
        public override int GetHashCode() => HashCode.Combine(x, y, z, w);
        public override string ToString() => $"({x:F4}, {y:F4}, {z:F4}, {w:F4})";
    }
}
