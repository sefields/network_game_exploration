using System;
using System.Net;
using System.Net.Sockets;

namespace network_game_server
{
    public class Client
    {
        public static int dataBufferSize = 4096;
        public int id;
        public TCP tcp;
        public UDP udp;

        public Client(int _clientId)
        {
            id = _clientId;
            tcp = new TCP(id);
            udp = new UDP(id);
        }

        public class TCP {
            public TcpClient socket;
            private readonly int id;
            private NetworkStream mStream;
            private byte[] receiveBuffer;
            private Packet mReceivedPacket;

            public TCP(int _id)
            {
                id = _id;
            }

            public void Connect(TcpClient _socket)
            {
                socket = _socket;
                socket.ReceiveBufferSize = dataBufferSize;
                socket.SendBufferSize = dataBufferSize;

                mStream = socket.GetStream();

                mReceivedPacket = new Packet();
                receiveBuffer = new byte[dataBufferSize];

                mStream.BeginRead(receiveBuffer, 0, dataBufferSize, ReceiveCallback, null);

                ServerSend.Welcome(id, "Welcome to the server!");
            }

            public void SendData(Packet _packet)
            {
                try
                {
                    if (socket != null)
                    {
                        mStream.BeginWrite(_packet.ToArray(), 0, _packet.Length(), null, null);
                    }
                }
                catch (Exception _ex)
                {
                    Console.WriteLine($"Error sending data to player {id} via TCP: {_ex}");
                }
            }

            private void ReceiveCallback(IAsyncResult _result)
            {
                try
                {
                    // stops reading and looks at what was received.
                    int _byteLength = mStream.EndRead(_result);
                    if (_byteLength <= 0)
                    {
                        //TODO: disconnect
                        return;
                    }

                    byte[] _data = new byte[_byteLength];
                    Array.Copy(receiveBuffer, _data, _byteLength);

                    mReceivedPacket.Reset(HandleData(_data));

                    // start reading again.
                    mStream.BeginRead(receiveBuffer, 0, dataBufferSize, ReceiveCallback, null);
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
                            Server.packetHandlers[_packetId](id, _packet);
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
            public IPEndPoint endPoint;

            private int id;

            public UDP(int _id)
            {
                id = _id;
            }

            public void Connect(IPEndPoint _endPoint)
            {
                endPoint = _endPoint;
                ServerSend.UDPTest(id);
            }

            public void SendData(Packet _packet)
            {
                Server.SendUDPData(endPoint, _packet);
            }

            public void HandleData(Packet _packetData)
            {
                int _packetLength = _packetData.ReadInt();
                byte[] _packetBytes = _packetData.ReadBytes(_packetLength);

                ThreadManager.ExecuteOnMainThread(() =>
                {
                    using (Packet _packet = new Packet(_packetBytes))
                    {
                        int _packetId = _packet.ReadInt();
                        Server.packetHandlers[_packetId](id, _packet);
                    }
                });
            }
        }
    }
}
