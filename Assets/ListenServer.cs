using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using Unity.Collections;

public class ListenServer : SingletonBehaviour<ListenServer>
{
    [SerializeField] private bool isWebSocketServer;
    [SerializeField] private ushort port = 3333;

    NetworkDriver driver;
    NetworkPipeline pipeline;
    NativeList<NetworkConnection> connections;
    List<UserToken> tokens;
    MessageResolver resolver;

    Queue<UserPacketPair> sendQueue;

    private void Awake()
    {
        var settings = new NetworkSettings();
        settings.WithNetworkConfigParameters(
            connectTimeoutMS: 2000,
            maxConnectAttempts: 10,
            disconnectTimeoutMS: 600000,
            heartbeatTimeoutMS: 10000);
        settings.WithFragmentationStageParameters(payloadCapacity: 1048576);
        settings.WithReliableStageParameters(windowSize: 64);

        if (isWebSocketServer)
        {
            driver = NetworkDriver.Create(new WebSocketNetworkInterface(), settings);
        }
        else
        {
            driver = NetworkDriver.Create(settings);
        }

        pipeline = driver.CreatePipeline(
            typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));

        connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
        tokens = new();
        resolver = new MessageResolver();
        sendQueue = new();

        var endpoint = NetworkEndpoint.AnyIpv4.WithPort(port);
        if (driver.Bind(endpoint) != 0)
        {
            MyDebug.LogError($"Failed to bind to port {port}.");
            return;
        }
        driver.Listen();

        MyDebug.Log($"{(isWebSocketServer ? "Websocket" : "Debug")} Server Started");
    }

    // receive
    private void Update()
    {
        driver.ScheduleUpdate().Complete();

        // Accept new connections.
        NetworkConnection c;
        while ((c = driver.Accept()) != default)
        {
            MyDebug.Log("Accepted a connection.");
            connections.Add(c);
            var newClient = MainServer.Inst.OnNewClient();
            tokens.Add(newClient);
            newClient.ServerIdx = tokens.Count;
        }

        for (int i = 0; i < connections.Length; i++)
        {
            DataStreamReader stream;
            NetworkEvent.Type cmd;
            while ((cmd = driver.PopEventForConnection(connections[i], out stream)) != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Data)
                {
                    var buffer = new NativeArray<byte>(stream.Length, Allocator.Temp);
                    stream.ReadBytes(buffer);
                    resolver.OnReceive(buffer.ToArray(), 0, stream.GetBytesRead(), (p) => {
                        if (!MainServer.Inst.DisableReceiveLog)
                            MyDebug.Log($"[{tokens[i].UID}] receive {(PacketType)p.Type}");
                        tokens[i].AppendReceivedPacket(p);
                    });
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    MainServer.Inst.removedUserListBuffer.Add(tokens[i]);
                }
            }
        }
    }

    // send
    private void LateUpdate()
    {
        // send data
        while (sendQueue.Count > 0)
        {
            var toSend = sendQueue.Dequeue();
            if (!connections[toSend.TargetIndex].IsCreated) continue;

            var byteArr = new NativeArray<byte>(toSend.PacketToSend.GetSendBytes(), Allocator.Temp);

            driver.BeginSend(pipeline, connections[toSend.TargetIndex], out var writer);
            writer.WriteBytes(byteArr);
            driver.EndSend(writer);

            if (!MainServer.Inst.DisableSendLog)
                MyDebug.Log($"[{toSend.TargetId}] send {(PacketType)toSend.PacketToSend.Type}");
        }

        // Clean up connections.
        for (int i = 0; i < connections.Length; i++)
        {
            if (!connections[i].IsCreated)
            {
                connections.RemoveAtSwapBack(i);
                tokens.RemoveAtSwapBack(i);
                i--;
                continue;
            }

            tokens[i].ServerIdx = i;
        }
    }

    void OnDestroy()
    {
        if (driver.IsCreated)
        {
            driver.Dispose();
            connections.Dispose();
        }
    }

    public void AppendToSendQueue(UserToken user, Packet packet)
    {
        sendQueue.Enqueue(new UserPacketPair(user, packet));
    }

    public void DisconnectClient(UserToken user, bool sendDisconnectSignal = true)
    {
        if (sendDisconnectSignal) connections[user.ServerIdx].Disconnect(driver);
        connections[user.ServerIdx] = default;
    }
}

public struct UserPacketPair
{
    public uint TargetId;
    public int TargetIndex;
    public Packet PacketToSend;

    public UserPacketPair(UserToken user, Packet packet)
    {
        TargetIndex = user.ServerIdx;
        TargetId = user.UID;
        PacketToSend = packet;
    }
}
