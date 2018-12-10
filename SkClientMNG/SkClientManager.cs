using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Collections;
using System.Collections.Generic;

namespace SkClientMNG
{
    [Serializable]
    public class SkClientManager
    {
        public static Socket ClientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        public bool IsSKconnected { get; private set; } = false;
        public delegate void Changed(object T, ModeEventArgs e);
        public event Changed ChangeEvent;
        private Thread waitrespond;
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
        public void ConnectToServer(string IPServer)
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
                    ChangeEvent?.Invoke(ExMessage, new ModeEventArgs(ModeEvent.ServerError));
                    return;
                }
            }
            bool isOK = SendString(new KeyValuePair<TypeSend, object>(TypeSend.IpAdress,IpClient));
            if (isOK)
            {
                IsSKconnected = true;
                ExMessage = "Server connected!";
                ChangeEvent?.Invoke(ExMessage, new ModeEventArgs(ModeEvent.SocketMessage));
                waitrespond = new Thread(new ThreadStart(() =>
                {
                    while (IsSKconnected)
                    {
                        ExMessage = GetReceiveResponse();
                        if (ExMessage != null)
                        {
                            ChangeEvent?.Invoke(ExMessage, new ModeEventArgs(ModeEvent.ServerRespond));
                        }
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
                SendString(new KeyValuePair<TypeSend, object>(TypeSend.Exit, IpClient));
            }
            catch { }
            AbortSend = false;
            try
            {
                ClientSocket.Close();
            }
            catch { }
            try
            {
                waitrespond.Abort();
            }
            catch { }
            ExMessage = "Connect closed!";
            ChangeEvent?.Invoke(ExMessage, new ModeEventArgs(ModeEvent.SocketMessage));
        }

        private object GetReceiveResponse()
        {
            byte[] buffer = new byte[1024 * 32000];
            int received = 0;
            try
            {
                received = ClientSocket.Receive(buffer, SocketFlags.None);
            }
            catch
            {
                return null;
            }
            if (received == 0)
            {
                return "Data received empty!";
            }
            var data = new byte[received];
            Array.Copy(buffer, data, received);
            object text = null;
            try
            {
                text = DeserializeData(data);
            }
            catch { }
            return text == null ? "Data received empty!" : CommandEx(text);
        }
        private object CommandEx(object command)
        {
            try
            {
                KeyValuePair<TypeReceived, object> cmdarr = (KeyValuePair<TypeReceived, object>)command;
                switch (cmdarr.Key)
                {
                    case TypeReceived.Exit:
                        {
                            Close();
                            break;
                        }
                    case TypeReceived.Respond:
                        {
                            ExMessage = cmdarr.Value;
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
            catch
            {
                return command;
            }
        }
        public bool Send(object Object) => SendString(new KeyValuePair<TypeSend, object>(TypeSend.Object, Object));
        private bool SendString(KeyValuePair<TypeSend,object> keyValuePair)
        {
            try
            {
                byte[] buffer = SerializeData(keyValuePair);
                ClientSocket.Send(buffer, 0, buffer.Length, SocketFlags.None);
                return true;
            }
            catch
            {
                IsSKconnected = false;
                if (!AbortSend)
                {
                    ExMessage = "Cannot send to server!";
                    ChangeEvent?.Invoke(ExMessage, new ModeEventArgs(ModeEvent.ServerError));
                }
                return false;
            }
        }
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
    public enum TypeSend
    {
        IpAdress,
        Exit,
        Object,
    }
    public enum TypeReceived
    {
        Exit,
        Respond,
        Received,
    }
}