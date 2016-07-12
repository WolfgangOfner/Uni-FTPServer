namespace FTPServer
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Security.AccessControl;
    using System.Security.Principal;
    using System.Text;
    using System.Threading;
    using ClassLibrary1;
    public class Server
    {
        /// <summary>
        /// list contains all connected created threads
        /// </summary>
        private static List<Thread> clientThreads = new List<Thread>();

        /// <summary>
        /// list contains all connected clients
        /// </summary>
        private static List<TcpClient> list = new List<TcpClient>();

        /// <summary>
        /// root drive of this ftp server
        /// </summary>
        private static string root = "c:\\";

        /// <summary>
        /// udp client for monitoring
        /// </summary>
        private static UdpClient udpClient = new UdpClient();

        /// <summary>
        /// udp port for monitoring
        /// </summary>
        const int PORT = 35000;

        public Server(IPAddress IP, int port)
        {
            IP = IPAddress.Parse("127.0.0.1");
            IPEndPoint localEndpoint = new IPEndPoint(IP, port);
            TcpListener listener = new TcpListener(localEndpoint);

            SendLog("Server started");

            Working(listener, localEndpoint);
        }

        /// <summary>
        /// Start every client in a new thread and add them to a list
        /// </summary>
        /// <param name="listener"></param>
        /// <param name="localEndpoint"></param>
        private void Working(TcpListener listener, IPEndPoint localEndpoint)
        {
            listener.Start();

            int connectionID = 0;

            do
            {
                if (listener.Pending())
                {
                    Thread t = new Thread(new ParameterizedThreadStart(ClientWorker));

                    t.IsBackground = true;

                    t.Name = (connectionID++).ToString();

                    t.Start((object)listener.AcceptTcpClient());

                    clientThreads.Add(t);

                    SendLog("Client connected");
                }
                else
                {
                    Thread.Sleep(100);
                }
            }
            while (true);

            listener.Stop();

            foreach (Thread t in clientThreads)
            {
                t.Abort();
            }

            foreach (Thread t in clientThreads)
            {
                t.Join();
            }
        }

        private static void ClientWorker(object data)
        {
            TcpClient tcpClient = (TcpClient)data;
            list.Add(tcpClient);

            NetworkStream ns;
            string threadName = Thread.CurrentThread.Name;

            try
            {
                ns = tcpClient.GetStream();
            }
            catch
            {
                return;
            }

            // send directory of server to client
            SendDirectory(tcpClient);

            do
            {
                NetzwerkDings netzDings = null;

                do
                {
                    if (ns.DataAvailable)
                    {
                        netzDings = NetworkTalk.RecievePackage(ns);

                    }
                }
                while (netzDings == null);

                // process data
                if (netzDings.DirectoryArray == null)
                {
                    SendChildDirectory(tcpClient, netzDings);
                }
            } while (true);
        }

        /// <summary>
        /// Sends directory of the server to the client
        /// </summary>
        /// <param name="tcpClient"></param>
        /// <param name="error"></param>
        private static void SendDirectory(TcpClient tcpClient, bool error = false)
        {
            DirectoryInfo info = new DirectoryInfo(root);
            NetzwerkDings netzDings = new NetzwerkDings();

            netzDings.DirectoryArray = info.GetDirectories();
            netzDings.FileArray = info.GetFiles();
            netzDings.Paths = Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly);
            netzDings.RequestedDirectoryPath = root;
            netzDings.Error = error;

            NetworkTalk.SendPackage(netzDings, tcpClient.GetStream());

            SendLog("Directories of Server sent to client");
        }

        /// <summary>
        /// Send subdirectories if a folder is opened or downloads the files
        /// </summary>
        /// <param name="tcpClient"></param>
        /// <param name="netzDings"></param>
        private static void SendChildDirectory(TcpClient tcpClient, NetzwerkDings netzDings)
        {
            // if user wants to download a directorie
            if (netzDings.DirectoryDownload)
            {
                DirectoryDownload(tcpClient, netzDings);
            }

            // if user wants to download a file
            else if (netzDings.FileDownload)
            {
                FileDownload(tcpClient, netzDings);
            }

            // if user wants to upload a directory
            else if (netzDings.DirectoryUpload)
            {
                DirectoryUpload(tcpClient, netzDings);   
            }

            // if user wants to upload a file
            else if (netzDings.FileUpload)
            {
                FileUpload(tcpClient, netzDings);
            }

            // if user wants to delete directories
            else if (netzDings.DirectoryDelete)
            {
                DirectoryDelete(tcpClient, netzDings);
            }
            
            // if user wants to delete a file
            else if (netzDings.FileDelete)
            {
                FileDelete(tcpClient, netzDings);
            }

            // if user wants to create a directory
            else if (netzDings.CreateDirectory)
            {
                CreateDirectory(tcpClient, netzDings);
            }

            // if user wants to rename a directory
            else if (netzDings.DirectoryRename)
            {
                DirectoryRename(tcpClient, netzDings);
            }

            // if user wants to rename a file
            else if (netzDings.FileRename)
            {
                FileRename(tcpClient, netzDings);
            }

            // sends subdirectories
            else
            {
                DirectoryInfo info = new DirectoryInfo(netzDings.RequestedDirectoryPath);
                netzDings.DirectoryArray = info.GetDirectories();
                netzDings.FileArray = info.GetFiles();
                netzDings.Paths = Directory.GetDirectories(netzDings.RequestedDirectoryPath, "*", SearchOption.TopDirectoryOnly);
                NetworkTalk.SendPackage(netzDings, tcpClient.GetStream());
                SendLog("Content of selected folder sent to client");
            }
        }

        /// <summary>
        /// Send directory which user wants to download
        /// </summary>
        /// <param name="tcpClient"></param>
        /// <param name="netzDings"></param>
        private static void DirectoryDownload(TcpClient tcpClient, NetzwerkDings netzDings)
        {
            SendLog("Download started");
            DirectoryInfo directory = new DirectoryInfo(netzDings.RequestedDirectoryPath);
            netzDings.DownloadFiles = new List<FileInfo>();
            netzDings.Filepaths = new List<string>();

            netzDings.DownloadDirectory = Directory.GetDirectories(netzDings.RequestedDirectoryPath, "*", SearchOption.AllDirectories);
            FileInfo[] file = directory.GetFiles();

            // add for each file the path (from the first directory)
            foreach (var item in file)
            {
                netzDings.DownloadFiles.Add(item);
                netzDings.Filepaths.Add(RemoveRootPath(netzDings.RequestedDirectoryPath, netzDings.RequestedDirectoryPath) + "\\");
            }

            // add for each file the path (all subdirectories) 
            for (int i = 0; i < netzDings.DownloadDirectory.Length; i++)
            {
                directory = new DirectoryInfo(netzDings.DownloadDirectory[i]);
                file = directory.GetFiles();

                foreach (var item in file)
                {
                    netzDings.DownloadFiles.Add(item);
                    netzDings.Filepaths.Add(RemoveRootPath(netzDings.DownloadDirectory[i], netzDings.RequestedDirectoryPath) + "\\");
                }
            }

            // remove path from directory till only name is left
            for (int i = 0; i < netzDings.DownloadDirectory.Length; i++)
            {
                netzDings.DownloadDirectory[i] = RemoveRootPath(netzDings.DownloadDirectory[i], netzDings.RequestedDirectoryPath);
            }

            // if no subdirectories are selected use only requested path
            if (netzDings.DownloadDirectory.Length == 0)
            {
                netzDings.DownloadDirectory = new string[1];
                netzDings.DownloadDirectory[0] = RemoveRootPath(netzDings.RequestedDirectoryPath, netzDings.RequestedDirectoryPath);
            }

            NetworkTalk.SendPackage(netzDings, tcpClient.GetStream());
            SendLog("All files sent to client");
        }

        /// <summary>
        /// Sends file which user wants to download
        /// </summary>
        /// <param name="tcpClient"></param>
        /// <param name="netzDings"></param>
        private static void FileDownload(TcpClient tcpClient, NetzwerkDings netzDings)
        {
            FileInfo fileInfo = new FileInfo(netzDings.RequestedDirectoryPath);
            netzDings.DownloadFiles = new List<FileInfo>();
            netzDings.DownloadFiles.Add(fileInfo);
            NetworkTalk.SendPackage(netzDings, tcpClient.GetStream());
            SendLog("All files sent to client");
        }

        /// <summary>
        /// If user wants to upload a directory
        /// </summary>
        /// <param name="tcpClient"></param>
        /// <param name="netzDings"></param>
        private static void DirectoryUpload(TcpClient tcpClient, NetzwerkDings netzDings)
        {

            SendLog("Upload started");
            bool error = false;

            // Create downloaded folders if they dont exist already
            for (int i = 0; i < netzDings.DownloadDirectory.Length; i++)
            {
                netzDings.DownloadDirectory[i] = netzDings.RequestedDirectoryPath + netzDings.DownloadDirectory[i];

                if (!Directory.Exists(netzDings.DownloadDirectory[i]))
                {
                    try
                    {
                        Directory.CreateDirectory(netzDings.DownloadDirectory[i]);
                    }
                    catch (Exception)
                    {
                        error = true;
                    }
                }
            }

            for (int i = 0; i < netzDings.DownloadFiles.Count; i++)
            {
                string path = netzDings.RequestedDirectoryPath + netzDings.Filepaths[i] + netzDings.DownloadFiles[i];

                try
                {
                    netzDings.DownloadFiles[i].CopyTo(path);
                }
                catch (Exception)
                {
                    error = true;
                }
            }

            // if files were copied show message, if only error dont show message
            if (!error)
            {
                SendLog("File uploaded");
            }

            SendDirectory(tcpClient, error);
        }

        /// <summary>
        /// Upload a file to the server
        /// </summary>
        /// <param name="tcpClient"></param>
        /// <param name="netzDings"></param>
        private static void FileUpload(TcpClient tcpClient, NetzwerkDings netzDings)
        {
            bool error = false;
            SendLog("Upload started");

            string path = Convert.ToString(netzDings.DownloadFiles[0]);
            int index = 0;

            for (int i = path.Length - 1; i > 0; i--)
            {
                // find last // to delete everything except the name
                if (path[i] == 92)
                {
                    index = i;
                    break;
                }
            }

            // get file name
            path = path.Remove(0, index + 1);
            if (netzDings.RequestedDirectoryPath == string.Empty)
            {
                // add destination to file name
                path = root + path;
            }
            else
            {
                path = netzDings.RequestedDirectoryPath + "\\" + path;
            }
            try
            {
                netzDings.DownloadFiles[0].CopyTo(path);
                SendLog("File uploaded");
            }
            catch (Exception)
            {
                SendLog("File upload error");
                error = true;
            }

            SendDirectory(tcpClient, error);
        }

        /// <summary>
        /// Deletes a directory on the server
        /// </summary>
        /// <param name="tcpClient"></param>
        /// <param name="netzDings"></param>
        private static void DirectoryDelete(TcpClient tcpClient, NetzwerkDings netzDings)
        {
            bool error = false;

            try
            {
                Directory.Delete(netzDings.RequestedDirectoryPath, true);
                SendLog("Directories deleted");
            }
            catch (Exception)
            {
                SendLog("Error delete directory");
                error = true;
            }

            SendDirectory(tcpClient, error);
        }

        /// <summary>
        /// Delete a file on the server
        /// </summary>
        /// <param name="tcpClient"></param>
        /// <param name="netzDings"></param>
        private static void FileDelete(TcpClient tcpClient, NetzwerkDings netzDings)
        {
            bool error = false;

            try
            {
                File.Delete(netzDings.RequestedDirectoryPath);
                SendLog("File deleted");
            }
            catch (Exception)
            {
                error = true;
                SendLog("Error deleting file");
            }

            SendDirectory(tcpClient, error);
        }

        /// <summary>
        /// Create a new directory on the server
        /// </summary>
        /// <param name="tcpClient"></param>
        /// <param name="netzDings"></param>
        private static void CreateDirectory(TcpClient tcpClient, NetzwerkDings netzDings)
        {
            string path = netzDings.RequestedDirectoryPath + "\\" + netzDings.NewName;

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(netzDings.RequestedDirectoryPath + "\\" + netzDings.NewName);
                SendLog("Directory created");
            }
            else
            {
                SendLog("Directory not created. Directory already exists.");
            }

            SendDirectory(tcpClient);
        }

        /// <summary>
        /// Rename a directory on the server
        /// </summary>
        /// <param name="tcpClient"></param>
        /// <param name="netzDings"></param>
        private static void DirectoryRename(TcpClient tcpClient, NetzwerkDings netzDings)
        {
            if (Directory.Exists(netzDings.RequestedDirectoryPath))
            {
                Directory.Move(netzDings.RequestedDirectoryPath, netzDings.NewName);
                SendLog("Directory renamed");
            }
            else
            {
                SendLog("Directory not renamed. Directory doesnt exist.");
            }

            SendDirectory(tcpClient);
        }

        /// <summary>
        /// Rename a file
        /// </summary>
        /// <param name="tcpClient"></param>
        /// <param name="netzDings"></param>
        private static void FileRename(TcpClient tcpClient, NetzwerkDings netzDings)
        {
            if (File.Exists(netzDings.RequestedDirectoryPath))
            {
                File.Move(netzDings.RequestedDirectoryPath, netzDings.NewName);
                SendLog("File renamed");
            }
            else
            {
                SendLog("File not renamed. Directory doesnt exist.");
            }

            SendDirectory(tcpClient);
        }

        /// <summary>
        /// Modify path
        /// </summary>
        /// <param name="path1"></param>
        /// <param name="path2"></param>
        /// <returns></returns>
        private static string RemoveRootPath(string path1, string path2)
        {
            int index = 0;

            for (int i = path2.Length - 1; i > 0; i--)
            {
                // find last // to delete //name
                if (path2[i] == 92)
                {
                    index = i;
                    break;
                }
            }

            path2 = path2.Remove(index, path2.Length - index);
            path2 += "\\";
            path1 = path1.Replace(path2, string.Empty);

            return path1;
        }

        /// <summary>
        /// Send UDP broadcast to monitoring tool
        /// </summary>
        /// <param name="message">Message</param>
        private static void SendLog(string message)
        {
            UdpClient client = new UdpClient();
            IPEndPoint ip = new IPEndPoint(IPAddress.Parse("255.255.255.255"), PORT);
            message = DateTime.Now + ": " + message;
            byte[] bytes = Encoding.ASCII.GetBytes(message);
            client.Send(bytes, bytes.Length, ip);
            client.Close();
        }
    }
}