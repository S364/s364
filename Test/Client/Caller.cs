using Shared;
using System;
using System.Net.WebSockets;
using System.Threading.Tasks;

namespace Client
{
    public class Caller
    {
        private WebSocketClient _ws;
        private bool _isRunning;

        private string _host;
        private int _port;

        private Logger _logger;

        public Caller(string host, int port, int updateInterval = 100, int timeoutInterval = 2500)
        {
            _host = host;
            _port = port;

            _logger = new Logger();

            _ws = new WebSocketClient();
            _ws.Initialize(_logger, updateInterval, timeoutInterval);
        }

        public async Task Start()
        {
            await _ws.StartAsync($@"ws://{_host}:{_port}/");
            _logger.Log("Client initialized");
            _isRunning = true;
        }

        public async Task Stop()
        {
            if (_ws.State == WebSocketState.Open)
            {
                await _ws.StopAsync();
                _isRunning = false;
            }
        }

        public void Update(string message)
        {
            if (_ws.State == WebSocketState.Open)
            {
                _ws.QueueUpdate(message);
            }
            else
            {
                throw new Exception("Client is not initialized");
            }
        }

        private async Task Run()
        {
            while (_isRunning)
            {
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }

        private async Task Loop()
        {
            try
            {
                await _ws.StartAsync($@"ws://{_host}:{_port}/");

                _logger.Log("Client initialized");

                bool running = true;
                while (running && _ws.State == WebSocketState.Open)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.Escape)
                        {
                            running = false;
                        }
                        else
                        {
                            _ws.QueueUpdate(key.KeyChar.ToString());
                        }
                    }
                }
                
            }
            catch (OperationCanceledException)
            {
                //excepted
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
