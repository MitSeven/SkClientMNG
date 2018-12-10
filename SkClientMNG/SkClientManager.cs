using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace SkClientMNG
{
    [Serializable]
    public class SkClientManager
    {
        public static Socket ClientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        public bool IsSKconnected { get; private set; } = false;
        public delegate void Changed(object T, ModeEventArgs e);
        public event Changed ChangeEvent;
        private bool AbortSend = false;
        public object ExMessage { get; private set; }
        public string IpClient
        {
            get
            {
                string IP_current;
                try
                {
                    IP_current = GetLocalIPv4(NetworkInterfaceType.Ethernet);
                    if (string.IsNullOrEmpty(IP_current))
                    {
                        IP_current = GetLocalIPv4(NetworkInterfaceType.Wireless80211);
                    }
                }
                catch
                {
                    IP_current = "127.0.0.1";
                }
                return IP_current;
            }
        }
        private Thread waitrespond;

        public void ConnectToServer(string IPServer, int Timeout)
        {
            if (!ClientSocket.Connected)
            {
                ClientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    ClientSocket.Connect(IPAddress.Parse(IPServer), 777);
                }
                catch
                {
                    IsSKconnected = false;
                    ExMessage = "Cannot connect to server!";
                    ChangeEvent?.Invoke(ExMessage, new ModeEventArgs((int)ModeEvent.ServerError));
                    return;
                }
            }
            bool isOK = SendString("ipclient&" + IpClient, Timeout);
            if (isOK)
            {
                IsSKconnected = true;
                ExMessage = "Connected!";
                ChangeEvent?.Invoke(ExMessage, new ModeEventArgs(ModeEvent.SocketMessage));
                waitrespond = new Thread(new ThreadStart(() =>
                {
                    while (IsSKconnected)
                    {
                        ExMessage = ReceiveResponse;
                        ChangeEvent?.Invoke(ExMessage, new ModeEventArgs(ModeEvent.ServerRespond));
                    }
                }))
                {
                    IsBackground = true
                };
                waitrespond.Start();
            }
            else
            {
                IsSKconnected = false;
                try
                {
                    ClientSocket.Close();
                }
                catch { }
            }
        }
        public void Close()
        {
            IsSKconnected = false;
            try
            {
                AbortSend = true;
                SendString("exit&" + IpClient, 5000); // Tell the server we are exiting
            }
            catch { }
            AbortSend = false;
            try
            {
                ClientSocket.Close();
            }
            catch { }
            ExMessage = "Connect closed!";
            ChangeEvent?.Invoke(ExMessage, new ModeEventArgs(ModeEvent.SocketMessage));
            try
            {
                waitrespond.Abort();
            }
            catch { }
        }
        public bool Send(string Text, int Timeout)
        {
            try
            {
                SendString("rcvrq&" + Text, Timeout);
                return true;
            }
            catch
            {
                return false;
            }
        }
        /// <summary>
        /// Sends a string to the server with ASCII encoding.
        /// </summary>
        private bool SendString(string Text, int Timeout)
        {
            try
            {
                if (CheckConnected(ClientSocket, Timeout))
                {
                    byte[] buffer = SerializeData(Text);
                    ClientSocket.Send(buffer, 0, buffer.Length, SocketFlags.None);
                }
                else
                {
                    IsSKconnected = false;
                    if (!AbortSend)
                    {
                        ExMessage = "Lost connect from server!";
                        ChangeEvent?.Invoke(ExMessage, new ModeEventArgs((int)ModeEvent.ServerError));
                    }
                    ClientSocket.Close();
                    return false;
                }
            }
            catch
            {
                IsSKconnected = false;
                if (!AbortSend)
                {
                    ExMessage = "Lost connect from server!";
                    ChangeEvent?.Invoke(ExMessage, new ModeEventArgs((int)ModeEvent.ServerError));
                }
                ClientSocket.Close();
                return false;
            }
            return true;
        }
        private object ReceiveResponse
        {
            get
            {
                object rcvobj = null;
                var buffer = new byte[1024 * 32000];
                int received = 0;
                try
                {
                    received = ClientSocket.Receive(buffer, SocketFlags.None);
                }
                catch
                {
                    IsSKconnected = false;
                    rcvobj = "No respond from server!";
                    Close();
                    return rcvobj;
                }
                if (received == 0)
                {
                    rcvobj = "Data received empty!";
                    return rcvobj;
                }
                var data = new byte[received];
                Array.Copy(buffer, data, received);
                object text = DeserializeData(data);
                if (text == null)
                {
                    rcvobj = "Data received empty!";
                    return rcvobj;
                }
                rcvobj = CommandEx(text);
                return rcvobj;
            }
        }
        private object CommandEx(object command)
        {
            string[] cmdarr = command.ToString().Split(new char[] { '&' }, 2);
            switch (cmdarr.First())
            {
                case "exit":
                    {
                        Close();
                        break;
                    }
                default:
                    {
                        ExMessage = command;
                        break;
                    }
            }
            return ExMessage;
        }
        public bool CheckConnected(Socket Socket, int Timeout)
        {
            try
            {
                return !(Socket.Poll(Timeout, SelectMode.SelectRead) && Socket.Available == 0);
            }
            catch (SocketException)
            {
                return false;
            }
        }


        /// <summary>
        /// Nén đối tượng thành mảng byte[]
        /// </summary>
        /// <param name="o"></param>
        /// <returns></returns>
        private byte[] SerializeData(Object o)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    BinaryFormatter bf1 = new BinaryFormatter();
                    bf1.Serialize(ms, o);
                    return ms.ToArray();
                }
            }
            catch
            {
                return new byte[0];
            }
        }

        /// <summary>
        /// Giải nén mảng byte[] thành đối tượng object
        /// </summary>
        /// <param name="theByteArray"></param>
        /// <returns></returns>
        private object DeserializeData(byte[] theByteArray)
        {
            if (theByteArray.Length == 0)
            {

                return null;
            }
            else
            {
                try
                {
                    using (MemoryStream ms = new MemoryStream(theByteArray))
                    {
                        BinaryFormatter bf1 = new BinaryFormatter();
                        ms.Position = 0;
                        return bf1.Deserialize(ms);
                    }
                }
                catch
                {
                    return null;
                }
            }
        }
        /// <summary>
        /// Lấy ra IP V4 của card mạng đang dùng
        /// </summary>
        /// <param name="_type"></param>
        /// <returns></returns>
        private string GetLocalIPv4(NetworkInterfaceType _type)
        {
            string output = "";
            foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (item.NetworkInterfaceType == _type && item.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (UnicastIPAddressInformation ip in item.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            output = ip.Address.ToString();
                        }
                    }
                }
            }
            return output;
        }

    }
    public class ModeEventArgs
    {
        private ModeEvent _mode;
        public ModeEvent ModeEvent { get => _mode; set => _mode = value; }
        public ModeEventArgs(ModeEvent mode) => ModeEvent = mode;
    }
    public enum ModeEvent
    {
        ServerError,
        ServerRespond,
        SocketMessage,
    }
}