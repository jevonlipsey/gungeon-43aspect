using UnityEngine;

// the unity-facing half of the helper. cloned into Assembly-CSharp by the patcher and
// called only from the patched sites in BraveCameraUtility, Pixelator and GameOptions;
// nothing hooks a game Update. every getter falls back to the vanilla number when the
// screen is not narrow, so 16:9 behaves exactly as shipped.
public static partial class Gungeon43
{
    public static bool Active()
    {
        return IsNarrow(Screen.width, Screen.height);
    }

    public static int HPixels()
    {
        return PixelsH(Screen.width, Screen.height);
    }

    public static int VPixels()
    {
        return PixelsV(Screen.width, Screen.height);
    }

    public static float Aspect()
    {
        return (float)HPixels() / (float)VPixels();
    }

    // menu = BraveCameraUtility.OverrideAspect.HasValue at the call site: the menu keeps the
    // vanilla 270 rows at the screen's own aspect, gameplay uses the 4:3 budget
    public static int HPixels(bool menu)
    {
        return menu ? MenuPixelsH(Screen.width, Screen.height) : HPixels();
    }

    public static int VPixels(bool menu)
    {
        return menu ? MenuPixelsV(Screen.width, Screen.height) : VPixels();
    }

    public static float Aspect(bool menu)
    {
        return menu ? (float)Screen.width / (float)Screen.height : Aspect();
    }

    // size of the box the ui cameras force to 16:9
    public static float ForceH()
    {
        return Active() ? (float)HPixels() : 480f;
    }

    public static float ForceV()
    {
        return Active() ? (float)ForcedV(Screen.width, Screen.height) : 270f;
    }

    public static int ForceHi()
    {
        return Active() ? HPixels() : 480;
    }

    public static int ForceVi()
    {
        return Active() ? ForcedV(Screen.width, Screen.height) : 270;
    }

    // the panel's real shape: currentResolution is the desktop mode, which stays put
    // while Screen.width/height follow a window
    public static float NativeAspect()
    {
        int w = Screen.currentResolution.width;
        int h = Screen.currentResolution.height;
        if (w <= 0 || h <= 0)
        {
            w = Screen.width;
            h = Screen.height;
        }
        return (float)w / (float)h;
    }
}
