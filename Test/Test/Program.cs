using Client;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Test
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Caller client = new Caller("localhost", 8080);
            await client.Start();

        kek:
            var msg = Console.ReadLine();
            client.Update(msg);
            goto kek;
        }
    }
}
