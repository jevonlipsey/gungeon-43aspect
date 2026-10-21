using System;

namespace EtgVerify;

/// numeric model of the patched camera math. the display policy is the shipped
/// DisplayPolicy.cs compiled in; the rest mirrors the game's BraveCameraUtility
/// (GetRect / MaintainCameraAspect / MaintainCameraAspectForceAspect) and Pixelator.CheckSize
/// in the pixel-perfect scaling modes, with the patched site values substituted. it pins the
/// resolutions the device and the dev machine actually run.
public sealed class Simulator
{
    private readonly Checks checks;

    public Simulator(Checks checks) => this.checks = checks;

    private readonly record struct Result(int H, int V, int K, int CamW, int CamH, int BandW, int BandH, float TileScale);

    // the game's pixel-perfect rect: k = max(1, min(floor(w/H), floor(h/V))), rect = H*k by V*k
    private static (int k, int w, int h) PerfectRect(int sw, int sh, int H, int V)
    {
        int k = Math.Max(1, Math.Min(sw / H, sh / V));
        return (k, H * k, V * k);
    }

    private static Result Simulate(int w, int h)
    {
        // patched get_H_PIXELS / get_V_PIXELS are the policy when narrow; otherwise the
        // original 480f * (ASPECT / 1.7777778f) with ASPECT 16:9 in pixel-perfect modes
        bool narrow = Gungeon43.IsNarrow(w, h);
        int H = narrow ? Gungeon43.PixelsH(w, h) : 480;
        int V = narrow ? Gungeon43.PixelsV(w, h) : 270;
        var cam = PerfectRect(w, h, H, V);

        // ui cameras: ForceH/ForceV when narrow, the original 480/270 otherwise
        int fh = narrow ? Gungeon43.PixelsH(w, h) : 480;
        int fv = narrow ? Gungeon43.ForcedV(w, h) : 270;
        var band = PerfectRect(w, h, fh, fv);

        // Pixelator.CheckSize: Screen.height * camera.rect.height / V_PIXELS (was 270f)
        float rectHeight = 1f - (1f - (float)cam.h / h);
        float tile = Math.Max(1f, Math.Min(20f, h * rectHeight / V));
        return new Result(H, V, cam.k, cam.w, cam.h, band.w, band.h, tile);
    }

    private void Pin(int w, int h, bool narrow, int H, int V, int k, int camW, int camH, int bandW, int bandH, float tile)
    {
        var r = Simulate(w, h);
        checks.Check(Gungeon43.IsNarrow(w, h) == narrow, $"{w}x{h}: narrow = {narrow}");
        checks.Check(r.H == H && r.V == V && r.K == k,
            $"{w}x{h}: H={r.H} V={r.V} k={r.K} (expected {H}x{V} k={k})");
        checks.Check(r.CamW == camW && r.CamH == camH,
            $"{w}x{h}: camera rect {r.CamW}x{r.CamH} (expected {camW}x{camH})");
        checks.Check(r.BandW == bandW && r.BandH == bandH,
            $"{w}x{h}: ui force-16:9 band {r.BandW}x{r.BandH} (expected {bandW}x{bandH})");
        checks.Check(Math.Abs(r.TileScale - tile) < 0.001f,
            $"{w}x{h}: ScaleTileScale {r.TileScale} (expected {tile})");
    }

    // vanilla uniform-scaling path of MaintainCameraAspect: the camera rect is letterboxed to
    // `aspect` with fractions num7 (width, screen wider) / num8 (height, screen taller). the
    // exact 16:9 screens short-circuit to the full rect.
    private static (float w, float h) UniformRect(int sw, int sh, float aspect)
    {
        float num6 = (float)sw / sh;
        float num7 = 0f, num8 = 0f;
        if (sw % 16 == 0 && sh % 9 == 0 && sw / 16 == sh / 9) return (1f, 1f);
        if (num6 > aspect) num7 = 1f - aspect / num6;
        else if (num6 < aspect) num8 = 1f - num6 / aspect;
        return (1f - num7, 1f - num8);
    }

    private void PinMenu(int w, int h, int H, int V, int k, int rectW, int rectH)
    {
        // while OverrideAspect is set: Gungeon43.MenuPixelsH/V and ASPECT = screen w/h
        int mh = Gungeon43.MenuPixelsH(w, h), mv = Gungeon43.MenuPixelsV(w, h);
        var pp = PerfectRect(w, h, mh, mv);
        var uni = UniformRect(w, h, (float)w / h);
        checks.Check(mh == H && mv == V, $"{w}x{h} menu: H={mh} V={mv} (expected {H}x{V})");
        checks.Check(pp.k == k && pp.w == rectW && pp.h == rectH,
            $"{w}x{h} menu: pixel-perfect k={pp.k} rect {pp.w}x{pp.h} (expected k={k}, {rectW}x{rectH})");
        checks.Check(uni.w == 1f && uni.h == 1f, $"{w}x{h} menu: uniform rect is the full screen ({uni.w}x{uni.h})");
    }

    public void Run()
    {
        checks.Section("simulator (policy + camera math)");

        //   w     h   narrow   H    V    k   camW camH bandW bandH tile
        Pin(1280, 960, true, 426, 320, 3, 1278, 960, 1278, 720, 3f);
        Pin(1920, 1080, false, 480, 270, 4, 1920, 1080, 1920, 1080, 4f);
        Pin(1024, 768, true, 340, 256, 3, 1020, 768, 1020, 570, 3f);
        Pin(960, 720, true, 480, 360, 2, 960, 720, 960, 540, 2f);
        Pin(1280, 800, true, 426, 266, 3, 1278, 798, 1278, 720, 3f);
        Pin(640, 480, true, 320, 240, 2, 640, 480, 640, 360, 2f);
        Pin(2560, 1080, false, 480, 270, 4, 1920, 1080, 1920, 1080, 4f);

        // menu / intro framing (OverrideAspect set): vanilla 270 rows at the screen's aspect.
        // 1280x960 at CameraController zoom 0.5: macro view 720x540, which covers the 540-row art
        PinMenu(1280, 960, 360, 270, 3, 1080, 810);
        PinMenu(1024, 768, 360, 270, 2, 720, 540);
        PinMenu(1280, 800, 432, 270, 2, 864, 540);
        PinMenu(640, 480, 360, 270, 1, 360, 270);

        // gameplay in uniform scaling (UNIFORM_SCALING_FAST on the device): ASPECT = H/V, so the
        // 4:3 screen loses 2 columns of 1280 and nothing vertically
        var g = UniformRect(1280, 960, (float)Gungeon43.PixelsH(1280, 960) / Gungeon43.PixelsV(1280, 960));
        checks.Check(Math.Abs(g.w - 1278f / 1280f) < 0.00005f && g.h == 1f,
            $"1280x960 gameplay, uniform scaling: rect {g.w} x {g.h} (expected {1278f / 1280f} x 1)");
        var v = UniformRect(1920, 1080, 16f / 9f);
        checks.Check(v.w == 1f && v.h == 1f, "1920x1080 uniform scaling stays the full rect");

        // 16:9 and wider stay exactly vanilla, 4:3 and taller never do
        bool wide = true;
        foreach (var (w, h) in new[] { (1280, 720), (1366, 768), (1920, 1080), (2560, 1440), (3840, 2160), (2560, 1080), (3440, 1440), (1600, 900) })
            wide &= !Gungeon43.IsNarrow(w, h);
        checks.Check(wide, "16:9 and ultrawide screens are never narrow");
        bool narrowOk = true;
        foreach (var (w, h) in new[] { (1280, 960), (1024, 768), (800, 600), (640, 480), (1600, 1200), (1280, 1024), (1280, 800), (1920, 1200) })
            narrowOk &= Gungeon43.IsNarrow(w, h);
        checks.Check(narrowOk, "4:3, 5:4 and 16:10 screens are narrow");
        checks.Check(!Gungeon43.IsNarrow(0, 0) && !Gungeon43.IsNarrow(1280, 0) && !Gungeon43.IsNarrow(0, 960),
            "a zero-sized screen is never narrow");

        // sweep: on every narrow screen the camera rect fits the screen, pixel counts are even,
        // the force band fits inside the camera rect and the tile scale stays in 1..20
        int bad = 0, count = 0;
        string first = "";
        for (int h = 240; h <= 2400; h += 8)
            for (int w = 320; w <= 3840; w += 8)
            {
                if (!Gungeon43.IsNarrow(w, h)) continue;
                count++;
                var r = Simulate(w, h);
                bool ok = r.CamW <= w && r.CamH <= h && r.H % 2 == 0 && r.V % 2 == 0 && r.K >= 1
                    && r.BandW <= w && r.BandH <= h && r.BandH <= r.CamH
                    && r.TileScale >= 1f && r.TileScale <= 20f;
                if (!ok && bad++ == 0) first = $"{w}x{h} -> {r}";
            }
        checks.Check(bad == 0, bad == 0
            ? $"sweep of {count} narrow screens: rect fits, H/V even, force band fits"
            : $"sweep: {bad} of {count} narrow screens broke an invariant, first {first}");
    }
}
