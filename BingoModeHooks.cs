using MonoMod.RuntimeDetour;
using System.Reflection;
using Steamworks;
using MoreSlugcats;
using System.Globalization;

namespace LiveBoardViewer;

public static class BingoModeHooks
{
    public static int completedGoals = 0;

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
            .Where(x => x.IsClass && x.Namespace == "BingoMode.BingoSteamworks")
            .FirstOrDefault(x => x.Name == "InnerWorkings");
        if (InnerWorkings != null)
        {
            new Hook(InnerWorkings.GetMethod("SendMessage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), On_InnerWorkings_SendMessage);
            new Hook(InnerWorkings.GetMethod("MessageReceived", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), On_InnerWorkings_MessageReceived);
        }

        new Hook(typeof(BingoMode.BingoChallenges.BingoChallenge).GetMethod("OnChallengeCompleted", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), On_OnChallengeCompleted);
        new Hook(typeof(BingoMode.BingoChallenges.BingoChallenge).GetMethod("OnChallengeFailed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), On_OnChallengeFailed);

        new Hook(typeof(BingoMode.BingoData).GetMethod("InitializeBingo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), On_InitializeBingo);
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
        orig(self, team);
        LiveBoardViewer.logger.LogInfo($"Completed goals: {++completedGoals}");
    }

    public static void On_OnChallengeFailed(Action<BingoMode.BingoChallenges.BingoChallenge, int> orig, BingoMode.BingoChallenges.BingoChallenge self, int team)
    {
        orig(self, team);

        // BingoMode.BingoSteamworks.SteamTest is an internal class
        var SteamTest = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(x => x.GetTypes())
            .Where(x => x.IsClass && x.Namespace == "BingoMode.BingoSteamworks")
            .FirstOrDefault(x => x.Name == "SteamTest");
        var _team = SteamTest?
            .GetField("team", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?
            .GetValue(null);
        if (self.completed && (int)_team == team)
        {
            LiveBoardViewer.logger.LogInfo($"Goal failed: {--completedGoals}");
        }
    }

    public static void On_InnerWorkings_MessageReceived(Action<string> orig, string _message)
    {
        // BingoMode.BingoSteamworks.SteamTest is an internal class
        var SteamTest = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(x => x.GetTypes())
            .Where(x => x.IsClass && x.Namespace == "BingoMode.BingoSteamworks")
            .FirstOrDefault(x => x.Name == "SteamTest");
        var team = SteamTest?
            .GetField("team", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?
            .GetValue(null);

        char c = _message[0];
        string message = _message.Substring(1);
        string[] array = message.Split([';']);
        if (c <= 'L')
        {
            if (c <= '%')
            {
                if (c != '#')
                {
                }
                else
                {
                    if (array.Length == 3)
                    {
                        int num = int.Parse(array[0], NumberStyles.Any);
                        int num2 = int.Parse(array[1], NumberStyles.Any);
                        int num3 = int.Parse(array[2], NumberStyles.Any);
                        if (num != -1 && num2 != -1)
                        {
                            BingoMode.BingoChallenges.BingoChallenge bingoChallenge = BingoMode.BingoHooks.GlobalBoard.challengeGrid[num, num2] as BingoMode.BingoChallenges.BingoChallenge;
                            if ((int)team != 8 && BingoMode.BingoData.IsCurrentSaveLockout())
                            {
                                if (!bingoChallenge.TeamsCompleted.Any(x => x))
                                {
                                    if (num3 != (int)team)
                                    {
                                        // bingoChallenge.OnChallengeLockedOut(num3);
                                    }
                                    else
                                    {
                                        // bingoChallenge.OnChallengeCompleted(num3);
                                        completedGoals--;
                                    }
                                }
                            }
                            else
                            {
                                // bingoChallenge.OnChallengeCompleted(num3);
                                completedGoals--;
                            }
                        }
                    }
                }
            }
        }
        else if (c <= 'U')
        {
        }
        else if (c != '^')
        {
        }
        else
        {
            if (array.Length == 3)
            {
                int num4 = int.Parse(array[0], NumberStyles.Any);
                int num5 = int.Parse(array[1], NumberStyles.Any);
                int _team = int.Parse(array[2], NumberStyles.Any);
                if (num4 != -1 && num5 != -1)
                {
                    if ((int)team == _team)
                        completedGoals++;
                    // (BingoHooks.GlobalBoard.challengeGrid[num4, num5] as BingoChallenge).OnChallengeFailed(team);
                    // SteamFinal.BroadcastCurrentBoardState();
                }
            }
        }

        orig(_message);

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
            var GetSteamID = selfIdentity?
                .GetType()?
                .GetMethod("GetSteamID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var team = SteamTest?
                .GetField("team", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?
                .GetValue(null);

            var name = SteamFriends.GetFriendPersonaName((CSteamID)GetSteamID?.Invoke(selfIdentity, null));
            RainWorldGame _game = (RainWorldGame)LiveBoardViewer.game.processManager.currentMainLoop;
            SpeedRunTimer.CampaignTimeTracker campaignTimeTracker = SpeedRunTimer.GetCampaignTimeTracker(_game.GetStorySession.saveStateNumber);
            LiveBoardViewer.wsConnection.SendAsync(
                BingoMode.BingoHooks.GlobalBoard.ToString() + ";;" +
                state.Replace('3', '1') + ";;" +
                name + ";;" +
                team + ";;" +
                campaignTimeTracker?.TotalFreeTimeSpan.GetIGTFormat(true) + ";;" +
                completedGoals
            ).Wait();
            // wsConnection.DisconnectAsync();
        });
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

                    var name = SteamFriends.GetFriendPersonaName((CSteamID)GetSteamID?.Invoke(selfIdentity, null));

                    string state = BingoMode.BingoHooks.GlobalBoard.GetBingoState();
                    RainWorldGame _game = (RainWorldGame)LiveBoardViewer.game.processManager.currentMainLoop;
                    SpeedRunTimer.CampaignTimeTracker campaignTimeTracker = SpeedRunTimer.GetCampaignTimeTracker(_game.GetStorySession.saveStateNumber);
                    LiveBoardViewer.wsConnection.SendAsync(
                        BingoMode.BingoHooks.GlobalBoard.ToString() + ";;" +
                        state.Replace('3', '1') + ";;" +
                        name + ";;" +
                        team + ";;" +
                        campaignTimeTracker?.TotalFreeTimeSpan.GetIGTFormat(true) + ";;" +
                        completedGoals
                    ).Wait();
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
            var GetSteamID = selfIdentity?
                .GetType()?
                .GetMethod("GetSteamID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var team = SteamTest?
                .GetField("team", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?
                .GetValue(null);

            var name = SteamFriends.GetFriendPersonaName((CSteamID)GetSteamID?.Invoke(selfIdentity, null));
            RainWorldGame _game = (RainWorldGame)LiveBoardViewer.game.processManager.currentMainLoop;
            SpeedRunTimer.CampaignTimeTracker campaignTimeTracker = SpeedRunTimer.GetCampaignTimeTracker(_game.GetStorySession.saveStateNumber);
            LiveBoardViewer.wsConnection.SendAsync(
                BingoMode.BingoHooks.GlobalBoard.ToString() + ";;" +
                state.Replace('3', '1') + ";;" +
                name + ";;" +
                team + ";;" +
                campaignTimeTracker?.TotalFreeTimeSpan.GetIGTFormat(true) + ";;" +
                completedGoals
            ).Wait();
            // wsConnection.DisconnectAsync();
        });

        orig();
    }
}
