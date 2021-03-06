﻿namespace midspace.adminscripts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;

    using Sandbox.ModAPI;
    using VRage.Game.ModAPI;
    using VRage.ModAPI;

    public class CommandAsteroidsList : ChatCommand
    {
        /// <summary>
        /// Temporary hotlist cache created when player requests a list of in game asteroids, populated only by search results.
        /// </summary>
        private readonly static Dictionary<ulong, List<IMyVoxelBase>> ServerAsteroidCache = new Dictionary<ulong, List<IMyVoxelBase>>();

        public CommandAsteroidsList()
            : base(ChatCommandSecurity.Admin, ChatCommandFlag.Server, "listasteroids", new[] { "/listasteroids" })
        {
            ServerAsteroidCache.Clear();
        }

        public override void Help(ulong steamId, bool brief)
        {
            MyAPIGateway.Utilities.ShowMessage("/listasteroids <filter>", "List in-game asteroids. Optional <filter> to refine your search by name.");
        }

        public override bool Invoke(ulong steamId, long playerId, string messageText)
        {
            if (messageText.StartsWith("/listasteroids", StringComparison.InvariantCultureIgnoreCase))
            {
                string asteroidName = null;
                var match = Regex.Match(messageText, @"/listasteroids\s{1,}(?<Key>.+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    asteroidName = match.Groups["Key"].Value;
                }

                var currentAsteroidList = new List<IMyVoxelBase>();
                MyAPIGateway.Session.VoxelMaps.GetInstances(currentAsteroidList, v => v is IMyVoxelMap && (asteroidName == null || v.StorageName.IndexOf(asteroidName, StringComparison.InvariantCultureIgnoreCase) >= 0));

                ServerAsteroidCache[steamId] = new List<IMyVoxelBase>();

                // Only display the list in chat if the chat allows to fully show it, else display it in a mission screen.
                if (currentAsteroidList.Count <= 9)
                {
                    MyAPIGateway.Utilities.SendMessage(steamId, "Count", currentAsteroidList.Count.ToString());
                    var index = 1;
                    foreach (var voxelMap in currentAsteroidList)
                    {
                        ServerAsteroidCache[steamId].Add(voxelMap);
                        MyAPIGateway.Utilities.SendMessage(steamId, string.Format("#{0}", index++), voxelMap.StorageName);
                    }
                }
                else
                {
                    var description = new StringBuilder();
                    var prefix = string.Format("Count: {0}", currentAsteroidList.Count);
                    var index = 1;
                    foreach (var voxelMap in currentAsteroidList.OrderBy(s => s.StorageName))
                    {
                        ServerAsteroidCache[steamId].Add(voxelMap);
                        description.AppendFormat("#{0}: {1}\r\n", index++, voxelMap.StorageName);
                    }

                    MyAPIGateway.Utilities.SendMissionScreen(steamId, "List Asteroids", prefix, " ", description.ToString());
                }

                return true;
            }

            return false;
        }

        public static List<IMyVoxelBase> GetAsteroidCache(ulong steamId)
        {
            List<IMyVoxelBase> cacheList;
            if (!ServerAsteroidCache.TryGetValue(steamId, out cacheList))
            {
                ServerAsteroidCache.Add(steamId, new List<IMyVoxelBase>());
                cacheList = ServerAsteroidCache[steamId];
            }
            return cacheList;
        }
    }
}
