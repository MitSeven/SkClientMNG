using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;

namespace SkClientMNG
{
    [Serializable]
    public class SkClientManager
    {
        public static Socket ClientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        public bool IsSKconnected { get; private set; } = false;
        public delegate void Changed(object T);
        public event Changed ChangeEvent;
        public object ExMessage;
        public bool ConnectToServer(string IPServer, int Timeout)
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
                    ExMessage = "Không thể kết nối đến máy chủ!";
                    ChangeEvent?.Invoke(ExMessage);
                    return false;
                }
            }
            bool isOK = SendString("ipclient&" + GetIpClient(), Timeout);
            if (isOK)
            {
                IsSKconnected = true;
                return true;
            }
            else
            {
                IsSKconnected = false;
                try
                {
                    ClientSocket.Close();
                }
                catch { }
                return false;
            }
        }
        private string GetIpClient()
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
        public object RequestLoop(string Text, int Timeout)
        {
            object rcvrq = null;
            try
            {
                SendString("rcvrq&" + Text, Timeout);
                rcvrq = ReceiveResponse();
            }
            catch
            {
                return rcvrq;
            }
            return rcvrq;
        }
        /// <summary>
        /// Sends a string to the server with ASCII encoding.
        /// </summary>
        public bool SendString(string Text, int Timeout)
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
                    ExMessage = "Mất kết nối đến máy chủ!";
                    ChangeEvent?.Invoke(ExMessage);
                    ClientSocket.Close();
                    return false;
                }
            }
            catch
            {
                IsSKconnected = false;
                ExMessage = "Mất kết nối đến máy chủ!";
                ChangeEvent?.Invoke(ExMessage);
                ClientSocket.Close();
                return false;
            }
            return true;
        }
        public object ReceiveResponse()
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
                ExMessage = "Máy chủ không phản hồi!";
                ChangeEvent?.Invoke(ExMessage);
                ClientSocket.Close();
                return rcvobj;
            }
            if (received == 0)
            {
                ExMessage = "Không có dữ liệu!";
                ChangeEvent?.Invoke(ExMessage);
                return rcvobj;
            }
            var data = new byte[received];
            Array.Copy(buffer, data, received);
            object text = DeserializeData(data);
            if (text == null)
            {
                ExMessage = "Không có dữ liệu!";
                ChangeEvent?.Invoke(ExMessage);
                return rcvobj;
            }
            rcvobj = text;
            ExMessage = text;
            ChangeEvent?.Invoke(ExMessage);
            return rcvobj;
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
        /// Close socket 
        /// </summary>
        /// 
        public bool Close()
        {
            try
            {
                SendString("exit&" + GetIpClient(), 5000); // Tell the server we are exiting
                try
                {
                    ClientSocket.Close();
                }
                catch { }
                IsSKconnected = false;
            }
            catch
            {
                IsSKconnected = false;
                return false;
            }
            return true;
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
}
