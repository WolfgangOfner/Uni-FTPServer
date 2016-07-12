using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Diagnostics;
using ClassLibrary1;

namespace FTPServer
{
    static class Program
    {
        static void Main()
        {
            Server server = new Server(IPAddress.Any, 12345);
        }
    }
}
