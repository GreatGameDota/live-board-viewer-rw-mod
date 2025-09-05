using BepInEx;
using BepInEx.Logging;
using System.Security;
using System.Security.Permissions;

#pragma warning disable CS0618
[module: UnverifiableCode]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace LiveBoardViewer;

[BepInPlugin("greatgamedota.liveboardviewer", "Live Board Viewer for Bingo Mode", "0.1.0")]
public class LiveBoardViewer : BaseUnityPlugin
{
    public static WebSocketConnection wsConnection;
    internal static ManualLogSource logger;
    private static bool init;

    public void OnEnable()
    {
        On.RainWorld.PostModsInit += On_RainWorld_PostModsInit;
        logger = Logger;
    }

    public void OnDisable()
    {
        logger = null;
        wsConnection?.Dispose();
    }

    public static void On_RainWorld_PostModsInit(On.RainWorld.orig_PostModsInit orig, RainWorld self)
    {
        orig(self);
        if (init) return;

        init = true;
        try
        {
            wsConnection = new();
            BingoModeHooks.Apply();
        }
        catch (Exception ex)
        {
            logger.LogInfo($"LiveBoardViewer failed to load! ${ex.Message}");
        }
    }
}