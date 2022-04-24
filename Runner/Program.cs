using System.Reflection;
#if I1
using word = System.Byte;
using sword = System.SByte;
#elif I2
using word = System.UInt16;
using sword = System.Int16;
#elif I4
using word = System.UInt32;
using sword = System.Int32;
#elif I8
using word = System.UInt64;
using sword = System.Int64;
#elif DEBUG
using word = System.UInt64;
using sword = System.Int64;
#else
#error No word size constant
#endif

var asm = Assembly.LoadFile(Path.GetFullPath(args[0]));

AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
{
    if (args.Name == "URCL, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null") return asm;
    return null;
};
Runner();

static void Runner()
{
    URCL.Execute(new PortImpl());
}

class PortImpl : Port
{
    Random rng = new();
    word waitTime;
    public void Write(string port, word value)
    {
        switch (port)
        {
            case "NUMB":
                Console.Write(value);
                break;
            case "TEXT":
                Console.Write((char)value);
                break;
            case "RNG":
                rng = new((int)value);
                break;
            case "WAIT":
                waitTime = value;
                break;
            default:
                Console.WriteLine($"{port} <- {value}");
                break;
        }
    }

    public word Read(string port)
    {
        switch (port)
        {
            case "RNG":
#if I1
                return (byte)rng.Next(0x100);
#elif I2
                return (ushort)rng.Next(0x10000);
#elif I4
                return (uint)rng.Next();
#elif I8
                return (ulong)rng.NextInt64();
#endif
            case "WAIT":
                Task.Delay(TimeSpan.FromTicks((long)waitTime)).Wait();
                return 1;
            default:
                return 0;
        }
    }
}
