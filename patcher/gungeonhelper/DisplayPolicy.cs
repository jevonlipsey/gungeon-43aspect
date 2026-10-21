// the 4:3 display policy as pure integer functions, so the exact code that ships in
// the game assembly is also compiled into verify/ and driven by the simulator.
//
// no usings on purpose. nothing here may touch UnityEngine (verify has no unity), and
// nothing may touch System.Math: the cloned helper is imported into the game assembly
// and a stray Math call binds to the patcher's own runtime (System.Private.CoreLib),
// which unity's mono cannot load. integer arithmetic only.
//
// the unity-facing half (Screen reads) lives in Gungeon43.cs.
public static partial class Gungeon43
{
    // anything narrower than 16:9 gets the 4:3 pixel budget; 16:9 and wider stay vanilla
    public static bool IsNarrow(int w, int h)
    {
        return w > 0 && h > 0 && w * 9 < h * 16;
    }

    // integer scale for a screen of height h: the vanilla art is 270 tall, but the budget
    // here is ~300 rows, so 960 -> 3, 768 -> 3, 720 -> 2, 480 -> 2
    public static int Multiple(int h)
    {
        int k = (h + 150) / 300;
        return k < 1 ? 1 : k;
    }

    // even so the half-size render targets and the rect math never land on a half pixel
    public static int PixelsV(int w, int h)
    {
        return (h / Multiple(h)) & ~1;
    }

    public static int PixelsH(int w, int h)
    {
        return (w / Multiple(h)) & ~1;
    }

    // height of the 16:9 band the ui cameras (ForceToSixteenNine) keep inside that width
    public static int ForcedV(int w, int h)
    {
        return ((PixelsH(w, h) * 9 + 8) / 16) & ~1;
    }

    // title screen and intro art is authored for exactly 270 macro rows (the diorama is seen
    // through the camera at 0.5 zoom), so while the game marks menu framing (OverrideAspect
    // set) the vanilla row budget is kept and only the width follows the screen's aspect
    public static int MenuPixelsV(int w, int h)
    {
        return 270;
    }

    public static int MenuPixelsH(int w, int h)
    {
        return ((270 * w) / h) & ~1;
    }
}
