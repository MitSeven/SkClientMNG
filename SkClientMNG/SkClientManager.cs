using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace SkClientMNG
{
    public class SkClientManager
    {
        public static Socket ClientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        public bool IsSKconnected { get; private set; } = false;
        public delegate void Changed(object T, ModeEventArgs e);
        public event Changed ReceivedEvent;
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

        public void ConnectToServer(string IPServer)
        {
            if (!ClientSocket.Connected)
            {
                ClientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    AbortSend = false;
                    ClientSocket.Connect(IPAddress.Parse(IPServer), 777);
                }
                catch
                {
                    IsSKconnected = false;
                    ExMessage = "Cannot connect to server!";
                    ReceivedEvent?.Invoke(ExMessage, new ModeEventArgs(ModeEvent.SocketError));
                    return;
                }
            }
            bool isOK = SendString(new DictionaryEntry(DataType.IpClient, IpClient));
            if (isOK)
            {
                IsSKconnected = true;
                ExMessage = "Connected!";
                ReceivedEvent?.Invoke(ExMessage, new ModeEventArgs(ModeEvent.SocketError));
                waitrespond = new Thread(new ThreadStart(() =>
                {
                    while (IsSKconnected)
                    {
                        ExMessage = ReceiveResponse;
                        ReceivedEvent?.Invoke(ExMessage, new ModeEventArgs(ModeEvent.ServerRespond));
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
                SendString(new DictionaryEntry(DataType.Exit, IpClient)); // Tell the server we are exiting
            }
            catch { }
            try
            {
                ClientSocket.Close();
            }
            catch { }
            ExMessage = "Connect closed!";
            ReceivedEvent?.Invoke(ExMessage, new ModeEventArgs(ModeEvent.SocketError));
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
                SendString(new DictionaryEntry(DataType.Object, Text));
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
        private bool SendString(DictionaryEntry dictionaryEntry)
        {
            var dtsend = new DictionaryEntry
            {
                Key = (int)dictionaryEntry.Key,
                Value = dictionaryEntry.Value
            };
            try
            {
                byte[] buffer = SerializeData(dtsend);
                ClientSocket.Send(buffer, 0, buffer.Length, SocketFlags.None);
            }
            catch
            {
                IsSKconnected = false;
                if (!AbortSend)
                {
                    ExMessage = "Lost connect from server!";
                    ReceivedEvent?.Invoke(ExMessage, new ModeEventArgs(ModeEvent.SocketError));
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
                object text = "";
                try
                {
                    text = DeserializeData(data);
                    if (text == null)
                    {
                        rcvobj = "Data received empty!";
                        return rcvobj;
                    }
                }
                catch
                {
                    rcvobj = "Data struct false!";
                    return rcvobj;
                }
                rcvobj = CommandEx(text);
                return rcvobj;
            }
        }
        private object CommandEx(object command)
        {
            try
            {
                var cmd = (DictionaryEntry)command;
                var cmdarr = (DataType)cmd.Key;
                switch (cmdarr)
                {
                    case DataType.Exit:
                        {
                            Close();
                            break;
                        }
                    case DataType.Object:
                        {
                            ExMessage = cmd.Value;
                            break;
                        }
                    default:
                        {
                            break;
                        }
                }
            }
            catch
            {
                ExMessage = command;
            }
            return ExMessage;
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
        ServerRespond,
        SocketError,
    }
    public enum DataType
    {
        IpClient,
        Exit,
        Object,
        Received,
    }
}