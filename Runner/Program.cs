#if DEBUG && !(I1 || I2 || I4 || I8)
#define I8 // debug word size, only necessary to edit if you're actually editing runner
#endif
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
#else
#error No word size constant
#endif
using System.Reflection;

var asm = Assembly.LoadFile(Path.GetFullPath(args[0]));

AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
{
    if (args.Name == "URCL, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null") return asm;
    return null;
};
Runner(args.Length > 1 && args[1] == "-d");

static void Runner(bool debugState)
{
    (word[] regs, word[] mem, word sp) = URCL.Execute(new PortImpl());
    if (debugState)
    {
        Console.WriteLine("\n---");
        Console.WriteLine("HLT reached; control returned to Runner");
        if (mem.Length > 0) Console.WriteLine($"SP: {sp}");
        for (var i = 0; i < regs.Length; i++)
        {
            Console.WriteLine($"${i + 1}: {regs[i]}");
        }
        if (mem.Length > 0)
        {
            Console.WriteLine($"Memory (length {mem.Length}, in absolute addresses including DWs, idk what heap zero is in absolute terms sorry)");
            for (var i = 0; i < mem.Length; i++)
            {
                Console.WriteLine($"({i}) -> {mem[i]}");
            }
        }
    }
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
