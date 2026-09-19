namespace Kern.PlanetBaker;

public static class PlanetMath
{
    public const double CONTINENT_SCALE = 3.0;
    public const double WARP_STRENGTH = 0.50;
    public const double RIDGE_SCALE = 11.0;
    public const double MOUNTAIN_HEIGHT = 0.28;
    public const double DETAIL_SCALE = 140.0;
    public const double DETAIL_STRENGTH = 0.20;
    public const double GRAIN_SCALE = 390.0;
    public const double GRAIN_STRENGTH = 0.014;
    public const double ROUGHNESS_GRAIN = 0.13;
    public const double CRACK_SCALE = 6.0;
    public const int CRACK_OCTAVES = 2;
    public const double CRACK_THRESHOLD = 0.845;
    public const double CRACK_REGION_SCALE = 1.6;
    public const double CRACK_REGION_THRESHOLD = 0.12;
    public const double CLOUD_SCALE = 2.6;
    public const double CLOUD_WARP = 0.55;
    public const double CLOUD_BANDS = 3.5;
    public const double CLOUD_BAND_STRENGTH = 0.18;
    public const double CLOUD_COVERAGE_BIAS = -0.04;
    public const double CRATER_SCALE_MAJOR = 7.0;
    public const double CRATER_DENSITY_MAJOR = 0.30;
    public const double CRATER_DEPTH_MAJOR = 0.070;
    public const double CRATER_SCALE_MINOR = 21.0;
    public const double CRATER_DENSITY_MINOR = 0.38;
    public const double CRATER_DEPTH_MINOR = 0.022;
    public const double BASIN_LEVEL = 0.42;
    public const double PEAK_LEVEL = 0.80;
    public const double NORMAL_SPAN = 1.15;
    public const double BASALT_R = 0.088;
    public const double BASALT_G = 0.076;
    public const double BASALT_B = 0.064;
    public const double REGOLITH_R = 0.246;
    public const double REGOLITH_G = 0.190;
    public const double REGOLITH_B = 0.112;
    public const double CRUST_R = 0.445;
    public const double CRUST_G = 0.382;
    public const double CRUST_B = 0.238;
    public const double PEAK_R = 0.480;
    public const double PEAK_G = 0.430;
    public const double PEAK_B = 0.340;
    public const double ROUGHNESS_FLAT = 0.88;
    public const double ROUGHNESS_STEEP = 0.66;
    public const double PROVINCE_SCALE = 1.9;
    public const double HUE_SCALE = 1.35;
    public const double OXIDE_R = 0.390;
    public const double OXIDE_G = 0.132;
    public const double OXIDE_B = 0.048;
    public const double EVAPORITE_R = 0.505;
    public const double EVAPORITE_G = 0.462;
    public const double EVAPORITE_B = 0.330;
    public const double HUE_STRENGTH = 0.42;
    public const double POLAR_LATITUDE = 1.40;
    public const double POLAR_EDGE = 0.21;
    public const double POLAR_NOISE_SCALE = 7.0;
    public const double POLAR_R = 0.620;
    public const double POLAR_G = 0.640;
    public const double POLAR_B = 0.660;
    public const double LACUNARITY = 2.037;
    public static readonly double[] FBM_OFFSET = { 19.31, 7.53, 13.77 };
    public static readonly double[] RIDGE_OFFSET = { 5.17, 11.93, 3.71 };

    private const ulong A = 1664525;
    private const ulong B = 1013904223;

    private static (ulong x, ulong y, ulong z) Pcg3d(int ix, int iy, int iz)
    {
        ulong ux = (ulong)(uint)ix;
        ulong uy = (ulong)(uint)iy;
        ulong uz = (ulong)(uint)iz;

        ux = ux * A + B;
        uy = uy * A + B;
        uz = uz * A + B;

        ux += uy * uz;
        uy += uz * ux;
        uz += ux * uy;

        ux ^= ux >> 16;
        uy ^= uy >> 16;
        uz ^= uz >> 16;

        ux += uy * uz;
        uy += uz * ux;
        uz += ux * uy;

        return (ux, uy, uz);
    }

    private static double HashGradient(int ix, int iy, int iz)
    {
        var (x, y, z) = Pcg3d(ix, iy, iz);
        return (double)x / 4294967295.0 * 2.0 - 1.0;
    }

    public static double GradientNoise(double px, double py, double pz)
    {
        int ix = (int)Math.Floor(px);
        int iy = (int)Math.Floor(py);
        int iz = (int)Math.Floor(pz);
        double fx = px - ix;
        double fy = py - iy;
        double fz = pz - iz;

        double u = fx * fx * fx * (fx * (fx * 6.0 - 15.0) + 10.0);
        double v = fy * fy * fy * (fy * (fy * 6.0 - 15.0) + 10.0);
        double w = fz * fz * fz * (fz * (fz * 6.0 - 15.0) + 10.0);

        double total = 0.0;
        for (int corner = 0; corner < 8; corner++)
        {
            int ox = (corner >> 2) & 1;
            int oy = (corner >> 1) & 1;
            int oz = corner & 1;

            double gx = HashGradient(ix + ox, iy + oy, iz + oz);
            double gy = HashGradient(ix + ox + 31, iy + oy + 17, iz + oz + 43);
            double gz = HashGradient(ix + ox + 73, iy + oy + 59, iz + oz + 97);
            double dx = fx - ox;
            double dy = fy - oy;
            double dz = fz - oz;

            double wx = ox == 0 ? 1.0 - u : u;
            double wy = oy == 0 ? 1.0 - v : v;
            double wz = oz == 0 ? 1.0 - w : w;

            total += wx * wy * wz * (gx * dx + gy * dy + gz * dz);
        }

        return Math.Clamp(total * 1.4 * 0.5 + 0.5, 0.0, 1.0) * 2.0 - 1.0;
    }

    public static double Fbm(double px, double py, double pz, int octaves)
    {
        double total = 0.0;
        double amplitude = 0.5;
        double norm = 0.0;
        double x = px;
        double y = py;
        double z = pz;

        for (int i = 0; i < octaves; i++)
        {
            total += amplitude * GradientNoise(x, y, z);
            norm += amplitude;
            amplitude *= 0.5;
            x = x * LACUNARITY + FBM_OFFSET[0];
            y = y * LACUNARITY + FBM_OFFSET[1];
            z = z * LACUNARITY + FBM_OFFSET[2];
        }

        return total / Math.Max(norm, 1e-4);
    }

    public static double RidgedFbm(double px, double py, double pz, int octaves)
    {
        double total = 0.0;
        double amplitude = 0.5;
        double norm = 0.0;
        double prev = 1.0;
        double x = px;
        double y = py;
        double z = pz;

        for (int i = 0; i < octaves; i++)
        {
            double r = 1.0 - Math.Abs(GradientNoise(x, y, z));
            r *= r;
            r *= prev;
            prev = Math.Clamp(r * 2.0, 0.0, 1.0);
            total += amplitude * r;
            norm += amplitude;
            amplitude *= 0.5;
            x = x * LACUNARITY + RIDGE_OFFSET[0];
            y = y * LACUNARITY + RIDGE_OFFSET[1];
            z = z * LACUNARITY + RIDGE_OFFSET[2];
        }

        return Math.Clamp(total / Math.Max(norm, 1e-4), 0.0, 1.0);
    }

    public static double Smoothstep(double edge0, double edge1, double x)
    {
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    private static double CellHash(int cx, int cy, int cz, int salt)
    {
        var (x, y, z) = Pcg3d(cx + salt, cy + salt, cz + salt);
        return (double)x / 4294967295.0;
    }

    private static double CraterProfile(double x)
    {
        double bowl = -(1.0 - Math.Clamp(x / 0.74, 0.0, 1.0) * Math.Clamp(x / 0.74, 0.0, 1.0));
        double rim = Math.Exp(-(((x - 0.90) / 0.14) * ((x - 0.90) / 0.14))) * 0.85;
        double ejecta = Math.Exp(-(((x - 1.20) / 0.40) * ((x - 1.20) / 0.40))) * 0.16;
        return x < 1.9 ? bowl + rim + ejecta : 0.0;
    }

    public static double CraterField(double dx, double dy, double dz, double scale, double density, int salt)
    {
        double px = dx * scale;
        double py = dy * scale;
        double pz = dz * scale;
        int baseX = (int)Math.Floor(px);
        int baseY = (int)Math.Floor(py);
        int baseZ = (int)Math.Floor(pz);

        double total = 0.0;
        for (int ox = -1; ox <= 1; ox++)
        {
            for (int oy = -1; oy <= 1; oy++)
            {
                for (int oz = -1; oz <= 1; oz++)
                {
                    int cx = baseX + ox;
                    int cy = baseY + oy;
                    int cz = baseZ + oz;

                    double jitter = CellHash(cx, cy, cz, salt);
                    double shape = CellHash(cx, cy, cz, salt + 977);

                    bool present = shape < density;
                    double radius = 0.22 + shape * 0.40;
                    double weight = 0.35 + shape * 0.65;

                    double deltaX = px - (cx + jitter);
                    double deltaY = py - (cy + jitter);
                    double deltaZ = pz - (cz + jitter);
                    double distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ) / radius;

                    total += present ? CraterProfile(distance) * weight : 0.0;
                }
            }
        }

        return total;
    }

    public static double FaultField(double dx, double dy, double dz)
    {
        return RidgedFbm(dx * CRACK_SCALE, dy * CRACK_SCALE, dz * CRACK_SCALE, CRACK_OCTAVES);
    }

    public static double CrackRegion(double dx, double dy, double dz)
    {
        double region = Fbm(dx * CRACK_REGION_SCALE, dy * CRACK_REGION_SCALE, dz * CRACK_REGION_SCALE, 2);
        return Smoothstep(CRACK_REGION_THRESHOLD, CRACK_REGION_THRESHOLD + 0.30, region);
    }

    public static double CloudField(double dx, double dy, double dz)
    {
        double px = dx * CLOUD_SCALE;
        double py = dy * CLOUD_SCALE;
        double pz = dz * CLOUD_SCALE;

        double wx = GradientNoise(px + 11.3, py + 5.1, pz + 27.7);
        double wy = GradientNoise(px + 47.9, py + 63.2, pz + 8.4);
        double wz = GradientNoise(px + 83.1, py + 19.6, pz + 51.3);

        double coverage = Fbm(px + wx * CLOUD_WARP, py + wy * CLOUD_WARP, pz + wz * CLOUD_WARP, 4);
        double wobble = GradientNoise(px * 0.5, py * 0.5, pz * 0.5) * 0.25;
        coverage += Math.Sin((dz + wobble) * CLOUD_BANDS * Math.PI + 1.1) * CLOUD_BAND_STRENGTH;
        coverage += GradientNoise(px * 3.2 + wx * 0.3, py * 3.2 + wy * 0.3, pz * 3.2 + wz * 0.3) * 0.12;

        return Math.Clamp(coverage * 0.5 + 0.5 + CLOUD_COVERAGE_BIAS, 0.0, 1.0);
    }

    public static double ElevationBase(double dx, double dy, double dz)
    {
        double cx = dx * CONTINENT_SCALE;
        double cy = dy * CONTINENT_SCALE;
        double cz = dz * CONTINENT_SCALE;

        double warpX = GradientNoise(cx + 17.1, cy + 3.2, cz + 8.9);
        double warpY = GradientNoise(cx + 43.7, cy + 21.4, cz + 2.6);
        double warpZ = GradientNoise(cx + 91.3, cy + 12.8, cz + 33.1);

        double continents = Fbm(cx + warpX * WARP_STRENGTH, cy + warpY * WARP_STRENGTH, cz + warpZ * WARP_STRENGTH, 5);
        double elev = Math.Clamp(continents * 0.5 + 0.5, 0.0, 1.0);

        double uplift = Smoothstep(0.40, 0.78, elev);
        double ranges = RidgedFbm(dx * RIDGE_SCALE, dy * RIDGE_SCALE, dz * RIDGE_SCALE, 5);

        double detail = Fbm(dx * DETAIL_SCALE, dy * DETAIL_SCALE, dz * DETAIL_SCALE, 3) * DETAIL_STRENGTH * 0.22;
        detail += Fbm(dx * GRAIN_SCALE, dy * GRAIN_SCALE, dz * GRAIN_SCALE, 2) * GRAIN_STRENGTH;

        double craters = CraterField(dx, dy, dz, CRATER_SCALE_MAJOR, CRATER_DENSITY_MAJOR, 17) * CRATER_DEPTH_MAJOR
                       + CraterField(dx, dy, dz, CRATER_SCALE_MINOR, CRATER_DENSITY_MINOR, 613) * CRATER_DEPTH_MINOR;

        return elev + ranges * uplift * MOUNTAIN_HEIGHT + detail + craters;
    }

    public static double ProvinceField(double dx, double dy, double dz)
    {
        return Math.Clamp(Fbm(dx * PROVINCE_SCALE, dy * PROVINCE_SCALE, dz * PROVINCE_SCALE, 3) * 0.5 + 0.5, 0.0, 1.0);
    }

    public static double HueField(double dx, double dy, double dz)
    {
        return Smoothstep(0.30, 0.70, Math.Clamp(Fbm(dx * HUE_SCALE, dy * HUE_SCALE, dz * HUE_SCALE, 3) * 0.5 + 0.5, 0.0, 1.0));
    }

    public static double PolarCap(double dx, double dy, double dz, double latitude)
    {
        double edge = Fbm(dx * POLAR_NOISE_SCALE, dy * POLAR_NOISE_SCALE, dz * POLAR_NOISE_SCALE, 3) * 0.16;
        return Smoothstep(POLAR_LATITUDE - POLAR_EDGE, POLAR_LATITUDE, Math.Abs(latitude) + edge);
    }
}
