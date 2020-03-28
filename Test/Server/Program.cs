using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                WebSocketServer.Start("http://localhost:8080/");
                Console.WriteLine("Press any key to exit...\n");
                Console.ReadKey(true);
                await WebSocketServer.StopAsync();
            }
            catch (OperationCanceledException)
            {
               // expected
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
