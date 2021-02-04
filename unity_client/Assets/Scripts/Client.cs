using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net;
using System.Net.Sockets;

public class Client : MonoBehaviour
{
    public static Client instance;
    public static int dataBufferSize = 4096;

    public string ip = "127.0.0.1"; // localhost
    public int port = 26951;
    public int myId = 0;
    public TCP tcp;
    public UDP udp;

    private delegate void PacketHandler(Packet _packet);
    private static Dictionary<int, PacketHandler> packetHandlers;
    
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else if (instance != this)
        {
            Debug.Log("Instance already exists, destroying object!");
            Destroy(this);
        }
    }

    private void Start()
    {
        tcp = new TCP();
        udp = new UDP();
    }

    public void ConnectToServer()
    {
        InitializeClientData();
        tcp.Connect();
    }

    public class TCP
    {
        public TcpClient mSocket;

        private NetworkStream mStream;
        private Packet mReceivedPacket;
        private byte[] mReceiveBuffer;

        public void Connect()
        {
            mSocket = new TcpClient
            {
                ReceiveBufferSize = dataBufferSize,
                SendBufferSize = dataBufferSize
            };

            mReceiveBuffer = new byte[dataBufferSize];
            mSocket.BeginConnect(instance.ip, instance.port, ConnectCallback, mSocket);
        }

        private void ConnectCallback(IAsyncResult _result)
        {
            mSocket.EndConnect(_result);

            if (!mSocket.Connected)
            {
                return;
            }

            mStream = mSocket.GetStream();

            mReceivedPacket = new Packet();

            mStream.BeginRead(mReceiveBuffer, 0, dataBufferSize, ReceiveCallback, null);
        }

        public void SendData(Packet _packet)
        {
            try
            {
                if (mSocket != null)
                {
                    mStream.BeginWrite(
                        _packet.ToArray(),
                        0,
                        _packet.Length(),
                        null,
                        null);
                }
            }
            catch (Exception _ex)
            {
                Debug.Log($"Error sending data to server via TCP: {_ex}");
            }
        }

        private void ReceiveCallback(IAsyncResult _result)
        {
            try
            {
                int _byteLength = mStream.EndRead(_result);
                if (_byteLength <= 0)
                {
                    //TODO: disconnect
                    return;
                }

                byte[] _data = new byte[_byteLength];
                Array.Copy(mReceiveBuffer, _data, _byteLength);

                mReceivedPacket.Reset(HandleData(_data));
                mStream.BeginRead(mReceiveBuffer, 0, dataBufferSize, ReceiveCallback, null);
            }
            catch (Exception _ex)
            {
                Console.WriteLine($"Error receiving TCP data: {_ex}");
                //TODO: disconnect
            }
        }

        private bool HandleData(byte[] _data)
        {
            int _packetLength = 0;
            mReceivedPacket.SetBytes(_data);

            //    If we find that this byte array is 4 or longer,
            //    this is a packet length according to our packet implementation.
            if (mReceivedPacket.UnreadLength() >= 4)
            {
                _packetLength = mReceivedPacket.ReadInt();
                if (_packetLength <= 0) // i.e. if the packet was already read
                {
                    return true;
                }
            }

            while (_packetLength > 0 && _packetLength <= mReceivedPacket.UnreadLength())
            {
                byte[] _packetBytes = mReceivedPacket.ReadBytes(_packetLength);
                ThreadManager.ExecuteOnMainThread(() =>
                {
                    using (Packet _packet = new Packet(_packetBytes))
                    {
                        int _packetId = _packet.ReadInt();
                        packetHandlers[_packetId](_packet);
                    }
                });

                _packetLength = 0;
                if (mReceivedPacket.UnreadLength() >= 4)
                {
                    _packetLength = mReceivedPacket.ReadInt();
                    if (_packetLength <= 0) // i.e. if the packet was already read
                    {
                        return true;
                    }
                }
            }

            if (_packetLength <= 1)
            {
                return true;
            }

            return false;
        }
    }

    public class UDP
    {
        public UdpClient socket;
        public IPEndPoint endPoint;

        public UDP()
        {
            endPoint = new IPEndPoint(IPAddress.Parse(instance.ip), instance.port);
        }

        public void Connect(int _localPort)
        {
            socket = new UdpClient(_localPort);
            socket.Connect(endPoint);
            socket.BeginReceive(ReceiveCallback, null);

            using (Packet _packet = new Packet())
            {
                SendData(_packet);
            }
        }

        public void SendData(Packet _packet)
        {
            try
            {
                _packet.InsertInt(instance.myId);
                if (socket != null)
                {
                    socket.BeginSend(
                        _packet.ToArray(),
                        _packet.Length(),
                        null,
                        null);
                }
            }
            catch (Exception _ex)
            {
                Debug.Log($"Error sending data to server via UDP: {_ex}");
            }
        }

        private void ReceiveCallback(IAsyncResult _result)
        {
            try
            {
                byte[] _data = socket.EndReceive(_result, ref endPoint);
                socket.BeginReceive(ReceiveCallback, null);

                if (_data.Length < 4)
                {
                    //TODO: disconnect
                    return;
                }

                HandleData(_data);
            }
            catch
            {
                //TODO: disconnect
            }
        }

        private void HandleData(byte[] _data)
        {
            using (Packet _packet = new Packet(_data))
            {
                int _packetLength = _packet.ReadInt();
                _data = _packet.ReadBytes(_packetLength);
            }

            ThreadManager.ExecuteOnMainThread(() =>
            {
                using (Packet _packet = new Packet(_data))
                {
                    int _packetId = _packet.ReadInt();
                    packetHandlers[_packetId](_packet);
                }
            });
        }
    }
    
    private void InitializeClientData()
    {
        packetHandlers = new Dictionary<int, PacketHandler>()
        {
            { (int)ServerPackets.welcome, ClientHandle.Welcome },
            { (int)ServerPackets.udpTest, ClientHandle.UDPTest }
        };

        Debug.Log("Initialized packets.");
    }
}
