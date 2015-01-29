namespace midspace.adminscripts
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Timers;

    using Sandbox.Common.ObjectBuilders;
    using Sandbox.Definitions;
    using Sandbox.ModAPI;
    using Sandbox.ModAPI.Interfaces;

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
    [Sandbox.Common.MySessionComponentDescriptor(Sandbox.Common.MyUpdateOrder.BeforeSimulation)]
    public class ChatCommandLogic : Sandbox.Common.MySessionComponentBase
    {
        #region fields and constants

        public static ChatCommandLogic Instance;
        public ServerConfig ServerCfg;

        private bool _isInitialized;
        private Timer _timer100;
        private bool _100MsTimerElapsed;
        private bool _1000MsTimerElapsed;
        private int _timerCounter = 0;
        private static string[] _oreNames;
        private static List<string> _ingotNames;
        private static MyPhysicalItemDefinition[] _physicalItems;

        #endregion

        #region attaching events and wiring up

        public override void UpdateBeforeSimulation()
        {
            Instance = this;
            // This needs to wait until the MyAPIGateway.Session.Player is created, as running on a Dedicated server can cause issues.
            // It would be nicer to just read a property that indicates this is a dedicated server, and simply return.
            if (!_isInitialized && MyAPIGateway.Session != null && MyAPIGateway.Session.Player != null)
                Init();

            if (!_isInitialized && MyAPIGateway.Utilities != null && MyAPIGateway.Multiplayer != null
                && MyAPIGateway.Session != null && MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Multiplayer.IsServer)
            {
                InitServer();
                return;
            }

            base.UpdateBeforeSimulation();

            if (_100MsTimerElapsed)
            {
                _100MsTimerElapsed = false;
                ChatCommandService.UpdateBeforeSimulation100();
            }

            if (_1000MsTimerElapsed)
            {
                _1000MsTimerElapsed = false;
                ChatCommandService.UpdateBeforeSimulation1000();
            }

            ChatCommandService.UpdateBeforeSimulation();
        }

        protected override void UnloadData()
        {
            DetachEvents();
            base.UnloadData();
        }

        #endregion

        private void Init()
        {
            _isInitialized = true; // Set this first to block any other calls from UpdateBeforeSimulation().
            MyAPIGateway.Utilities.MessageEntered += Utilities_MessageEntered;

            // This will populate the _oreNames, _ingotNames, ready for the ChatCommands.
            BuildResourceLookups();

            // New command classes must be added in here.

            //ChatCommandService.Register(new CommandAsteroidFindOre(_oreNames));
            ChatCommandService.Register(new CommandAsteroidEditClear());
            ChatCommandService.Register(new CommandAsteroidEditSet());
            ChatCommandService.Register(new CommandAsteroidsList());
            //ChatCommandService.Register(new CommandAsteroidRotate());  //not working any more
            //ChatCommandService.Register(new CommandAsteroidSpread());  //not working
            ChatCommandService.Register(new CommandDate());
            ChatCommandService.Register(new CommandFactionDemote());
            ChatCommandService.Register(new CommandFactionJoin());
            ChatCommandService.Register(new CommandFactionKick());
            ChatCommandService.Register(new CommandFactionPromote());
            ChatCommandService.Register(new CommandFactionRemove());
            ChatCommandService.Register(new CommandFlyTo());
            ChatCommandService.Register(new CommandGameName());
            ChatCommandService.Register(new CommandHeading());
            ChatCommandService.Register(new CommandHelloWorld());
            ChatCommandService.Register(new CommandHelp());
            ChatCommandService.Register(new CommandIdentify());
            ChatCommandService.Register(new CommandInventoryAdd(_oreNames, _ingotNames.ToArray(), _physicalItems));
            ChatCommandService.Register(new CommandInventoryClear());
            ChatCommandService.Register(new CommandInventoryDrop(_oreNames, _ingotNames.ToArray(), _physicalItems));
            ChatCommandService.Register(new CommandListBots());
            //ChatCommandService.Register(new CommandListBlueprints()); // no API currently.
            ChatCommandService.Register(new CommandListPrefabs());
            ChatCommandService.Register(new CommandListShips());
            ChatCommandService.Register(new CommandListShips2());
            ChatCommandService.Register(new CommandMessageOfTheDay());
            ChatCommandService.Register(new CommandMeteor(_oreNames));
            ChatCommandService.Register(new CommandObjectsCollect());
            ChatCommandService.Register(new CommandObjectsCount());
            ChatCommandService.Register(new CommandObjectsPull());
            ChatCommandService.Register(new CommandPlayerEject());
            ChatCommandService.Register(new CommandPlayerSlay());
            ChatCommandService.Register(new CommandPlayerSmite(_oreNames));
            //ChatCommandService.Register(new CommandPlayerRespawn());  //not working any more
            ChatCommandService.Register(new CommandPlayerStatus());
            ChatCommandService.Register(new CommandPosition());
            ChatCommandService.Register(new CommandPrefabAdd());
            ChatCommandService.Register(new CommandPrefabAddWireframe());
            ChatCommandService.Register(new CommandPrefabPaste());
            ChatCommandService.Register(new CommandSaveGame());
            ChatCommandService.Register(new CommandSessionCargoShips());
            ChatCommandService.Register(new CommandSessionCopyPaste());
            ChatCommandService.Register(new CommandSessionCreative());
            ChatCommandService.Register(new CommandSetVector());
            ChatCommandService.Register(new CommandShipOff());
            ChatCommandService.Register(new CommandShipOn());
            ChatCommandService.Register(new CommandShipOwnerClaim());
            ChatCommandService.Register(new CommandShipOwnerRevoke());
            //ChatCommandService.Register(new CommandShipOwnerShare());  //not working
            ChatCommandService.Register(new CommandStop());
            ChatCommandService.Register(new CommandTeleport());
            ChatCommandService.Register(new CommandTeleportBack());
            ChatCommandService.Register(new CommandTeleportDelete());
            ChatCommandService.Register(new CommandTeleportFavorite());
            ChatCommandService.Register(new CommandTeleportJump());
            ChatCommandService.Register(new CommandTeleportList());
            ChatCommandService.Register(new CommandTeleportOffset());
            ChatCommandService.Register(new CommandTeleportSave());
            ChatCommandService.Register(new CommandTeleportToPlayer());
            ChatCommandService.Register(new CommandTeleportToShip());
            ChatCommandService.Register(new CommandTest());
            ChatCommandService.Register(new CommandTime());
            ChatCommandService.Register(new CommandVersion());
            //ChatCommandService.Register(new CommandVoxelAdd());  //not working any more
            //ChatCommandService.Register(new CommandVoxelsList()); //not working any more


            _timer100 = new Timer(100);
            _timer100.Elapsed += TimerOnElapsed100;
            _timer100.Start();
            // Attach any other events here.

            ChatCommandService.Init();

            if (MyAPIGateway.Multiplayer.MultiplayerActive) //only need this in mp
            {
                //let the server know we are ready for connections
                MyAPIGateway.Entities.OnEntityAdd += Entities_OnEntityAdd_Client;
                var data = new Dictionary<string, string>();
                data.Add("connect", MyAPIGateway.Session.Player.SteamUserId.ToString());
                ConnectionHelper.CreateAndSendConnectionEntity(ConnectionHelper.BasicPrefix, data);
                ConnectionHelper.SentIdRequest = true;
                CommandMessageOfTheDay.ShowMotdOnSpawn = true;
            }
        }

        /// <summary>
        /// Server side initialization.
        /// </summary>
        private void InitServer()
        {
            _isInitialized = true; // Set this first to block any other calls from UpdateBeforeSimulation().
            ConnectionHelper.ServerPrefix = ConnectionHelper.RandomString(8);
            MyAPIGateway.Entities.OnEntityAdd += Entities_OnEntityAdd_Server;

            MyAPIGateway.Utilities.ConfigDedicated.Load();
            //just to prevent it from being null
            while (MyAPIGateway.Utilities.ConfigDedicated == null)
                ;

            ServerCfg = new ServerConfig();

            var file = string.Format("Motd_{0}.txt", ServerCfg.MotdFileSuffix);

            if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(file, typeof(ChatCommandLogic)))
            {
                CreateConfig(file);
            }

            TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(file, typeof(ChatCommandLogic));
            var text = reader.ReadToEnd();
            if (!string.IsNullOrEmpty(text))
            {
                //prepare MOTD, replace variables
                var dedicatedConfig = MyAPIGateway.Utilities.ConfigDedicated;


                text = text.Replace("%SERVER_NAME%", dedicatedConfig.ServerName);
                text = text.Replace("%WORLD_NAME%", dedicatedConfig.WorldName);
                //text = text.Replace("%SERVER_IP%", dedicatedConfig.IP); returns the 'listen ip' default: 0.0.0.0
                text = text.Replace("%SERVER_PORT%", dedicatedConfig.ServerPort.ToString());

                CommandMessageOfTheDay.MessageOfTheDay = text;
            }
        }

        /// <summary>
        /// Create cfg file
        /// </summary>
        private void CreateConfig(string file)
        {
            TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(file, typeof(ChatCommandLogic));
            writer.Flush();
            writer.Close();
        }

        #region detaching events

        private void DetachEvents()
        {
            if (MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Multiplayer.IsServer)
            {
                MyAPIGateway.Entities.OnEntityAdd -= Entities_OnEntityAdd_Server;
                return;
            }

            MyAPIGateway.Utilities.MessageEntered -= Utilities_MessageEntered;

            if (MyAPIGateway.Multiplayer.MultiplayerActive)
            {
                MyAPIGateway.Entities.OnEntityAdd -= Entities_OnEntityAdd_Client;
            }

            if (_timer100 != null)
            {
                _timer100.Stop();
                _timer100.Elapsed -= TimerOnElapsed100;
            }
        }

        private void TimerOnElapsed100(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            _timerCounter++;

            // Run timed events that do not affect Threading in game.
            _100MsTimerElapsed = true;

            if (_timerCounter % 10 == 0)
                _1000MsTimerElapsed = true;

            // DO NOT SET ANY IN GAME API CALLS HERE. AT ALL!

            if (_timerCounter == 100)
                _timerCounter = 0;
        }

        #endregion

        #region message processing

        private void Utilities_MessageEntered(string messageText, ref bool sendToOthers)
        {
            if (ChatCommandService.ProcessMessage(messageText))
            {
                sendToOthers = false;
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
        }

        #endregion

        #region connection handling

        /// <summary>
        /// Server side processing of incoming connections
        /// </summary>
        /// <param name="entity"></param>
        void Entities_OnEntityAdd_Server(IMyEntity entity)
        {
            if (entity is IMyCubeGrid)
            {
                //we use the different prefixes to prevent failures
                if (entity.DisplayName.StartsWith(ConnectionHelper.ServerPrefix))
                    ConnectionHelper.ProcessServerData(entity.DisplayName.Substring(8));
                else if (entity.DisplayName.StartsWith(ConnectionHelper.BasicPrefix))
                    ConnectionHelper.ProcessIdRequest(entity.DisplayName.Substring(8));
            }
        }

        /// <summary>
        /// Client side processing of incoming connections
        /// </summary>
        /// <param name="entity"></param>
        void Entities_OnEntityAdd_Client(IMyEntity entity)
        {
            if (entity is IMyCubeGrid)
            {
                //if the custom prefix isnt set we assume that it's the 'first contact'
                if (ConnectionHelper.ClientPrefix == null)
                {
                    //if it doesn't start with the basic prefix it's not a connection
                    if (!entity.DisplayName.StartsWith(ConnectionHelper.BasicPrefix) || !ConnectionHelper.SentIdRequest)
                        return;

                    ConnectionHelper.ProcessIdData(entity.DisplayName.Substring(8));
                    return;
                }
                //if there is a custom prefix and the displayname doesn't start with it we can stop here
                if (!entity.DisplayName.StartsWith(ConnectionHelper.ClientPrefix))
                    return;

                ConnectionHelper.ProcessClientData(entity.DisplayName.Substring(8));
            }
            else if (entity is IMyCharacter && CommandMessageOfTheDay.ShowMotdOnSpawn && entity.DisplayName.Equals(MyAPIGateway.Session.Player.DisplayName, StringComparison.InvariantCultureIgnoreCase))
            {
                if (CommandMessageOfTheDay.Received)
                    CommandMessageOfTheDay.ShowMotd();
                else
                    CommandMessageOfTheDay.ShowMotdOnReceive = true;
                CommandMessageOfTheDay.ShowMotdOnSpawn = false;
            }
        }

        #endregion
    }
}