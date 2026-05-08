using Server.Utils;
using Server.NetworkContracts_Generater;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;


namespace Server
{
    internal class ClientSession : IDisposable
    {
        public int ClientId { get; }
        public string PlayerName { get; set; }
        public bool IsConnected => _tcp.Connected;

        readonly TcpClient _tcp;
        // NetworkStream
        // Network 상의 두 이용자(EndPoint) 간에 데이터를 전송하는 추상화 계층.
        // Tcp 연결 이후, 
        // 이 스트림에 데이터 쓰기를 하면 송신 버퍼 에 복사. 복사된 데이터는 Segment 로 잘라서 IP 계층으로 내려보냄
        // 패킷이 수신되면 수신 버퍼에 쌓음. 이후 패킷 순서 재조립.
        readonly NetworkStream _stream;
        ClientSessionManager _manager;
        public event Action<int> OnDisconnected;

        public ClientSession(int clientId, TcpClient tcp, ClientSessionManager manager)
        {
            ClientId = clientId;
            _manager = manager;
            _tcp = tcp;
            _tcp.NoDelay = true; // false이면 버퍼에 일정 량 쌓이기 전까지 데이터를 송신하지않음
            _stream = _tcp.GetStream();
        }
        public void StartRecvLoop()
        {
            // task.run : caller 가 아닌 다른 스레드에서 실행하도록 함. (보통 스레드작업 분리용)
            Task.Run(RecvLoop);
        }
        async Task RecvLoop()
        {
            try
            {
                while (IsConnected)
                {
                    IPacket? packet = PacketIO.Recv(_stream);
                    if (packet == null)
                        break; // 연결 끊김
                    HandlePacket(packet);
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine($"[ClientSession]error : {ClientId} : {ex.Message}");
                // 연결 끊김 처리
            }
            finally
            {
                Console.WriteLine($"[ClientSession] Disconnected client {ClientId}");
                _tcp.Close();
                OnDisconnected?.Invoke(ClientId);
            }
        }
        public void Send(IPacket packet)
        {
            try
            {
                lock (_stream)
                {
                    PacketIO.Send(_stream, packet);
                }
            }
            catch
            {
                _tcp?.Close();
                OnDisconnected?.Invoke(ClientId);
            }
        }
        void HandlePacket(IPacket packet)
        {
            _manager.QueuePacket(this, packet);
        }
        public void Dispose()
        {
            _tcp?.Close();
            OnDisconnected?.Invoke(ClientId);
        }
    }
}