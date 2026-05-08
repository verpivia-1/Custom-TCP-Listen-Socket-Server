using Server.NetworkContracts_Generater;
using Server.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    internal class ClientSessionManager
    {
        static IdGenerator s_idGenerator = new();

        readonly ConcurrentDictionary<int, ClientSession> _sessions = new();
        readonly ConcurrentQueue<(ClientSession session, IPacket packet)> _packetQueue = new();

        public event Action<int, IPacket> OnPacketReceived; // client id, packet
        public event Action<ClientSession> OnClientDisconnected;

        public ClientSessionManager()
        {
            Task.Run(ProcessPacketQueue);
        }
        public bool TryGetClientSession(int clientId, out ClientSession session)
        {
            return _sessions.TryGetValue(clientId, out session);
        }

        public bool TryAddClient(TcpClient client, out ClientSession session)
        {
            int clientId = s_idGenerator.AssignId();
            session = new ClientSession(clientId, client, this);
            if (clientId < 0)
            {
                S_ConnectionFailed connectionFailurePacket = new S_ConnectionFailed
                {
                    Failed_Respones = "Server is full."
                };
                session.Send(connectionFailurePacket);
                // TODO : Dispose client Session.
                return false;
            }
            else
            {
                _sessions[clientId] = session;
                session.OnDisconnected += OnDisconnected;
                S_ConnectionSuccess connectionSuccessPacket = new S_ConnectionSuccess
                {
                    AssignedClientId = clientId,
                    Content = "Welcome to the 'EndWalker'!"
                };
                session.Send(connectionSuccessPacket);
            } 
            session.StartRecvLoop();
            Console.WriteLine($"[Server] New client accpeted (id : {clientId})");
            return true;
        }
        void OnDisconnected(int clientId)
        {
            s_idGenerator.ReleaseId(clientId);
            _sessions.TryRemove(clientId, out ClientSession session);
            OnClientDisconnected?.Invoke(session);
            Console.WriteLine($"[Server] Client {clientId} disconnected.");
        }

        public void QueuePacket(ClientSession session, IPacket packet) // 수신 받은 패킷 담아두기(선입선출)
        {
            _packetQueue.Enqueue((session, packet));
        }

        async Task ProcessPacketQueue() // 담아둔 패킷 처리하기
        {
            while (true)
            {
                while (_packetQueue.TryDequeue(out var kv))
                {
                    int clientId = kv.session.ClientId;
                    IPacket packet = kv.packet;
                    OnPacketReceived?.Invoke(clientId, packet);
                }
                await Task.Delay(30);
            }
        }
        public void Send(int clientId, IPacket packet)
        {
            if (_sessions.TryGetValue(clientId, out ClientSession session))
            {
                session.Send(packet);
            }
            else
            {
                // TODO : 대상 client 가 연결 끊어져있을때 처리할내용
            }
        }
        public void Broadcast(IPacket packet)
        {
            foreach (var session in _sessions.Values)
                session.Send(packet);
        }
    }
}