using midspace.adminscripts.Utils.Timer;

namespace midspace.adminscripts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Timers;
    using midspace.adminscripts.Messages;
    using midspace.adminscripts.Protection;
    using midspace.adminscripts.Protection.Commands;
    using Sandbox.Common.ObjectBuilders;
    using Sandbox.Definitions;
    using Sandbox.ModAPI;
    using VRage.Game;
    using VRage.Game.Components;
    using VRage.Game.ModAPI;

    /// <summary>
    /// Adds special chat commands, allowing the player to get their position, date, time, change their location on the map.
    /// Authors: Midspace. AKA Screaming Angels. & Sp[a]cemarine.
    /// 
    /// The main Steam workshop link to this mod is:
    /// http://steamcommunity.com/sharedfiles/filedetails/?id=316190120
    /// 
    /// My other Steam workshop items:
    /// http://steamcommunity.com/id/ScreamingAngels/myworkshopfiles/?appid=244850
    /// </summary>
    /// <example>
    /// To use, simply open the chat window, and enter "/command", where command is one of the specified.
    /// Enter "/help" or "/help command" for more detail on individual commands.
    /// Chat commands do not have to start with "/". This model allows practically any text to become a command.
    /// Each ChatCommand can determine what it's own allowable command is.
    /// </example>
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class ChatCommandLogic : MySessionComponentBase
    {
        #region fields and constants

        public static ChatCommandLogic Instance;
        public ServerConfig ServerCfg;
        public AdminNotification AdminNotification;
        public bool ShowDialogsOnReceive = false;

        public ThreadsafeTimer PermissionRequestTimer;
        public bool BlockCommandExecution = false;
        public bool AllowBuilding = false;

        private bool _isInitialized;
        private bool _commandsRegistered;
        private ThreadsafeTimer _timer100;
        private int _timerCounter = 0;
        private static string[] _oreNames;
        private static List<string> _ingotNames;
        private static List<string> _botModelNames;
        private static MyPhysicalItemDefinition[] _physicalItems;

        private Action<byte[]> MessageHandler = new Action<byte[]>(HandleMessage);

        /// <summary>
        /// Set manually to true for testing purposes. No need for this function in general.
        /// </summary>
        public bool Debug = false;

        #endregion

        #region attaching events and wiring up

        public override void UpdateBeforeSimulation()
        {
            Instance = this;
            // This needs to wait until the MyAPIGateway.Session.Player is created, as running on a Dedicated server can cause issues.
            // It would be nicer to just read a property that indicates this is a dedicated server, and simply return.
            if (!_isInitialized && MyAPIGateway.Session != null && MyAPIGateway.Session.Player != null)
            {
                Debug = MyAPIGateway.Session.Player.IsExperimentalCreator();

                if (!MyAPIGateway.Session.OnlineMode.Equals(MyOnlineModeEnum.OFFLINE) && MyAPIGateway.Multiplayer.IsServer && !MyAPIGateway.Utilities.IsDedicated)
                    InitServer();
                Init();
            }
            if (!_isInitialized && MyAPIGateway.Utilities != null && MyAPIGateway.Multiplayer != null
                && MyAPIGateway.Session != null && MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Multiplayer.IsServer)
            {
                InitServer();
                return;
            }

            base.UpdateBeforeSimulation();

            ChatCommandService.UpdateBeforeSimulation();
            TimerRegistry.Update();
        }

        protected override void UnloadData()
        {
            Logger.Debug("Closing...");
            DetachEvents();

            if (_commandsRegistered)
                ChatCommandService.DisposeCommands();

            Logger.Terminate();
            base.UnloadData();
        }

        public override void SaveData()
        {
            base.SaveData();

            if (ServerCfg != null)
                ServerCfg.Save();
        }

        #endregion

        private void Init()
        {
            _isInitialized = true; // Set this first to block any other calls from UpdateBeforeSimulation().
            Logger.Init();
            MyAPIGateway.Utilities.MessageEntered += Utilities_MessageEntered;
            Logger.Debug("Attach MessageEntered");
            VRage.Utils.MyLog.Default.WriteLine("##Mod## Admin Helper Client Initialisation");


            _timer100 = new ThreadsafeTimer(100);
            _timer100.Elapsed += TimerOnElapsed100;
            _timer100.Start();
            // Attach any other events here.

            if (!_commandsRegistered)
            {
                foreach (ChatCommand command in GetAllChatCommands())
                    ChatCommandService.Register(command);
                _commandsRegistered = true;

                ChatCommandService.Init();
            }

            //MultiplayerActive is false when initializing host... extreamly weird
            if (MyAPIGateway.Multiplayer.MultiplayerActive || ServerCfg != null) //only need this in mp
            {
                MyAPIGateway.Session.OnSessionReady += Session_OnSessionReady;
                Logger.Debug("Attach Session_OnSessionReady");
                if (ServerCfg == null) // if the config is already present, the messagehandler is also already registered
                {
                    MyAPIGateway.Multiplayer.RegisterMessageHandler(ConnectionHelper.ConnectionId, MessageHandler);
                    Logger.Debug("Registered ProcessMessage");
                }
                BlockCommandExecution = true;
                PermissionRequestTimer = new ThreadsafeTimer(10000);
                PermissionRequestTimer.Elapsed += PermissionRequestTimer_Elapsed;
                PermissionRequestTimer.Start();
                // tell the server that we need everything now, permissions, protection, etc.
                ConnectionHelper.SendMessageToServer(new MessageConnectionRequest());
            }
        }

        /// <summary>
        /// Server side initialization.
        /// </summary>
        private void InitServer()
        {
            //Debug = true;
            _isInitialized = true; // Set this first to block any other calls from UpdateBeforeSimulation().
            Logger.Init();
            Logger.Debug("Server Logger started");
            VRage.Utils.MyLog.Default.WriteLine("##Mod## Admin Helper Server Initialisation");

            // TODO: restructure the ChatCommandLogic to encapsulate ChatCommandService on the server side.
            // Required to check security for user calls on the Server side, and call the UpdateBeforeSimulation...() methods for each command.
            if (!_commandsRegistered)
            {
                foreach (ChatCommand command in GetAllChatCommands())
                    ChatCommandService.Register(command);
                _commandsRegistered = true;

                ChatCommandService.Init();
            }

            AdminNotificator.Init();
            ProtectionHandler.Init_Server();
            MyAPIGateway.Multiplayer.RegisterMessageHandler(ConnectionHelper.ConnectionId, MessageHandler);
            Logger.Debug("Registered ProcessMessage");

            ServerCfg = new ServerConfig(GetAllChatCommands());

            Sandbox.Game.MyVisualScriptLogicProvider.PlayerConnected += PlayerConnected;
            Sandbox.Game.MyVisualScriptLogicProvider.PlayerDropped += PlayerDropped;
            Sandbox.Game.MyVisualScriptLogicProvider.PlayerDisconnected += PlayerDisconnected;
        }

        private List<ChatCommand> GetAllChatCommands()
        {
            // This will populate the _oreNames, _ingotNames, ready for the ChatCommands.
            BuildResourceLookups();

            List<ChatCommand> commands = new List<ChatCommand>();
            // New command classes must be added in here.

            //commands.Add(new CommandAsteroidFindOre(_oreNames));
            commands.Add(new CommandAsteroidScanOre());
            //commands.Add(new CommandAsteroidEditClear());
            //commands.Add(new CommandAsteroidEditSet());
            commands.Add(new CommandAsteroidFill());
            commands.Add(new CommandAsteroidReplace());
            commands.Add(new CommandAsteroidCreate());
            commands.Add(new CommandAsteroidCreateSphere());
            commands.Add(new CommandAsteroidsList());
            commands.Add(new CommandPlanetsList());
            commands.Add(new CommandPlanetDelete());
            commands.Add(new CommandAsteroidRotate());
            //commands.Add(new CommandAsteroidSpread()); //not working
            commands.Add(new CommandChatHistory());
            commands.Add(new CommandVoxelAdd());
            commands.Add(new CommandVoxelsList());
            commands.Add(new CommandConfig());
            commands.Add(new CommandDate());
            commands.Add(new CommandExtendedListShips());
            commands.Add(new CommandFactionDemote());
            commands.Add(new CommandFactionJoin());
            commands.Add(new CommandFactionKick());
            commands.Add(new CommandFactionPromote());
            commands.Add(new CommandFactionRemove());
            commands.Add(new CommandFactionPeace());
            commands.Add(new CommandForceBan());
            commands.Add(new CommandForceKick());
            commands.Add(new CommandFlyTo());
            commands.Add(new CommandGameName());
            commands.Add(new CommandGodMode());
            commands.Add(new CommandHeading());
            commands.Add(new CommandHelloWorld());
            commands.Add(new CommandLaserUpDown());
            commands.Add(new CommandSunTrack());
            commands.Add(new CommandLaserRangefinder());
            commands.Add(new CommandSettings());
            commands.Add(new CommandHelp());
            commands.Add(new CommandIdentify());
            commands.Add(new CommandDetail());
            commands.Add(new CommandInventoryAdd(_oreNames, _ingotNames.ToArray(), _physicalItems));
            commands.Add(new CommandInventoryInsert(_oreNames, _ingotNames.ToArray(), _physicalItems));
            commands.Add(new CommandInventoryClear());
            commands.Add(new CommandInventoryDrop(_oreNames, _ingotNames.ToArray(), _physicalItems));
            commands.Add(new CommandListBots());
            //commands.Add(new CommandListBlueprints()); // no API currently.
            commands.Add(new CommandListPrefabs());
            commands.Add(new CommandListShips());
            commands.Add(new CommandMessageOfTheDay());
            commands.Add(new CommandBomb());
            commands.Add(new CommandInvisible());
            commands.Add(new CommandMeteor(_oreNames[0]));
            commands.Add(new CommandAddSpider());
            commands.Add(new CommandAddWolf());
            commands.Add(new CommandObjectsCollect());
            commands.Add(new CommandObjectsDelete());
            commands.Add(new CommandObjectsCount());
            commands.Add(new CommandObjectsPull());
            commands.Add(new CommandPardon());
            commands.Add(new CommandPermission());
            commands.Add(new CommandPlayerEject());
            commands.Add(new CommandPlayerSlay());
            commands.Add(new CommandPlayerSlap());
            commands.Add(new CommandPlayerSmite(_oreNames[0]));
            //commands.Add(new CommandPlayerRespawn());  //not working any more
            commands.Add(new CommandPlayerStatus());
            commands.Add(new CommandPosition());
            commands.Add(new CommandPrefabAdd());
            commands.Add(new CommandPrefabAddDrone());
            commands.Add(new CommandPrefabAddWireframe());
            //commands.Add(new CommandPrefabPaste());  //not working any more
            commands.Add(new CommandPrivateMessage());
            commands.Add(new CommandFactionChat());
            commands.Add(new CommandProtectionArea());
            commands.Add(new CommandSaveGame());
            commands.Add(new CommandSessionCargoShips());
            commands.Add(new CommandSessionCopyPaste());
            commands.Add(new CommandSessionCreative());
            commands.Add(new CommandSessionSpectator());
            commands.Add(new CommandSessionWeapons());
            commands.Add(new CommandSessionSpiders());
            commands.Add(new CommandSessionWolves());
            commands.Add(new CommandSetVector());
            commands.Add(new CommandSpeed());
            commands.Add(new CommandShipOff());
            commands.Add(new CommandShipOn());
            commands.Add(new CommandStationToShip());
            commands.Add(new CommandShipToStation());
            commands.Add(new CommandShipSwitch());
            commands.Add(new CommandShipOwnerClaim());
            commands.Add(new CommandShipOwnerRevoke());
            commands.Add(new CommandShipBuiltBy());
            commands.Add(new CommandShipDelete());
            commands.Add(new CommandShipRepair());
            commands.Add(new CommandShipMirror());
            commands.Add(new CommandShipDestructible());
            commands.Add(new CommandShipScaleDown());
            commands.Add(new CommandShipScaleUp());
            commands.Add(new CommandShipOwnerShare());
            commands.Add(new CommandShipCubeRename());
            commands.Add(new CommandShipCubeRenumber());
            commands.Add(new CommandStop());
            commands.Add(new CommandStopAll());
            commands.Add(new CommandTeleport());
            commands.Add(new CommandTeleportBack());
            commands.Add(new CommandTeleportJump());
            commands.Add(new CommandTeleportOffset());
            commands.Add(new CommandTeleportToPlayer());
            commands.Add(new CommandTeleportToShip());
            commands.Add(new CommandTest());
            commands.Add(new CommandTime());

            return commands;
        }

        #region detaching events

        private void DetachEvents()
        {
            TimerRegistry.Close();

            Sandbox.Game.MyVisualScriptLogicProvider.PlayerConnected = null;
            Sandbox.Game.MyVisualScriptLogicProvider.PlayerDropped = null;
            Sandbox.Game.MyVisualScriptLogicProvider.PlayerDisconnected = null;

            // servers: listen and dedicated, MP
            if (ServerCfg != null)
            {
                MyAPIGateway.Multiplayer.UnregisterMessageHandler(ConnectionHelper.ConnectionId, MessageHandler);
                ProtectionHandler.Close();
                Logger.Debug("Unregistered MessageHandler");
            }

            if (MyAPIGateway.Utilities != null && MyAPIGateway.Multiplayer != null && MyAPIGateway.Multiplayer.IsServer && MyAPIGateway.Utilities.IsDedicated)
                return;

            if (MyAPIGateway.Multiplayer != null && MyAPIGateway.Multiplayer.MultiplayerActive || (ServerCfg != null && ServerConfig.ServerIsClient))
            {
                // all clients, including hosts, MP
                if (PermissionRequestTimer != null)
                {
                    PermissionRequestTimer.Stop();
                    PermissionRequestTimer.Close();
                }
                MyAPIGateway.Session.OnSessionReady -= Session_OnSessionReady;
                Logger.Debug("Detached Session_OnSessionReady");

                // only clients, not the host
                if (ServerCfg == null)
                {
                    MyAPIGateway.Multiplayer.UnregisterMessageHandler(ConnectionHelper.ConnectionId, MessageHandler);
                    Logger.Debug("Unregistered MessageHandler");
                }
            }

            if (MyAPIGateway.Utilities != null)
            {
                MyAPIGateway.Utilities.MessageEntered -= Utilities_MessageEntered;
                Logger.Debug("Detached MessageEntered");
            }
        }

        private void TimerOnElapsed100(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            _timerCounter++;

            if (_timerCounter % 10 == 0)
                ChatCommandService.UpdateBeforeSimulation1000();

            if (_timerCounter == 100)
                _timerCounter = 0;

            ChatCommandService.UpdateBeforeSimulation100();
        }

        void PermissionRequestTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            ConnectionHelper.SendMessageToServer(new MessagePermissionRequest());
        }

        #endregion

        #region message processing

        private void Utilities_MessageEntered(string messageText, ref bool sendToOthers)
        {
            if (ChatCommandService.ProcessClientMessage(messageText))
                sendToOthers = false;
            else
            {
                var globalMessage = new MessageGlobalMessage
                {
                    ChatMessage = new ChatMessage
                    {
                        Text = messageText,
                        Sender = new Player
                        {
                            SteamId = MyAPIGateway.Session.Player.SteamUserId,
                            PlayerName = MyAPIGateway.Session.Player.DisplayName
                        },
                    }
                };
                ConnectionHelper.SendMessageToServer(globalMessage);
            }
        }

        #endregion

        #region helpers

        private static void BuildResourceLookups()
        {
            MyDefinitionManager.Static.GetOreTypeNames(out _oreNames);
            var physicalItems = MyDefinitionManager.Static.GetPhysicalItemDefinitions();
            _physicalItems = physicalItems.Where(item => item.Public).ToArray();  // Limit to public items.  This will remove the CubePlacer. :)
            _ingotNames = new List<string>();

            foreach (var physicalItem in _physicalItems)
            {
                if (physicalItem.Id.TypeId == typeof(MyObjectBuilder_Ingot))
                {
                    _ingotNames.Add(physicalItem.Id.SubtypeName);
                }
            }

            _botModelNames = MyDefinitionManager.Static.GetBotDefinitions().Where(e => e is MyAgentDefinition).Cast<MyAgentDefinition>().Select(e => e.BotModel).ToList();
        }

        #endregion

        void Session_OnSessionReady()
        {
            if (CommandMessageOfTheDay.Received && !String.IsNullOrEmpty(CommandMessageOfTheDay.Content))
                CommandMessageOfTheDay.ShowMotd();

            if (AdminNotification != null)
                AdminNotification.Show();

            ShowDialogsOnReceive = true;
        }

        #region connection handling

        private static void HandleMessage(byte[] message)
        {
            Logger.Debug("-- HandleMessage: --");
            Logger.Debug("--------------------");
            Logger.Debug(string.Format("{0}", System.Text.Encoding.Unicode.GetString(message)));
            Logger.Debug("--------------------");
            ConnectionHelper.ProcessData(message);
        }

        #endregion

        #region connection/disconnection events.
        // TODO: these should be added to the in game -Global Chat History-

        // player exited/crashed/quit.
        private void PlayerDisconnected(long playerId)
        {
            string displayName;
            if (!GetPlayerDisplayName(playerId, out displayName))
            {
                ChatCommandLogic.Instance.ServerCfg.LogGlobalMessage(
                    new ChatMessage
                    {
                        Date = DateTime.Now,
                        Sender = new Player { PlayerName = "Server", SteamId = MyAPIGateway.Multiplayer.ServerId },
                        Text = $"'{displayName}' disconnected"
                    });
            }
        }

        // I've never seen 'Dropped'.
        private void PlayerDropped(string itemTypeName, string itemSubTypeName, long playerId, int amount)
        {
            string displayName;
            if (!GetPlayerDisplayName(playerId, out displayName))
            {
                ChatCommandLogic.Instance.ServerCfg.LogGlobalMessage(
                    new ChatMessage
                    {
                        Date = DateTime.Now,
                        Sender = new Player { PlayerName = "Server", SteamId = MyAPIGateway.Multiplayer.ServerId },
                        Text = $"'{displayName}' dropped"
                    });
            }
        }

        // player connect/reconnected.
        private void PlayerConnected(long playerId)
        {
            string displayName;
            if (!GetPlayerDisplayName(playerId, out displayName))
            {
                ChatCommandLogic.Instance.ServerCfg.LogGlobalMessage(
                    new ChatMessage
                    {
                        Date = DateTime.Now,
                        Sender = new Player { PlayerName = "Server", SteamId = MyAPIGateway.Multiplayer.ServerId },
                        Text = $"'{displayName}' joined server"
                    });
            }
        }

        private bool GetPlayerDisplayName(long identityId, out string displayName)
        {
            displayName = "Unknown";
            bool isBot = false;

            IMyPlayer player;
            MyAPIGateway.Players.TryGetPlayer(identityId, out player);
            if (player != null)
            {
                isBot = player.IsBot;
                displayName = player.DisplayName;
            }
            else
            {
                IMyIdentity identity;
                MyAPIGateway.Players.TryGetIdentity(identityId, out identity);

                if (identity != null)
                {
                    displayName = identity.DisplayName;

                    // Making a strong assumption, that if the name of the identity is blank, then it's a bot.
                    isBot = string.IsNullOrEmpty(displayName) && _botModelNames.Contains(identity.Model);
                }
            }
            return isBot;
        }

        #endregion

    }
}