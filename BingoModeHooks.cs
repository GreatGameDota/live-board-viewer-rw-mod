using MonoMod.RuntimeDetour;
using System.Reflection;
using Steamworks;

namespace LiveBoardViewer;

public static class BingoModeHooks
{
    public static void Apply()
    {
        new Hook(typeof(BingoMode.BingoSteamworks.SteamFinal).GetMethod("BroadcastCurrentBoardState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), On_BroadcastCurrentBoardState);

        // BingoMode.BingoSteamworks.InnerWorkings is an internal class
        var InnerWorkings = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(x => x.GetTypes())
            .Where(x => x.IsClass && x.Namespace == "BingoMode.BingoSteamworks")
            .FirstOrDefault(x => x.Name == "InnerWorkings");
        if (InnerWorkings != null)
            new Hook(InnerWorkings.GetMethod("SendMessage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), On_InnerWorkings_SendMessage);
    }

    public static void On_InnerWorkings_SendMessage(Action<string, SteamNetworkingIdentity, bool> orig, string data, SteamNetworkingIdentity receiver, bool reliable = true)
    {
        Task.Run(() =>
        {
            // BingoMode.BingoSteamworks.SteamTest is an internal class
            var SteamTest = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(x => x.GetTypes())
                .Where(x => x.IsClass && x.Namespace == "BingoMode.BingoSteamworks")
                .FirstOrDefault(x => x.Name == "SteamTest");
            var selfIdentity = SteamTest?
                .GetField("selfIdentity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?
                .GetValue(null);
            var GetSteamID64 = selfIdentity?
                .GetType()?
                .GetMethod("GetSteamID64", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var GetSteamID = selfIdentity?
                .GetType()?
                .GetMethod("GetSteamID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var team = SteamTest?
                .GetField("team", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?
                .GetValue(null);

            if (receiver.GetSteamID() != (CSteamID?)GetSteamID?.Invoke(selfIdentity, null))
            {
                if ((ulong?)GetSteamID64?.Invoke(selfIdentity, null) != BingoMode.BingoSteamworks.SteamFinal.GetHost().GetSteamID64())
                {
                    if (!LiveBoardViewer.wsConnection.IsConnected)
                        LiveBoardViewer.wsConnection.ConnectAsync().Wait();

                    string state = BingoMode.BingoHooks.GlobalBoard.GetBingoState();
                    LiveBoardViewer.wsConnection.SendAsync(BingoMode.BingoHooks.GlobalBoard.ToString() + ";;" + state.Replace('3', '1') + ";;" + GetSteamID64?.Invoke(selfIdentity, null) + ";;" + team + ";;" + data[0]).Wait();
                    // await wsConnection.DisconnectAsync();
                }
            }
        });

        orig(data, receiver, reliable);
    }

    public static void On_BroadcastCurrentBoardState(Action orig)
    {
        Task.Run(() =>
        {
            if (!LiveBoardViewer.wsConnection.IsConnected)
                LiveBoardViewer.wsConnection.ConnectAsync().Wait();

            string state = BingoMode.BingoHooks.GlobalBoard.GetBingoState();

            // BingoMode.BingoSteamworks.SteamTest is an internal class
            var SteamTest = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(x => x.GetTypes())
                .Where(x => x.IsClass && x.Namespace == "BingoMode.BingoSteamworks")
                .FirstOrDefault(x => x.Name == "SteamTest");
            var selfIdentity = SteamTest?
                .GetField("selfIdentity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?
                .GetValue(null);
            var GetSteamID64 = selfIdentity?
                .GetType()?
                .GetMethod("GetSteamID64", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var team = SteamTest?
                .GetField("team", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?
                .GetValue(null);

            LiveBoardViewer.wsConnection.SendAsync(BingoMode.BingoHooks.GlobalBoard.ToString() + ";;" + state.Replace('3', '1') + ";;" + GetSteamID64?.Invoke(selfIdentity, null) + ";;" + team).Wait();
            // wsConnection.DisconnectAsync();
        });

        orig();
    }
}
