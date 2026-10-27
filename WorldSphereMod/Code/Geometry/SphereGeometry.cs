using System;

namespace WorldSphereMod.Geometry
{
    /// <summary>
    /// Equirectangular map contract. Integer coordinates are tile centers.
    /// Longitude repeats every width; latitude edges [-0.5,height-0.5] are
    /// the south/north poles. Only longitude is periodic. The sphere is
    /// centered at world origin; elevation is radial, in world units.
    /// </summary>
    public static class SphereGeometry
    {
        public readonly struct Point
        {
            public readonly double X, Y, Z;
            public Point(double x, double y, double z) { X = x; Y = y; Z = z; }
        }

        public static double WrapLongitude(double x, double width)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            return x - Math.Floor(x / width) * width;
        }

        public static double ClampLatitude(double y, double height)
        {
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            return Math.Max(-0.5, Math.Min(height - 0.5, y));
        }

        public static Point Forward(double x, double y, double elevation,
            double width, double height, double radius)
        {
            if (radius <= 0 || radius + elevation <= 0)
                throw new ArgumentOutOfRangeException(nameof(radius));
            double longitude = -2 * Math.PI * WrapLongitude(x, width) / width;
            double latitude = Math.PI * (ClampLatitude(y, height) + 0.5) / height - Math.PI / 2;
            double r = radius + elevation;
            double equator = r * Math.Cos(latitude);
            return new Point(equator * Math.Cos(longitude), r * Math.Sin(latitude),
                equator * Math.Sin(longitude));
        }

        /// <summary>Returns map X,Y and radial elevation. Longitude at a pole is canonically zero.</summary>
        public static Point Inverse(double x, double y, double z,
            double width, double height, double radius)
        {
            if (radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
            double length = Math.Sqrt(x * x + y * y + z * z);
            if (length <= 1e-12) throw new ArgumentException("The sphere center has no surface coordinate.");
            double horizontal = Math.Sqrt(x * x + z * z);
            double longitude = horizontal <= length * 1e-12 ? 0 : Math.Atan2(z, x);
            double latitude = Math.Atan2(y, horizontal);
            return new Point(WrapLongitude(-longitude * width / (2 * Math.PI), width),
                ClampLatitude((latitude / Math.PI + 0.5) * height - 0.5, height), length - radius);
        }

        /// <summary>Unit tangent pointing toward increasing map latitude, including at either pole.</summary>
        public static Point North(double x, double y, double width, double height)
        {
            double longitude = -2 * Math.PI * WrapLongitude(x, width) / width;
            double latitude = Math.PI * (ClampLatitude(y, height) + 0.5) / height - Math.PI / 2;
            return new Point(-Math.Sin(latitude) * Math.Cos(longitude), Math.Cos(latitude),
                -Math.Sin(latitude) * Math.Sin(longitude));
        }

        /// <summary>
        /// Minimum downward pitch from the tangent plane. At increasing altitude
        /// the globe subtends a smaller angle, so a fixed strategy-camera pitch
        /// would point over the horizon. Keep the aim within half its angular radius.
        /// </summary>
        public static double MinimumCameraPitch(double radius, double altitude)
        {
            if (radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
            double angularRadius = Math.Asin(radius / (radius + Math.Max(0, altitude)));
            return 90 - angularRadius * 90 / Math.PI;
        }

        /// <summary>Map-space movement takes the longitude seam shortcut, never a north/south shortcut.</summary>
        public static Point MoveTowards(Point current, Point target, double step, double width, double height)
        {
            double x = WrapLongitude(current.X, width);
            double y = Math.Max(0, Math.Min(height - 1, current.Y));
            double dx = WrapLongitude(target.X, width) - x;
            if (dx > width / 2) dx -= width;
            else if (dx < -width / 2) dx += width;
            double dy = Math.Max(0, Math.Min(height - 1, target.Y)) - y;
            double dz = target.Z - current.Z;
            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double fraction = length == 0 ? 0 : Math.Min(Math.Max(0, step) / length, 1);
            return new Point(WrapLongitude(x + dx * fraction, width), y + dy * fraction, current.Z + dz * fraction);
        }

        /// <summary>Nearest nonnegative intersection, with arbitrary-length ray direction.</summary>
        public static bool RayIntersection(Point origin, Point direction, double radius, out double distance)
        {
            distance = 0;
            double a = direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z;
            if (a <= 1e-24 || radius <= 0) return false;
            double b = origin.X * direction.X + origin.Y * direction.Y + origin.Z * direction.Z;
            double c = origin.X * origin.X + origin.Y * origin.Y + origin.Z * origin.Z - radius * radius;
            double discriminant = b * b - a * c;
            if (discriminant < 0) return false;
            double root = Math.Sqrt(discriminant);
            double near = (-b - root) / a;
            double far = (-b + root) / a;
            if (far < 0) return false;
            distance = near >= 0 ? near : far;
            return true;
        }
    }
}
