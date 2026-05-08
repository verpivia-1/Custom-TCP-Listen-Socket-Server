using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Server.InGame.Networking
{
    internal static class NetworkVariableSerializer<T>
    {
        public static readonly Action<BinaryWriter, T> Write;
        public static readonly Func<BinaryReader, T> Read;
        static NetworkVariableSerializer()
        {
            Type t = typeof(T);

            // typelookup 테이블 기반으로 코드 자동생성하게하면 지원하는 타입에 대해 유지보수하기가 편해지긴함
            if (t == typeof(bool)) { Write = (w, v) => w.Write(Unsafe.As<T, bool>(ref v)); Read = r => { bool x = r.ReadBoolean(); return Unsafe.As<bool, T>(ref x); }; return; }
            if (t == typeof(byte)) { Write = (w, v) => w.Write(Unsafe.As<T, byte>(ref v)); Read = r => { byte x = r.ReadByte(); return Unsafe.As<byte, T>(ref x); }; return; }
            if (t == typeof(sbyte)) { Write = (w, v) => w.Write(Unsafe.As<T, sbyte>(ref v)); Read = r => { sbyte x = r.ReadSByte(); return Unsafe.As<sbyte, T>(ref x); }; return; }
            if (t == typeof(short)) { Write = (w, v) => w.Write(Unsafe.As<T, short>(ref v)); Read = r => { short x = r.ReadInt16(); return Unsafe.As<short, T>(ref x); }; return; }
            if (t == typeof(ushort)) { Write = (w, v) => w.Write(Unsafe.As<T, ushort>(ref v)); Read = r => { ushort x = r.ReadUInt16(); return Unsafe.As<ushort, T>(ref x); }; return; }
            if (t == typeof(int)) { Write = (w, v) => w.Write(Unsafe.As<T, int>(ref v)); Read = r => { int x = r.ReadInt32(); return Unsafe.As<int, T>(ref x); }; return; }
            if (t == typeof(uint)) { Write = (w, v) => w.Write(Unsafe.As<T, uint>(ref v)); Read = r => { uint x = r.ReadUInt32(); return Unsafe.As<uint, T>(ref x); }; return; }
            if (t == typeof(long)) { Write = (w, v) => w.Write(Unsafe.As<T, long>(ref v)); Read = r => { long x = r.ReadInt64(); return Unsafe.As<long, T>(ref x); }; return; }
            if (t == typeof(ulong)) { Write = (w, v) => w.Write(Unsafe.As<T, ulong>(ref v)); Read = r => { ulong x = r.ReadUInt64(); return Unsafe.As<ulong, T>(ref x); }; return; }
            if (t == typeof(float)) { Write = (w, v) => w.Write(Unsafe.As<T, float>(ref v)); Read = r => { float x = r.ReadSingle(); return Unsafe.As<float, T>(ref x); }; return; }
            if (t == typeof(double)) { Write = (w, v) => w.Write(Unsafe.As<T, double>(ref v)); Read = r => { double x = r.ReadDouble(); return Unsafe.As<double, T>(ref x); }; return; }

            if (t == typeof(Vector3))
            {
                Write = (w, v) =>
                {
                    Vector3 x = Unsafe.As<T, Vector3>(ref v);
                    w.Write(x.X);
                    w.Write(x.Y);
                    w.Write(x.Z);
                };
                Read = r =>
                {
                    Vector3 x = new Vector3
                    {
                        X = r.ReadSingle(),
                        Y = r.ReadSingle(),
                        Z = r.ReadSingle()
                    };
                    return Unsafe.As<Vector3, T>(ref x);
                };
                return;
            }

            if (t == typeof(Quaternion))
            {
                Write = (w, v) =>
                {
                    Quaternion x = Unsafe.As<T, Quaternion>(ref v);
                    w.Write(x.X);
                    w.Write(x.Y);
                    w.Write(x.Z);
                    w.Write(x.W);
                };
                Read = r =>
                {
                    Quaternion x = new Quaternion
                    {
                        X = r.ReadSingle(),
                        Y = r.ReadSingle(),
                        Z = r.ReadSingle(),
                        W = r.ReadSingle()
                    };
                    return Unsafe.As<Quaternion, T>(ref x);
                };
                return;
            }

            throw new NotSupportedException($"NetworkVariable<{t.Name}> is not supported. Only primitives & Vector3 & Quaternion are available.");
        }
    }
}