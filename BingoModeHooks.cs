using MonoMod.RuntimeDetour;
using System.Reflection;
using Steamworks;
using MoreSlugcats;
using System.Globalization;

namespace LiveBoardViewer;

public static class BingoModeHooks
{
    public static int completedGoals = 0;

    public static Type? _steamTestType;
    public static Type? SteamTestType => _steamTestType ??= AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(x => x.GetTypes())
        .FirstOrDefault(x => x.IsClass && x.Namespace == "BingoMode.BingoSteamworks" && x.Name == "SteamTest");

    public static object? GetSteamTestField(string fieldName) =>
        SteamTestType?.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);

    public static string GetIGTFormat(this TimeSpan timeSpan, bool includeMilliseconds)
    {
        string text = string.Format("{0:D3}h:{1:D2}m:{2:D2}s",
        [
            timeSpan.Hours + timeSpan.Days * 24,
            timeSpan.Minutes,
            timeSpan.Seconds
        ]);
        if (!includeMilliseconds)
            return text;
        return text + string.Format(":{0:000}ms", timeSpan.Milliseconds);
    }

    public static void Apply()
    {
        new Hook(typeof(BingoMode.BingoSteamworks.SteamFinal).GetMethod("BroadcastCurrentBoardState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), On_BroadcastCurrentBoardState);

        // BingoMode.BingoSteamworks.InnerWorkings is an internal class
        var InnerWorkings = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(x => x.GetTypes())
            .FirstOrDefault(x => x.IsClass && x.Namespace == "BingoMode.BingoSteamworks" && x.Name == "InnerWorkings");
        if (InnerWorkings != null)
        {
            new Hook(InnerWorkings.GetMethod("SendMessage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), On_InnerWorkings_SendMessage);
            new Hook(InnerWorkings.GetMethod("MessageReceived", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), On_InnerWorkings_MessageReceived);
        }

        new Hook(typeof(BingoMode.BingoChallenges.BingoChallenge).GetMethod("OnChallengeCompleted", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), On_OnChallengeCompleted);
        new Hook(typeof(BingoMode.BingoChallenges.BingoChallenge).GetMethod("OnChallengeFailed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), On_OnChallengeFailed);

        new Hook(typeof(BingoMode.BingoData).GetMethod("InitializeBingo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), On_InitializeBingo);
    }

    public static string BuildBoardPayload()
    {
        string state = BingoMode.BingoHooks.GlobalBoard.GetBingoState();

        var selfIdentity = GetSteamTestField("selfIdentity");
        var GetSteamID = selfIdentity?.GetType()?.GetMethod("GetSteamID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var team = GetSteamTestField("team");

        var name = SteamFriends.GetFriendPersonaName((CSteamID)GetSteamID?.Invoke(selfIdentity, null));
        RainWorldGame _game = (RainWorldGame)LiveBoardViewer.game.processManager.currentMainLoop;
        SpeedRunTimer.CampaignTimeTracker campaignTimeTracker = SpeedRunTimer.GetCampaignTimeTracker(_game.GetStorySession.saveStateNumber);

        return BingoMode.BingoHooks.GlobalBoard.ToString() + ";;" +
               state.Replace('3', '1') + ";;" +
               name + ";;" +
               team + ";;" +
               campaignTimeTracker?.TotalFreeTimeSpan.GetIGTFormat(true) + ";;" +
               completedGoals;
    }

    public static void SendBoardAsync(string payload)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (!LiveBoardViewer.wsConnection.IsConnected)
                    await LiveBoardViewer.wsConnection.ConnectAsync();
                await LiveBoardViewer.wsConnection.SendAsync(payload);
            }
            catch (Exception ex)
            {
                LiveBoardViewer.logger.LogError($"WebSocket send failed: {ex.Message}");
            }
        });
    }

    public static void On_InitializeBingo(Action orig)
    {
        if (!LiveBoardViewer.game.progression.IsThereASavedGame(Expedition.ExpeditionData.slugcatPlayer))
        {
            completedGoals = 0;
            LiveBoardViewer.logger.LogInfo($"Completed goals reset: {completedGoals}");
        }
        orig();
    }

    public static void On_OnChallengeCompleted(Action<BingoMode.BingoChallenges.BingoChallenge, int> orig, BingoMode.BingoChallenges.BingoChallenge self, int team)
    {
        var _team = GetSteamTestField("team");
        if ((int)_team == team)
            LiveBoardViewer.logger.LogInfo($"Completed goals: {++completedGoals}");
        orig(self, team);
    }

    public static void On_OnChallengeFailed(Action<BingoMode.BingoChallenges.BingoChallenge, int> orig, BingoMode.BingoChallenges.BingoChallenge self, int team)
    {
        var _team = GetSteamTestField("team");
        if (self.completed && (int)_team == team)
            LiveBoardViewer.logger.LogInfo($"Goal failed: {--completedGoals}");
        orig(self, team);
    }

    public static void On_InnerWorkings_MessageReceived(Action<string> orig, string _message)
    {
        char c = _message[0];
        string message = _message.Substring(1);
        string[] array = message.Split([';']);
        if (c == '#' && array.Length == 3)
        {
            var team = GetSteamTestField("team");
            int num = int.Parse(array[0], NumberStyles.Any);
            int num2 = int.Parse(array[1], NumberStyles.Any);
            int num3 = int.Parse(array[2], NumberStyles.Any);
            if (num != -1 && num2 != -1)
            {
                BingoMode.BingoChallenges.BingoChallenge bingoChallenge = BingoMode.BingoHooks.GlobalBoard.challengeGrid[num, num2] as BingoMode.BingoChallenges.BingoChallenge;
                if ((int)team != 8 && BingoMode.BingoData.IsCurrentSaveLockout())
                {
                    if (!bingoChallenge.TeamsCompleted.Any(x => x) && num3 == (int)team)
                        completedGoals--;
                }
                else if (num3 == (int)team)
                {
                    completedGoals--;
                }
            }
        }

        orig(_message);
    }

    public static void On_InnerWorkings_SendMessage(Action<string, SteamNetworkingIdentity, bool> orig, string data, SteamNetworkingIdentity receiver, bool reliable = true)
    {
        orig(data, receiver, reliable);

        var selfIdentity = GetSteamTestField("selfIdentity");
        var GetSteamID64 = selfIdentity?.GetType()?.GetMethod("GetSteamID64", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var GetSteamID = selfIdentity?.GetType()?.GetMethod("GetSteamID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        bool isSelf = receiver.GetSteamID() == (CSteamID?)GetSteamID?.Invoke(selfIdentity, null);
        bool isHost = (ulong?)GetSteamID64?.Invoke(selfIdentity, null) == BingoMode.BingoSteamworks.SteamFinal.GetHost().GetSteamID64();

        if (!isSelf && !isHost)
        {
            string payload = BuildBoardPayload();
            SendBoardAsync(payload);
        }
    }

    public static void On_BroadcastCurrentBoardState(Action orig)
    {
        orig();

        string payload = BuildBoardPayload();
        SendBoardAsync(payload);
    }
}
