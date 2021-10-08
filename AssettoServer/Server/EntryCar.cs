﻿using AssettoServer.Network.Packets.Shared;
using AssettoServer.Network.Tcp;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AssettoServer.Network.Packets.Outgoing;
using AssettoServer.Server.Ai;
using Serilog;

namespace AssettoServer.Server
{
    public enum AiMode
    {
        Disabled,
        Auto,
        Fixed
    }
    
    public class EntryCar
    {
        public ACServer Server { get; internal set; }
        public ACTcpClient Client { get; internal set; }
        public CarStatus Status { get; private set; } = new CarStatus();

        public bool ForceLights { get; internal set; }
        public int HighPingSeconds { get; internal set; }
        public int LightFlashCount { get; internal set; }

        public long LastActiveTime { get; internal set; }
        public bool HasSentAfkWarning { get; internal set; }
        public bool HasUpdateToSend { get; internal set; }
        public int TimeOffset { get; internal set; }
        public byte SessionId { get; internal set; }
        public uint LastRemoteTimestamp { get; internal set; }
        public int LastPingTime { get; internal set; }
        public int LastPongTime { get; internal set; }
        public ushort Ping { get; internal set; }

        public bool IsSpectator { get; internal set; }
        public string Model { get; internal set; }
        public string Skin { get; internal set; }
        public int SpectatorMode { get; internal set; }
        public int Ballast { get; internal set; }
        public int Restrictor { get; internal set; }

        internal long[] OtherCarsLastSentUpdateTime { get; set; }
        internal Race CurrentRace { get; set; }
        internal EntryCar TargetCar { get; set; }
        internal long LastFallCheckTime{ get; set;}

        private long LastLightFlashTime { get; set; }
        private long LastRaceChallengeTime { get; set; }
        
        public bool AiControlled { get; set; }
        public AiMode AiMode { get; init; }

        public byte[] AiPakSequenceIds { get; init; }
        private readonly List<AiState> _aiStates = new List<AiState>();
        private readonly ReaderWriterLockSlim _aiStatesLock = new ReaderWriterLockSlim();
        public int TargetAiStateCount { get; private set; } = 1;

        public List<AiState> GetAiStatesCopy()
        {
            _aiStatesLock.EnterReadLock();
            try
            {
                return new List<AiState>(_aiStates);
            }
            finally
            {
                _aiStatesLock.ExitReadLock();
            }
        }
        
        public bool CanSpawnAiState(Vector3 spawnPoint, AiState aiState)
        {
            _aiStatesLock.EnterUpgradeableReadLock();
            try
            {
                // Remove state if AI slot overbooking was reduced
                if (_aiStates.IndexOf(aiState) >= TargetAiStateCount)
                {
                    _aiStatesLock.EnterWriteLock();
                    try
                    {
                        _aiStates.Remove(aiState);
                    }
                    finally
                    {
                        _aiStatesLock.ExitWriteLock();
                    }

                    Log.Verbose("Removed state of Traffic {0} due to overbooking reduction", SessionId);

                    if (_aiStates.Count == 0)
                    {
                        Log.Verbose("Traffic {0} has no states left, disconnecting", SessionId);
                        Server.BroadcastPacket(new CarDisconnected { SessionId = SessionId });
                    }

                    return false;
                }

                foreach (var state in _aiStates)
                {
                    if (state == aiState || !state.Initialized) continue;

                    if (Vector3.DistanceSquared(spawnPoint, state.Status.Position) < Server.Configuration.Extra.AiParams.StateSafetyDistanceSquared)
                    {
                        return false;
                    }
                }

                return true;
            }
            finally
            {
                _aiStatesLock.ExitUpgradeableReadLock();
            }
        }
        
        public void SetAiControl(bool aiControlled)
        {
            if (AiControlled != aiControlled)
            {
                AiControlled = aiControlled;

                if (AiControlled)
                {
                    Log.Debug("Slot {0} is now controlled by AI", SessionId);

                    AiReset();
                    Server.BroadcastPacket(new CarConnected
                    {
                        SessionId = SessionId,
                        Name = $"Traffic {SessionId}"
                    });
                }
                else
                {
                    Log.Debug("Slot {0} is no longer controlled by AI", SessionId);
                    if (_aiStates.Count > 0)
                    {
                        Server.BroadcastPacket(new CarDisconnected {SessionId = SessionId});
                    }

                    AiReset();
                }
            }
        }

        public void SetAiOverbooking(int count)
        {
            _aiStatesLock.EnterUpgradeableReadLock();
            try
            {
                if (count > _aiStates.Count)
                {
                    _aiStatesLock.EnterWriteLock();
                    try
                    {
                        int newAis = count - _aiStates.Count;
                        for (int i = 0; i < newAis; i++)
                        {
                            _aiStates.Add(new AiState(this));
                        }
                    }
                    finally
                    {
                        _aiStatesLock.ExitWriteLock();
                    }
                }
                
                TargetAiStateCount = count;
            }
            finally
            {
                _aiStatesLock.ExitUpgradeableReadLock();
            }
        }

        private void AiReset()
        {
            _aiStatesLock.EnterWriteLock();
            try
            {
                _aiStates.Clear();
                _aiStates.Add(new AiState(this));
            }
            finally
            {
                _aiStatesLock.ExitWriteLock();
            }
        }

        internal void Reset()
        {
            IsSpectator = false;
            SpectatorMode = 0;
            LastActiveTime = 0;
            HasSentAfkWarning = false;
            HasUpdateToSend = false;
            TimeOffset = 0;
            LastRemoteTimestamp = 0;
            HighPingSeconds = 0;
            LastPingTime = 0;
            Ping = 0;
            ForceLights = false;
            Status = new CarStatus();
            CurrentRace = null;
            TargetCar = null;
        }

        internal void CheckAfk()
        {
            if (!Server.Configuration.Extra.EnableAntiAfk || Client?.IsAdministrator == true)
                return;

            long timeAfk = Environment.TickCount64 - LastActiveTime;
            if (timeAfk > Server.Configuration.Extra.MaxAfkTimeMilliseconds)
                _ = Server.KickAsync(Client, Network.Packets.Outgoing.KickReason.None, $"{Client?.Name} has been kicked for being AFK.");
            else if (!HasSentAfkWarning && Server.Configuration.Extra.MaxAfkTimeMilliseconds - timeAfk < 60000)
            {
                HasSentAfkWarning = true;
                Client?.SendPacket(new ChatMessage { SessionId = 255, Message = "You will be kicked in 1 minute for being AFK." });
            }
        }

        internal void SetActive()
        {
            LastActiveTime = Environment.TickCount64;
            HasSentAfkWarning = false;
        }

        internal void UpdatePosition(PositionUpdate positionUpdate)
        {
            HasUpdateToSend = true;
            LastRemoteTimestamp = positionUpdate.LastRemoteTimestamp;

            if (positionUpdate.StatusFlag != Status.StatusFlag || positionUpdate.Gas != Status.Gas || positionUpdate.SteerAngle != Status.SteerAngle)
                SetActive();

            long currentTick = Environment.TickCount64;
            if(((Status.StatusFlag & CarStatusFlags.LightsOn) == 0 && (positionUpdate.StatusFlag & CarStatusFlags.LightsOn) != 0) || ((Status.StatusFlag & CarStatusFlags.HighBeamsOff) == 0 && (positionUpdate.StatusFlag & CarStatusFlags.HighBeamsOff) != 0))
            {
                LastLightFlashTime = currentTick;
                LightFlashCount++;
            }

            if ((Status.StatusFlag & CarStatusFlags.HazardsOn) == 0 && (positionUpdate.StatusFlag & CarStatusFlags.HazardsOn) != 0)
            {
                if (CurrentRace != null && !CurrentRace.HasStarted && !CurrentRace.LineUpRequired)
                    _ = CurrentRace.StartAsync();
            }

            if (currentTick - LastLightFlashTime > 3000 && LightFlashCount > 0)
            {
                LightFlashCount = 0;
            }

            if (LightFlashCount == 3)
            {
                LightFlashCount = 0;

                if(currentTick - LastRaceChallengeTime > 20000)
                {
                    Task.Run(ChallengeNearbyCar);
                    LastRaceChallengeTime = currentTick;
                }
            }

            /*if (!AiControlled && Status.StatusFlag != positionUpdate.StatusFlag)
            {
                Log.Debug("Status flag from {0:X} to {1:X}", Status.StatusFlag, positionUpdate.StatusFlag);
            }*/

            Status.Timestamp = LastRemoteTimestamp + TimeOffset;
            Status.PakSequenceId = positionUpdate.PakSequenceId;
            Status.Position = positionUpdate.Position;
            Status.Rotation = positionUpdate.Rotation;
            Status.Velocity = positionUpdate.Velocity;
            Status.TyreAngularSpeed[0] = positionUpdate.TyreAngularSpeedFL;
            Status.TyreAngularSpeed[1] = positionUpdate.TyreAngularSpeedFR;
            Status.TyreAngularSpeed[2] = positionUpdate.TyreAngularSpeedRL;
            Status.TyreAngularSpeed[3] = positionUpdate.TyreAngularSpeedRR;
            Status.SteerAngle = positionUpdate.SteerAngle;
            Status.WheelAngle = positionUpdate.WheelAngle;
            Status.EngineRpm = positionUpdate.EngineRpm;
            Status.Gear = positionUpdate.Gear;
            Status.StatusFlag = positionUpdate.StatusFlag;
            Status.PerformanceDelta = positionUpdate.PerformanceDelta;
            Status.Gas = positionUpdate.Gas;
            Status.NormalizedPosition = positionUpdate.NormalizedPosition;
        }

        internal void ChallengeCar(EntryCar car, bool lineUpRequired = true)
        {
            void Reply(string message)
                => Client.SendPacket(new ChatMessage { SessionId = 255, Message = message });

            Race currentRace = CurrentRace;
            if (currentRace != null)
            {
                if (currentRace.HasStarted)
                    Reply("You are currently in a race.");
                else
                    Reply("You have a pending race request.");
            }
            else
            {
                if (car == this)
                    Reply("You cannot challenge yourself to a race.");
                else
                {
                    currentRace = car.CurrentRace;
                    if (currentRace != null)
                    {
                        if (currentRace.HasStarted)
                            Reply("This car is currently in a race.");
                        else
                            Reply("This car has a pending race request.");
                    }
                    else
                    {
                        currentRace = new Race(Server, this, car, lineUpRequired);
                        CurrentRace = currentRace;
                        car.CurrentRace = currentRace;

                        Client.SendPacket(new ChatMessage { SessionId = 255, Message = $"You have challenged {car.Client.Name} to a race." });

                        if (lineUpRequired)
                            car.Client.SendPacket(new ChatMessage { SessionId = 255, Message = $"{Client.Name} has challenged you to a race. Send /accept within 10 seconds to accept." });
                        else
                            car.Client.SendPacket(new ChatMessage { SessionId = 255, Message = $"{Client.Name} has challenged you to a race. Flash your hazard lights or send /accept within 10 seconds to accept." });

                        _ = Task.Delay(10000).ContinueWith(t =>
                        {
                            if (!currentRace.HasStarted)
                            {
                                CurrentRace = null;
                                car.CurrentRace = null;

                                ChatMessage timeoutMessage = new ChatMessage { SessionId = 255, Message = $"Race request has timed out." };
                                Client.SendPacket(timeoutMessage);
                                car.Client.SendPacket(timeoutMessage);
                            }
                        });
                    }
                }
            }
        }

        private void ChallengeNearbyCar()
        {
            EntryCar bestMatch = null;
            float distanceSquared = 30 * 30;

            foreach(EntryCar car in Server.EntryCars)
            {
                ACTcpClient carClient = car.Client;
                if(carClient != null && car != this)
                {
                    float challengedAngle = (float)(Math.Atan2(Status.Position.X - car.Status.Position.X, Status.Position.Z - car.Status.Position.Z) * 180 / Math.PI);
                    if (challengedAngle < 0)
                        challengedAngle += 360;
                    float challengedRot = car.Status.GetRotationAngle();

                    challengedAngle += challengedRot;
                    challengedAngle %= 360;

                    if (challengedAngle > 110 && challengedAngle < 250 && Vector3.DistanceSquared(car.Status.Position, Status.Position) < distanceSquared)
                        bestMatch = car;
                }
            }

            if (bestMatch != null)
                ChallengeCar(bestMatch, false);
        }
    }
}
