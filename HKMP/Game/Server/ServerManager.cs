﻿using System.Collections.Generic;
using System.Diagnostics;
using HKMP.Networking;
using HKMP.Networking.Packet;
using HKMP.Networking.Packet.Custom;
using HKMP.Networking.Server;
using HKMP.Util;
using Modding;
using UnityEngine;

namespace HKMP.Game.Server {
    /**
     * Class that manages the server state (similar to ClientManager).
     * For example the current scene of each player, to prevent sending redundant traffic.
     */
    public class ServerManager {
        // TODO: decide whether it is better to always transmit entire PlayerData objects instead of
        // multiple packets (one for position, one for scale, one for animation, etc.)
        private const int ConnectionTimeout = 5000;

        private readonly NetServer _netServer;

        private readonly Game.Settings.GameSettings _gameSettings;

        private readonly Dictionary<int, PlayerData> _playerData;
        
        public ServerManager(NetworkManager networkManager, Game.Settings.GameSettings gameSettings, PacketManager packetManager) {
            _netServer = networkManager.GetNetServer();
            _gameSettings = gameSettings;

            _playerData = new Dictionary<int, PlayerData>();

            // Register packet handlers
            packetManager.RegisterServerPacketHandler<HelloServerPacket>(PacketId.HelloServer, OnHelloServer);
            packetManager.RegisterServerPacketHandler<PlayerChangeScenePacket>(PacketId.PlayerChangeScene, OnClientChangeScene);
            packetManager.RegisterServerPacketHandler<ServerPlayerUpdatePacket>(PacketId.PlayerUpdate, OnPlayerUpdate);
            packetManager.RegisterServerPacketHandler<ServerPlayerDisconnectPacket>(PacketId.PlayerDisconnect, OnPlayerDisconnect);
            packetManager.RegisterServerPacketHandler<ServerPlayerDeathPacket>(PacketId.PlayerDeath, OnPlayerDeath);
            packetManager.RegisterServerPacketHandler<ServerDreamshieldSpawnPacket>(PacketId.DreamshieldSpawn, OnDreamshieldSpawn);
            packetManager.RegisterServerPacketHandler<ServerDreamshieldDespawnPacket>(PacketId.DreamshieldDespawn, OnDreamshieldDespawn);
            packetManager.RegisterServerPacketHandler<ServerDreamshieldUpdatePacket>(PacketId.DreamshieldUpdate, OnDreamshieldUpdate);
            
            // Register server shutdown handler
            _netServer.RegisterOnShutdown(OnServerShutdown);
            
            // Register application quit handler
            ModHooks.Instance.ApplicationQuitHook += OnApplicationQuit;
        }

        /**
         * Starts a server with the given port
         */
        public void Start(int port) {
            // Stop existing server
            if (_netServer.IsStarted) {
                Logger.Warn(this, "Server was running, shutting it down before starting");
                _netServer.Stop();
            }

            // Start server again with given port
            _netServer.Start(port);

            MonoBehaviourUtil.Instance.OnUpdateEvent += CheckHeartBeat;
        }

        /**
         * Stops the currently running server
         */
        public void Stop() {
            if (_netServer.IsStarted) {
                // Before shutting down, send TCP packets to all clients indicating
                // that the server is shutting down
                _netServer.BroadcastTcp(new ServerShutdownPacket().CreatePacket());
                
                _netServer.Stop();
            } else {
                Logger.Warn(this, "Could not stop server, it was not started");
            }
        }

        /**
         * Called when the game settings are updated, and need to be broadcast
         */
        public void OnUpdateGameSettings() {
            if (!_netServer.IsStarted) {
                return;
            }
        
            var settingsUpdatePacket = new GameSettingsUpdatePacket {
                GameSettings = _gameSettings
            };
            settingsUpdatePacket.CreatePacket();
            
            _netServer.BroadcastTcp(settingsUpdatePacket);
        }

        private void OnHelloServer(int id, HelloServerPacket packet) {
            Logger.Info(this, $"Received Hello packet from ID {id}");
            
            // Start by sending the new client the current Server Settings
            var settingsUpdatePacket = new GameSettingsUpdatePacket {
                GameSettings = _gameSettings
            };
            settingsUpdatePacket.CreatePacket();
            
            _netServer.SendTcp(id, settingsUpdatePacket);
            
            // Read username from packet
            var username = packet.Username;

            // Read scene name from packet
            var sceneName = packet.SceneName;
            
            // Read the rest of the data, since we know that we have it
            var position = packet.Position;
            var scale = packet.Scale;
            var currentClip = packet.AnimationClipName;
            
            // Create new player data object
            var playerData = new PlayerData(
                username,
                sceneName,
                position,
                scale,
                currentClip
            );
            // Store data in mapping
            _playerData[id] = playerData;

            // Create PlayerEnterScene packet
            var enterScenePacket = new PlayerEnterScenePacket {
                Id = id,
                Username = username,
                Position = position,
                Scale = scale,
                AnimationClipName = currentClip
            };
            enterScenePacket.CreatePacket();
            
            // Send the packets to all clients in the same scene except the source client
            foreach (var idPlayerDataPair in _playerData) {
                if (idPlayerDataPair.Key == id) {
                    continue;
                }

                var otherPlayerData = idPlayerDataPair.Value;
                if (otherPlayerData.CurrentScene.Equals(sceneName)) {
                    _netServer.SendTcp(idPlayerDataPair.Key, enterScenePacket);
                    
                    // Also send the source client a packet that this player is in their scene
                    var alreadyInScenePacket = new PlayerEnterScenePacket {
                        Id = idPlayerDataPair.Key,
                        Username = otherPlayerData.Name,
                        Position = otherPlayerData.LastPosition,
                        Scale = otherPlayerData.LastScale,
                        AnimationClipName = otherPlayerData.LastAnimationClip
                    };
                    alreadyInScenePacket.CreatePacket();
                    
                    _netServer.SendTcp(id, alreadyInScenePacket);
                }
                
                // Send the source client a map update packet of the last location of the other players
                // var mapUpdatePacket = new ClientPlayerMapUpdatePacket {
                //     Id = idPlayerDataPair.Key,
                //     Position = otherPlayerData.LastMapLocation
                // };
                // mapUpdatePacket.CreatePacket();
                //
                // _netServer.SendUdp(id, mapUpdatePacket);
            }
        }
        
        private void OnClientChangeScene(int id, PlayerChangeScenePacket packet) {
            // Initialize with default value, override if mapping has key
            var oldSceneName = "NonGameplay";
            if (_playerData.ContainsKey(id)) {
                oldSceneName = _playerData[id].CurrentScene;                
            }

            var newSceneName = packet.NewSceneName;
            
            // Check whether the scene has changed, it might not change if
            // a player died and respawned in the same scene
            if (oldSceneName.Equals(newSceneName)) {
                Logger.Warn(this, $"Received SceneChange packet from ID {id}, from and to {oldSceneName}, probably a Death event");
            } else {
                Logger.Info(this, $"Received SceneChange packet from ID {id}, from {oldSceneName} to {newSceneName}");
            }

            // Read the position and scale in the new scene
            var position = packet.Position;
            var scale = packet.Scale;
            var animationClipName = packet.AnimationClipName;
            
            // Store it in their PlayerData object
            var playerData = _playerData[id];
            playerData.CurrentScene = newSceneName;
            playerData.LastPosition = position;
            playerData.LastScale = scale;
            playerData.LastAnimationClip = animationClipName;
            
            // Create packets in advance
            // Create a PlayerLeaveScene packet containing the ID
            // of the player leaving the scene
            var leaveScenePacket = new PlayerLeaveScenePacket {
                Id = id
            };
            leaveScenePacket.CreatePacket();
            
            // Create a PlayerEnterScene packet containing the ID
            // of the player entering the scene and their position
            var enterScenePacket = new PlayerEnterScenePacket {
                Id = id,
                Username = playerData.Name,
                Position = position,
                Scale = scale,
                AnimationClipName = animationClipName
            };
            enterScenePacket.CreatePacket();
            
            foreach (var idPlayerDataPair in _playerData) {
                // Skip source player
                if (idPlayerDataPair.Key == id) {
                    continue;
                }

                var otherPlayerData = idPlayerDataPair.Value;
                
                // Send the packet to all clients on the old scene
                // to indicate that this client has left their scene
                if (otherPlayerData.CurrentScene.Equals(oldSceneName)) {
                    Logger.Info(this, $"Sending leave scene packet to {idPlayerDataPair.Key}");
                    _netServer.SendTcp(idPlayerDataPair.Key, leaveScenePacket);
                }
                
                // Send the packet to all clients on the new scene
                // to indicate that this client has entered their scene
                if (otherPlayerData.CurrentScene.Equals(newSceneName)) {
                    Logger.Info(this, $"Sending enter scene packet to {idPlayerDataPair.Key}");
                    _netServer.SendTcp(idPlayerDataPair.Key, enterScenePacket);
                    
                    Logger.Info(this, $"Sending that {idPlayerDataPair.Key} is already in scene to {id}");
                    
                    // Also send a packet to the client that switched scenes,
                    // notifying that these players are already in this new scene
                    var alreadyInScenePacket = new PlayerEnterScenePacket {
                        Id = idPlayerDataPair.Key,
                        Username = otherPlayerData.Name,
                        Position = otherPlayerData.LastPosition,
                        Scale = otherPlayerData.LastScale,
                        AnimationClipName = otherPlayerData.LastAnimationClip
                    };
                    alreadyInScenePacket.CreatePacket();
                    
                    _netServer.SendTcp(id, alreadyInScenePacket);
                }
            }
            
            // Store the new PlayerData object in the mapping
            _playerData[id] = playerData;
        }

        private void OnPlayerUpdate(int id, ServerPlayerUpdatePacket packet) {
            if (!_playerData.ContainsKey(id)) {
                Logger.Warn(this, $"Received PlayerUpdate packet, but player with ID {id} is not in mapping");
                return;
            }
            
            // Since we received an update from the player, we can reset their heart beat stopwatch
            _playerData[id].HeartBeatStopwatch.Reset();
            _playerData[id].HeartBeatStopwatch.Start();

            var playerUpdate = packet.PlayerUpdate;

            if (playerUpdate.UpdateTypes.Contains(UpdatePacketType.Position)) {
                _playerData[id].LastPosition = playerUpdate.Position;
            }
            
            if (playerUpdate.UpdateTypes.Contains(UpdatePacketType.Scale)) {
                _playerData[id].LastScale = playerUpdate.Scale;
            }
            
            if (playerUpdate.UpdateTypes.Contains(UpdatePacketType.MapPosition)) {
                _playerData[id].LastMapPosition = playerUpdate.MapPosition;
            }

            if (playerUpdate.UpdateTypes.Contains(UpdatePacketType.Animation)) {
                var animationInfos = playerUpdate.AnimationInfos;
                // Check whether there is any animation info to be stored
                if (animationInfos.Count != 0) {
                    // Set the last animation clip to be the last clip in the animation info list
                    // Since that is the last clip that the player updated
                    _playerData[id].LastAnimationClip = animationInfos[animationInfos.Count - 1].ClipName;

                    // Now we need to update each playerData instance to include all animation info instances,
                    // that way when we send them an update packet (as response), we can include that animation info
                    // of this player
                    foreach (var idPlayerDataPair in _playerData) {
                        // Skip over the player that we received from
                        if (idPlayerDataPair.Key == id) {
                            continue;
                        }

                        var otherPd = idPlayerDataPair.Value;
                        
                        // We only queue the animation info if the players are on the same scene,
                        // otherwise the animations get spammed once the players enter the same scene
                        if (otherPd.CurrentScene.Equals(_playerData[id].CurrentScene)) {
                            continue;
                        }

                        Queue<AnimationInfo> animationInfoQueue;
                        // If the queue did not exist yet, we create it and add it
                        if (!otherPd.AnimationInfoToSend.ContainsKey(id)) {
                            animationInfoQueue = new Queue<AnimationInfo>();
                            otherPd.AnimationInfoToSend.Add(id, animationInfoQueue);
                        } else {
                            animationInfoQueue = otherPd.AnimationInfoToSend[id];
                        }
                        
                        // For each of the animationInfo that the player sent, add them to this other player data instance
                        foreach (var animationInfo in animationInfos) {
                            animationInfoQueue.Enqueue(animationInfo);
                        }
                    }
                }
            }
            
            // Now we need to update the player from which we received an update of all current (and relevant)
            // information of the other players
            var clientPlayerUpdatePacket = new ClientPlayerUpdatePacket();

            foreach (var idPlayerDataPair in _playerData) {
                if (idPlayerDataPair.Key == id) {
                    continue;
                }

                var playerData = idPlayerDataPair.Value;

                // Keep track of whether we actually update any value of the player
                // we are looping over, otherwise, we don't have to add the PlayerUpdate instance
                var wasUpdated = false;
                
                // Create a new PlayerUpdate instance
                playerUpdate = new PlayerUpdate {
                    Id = (ushort) idPlayerDataPair.Key
                };

                // If the players are on the same scene, we need to update
                // position, scale and all unsent animations
                if (_playerData[id].CurrentScene.Equals(playerData.CurrentScene)) {
                    wasUpdated = true;
                    
                    playerUpdate.UpdateTypes.Add(UpdatePacketType.Position);
                    playerUpdate.Position = playerData.LastPosition;
                    
                    playerUpdate.UpdateTypes.Add(UpdatePacketType.Scale);
                    playerUpdate.Scale = playerData.LastScale;

                    // Get the queue of animation info corresponding to the player that we are
                    // currently looping over, which is meant for the player we need to update
                    // If the queue exists and is non-empty, we add the info
                    if (_playerData[id].AnimationInfoToSend.ContainsKey(idPlayerDataPair.Key)) {
                        var animationInfoQueue = _playerData[id].AnimationInfoToSend[idPlayerDataPair.Key];
                        if (animationInfoQueue.Count != 0) {
                            playerUpdate.UpdateTypes.Add(UpdatePacketType.Animation);
                            playerUpdate.AnimationInfos.AddRange(animationInfoQueue);
                        
                            animationInfoQueue.Clear();
                        }
                    }
                }
                
                // If the map icons need to be broadcast, we add those to the player update
                // TODO: this can be optimized, we don't need to repeatedly sent a zero vector if the
                // map icon is not being updated
                if (_gameSettings.AlwaysShowMapIcons || _gameSettings.OnlyBroadcastMapIconWithWaywardCompass) {
                    wasUpdated = true;
                    
                    playerUpdate.UpdateTypes.Add(UpdatePacketType.MapPosition);
                    playerUpdate.MapPosition = playerData.LastMapPosition;
                }

                // Finally, add the finalized playerUpdate instance to the packet
                // However, we only do this if any values were updated
                if (wasUpdated) {
                    clientPlayerUpdatePacket.PlayerUpdates.Add(playerUpdate);
                }
            }
            
            // Once this is done for each player that needs updates,
            // we can send the packet
            _netServer.SendPlayerUpdate(id, clientPlayerUpdatePacket);
        }

        private void OnPlayerDisconnect(int id, Packet packet) {
            Logger.Info(this, $"Received Disconnect packet from ID {id}");
            OnPlayerDisconnect(id);
        }

        private void OnPlayerDisconnect(int id) {
            // Always propagate this packet to the NetServer
            _netServer.OnClientDisconnect(id);

            if (!_playerData.ContainsKey(id)) {
                Logger.Warn(this, $"Player disconnect, but player with ID {id} is not in mapping");
                return;
            }
            
            // Send a player disconnect packet
            var playerDisconnectPacket = new ClientPlayerDisconnectPacket {
                Id = id
            };
            
            foreach (var idScenePair in _playerData) {
                if (idScenePair.Key == id) {
                    continue;
                }

                _netServer.SendTcp(idScenePair.Key, playerDisconnectPacket.CreatePacket());
            }

            // Now remove the client from the player data mapping
            _playerData.Remove(id);
        }

        private void OnPlayerDeath(int id, Packet packet) {
            if (!_playerData.ContainsKey(id)) {
                Logger.Warn(this, $"Received PlayerDeath packet, but player with ID {id} is not in mapping");
                return;
            }

            Logger.Info(this, $"Received PlayerDeath packet from ID {id}");
            
            // Get the scene that the client was last in
            var currentScene = _playerData[id].CurrentScene;
            
            // Create a new PlayerDeath packet containing the ID of the player that died
            var playerDeathPacket = new ClientPlayerDeathPacket {
                Id = id
            };
            playerDeathPacket.CreatePacket();
            
            // Send the packet to all clients in the same scene
            SendPacketToClientsInSameScene(playerDeathPacket, true, currentScene, id);
        }
        
        private void OnDreamshieldSpawn(int id, ServerDreamshieldSpawnPacket packet) {
            // if (!_playerData.ContainsKey(id)) {
            //     Logger.Warn(this, $"Received DreamshieldSpawn packet, but player with ID {id} is not in mapping");
            //     return;
            // }
            //
            // Logger.Info(this, $"Received DreamshieldSpawn packet from ID {id}");
            //
            // // Get the scene that the client was last in
            // var currentScene = _playerData[id].CurrentScene;
            //
            // // Create a new DreamshieldSpawn packet containing the ID of the player
            // var dreamshieldSpawnPacket = new ClientDreamshieldSpawnPacket {
            //     Id = id
            // };
            // dreamshieldSpawnPacket.CreatePacket();
            //
            // // Send the packet to all clients in the same scene
            // SendPacketToClientsInSameScene(dreamshieldSpawnPacket, false, currentScene, id);
        }
        
        private void OnDreamshieldDespawn(int id, ServerDreamshieldDespawnPacket packet) {
            // if (!_playerData.ContainsKey(id)) {
            //     Logger.Warn(this, $"Received DreamshieldDespawn packet, but player with ID {id} is not in mapping");
            //     return;
            // }
            //
            // Logger.Info(this, $"Received DreamshieldDespawn packet from ID {id}");
            //
            // // Get the scene that the client was last in
            // var currentScene = _playerData[id].CurrentScene;
            //
            // // Create a new DreamshieldDespawn packet containing the ID of the player
            // var dreamshieldDespawnPacket = new ClientDreamshieldDespawnPacket {
            //     Id = id
            // };
            // dreamshieldDespawnPacket.CreatePacket();
            //
            // // Send the packet to all clients in the same scene
            // SendPacketToClientsInSameScene(dreamshieldDespawnPacket, false, currentScene, id);
        }

        private void OnDreamshieldUpdate(int id, ServerDreamshieldUpdatePacket packet) {
            // if (!_playerData.ContainsKey(id)) {
            //     Logger.Warn(this, $"Received DreamshieldUpdate packet, but player with ID {id} is not in mapping");
            //     return;
            // }
            //
            // // Get the scene that the client was last in
            // var currentScene = _playerData[id].CurrentScene;
            //
            // // Create a new DreamshieldDespawn packet containing the ID of the player
            // var dreamshieldUpdatePacket = new ClientDreamshieldUpdatePacket {
            //     Id = id,
            //     BlockEffect = packet.BlockEffect,
            //     BreakEffect = packet.BreakEffect,
            //     ReformEffect = packet.ReformEffect
            // };
            // dreamshieldUpdatePacket.CreatePacket();
            //
            // // Send the packet to all clients in the same scene
            // SendPacketToClientsInSameScene(dreamshieldUpdatePacket, false, currentScene, id);
        }

        private void OnServerShutdown() {
            // Clear all existing player data
            _playerData.Clear();
            
            // De-register the heart beat update
            MonoBehaviourUtil.Instance.OnUpdateEvent -= CheckHeartBeat;
        }

        private void OnApplicationQuit() {
            Stop();
        }
        
        private void CheckHeartBeat() {
            // The server is not started, so there is no need to check heart beats
            if (!_netServer.IsStarted) {
                return;
            }

            // For each connected client, check whether a heart beat has been received recently
            foreach (var idPlayerDataPair in _playerData) {
                if (idPlayerDataPair.Value.HeartBeatStopwatch.ElapsedMilliseconds > ConnectionTimeout) {
                    // The stopwatch has surpassed the connection timeout value, so we disconnect the client
                    // var id = idPlayerDataPair.Key;
                    // Logger.Info(this, 
                    //     $"Didn't receive heart beat from player {id} in {ConnectionTimeout} milliseconds, dropping client");
                    // OnPlayerDisconnect(id);
                }                
            }
        }

        private void SendPacketToClientsInSameScene(Packet packet, bool tcp, string targetScene, int excludeId) {
            foreach (var idScenePair in _playerData) {
                if (idScenePair.Key == excludeId) {
                    continue;
                }
                
                if (idScenePair.Value.CurrentScene.Equals(targetScene)) {
                    if (tcp) {
                        _netServer.SendTcp(idScenePair.Key, packet);   
                    } else {
                        // _netServer.SendUdp(idScenePair.Key, packet);
                    }
                }
            }
        }

    }
}