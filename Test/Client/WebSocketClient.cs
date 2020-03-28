using Shared;
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Client
{
    public class WebSocketClient
    {
        private int _keystrokeInterval;
        private int _timeoutInterval;

        private Logger _logger;

        private ClientWebSocket _socket;
        private BlockingCollection<string> _updateQueue = new BlockingCollection<string>();
        private CancellationTokenSource _socketLoopTokenSource;
        private CancellationTokenSource _updateLoopTokenSource;

        public void Initialize(Logger logger, int keystrokeInterval = 100, int timeoutInterval = 2500)
        {
            _logger = logger;

            _keystrokeInterval = keystrokeInterval;
            _timeoutInterval = timeoutInterval;
        }

        public async Task StartAsync(string wsUri)
            => await StartAsync(new Uri(wsUri));

        public async Task StartAsync(Uri wsUri)
        {
            _logger.Log($"Connecting: {wsUri.ToString()}");

            _socketLoopTokenSource = new CancellationTokenSource();
            _updateLoopTokenSource = new CancellationTokenSource();

            try
            {
                _socket = new ClientWebSocket();
                await _socket.ConnectAsync(wsUri, CancellationToken.None);
                _ = Task.Run(() => SocketProcessingLoopAsync().ConfigureAwait(false));
                _ = Task.Run(() => UpdateSenderLoopAsync().ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                //expected
            }
        }

        public async Task StopAsync()
        {
            _logger.Log($"\nClosing connection");

            _updateLoopTokenSource.Cancel();
            if (_socket == null || _socket.State != WebSocketState.Open) return;

            var timeout = new CancellationTokenSource(_timeoutInterval);
            try
            {
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", timeout.Token);

                while (_socket.State != WebSocketState.Closed && !timeout.Token.IsCancellationRequested) ;
            }
            catch (OperationCanceledException)
            {
                //expected
            }

            _socketLoopTokenSource.Cancel();
        }

        public WebSocketState State
        {
            get => _socket?.State ?? WebSocketState.None;
        }

        public void QueueUpdate(string message)
            => _updateQueue.Add(message);

        private async Task SocketProcessingLoopAsync()
        {
            var cancellationToken = _socketLoopTokenSource.Token;
            try
            {
                var buffer = WebSocket.CreateClientBuffer(4096, 4096);
                while (_socket.State != WebSocketState.Closed && !cancellationToken.IsCancellationRequested)
                {
                    var receiveResult = await _socket.ReceiveAsync(buffer, cancellationToken);

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        if (_socket.State == WebSocketState.CloseReceived && receiveResult.MessageType == WebSocketMessageType.Close)
                        {
                            _logger.Log($"\nShutdown event received");
                            _updateLoopTokenSource.Cancel();

                            await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Shutdown", CancellationToken.None);
                        }

                        if (_socket.State == WebSocketState.Open && receiveResult.MessageType != WebSocketMessageType.Close)
                        {
                            string message = Encoding.UTF8.GetString(buffer.Array, 0, receiveResult.Count);
                            if (message.Length > 1) message = "\n" + message + "\n";
                            Console.Write(message);
                        }
                    }
                }

                _logger.Log($"Ending with state {_socket.State}");
            }
            catch (OperationCanceledException)
            {
                //expected
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                _updateLoopTokenSource.Cancel();
                _socket.Dispose();
                _socket = null;
            }
        }

        private async Task UpdateSenderLoopAsync()
        {
            var cancellationToken = _updateLoopTokenSource.Token;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_keystrokeInterval, cancellationToken);
                    if (!cancellationToken.IsCancellationRequested && _updateQueue.TryTake(out var message))
                    {
                        var msgbuf = new ArraySegment<byte>(Encoding.UTF8.GetBytes(message));
                        await _socket.SendAsync(msgbuf, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
                    }
                }
                catch (OperationCanceledException)
                {
                    //expected
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }
    }
}
