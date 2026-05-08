using Server.InGame;
using Server.OutGame;
using System.Net;
using System.Net.Sockets;

namespace Server
{
    internal class Program
    {
        static ClientSessionManager _clientSessionManager = new();

        static async Task Main(string[] args)
        {
            LobbyService lobbyService = new LobbyService(_clientSessionManager);

            lobbyService.Start();

            int port = 9000;
            TcpListener listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine($"[Server] Listening on port {port}");

            await AcceptLoop(listener);
        }

        static async Task AcceptLoop(TcpListener listener)
        {
            while (true)
            {
                try
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    if (_clientSessionManager.TryAddClient(client, out ClientSession session))
                    {
                        Console.WriteLine($"[Server] Succeed to accept client");
                    }
                    else
                    {
                        Console.WriteLine($"[Server] Failed to accept client, is full");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Server] Error with accepting client {ex.Message}");
                }
            }
        }
    }
}
