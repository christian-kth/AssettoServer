﻿using AssettoServer.Network.Packets.Incoming;
using AssettoServer.Network.Packets.Outgoing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace AssettoServer.Network.Packets.Shared
{
    [Flags]
    public enum CarStatusFlags
    {
        BrakeLightsOn = 0x10,
        LightsOn = 0x20,
        Horn = 0x40,
        HazardsOn = 0x2000,
        HighBeamsOff = 0x4000,
        WiperLevel1 = 0x200000,
        WiperLevel2 = 0x400000,
        WiperLevel3 = WiperLevel1 | WiperLevel2
    }
    public struct PositionUpdate : IIncomingNetworkPacket, IOutgoingNetworkPacket
    {
        public byte SessionId;
        public byte PakSequenceId;
        public uint LastRemoteTimestamp;
        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 Velocity;
        public ushort Ping;
        public uint Timestamp;
        public byte TyreAngularSpeedFL;
        public byte TyreAngularSpeedFR;
        public byte TyreAngularSpeedRL;
        public byte TyreAngularSpeedRR;
        public byte SteerAngle;
        public byte WheelAngle;
        public ushort EngineRpm;
        public byte Gear;
        public CarStatusFlags StatusFlag;
        public short PerformanceDelta;
        public byte Gas;
        public float NormalizedPosition;

        public void FromReader(PacketReader reader)
        {
            PakSequenceId = reader.Read<byte>();
            LastRemoteTimestamp = reader.Read<uint>();
            Position = reader.Read<Vector3>();
            Rotation = reader.Read<Vector3>();
            Velocity = reader.Read<Vector3>();
            TyreAngularSpeedFL = reader.Read<byte>();
            TyreAngularSpeedFR = reader.Read<byte>();
            TyreAngularSpeedRL = reader.Read<byte>();
            TyreAngularSpeedRR = reader.Read<byte>();
            SteerAngle = reader.Read<byte>();
            WheelAngle = reader.Read<byte>();
            EngineRpm = reader.Read<ushort>();
            Gear = reader.Read<byte>();
            StatusFlag = (CarStatusFlags)reader.Read<uint>();
            PerformanceDelta = reader.Read<short>();
            Gas = reader.Read<byte>();
            NormalizedPosition = reader.Read<float>();
        }

        public void ToWriter(ref PacketWriter writer)
        {
            writer.Write<byte>(0x46);
            writer.Write(SessionId);
            writer.Write(PakSequenceId);
            writer.Write(Timestamp);
            writer.Write(Ping);
            writer.Write(Position);
            writer.Write(Rotation);
            writer.Write(Velocity);
            writer.Write(TyreAngularSpeedFL);
            writer.Write(TyreAngularSpeedFR);
            writer.Write(TyreAngularSpeedRL);
            writer.Write(TyreAngularSpeedRR);
            writer.Write(SteerAngle);
            writer.Write(WheelAngle);
            writer.Write(EngineRpm);
            writer.Write(Gear);
            writer.Write((uint)StatusFlag);
            writer.Write(PerformanceDelta);
            writer.Write(Gas);
        }
    }
}
