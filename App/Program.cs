using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using ObjectPrinting;
using Serialization;

namespace App
{
    internal static class Program
    {
        public static async Task Main(string[] args)
        {
            var arr = GetTestData();

            var serializer = new Serializer();
            var sendingData = serializer.Serialize(arr);
            
            var server = new Socket(SocketType.Stream, ProtocolType.Tcp);
            server.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 80));
            var acceptData = Task.Run(() => AcceptData(server));

            var sender = new Socket(SocketType.Stream, ProtocolType.Tcp);
            
            var comparer = new HashSet<string> { arr.PrintToString() };

            sender.Connect("127.0.0.1", 80);
            sender.Send(sendingData);

            await acceptData;
            var receivedData = acceptData.Result;

            var after = serializer.Deserialize<stest[]>(receivedData.ToArray());
            comparer.Add(after.PrintToString());
            after = serializer.Deserialize<stest[]>(sendingData);
            comparer.Add(after.PrintToString());
            Console.WriteLine(comparer.Count == 1 ? "all data equal" : "something goes wrong :(");
        }

        private static stest[] GetTestData()
        {
            var firstObject = new stest()
            {
                i = 999,
                s = "somestring",
                Ints = new[] { 3, 5, 1112, Int32.MaxValue, Int32.MinValue },
                something = 14,
                obj = new stest
                {
                    i = 1134,
                    Ints = new[] { 1, 1, 1 }
                }
            };

            var arr = new[] { firstObject, new stest() { i = 1010, s = "str" } };
            return arr;
        }

        private static List<byte> AcceptData(Socket server)
        {
            server.Listen(10);
            var receiver = server.Accept();
            var result = new List<byte>();
            var counter = 4096;
            var buffer = new byte[counter];
            do
            {
                counter = receiver.Receive(buffer);
                result.AddRange(buffer[..counter]);
            } while (counter == 4096);

            return result;
        }
    }

    public class stest
    {
        public int i { get; set; }
        public string s { get; set; }
        public int[] Ints { get; set; }
        public int something { get; set; }
        public stest obj { get; set; }
    }
}