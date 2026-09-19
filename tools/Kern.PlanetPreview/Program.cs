using System.Numerics;
using Kern.PlanetBaker;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Kern.PlanetPreview;

internal readonly record struct Matrix3x3(
    float M11, float M12, float M13,
    float M21, float M22, float M23,
    float M31, float M32, float M33)
{
    public static readonly Matrix3x3 Identity = new(1, 0, 0, 0, 1, 0, 0, 0, 1);
}

public static class PlanetPreviewApi
{
    public const double CameraDistance = 2.90;
    private const string TextureDir = "Assets/Textures/UI";
    private const string MaterialPath = "Assets/Materials/PlanetSurface.mat";
    private const string AtmospherePath = "Assets/Materials/PlanetAtmosphere.mat";
    private const string ScenePath = "Assets/Scenes/MainMenu.unity";
    private const double FrameMargin = 1.14;

    public static Image<Rgb24> Render(int size, double zoom)
    {
        var mat = LoadMaterial(MaterialPath);
        var atmo = LoadMaterial(AtmospherePath);

        using var albedoMap = LoadTexture($"{TextureDir}/planet_albedo.png");
        using var normalMap = LoadTexture($"{TextureDir}/planet_normal.png");
        using var packedMap = LoadTexture($"{TextureDir}/planet_packed.png");

        var rotation = ScenePlanetRotation();

        double halfExtent = Math.Tan(Math.Asin(1.0 / CameraDistance) * FrameMargin) * CameraDistance / zoom;
        var axis = new double[size];
        for (int i = 0; i < size; i++)
        {
            axis[i] = -halfExtent + (2.0 * halfExtent * i) / (size - 1);
        }

        var outPixels = new Rgb24[size, size];
        for (int sy = 0; sy < size; sy++)
        {
            for (int sx = 0; sx < size; sx++)
            {
                double rx = axis[sx];
                double ry = -axis[sy];
                double rz = CameraDistance;
                double rLen = Math.Sqrt(rx * rx + ry * ry + rz * rz);
                double rdx = rx / rLen;
                double rdy = ry / rLen;
                double rdz = rz / rLen;

                double b = rdx * 0.0 + rdy * 0.0 + rdz * (-CameraDistance);
                double discriminant = b * b - (0.0 - 1.0);
                if (discriminant < 0.0)
                {
                    outPixels[sy, sx] = new Rgb24(5, 8, 15);
                    continue;
                }

                double dist = -b - Math.Sqrt(discriminant);
                double hx = rdx * dist;
                double hy = rdy * dist;
                double hz = rdz * dist;
                double hLen = Math.Sqrt(hx * hx + hy * hy + hz * hz);
                double dx = hx / hLen;
                double dy = hy / hLen;
                double dz = hz / hLen;

                double rdx2 = dx * rotation.M11 + dy * rotation.M12 + dz * rotation.M13;
                double rdy2 = dx * rotation.M21 + dy * rotation.M22 + dz * rotation.M23;
                double rdz2 = dx * rotation.M31 + dy * rotation.M32 + dz * rotation.M33;

                double u = Math.Atan2(rdz2, rdx2) * (0.5 / Math.PI) + 0.5;
                double v = Math.Asin(Math.Clamp(rdy2, -1.0, 1.0)) / Math.PI + 0.5;

                var albedo = SampleEquirect(albedoMap, u, v);
                var packed = SampleEquirect(packedMap, u, v);
                var normalTs = SampleEquirect(normalMap, u, v);

                double nx = normalTs.X * 2.0 - 1.0;
                double ny = normalTs.Y * 2.0 - 1.0;
                double nz = normalTs.Z * 2.0 - 1.0;

                double roughness = mat["_RoughnessMin"] + (mat["_RoughnessMax"] - mat["_RoughnessMin"]) * packed.X;
                double rift = packed.Y;
                double cloudCoverage = packed.Z;

                Vector3 up;
                if (Math.Abs(rdy2) < 0.99)
                {
                    up = new Vector3(0.0f, 1.0f, 0.0f);
                }
                else
                {
                    up = new Vector3(1.0f, 0.0f, 0.0f);
                }

                Vector3 tangent = Vector3.Cross(up, new Vector3((float)dx, (float)dy, (float)dz));
                tangent = Vector3.Normalize(tangent);
                Vector3 bitangent = Vector3.Cross(new Vector3((float)dx, (float)dy, (float)dz), tangent);

                double tnx = tangent.X * nx * (float)mat["_NormalStrength"] + bitangent.X * ny * (float)mat["_NormalStrength"] + dx;
                double tny = tangent.Y * nx * (float)mat["_NormalStrength"] + bitangent.Y * ny * (float)mat["_NormalStrength"] + dy;
                double tnz = tangent.Z * nx * (float)mat["_NormalStrength"] + bitangent.Z * ny * (float)mat["_NormalStrength"] + dz;
                double tLen = Math.Sqrt(tnx * tnx + tny * tny + tnz * tnz);
                double nnx = tnx / tLen;
                double nny = tny / tLen;
                double nnz = tnz / tLen;

                Vector3 light = Vector3.Normalize(new Vector3((float)mat["_SunDirX"], (float)mat["_SunDirY"], (float)mat["_SunDirZ"]));
                Vector3 view = new Vector3((float)(-rdx), (float)(-rdy), (float)(-rdz));
                Vector3 halfVec = Vector3.Add(light, view);
                halfVec = Vector3.Normalize(halfVec);

                double ndl = nnx * light.X + nny * light.Y + nnz * light.Z;
                double ndv = Math.Max(nnx * (-rdx) + nny * (-rdy) + nnz * (-rdz), 0.0);
                double ndh = Math.Max(nnx * halfVec.X + nny * halfVec.Y + nnz * halfVec.Z, 0.0);
                double vdh = Math.Max(view.X * halfVec.X + view.Y * halfVec.Y + view.Z * halfVec.Z, 0.0);

                double alpha = roughness * roughness;
                double alphaSq = alpha * alpha;
                double denom = ndh * ndh * (alphaSq - 1.0) + 1.0;
                double distribution = alphaSq / Math.Max(Math.PI * denom * denom, 1e-4);
                double k = Math.Pow(roughness + 1.0, 2.0) * 0.125;
                double ndlPos = Math.Max(ndl, 0.0);
                double geometry = (ndlPos / Math.Max(ndlPos * (1.0 - k) + k, 1e-4)) * (ndv / Math.Max(ndv * (1.0 - k) + k, 1e-4));
                double fresnel = 0.04 + 0.96 * Math.Pow(1.0 - vdh, 5.0);

                double cloud = PlanetMath.Smoothstep(mat["_CloudCoverage"], mat["_CloudCoverage"] + mat["_CloudSoftness"], cloudCoverage) * mat["_CloudOpacity"];
                double ar = albedo.X * (1.0 - cloud) + mat["_CloudColorR"] * cloud;
                double ag = albedo.Y * (1.0 - cloud) + mat["_CloudColorG"] * cloud;
                double ab = albedo.Z * (1.0 - cloud) + mat["_CloudColorB"] * cloud;
                rift = rift * (1.0 - cloud);

                double wrap = mat.GetValueOrDefault("_TerminatorSoftness", 0.0);
                double wrapped = Math.Clamp((ndl + wrap) / (1.0 + wrap), 0.0, 1.0);

                double sunR = mat["_SunColorR"] * mat["_SunIntensity"];
                double sunG = mat["_SunColorG"] * mat["_SunIntensity"];
                double sunB = mat["_SunColorB"] * mat["_SunIntensity"];
                double diffuseR = (ar / Math.PI) * (1.0 - fresnel) * wrapped;
                double diffuseG = (ag / Math.PI) * (1.0 - fresnel) * wrapped;
                double diffuseB = (ab / Math.PI) * (1.0 - fresnel) * wrapped;

                double spec = (distribution * geometry / Math.Max(4.0 * ndlPos * ndv, 1e-4)) * fresnel;

                double directR = (diffuseR + spec) * sunR;
                double directG = (diffuseG + spec) * sunG;
                double directB = (diffuseB + spec) * sunB;

                double twilight = Math.Pow(Math.Clamp(1.0 - Math.Abs(ndl), 0.0, 1.0), 1.8) * Math.Clamp(ndl + 0.55, 0.0, 1.0);
                double scatterR = ar * mat["_TwilightColorR"] * twilight * mat["_TwilightIntensity"];
                double scatterG = ag * mat["_TwilightColorG"] * twilight * mat["_TwilightIntensity"];
                double scatterB = ab * mat["_TwilightColorB"] * twilight * mat["_TwilightIntensity"];

                double emissionR = mat["_MagmaColorR"] * (rift * mat["_MagmaIntensity"]);
                double emissionG = mat["_MagmaColorG"] * (rift * mat["_MagmaIntensity"]);
                double emissionB = mat["_MagmaColorB"] * (rift * mat["_MagmaIntensity"]);

                double ambientR = ar * mat["_NightAmbientR"];
                double ambientG = ag * mat["_NightAmbientG"];
                double ambientB = ab * mat["_NightAmbientB"];

                double exposure = mat.GetValueOrDefault("_Exposure", 1.0);
                double colorR = (directR + scatterR + emissionR + ambientR) * exposure;
                double colorG = (directG + scatterG + emissionG + ambientG) * exposure;
                double colorB = (directB + scatterB + emissionB + ambientB) * exposure;

                double mappedR = (colorR * (2.51 * colorR + 0.03)) / (colorR * (2.43 * colorR + 0.59) + 0.14);
                double mappedG = (colorG * (2.51 * colorG + 0.03)) / (colorG * (2.43 * colorG + 0.59) + 0.14);
                double mappedB = (colorB * (2.51 * colorB + 0.03)) / (colorB * (2.43 * colorB + 0.59) + 0.14);

                if (discriminant < 0.0)
                {
                    outPixels[sy, sx] = new Rgb24(
                        (byte)Math.Clamp((int)Math.Round(Math.Clamp(mappedR, 0.0, 1.0) * 255.0), 0, 255),
                        (byte)Math.Clamp((int)Math.Round(Math.Clamp(mappedG, 0.0, 1.0) * 255.0), 0, 255),
                        (byte)Math.Clamp((int)Math.Round(Math.Clamp(mappedB, 0.0, 1.0) * 255.0), 0, 255));
                    continue;
                }

                double shellRadius = 1.0 / atmo["_RadiusRatio"];
                double shellB = rdx * 0.0 + rdy * 0.0 + rdz * (-CameraDistance);
                double shellDisc = shellB * shellB - (0.0 - shellRadius * shellRadius);
                bool shellHit = shellDisc >= 0.0;
                double shellDist = shellHit ? -shellB - Math.Sqrt(shellDisc) : 0.0;
                double shx = rdx * shellDist;
                double shy = rdy * shellDist;
                double shz = rdz * shellDist;
                double shLen = Math.Sqrt(shx * shx + shy * shy + shz * shz);
                double sdx = shx / shLen;
                double sdy = shy / shLen;
                double sdz = shz / shLen;

                double shellNdv = Math.Abs(sdx * (-rdx) + sdy * (-rdy) + sdz * (-rdz));
                double impact = Math.Sqrt(Math.Max(1.0 - shellNdv * shellNdv, 0.0));
                double outerHalf = Math.Sqrt(Math.Max(1.0 - impact * impact, 0.0));
                double innerHalf = Math.Sqrt(Math.Max(atmo["_RadiusRatio"] * atmo["_RadiusRatio"] - impact * impact, 0.0));
                double maxChord = Math.Max(Math.Sqrt(Math.Max(1.0 - atmo["_RadiusRatio"] * atmo["_RadiusRatio"], 0.0)), 1e-4);
                double rim = Math.Clamp((outerHalf - innerHalf) / maxChord, 0.0, 1.0);
                rim = Math.Pow(rim, atmo["_RimPower"]);

                double shellNdl = sdx * light.X + sdy * light.Y + sdz * light.Z;
                double sunAmount = Math.Max(Math.Clamp((shellNdl + atmo["_SunWrap"]) / (1.0 + atmo["_SunWrap"]), 0.0, 1.0), atmo["_NightFloor"]);
                double forward = Math.Pow(Math.Max(Vector3.Dot(view, -light), 0.0), 6.0) * atmo["_ForwardScatter"];

                double tintR = atmo["_AtmosphereColorR"] + (atmo["_HorizonColorR"] - atmo["_AtmosphereColorR"]) * Math.Clamp(rim * 1.4, 0.0, 1.0);
                double tintG = atmo["_AtmosphereColorG"] + (atmo["_HorizonColorG"] - atmo["_AtmosphereColorG"]) * Math.Clamp(rim * 1.4, 0.0, 1.0);
                double tintB = atmo["_AtmosphereColorB"] + (atmo["_HorizonColorB"] - atmo["_AtmosphereColorB"]) * Math.Clamp(rim * 1.4, 0.0, 1.0);
                double glowR = tintR * (rim * sunAmount * atmo["_Density"] * (1.0 + forward));
                double glowG = tintG * (rim * sunAmount * atmo["_Density"] * (1.0 + forward));
                double glowB = tintB * (rim * sunAmount * atmo["_Density"] * (1.0 + forward));

                double bgR = 0.02;
                double bgG = 0.03;
                double bgB = 0.06;

                double finalR = discriminant >= 0.0 ? mappedR : bgR;
                double finalG = discriminant >= 0.0 ? mappedG : bgG;
                double finalB = discriminant >= 0.0 ? mappedB : bgB;

                if (shellHit)
                {
                    finalR += glowR;
                    finalG += glowG;
                    finalB += glowB;
                }

                outPixels[sy, sx] = new Rgb24(
                    (byte)Math.Clamp((int)Math.Round(Math.Clamp(finalR, 0.0, 1.0) * 255.0), 0, 255),
                    (byte)Math.Clamp((int)Math.Round(Math.Clamp(finalG, 0.0, 1.0) * 255.0), 0, 255),
                    (byte)Math.Clamp((int)Math.Round(Math.Clamp(finalB, 0.0, 1.0) * 255.0), 0, 255));
            }
        }

        var flat = new Rgb24[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                flat[y * size + x] = outPixels[y, x];
            }
        }

        return Image.LoadPixelData<Rgb24>(flat, size, size);
    }

    private static RGBF SampleEquirect(Image<Rgb24> tex, double u, double v)
    {
        int w = tex.Width;
        int h = tex.Height;
        double x = u * w - 0.5;
        double y = v * h - 0.5;
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        double fx = x - x0;
        double fy = y - y0;
        int x1 = ((x0 + 1) % w + w) % w;
        int y0c = Math.Clamp(y0, 0, h - 1);
        int y1c = Math.Clamp(y0 + 1, 0, h - 1);

        var c00 = tex[x0, y0c];
        var c10 = tex[x1, y0c];
        var c01 = tex[x0, y1c];
        var c11 = tex[x1, y1c];

        float r = (float)((c00.R * (1 - fx) + c10.R * fx + c01.R * (1 - fx) + c11.R * fx) * 0.5 / 255.0);
        float g = (float)((c00.G * (1 - fx) + c10.G * fx + c01.G * (1 - fx) + c11.G * fx) * 0.5 / 255.0);
        float b = (float)((c00.B * (1 - fx) + c10.B * fx + c01.B * (1 - fx) + c11.B * fx) * 0.5 / 255.0);

        return new RGBF(r, g, b);
    }

    private static Dictionary<string, double> LoadMaterial(string path)
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (!line.StartsWith("- _")) continue;
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            string name = line.Substring(2, colon - 2).Trim();
            string rest = line.Substring(colon + 1).Trim();
            if (rest.StartsWith("{"))
            {
                int end = rest.IndexOf('}');
                string body = end >= 0 ? rest.Substring(1, end - 1) : rest[1..];
                var parts = new Dictionary<string, string>();
                foreach (string piece in body.Split(','))
                {
                    string[] kv = piece.Split(':', 2);
                    if (kv.Length == 2)
                    {
                        parts[kv[0].Trim()] = kv[1].Trim();
                    }
                }
                if (parts.ContainsKey("r") && parts.ContainsKey("g") && parts.ContainsKey("b"))
                {
                    values[$"{name}R"] = double.Parse(parts["r"]);
                    values[$"{name}G"] = double.Parse(parts["g"]);
                    values[$"{name}B"] = double.Parse(parts["b"]);
                }
            }
            else
            {
                if (double.TryParse(rest, out double val))
                {
                    values[name] = val;
                }
            }
        }

        return values;
    }

    private static Matrix3x3 ScenePlanetRotation()
    {
        string[] lines = File.ReadAllLines(ScenePath);
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "m_Name: PlanetSurface")
            {
                for (int back = i; back > Math.Max(i - 400, 0); back--)
                {
                    if (lines[back].Contains("m_LocalRotation:"))
                    {
                        string body = lines[back].Split('{')[1].Split('}')[0];
                        var q = new Dictionary<string, double>();
                        foreach (string piece in body.Split(','))
                        {
                            string[] kv = piece.Split(':', 2);
                            if (kv.Length == 2)
                            {
                                q[kv[0].Trim()] = double.Parse(kv[1].Trim());
                            }
                        }
                        double x = q["x"], y = q["y"], z = q["z"], w = q["w"];
                        return new Matrix3x3(
                            (float)(1 - 2 * (y * y + z * z)), (float)(2 * (x * y - w * z)), (float)(2 * (x * z + w * y)),
                            (float)(2 * (x * y + w * z)), (float)(1 - 2 * (x * x + z * z)), (float)(2 * (y * z - w * x)),
                            (float)(2 * (x * z - w * y)), (float)(2 * (y * z + w * x)), (float)(1 - 2 * (x * x + y * y)));
                    }
                }
            }
        }

        return Matrix3x3.Identity;
    }

    private static Image<Rgb24> LoadTexture(string path)
    {
        return SixLabors.ImageSharp.Image.Load<Rgb24>(path);
    }

    private readonly struct RGBF
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;
        public RGBF(float x, float y, float z) => (X, Y, Z) = (x, y, z);
        public static implicit operator SixLabors.ImageSharp.PixelFormats.Rgb24(RGBF c) =>
            new Rgb24((byte)Math.Clamp((int)Math.Round(c.X * 255.0), 0, 255),
                      (byte)Math.Clamp((int)Math.Round(c.Y * 255.0), 0, 255),
                      (byte)Math.Clamp((int)Math.Round(c.Z * 255.0), 0, 255));
    }
}

internal static class Program
{
    public static int Main(string[] args)
    {
        int size = args.Length > 0 ? int.Parse(args[0]) : 900;
        double zoom = args.Length > 1 ? double.Parse(args[1]) : 1.0;

        using var image = PlanetPreviewApi.Render(size, zoom);
        image.Save("planet_preview.png");
        Console.WriteLine($"записано planet_preview.png ({size}x{size}, приближение {zoom:G}x)");
        return 0;
    }
}
