using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server
{
    public static class WebSocketServer
    {
        private static HttpListener Listener;

        private static CancellationTokenSource SocketLoopTokenSource;
        private static CancellationTokenSource ListenerLoopTokenSource;

        private static int SocketCounter = 0;

        private static bool ServerIsRunning = true;

        private static ConcurrentDictionary<int, ConnectedClient> Clients = new ConcurrentDictionary<int, ConnectedClient>();

        public static void Start(string uriPrefix)
        {
            SocketLoopTokenSource = new CancellationTokenSource();
            ListenerLoopTokenSource = new CancellationTokenSource();
            Listener = new HttpListener();
            Listener.Prefixes.Add(uriPrefix);
            Listener.Start();
            if (Listener.IsListening)
            {
                Console.WriteLine($"Server listening: {uriPrefix}");

                Task.Run(() => ListenerProcessingLoopAsync().ConfigureAwait(false));
            }
            else
            {
                Console.WriteLine("Server failed to start.");
            }
        }

        public static async Task StopAsync()
        {
            if (Listener?.IsListening ?? false && ServerIsRunning)
            {
                Console.WriteLine("\nServer is stopping.");

                ServerIsRunning = false;
                await CloseAllSocketsAsync(); 
                ListenerLoopTokenSource.Cancel();
                Listener.Stop();
                Listener.Close();
            }
        }

        private static async Task ListenerProcessingLoopAsync()
        {
            var cancellationToken = ListenerLoopTokenSource.Token;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    HttpListenerContext context = await Listener.GetContextAsync();
                    if (ServerIsRunning)
                    {
                        if (context.Request.IsWebSocketRequest)
                        {
                            HttpListenerWebSocketContext wsContext = null;
                            try
                            {
                                wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
                                int socketId = Interlocked.Increment(ref SocketCounter);
                                var client = new ConnectedClient(socketId, wsContext.WebSocket);
                                Clients.TryAdd(socketId, client);
                                Console.WriteLine($"Socket {socketId}: New connection.");
                                _ = Task.Run(() => SocketProcessingLoopAsync(client).ConfigureAwait(false));
                            }
                            catch (Exception)
                            {
                                context.Response.StatusCode = 500;
                                context.Response.StatusDescription = "WebSocket upgrade failed";
                                context.Response.Close();
                                return;
                            }
                        }
                    }
                    else
                    {
                        context.Response.StatusCode = 409;
                        context.Response.StatusDescription = "Server is shutting down";
                        context.Response.Close();
                        return;
                    }
                }
            }
            catch (HttpListenerException ex) when (ServerIsRunning)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static async Task SocketProcessingLoopAsync(ConnectedClient client)
        {
            var socket = client.Socket;
            var loopToken = SocketLoopTokenSource.Token;
            try
            {
                var buffer = WebSocket.CreateServerBuffer(4096);
                while (socket.State != WebSocketState.Closed && socket.State != WebSocketState.Aborted && !loopToken.IsCancellationRequested)
                {
                    var receiveResult = await client.Socket.ReceiveAsync(buffer, loopToken);

                    if (!loopToken.IsCancellationRequested)
                    {
                        if (client.Socket.State == WebSocketState.CloseReceived && receiveResult.MessageType == WebSocketMessageType.Close)
                        {
                            Console.WriteLine($"Socket {client.SocketId}: Acknowledging Close frame received from client");
                            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Acknowledge Close frame", CancellationToken.None);
                        }

                        if (client.Socket.State == WebSocketState.Open)
                        {
                            Console.WriteLine($"Socket {client.SocketId}: Received {receiveResult.MessageType} frame ({receiveResult.Count} bytes).");
                            Console.WriteLine($"Socket {client.SocketId}: Echoing data to client.");
                            await socket.SendAsync(new ArraySegment<byte>(buffer.Array, 0, receiveResult.Count), receiveResult.MessageType, receiveResult.EndOfMessage, CancellationToken.None);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                //expected
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Socket {client.SocketId}:");
                Console.WriteLine(ex.Message);
            }
            finally
            {
                Console.WriteLine($"Socket {client.SocketId}: Ended processing loop in state {socket.State}");

                if (client.Socket.State != WebSocketState.Closed)
                    client.Socket.Abort();

                if (Clients.TryRemove(client.SocketId, out _))
                    socket.Dispose();
            }
        }

        private static async Task CloseAllSocketsAsync()
        {
            var disposeQueue = new List<WebSocket>(Clients.Count);

            while (Clients.Count > 0)
            {
                var client = Clients.ElementAt(0).Value;
                Console.WriteLine($"Closing Socket {client.SocketId}");

                Console.WriteLine("... ending broadcast loop");

                if (client.Socket.State != WebSocketState.Open)
                {
                    Console.WriteLine($"... socket not open, state = {client.Socket.State}");
                }
                else
                {
                    var timeout = new CancellationTokenSource(2500);
                    try
                    {
                        Console.WriteLine("... starting close handshake");
                        await client.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", timeout.Token);
                    }
                    catch (OperationCanceledException ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }

                if (Clients.TryRemove(client.SocketId, out _))
                {
                    disposeQueue.Add(client.Socket);
                }

                Console.WriteLine("... done");
            }

            SocketLoopTokenSource.Cancel();

            foreach (var socket in disposeQueue)
                socket.Dispose();
        }

    }
}