using Server.NetworkContracts_Generater;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Server.Utils
{
    internal static class PacketIO
    {
        public static void Send(NetworkStream stream, IPacket packet)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            using (BinaryWriter binaryWriter = new BinaryWriter(memoryStream))
            {
                // 패킷 길이 헤더
                binaryWriter.Write((int)0); // 아직 몇바이트 보낼지 계산이 안되었으므로 공간만 확보
                // 패킷 id
                binaryWriter.Write((ushort)packet.PacketId);
                // 패킷 데이터
                packet.Serialize(binaryWriter);

                byte[] buffer = memoryStream.ToArray();
                int length = buffer.Length - sizeof(int); // id + 데이터 바이트수
                BitConverter.TryWriteBytes(buffer.AsSpan(0, 4), length);

                stream.Write(buffer, 0, buffer.Length);
            }
        }

        public static IPacket? Recv(NetworkStream stream)
        {
            // 길이 헤더 읽기 (4byte)
            byte[] lengthBuffer = new byte[4];
            if (!ReadExact(stream, lengthBuffer, 4))
                return null; // 연결끊김

            int length = BitConverter.ToInt32(lengthBuffer);

            // 나머지 읽기
            byte[] dataBuffer = new byte[length];
            if (!ReadExact(stream, dataBuffer, length))
                return null;

            using (MemoryStream memoryStream = new MemoryStream(dataBuffer))
            using (BinaryReader binaryReader = new BinaryReader(memoryStream))
            {
                PacketId packetId = (PacketId)binaryReader.ReadUInt16();
                IPacket packet = PacketFactory.Create(packetId);
                packet.Deserialize(binaryReader);
                return packet;
            }
        }

        /// <summary>
        /// 정확히 count 만큼의 바이트를 읽을때까지 반복
        /// </summary>
        static bool ReadExact(NetworkStream stream, byte[] buffer, int count)
        {
            int offset = 0; // 누적된 바이트 위치
            while (offset < count)
            {
                int bytesRead = stream.Read(buffer, offset, count - offset);
                if (bytesRead <= 0)
                    return false; // 연결 끊김
                offset += bytesRead;
            }
            return true;
        }
    }
}
