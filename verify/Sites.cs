using System;

namespace EtgVerify;

/// the methods the patch edits, shared by the patcher (which edits them) and the verifier
/// (which checks them and treats everything else as untouchable).
public static class Sites
{
    public const string HelperType = "Gungeon43";

    public static readonly (string Type, string Method)[] Edited =
    {
        ("BraveCameraUtility", "get_ASPECT"),
        ("BraveCameraUtility", "get_H_PIXELS"),
        ("BraveCameraUtility", "get_V_PIXELS"),
        ("BraveCameraUtility", "MaintainCameraAspectForceAspect"),
        ("Pixelator", "CheckSize"),
        ("GameOptions", "GetRecommendedResolution"),
    };

    public static bool IsEdited(string type, string method) =>
        Array.IndexOf(Edited, (type, method)) >= 0;

    /// the types that identify the game, in the order the guard reports them
    public static readonly string[] IdentityTypes =
        { "BraveCameraUtility", "Pixelator", "FoyerPreloader", "AkBasePathGetter", "GameOptions" };

    /// the literal GetPlatformName returns is the folder wwise loads sound banks from
    /// (StreamingAssets/Audio/GeneratedSoundBanks/<platform>), so it has to match the build
    /// "auto" means no expectation (null): patch whatever platform the dll is
    public static string? TargetToPlatform(string target) => target switch
    {
        "auto" => null,
        "win" => "Windows",
        "mac" => "Mac",
        "linux" => "Linux",
        _ => throw new ArgumentException($"unknown target '{target}'"),
    };
}
