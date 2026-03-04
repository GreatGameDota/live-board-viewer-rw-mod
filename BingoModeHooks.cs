using MonoMod.RuntimeDetour;
using System.Reflection;
using Steamworks;
using MoreSlugcats;
using System.Text.RegularExpressions;

namespace LiveBoardViewer;

public static class BingoModeHooks
{
    public static int completedGoals = 0;
    public static string cachedTime = "";

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
        }

        new Hook(typeof(BingoMode.BingoChallenges.BingoChallenge).GetMethod("OnChallengeCompleted", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), On_OnChallengeCompleted);
        new Hook(typeof(BingoMode.BingoChallenges.BingoChallenge).GetMethod("OnChallengeFailed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), On_OnChallengeFailed);

        new Hook(typeof(BingoMode.BingoData).GetMethod("InitializeBingo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), On_InitializeBingo);
        new Hook(typeof(BingoMode.BingoBoard).GetMethod("InterpretBingoState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), On_InterpretBingoState);
    }

    public static void On_InterpretBingoState(Action<string> orig, string state)
    {
        LiveBoardViewer.logger.LogInfo($"InterpretBingoState called.");
        if (BingoMode.BingoHooks.GlobalBoard.challengeGrid != null)
        {
            string[] array = Regex.Split(state, "<>");
            int num = 0;
            for (int i = 0; i < BingoMode.BingoHooks.GlobalBoard.size; i++)
            {
                for (int j = 0; j < BingoMode.BingoHooks.GlobalBoard.size; j++)
                {
                    if (BingoMode.BingoHooks.GlobalBoard.challengeGrid[j, i] == null)
                    {
                        num++;
                    }
                    else
                    {
                        var team = GetSteamTestField("team");
                        BingoMode.BingoChallenges.BingoChallenge bingoChallenge = BingoMode.BingoHooks.GlobalBoard.challengeGrid[j, i] as BingoMode.BingoChallenges.BingoChallenge;
                        string text = bingoChallenge.TeamsToString();
                        string text2 = array[num++];
                        if (text != text2)
                        {
                            for (int k = 0; k < text.Length; k++)
                            {
                                if (text[k] != text2[k] && text2[k] == '1' && text[k] == '0' && ((int)team == k || !BingoMode.BingoData.IsCurrentSaveLockout()))
                                {
                                    completedGoals--;
                                }
                            }
                        }
                    }
                }
            }
        }
        orig(state);
    }

    public static string BuildBoardPayload()
    {
        string state = BingoMode.BingoHooks.GlobalBoard.GetBingoState();

        var selfIdentity = GetSteamTestField("selfIdentity");
        var GetSteamID = selfIdentity?.GetType()?.GetMethod("GetSteamID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var team = GetSteamTestField("team");

        var name = SteamFriends.GetFriendPersonaName((CSteamID)GetSteamID?.Invoke(selfIdentity, null));
        RainWorldGame? _game = LiveBoardViewer.game.processManager.currentMainLoop as RainWorldGame;
        if (_game != null)
        {
            SpeedRunTimer.CampaignTimeTracker campaignTimeTracker = SpeedRunTimer.GetCampaignTimeTracker(_game.GetStorySession.saveStateNumber);
            cachedTime = campaignTimeTracker?.TotalFreeTimeSpan.GetIGTFormat(true);
        }

        return BingoMode.BingoHooks.GlobalBoard.ToString() + ";;" +
               state.Replace('3', '1') + ";;" +
               name + ";;" +
               team + ";;" +
               cachedTime + ";;" +
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

    public static void On_InnerWorkings_SendMessage(Action<string, SteamNetworkingIdentity, bool> orig, string data, SteamNetworkingIdentity receiver, bool reliable = true)
    {
        orig(data, receiver, reliable);

        var selfIdentity = GetSteamTestField("selfIdentity");
        var GetSteamID64 = selfIdentity?.GetType()?.GetMethod("GetSteamID64", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var GetSteamID = selfIdentity?.GetType()?.GetMethod("GetSteamID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        bool isSelf = receiver.GetSteamID() == (CSteamID?)GetSteamID?.Invoke(selfIdentity, null);
        bool isHost = false;
        if (BingoMode.BingoData.BingoSaves.ContainsKey(Expedition.ExpeditionData.slugcatPlayer))
        {
            isHost = (ulong?)GetSteamID64?.Invoke(selfIdentity, null) == BingoMode.BingoSteamworks.SteamFinal.GetHost().GetSteamID64();
        }

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
