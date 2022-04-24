
using System.Reflection;
using System.Reflection.Emit;
using Lokad.ILPack;

var program = RemoveMultiLineComments(File.ReadAllText(args[0]))
                       .ReplaceLineEndings().Split(Environment.NewLine)
                       .Select(line => new string(line.Select(ch => char.IsWhiteSpace(ch) ? ' ' : ch).ToArray())) // normalize tabs and other forms of whitespace to ' '. fuck your char literals
                       .Select(line => line[..(line + "//").IndexOf("//")].Trim())
                       .Where(line => !string.IsNullOrWhiteSpace(line));
var InstructionLabels = new Dictionary<string, Label>();
var DataLabels = new Dictionary<string, int>();
var DefineWords = new List<ulong>();
var Instructions = new List<Instruction>();

var WordLength = 8u;
var SupportedWordLengths = new[] { 8u, 16u, 32u, 64u };
var Registers = 8u;
var HeapSize = 16u;
var StackSize = 32u;

var AssemblyName = "URCL";
var TypeName = "URCL";
var MethodName = "Execute";

{
    string? labelName = null;
    foreach (var line in program)
    {
        if (line.StartsWith("."))
        {
            if (labelName is not null)
            {
                throw ErrorAndQuit($"Label .{labelName} points to another label");
            }
            labelName = line[1..];
            continue;
        }
        var whitespace = (line + " ").IndexOf(" ");
        var op = line[..whitespace].ToUpperInvariant();
        var rest = line[whitespace..].TrimStart();
        switch (op)
        {
            case "@ASSEMBLY":
                AssemblyName = rest;
                continue;
            case "@TYPE":
                TypeName = rest;
                continue;
            case "@METHOD":
                MethodName = rest;
                continue;
            case "BITS":
                if (rest.StartsWith("==")) rest = rest[2..].TrimStart();
                if (TryParseInt(rest, out WordLength))
                {
                    if (SupportedWordLengths.Contains(WordLength)) continue;
                    throw ErrorAndQuit($"Unsupported BITS == {WordLength}");
                }
                if (rest.StartsWith(">="))
                {
                    var minWord = ParseInt(rest[2..].TrimStart());
                    if (minWord > 64)
                    {
                        throw ErrorAndQuit($"Unsupported BITS >= {WordLength}");
                    }
                    WordLength = SupportedWordLengths.Last(word => word >= minWord);
                }
                else if (rest.StartsWith("<="))
                {
                    var maxWord = ParseInt(rest[2..].TrimStart());
                    if (maxWord < 8)
                    {
                        throw ErrorAndQuit($"Unsupported BITS <= {WordLength}");
                    }
                    WordLength = SupportedWordLengths.Last(word => word <= maxWord);
                }
                continue;
            case "RUN":
                continue;  // RUN ROM/RAM is ignored. Code is always ROM, but define words can be written to if desired.
            case "MINREG":
                Registers = ParseInt(rest);
                continue;
            case "MINHEAP":
                HeapSize = ParseInt(rest);
                continue;
            case "MINSTACK":
                StackSize = ParseInt(rest);
                continue;
            case "DW":
                if (labelName is not null)
                {
                    DataLabels[labelName] = DefineWords.Count;
                    labelName = null;
                }
                if (rest.StartsWith('[') && rest.EndsWith(']')) rest = rest[1..^1].Trim();
                while (TryParseImmediate(ref rest, out var imm))
                {
                    DefineWords.Add(imm);
                }
                continue;
            default:
                Label? label = null;
                if (labelName is not null)
                {
                    label = InstructionLabels[labelName] = new Label(labelName, Instructions.Count);
                    labelName = null;
                }
                Instructions.Add(new Instruction(label, op, ParseOperands(rest).ToArray()));
                continue;
        }
    }
    if (labelName is not null)
    {
        throw ErrorAndQuit($"Label .{labelName} points to EOF");
    }
}
// lsh one less and multiply by two to prevent 1 << 64 == 1
// then subtract 1 because that will make 1 << 63 * 2 - 1 == -1 instead of 0
if (Registers > (ulong)((1 << ((int)WordLength - 1)) * 2) - 1) throw ErrorAndQuit("MINREG doesn't fit in word size");
if (HeapSize > (ulong)((1 << ((int)WordLength - 1)) * 2) - 1) throw ErrorAndQuit("MINHEAP doesn't fit in word size");
if (StackSize > (ulong)((1 << ((int)WordLength - 1)) * 2) - 1) throw ErrorAndQuit("MINSTACK doesn't fit in word size");
if ((uint)Instructions.Count > (ulong)((1 << ((int)WordLength - 1)) * 2) - 1) throw ErrorAndQuit("Instructions don't fit in word size");
var HeapZero = (ulong)DefineWords.Count; // corresponds to "RUN RAM" memory map where "Compiled Program" does not contain any instructions.
var HeapMax = (HeapSize + StackSize) * 2;

// headers are parsed, now we can run through instructions and process some more operands
for (var i = 0; i < Instructions.Count; i++)
{
    var instruction = Instructions[i];
    for (var j = 0; j < instruction.Operands.Length; j++)
    {
        instruction.Operands[j] = instruction.Operands[j] switch
        {
            ImmediateDef def when def.Name == "BITS" => new Immediate(WordLength),
            ImmediateDef def when def.Name == "MINREG" => new Immediate(Registers),
            ImmediateDef def when def.Name == "MINHEAP" => new Immediate(HeapSize),
            ImmediateDef def when def.Name == "MINSTACK" => new Immediate(StackSize),
            ImmediateDef def when def.Name == "HEAP" => new Immediate(HeapMax),
            ImmediateDef def when def.Name == "MSB" => new Immediate((1uL << (int)WordLength) >> 1),
            ImmediateDef def when def.Name == "SMSB" => new Immediate((1uL << (int)WordLength) >> 2),
            ImmediateDef def when def.Name == "MAX" => new Immediate((1uL << (int)WordLength >> 1) * 2 - 1), // prevent circular lsh
            ImmediateDef def when def.Name == "SMAX" => new Immediate((1uL << (int)WordLength >> 1) - 1),
            ImmediateDef def when def.Name == "UHALF" => new Immediate(((1uL << (int)WordLength / 2) - 1) << (int)WordLength / 2),
            ImmediateDef def when def.Name == "LHALF" => new Immediate((1uL << (int)WordLength / 2) - 1),
            ImmediateDef def => throw ErrorAndQuit($"Undefined immediate value &{def.Name}"),

            LabelPtr lbl when InstructionLabels.ContainsKey(lbl.Name) => lbl,
            LabelPtr lbl when DataLabels.ContainsKey(lbl.Name) => new Immediate((ulong)DataLabels[lbl.Name]),
            LabelPtr lbl => throw ErrorAndQuit($"Unknown label .{lbl.Name}"),

            RelativeNum n => new LabelPtr(GenerateLabel(InstructionLabels, Instructions, i + n.Value)),

            MemoryLocation mem => new Immediate(HeapZero + mem.Address),

            _ => instruction.Operands[j],
        };
    }
}

if (Instructions.Last().OpCode != "HLT")
{
    Instructions.Add(new Instruction(null, "HLT", Array.Empty<Operand>()));
}
// Give all instructions labels, and upgrade all ``LabelPtr`` to the correct ``Label``
for (var i = 0; i < Instructions.Count; i++)
{
    var instruction = Instructions[i];
    if (instruction.Label is null) GenerateLabel(InstructionLabels, Instructions, i);
    for (var j = 0; j < instruction.Operands.Length; j++)
    {
        if (instruction.Operands[j] is LabelPtr lbl)
        {
            instruction.Operands[j] = InstructionLabels[lbl.Name];
        }
    }
}

// Now operands are only of types ``Register``, ``Immediate``, ``Label``, ``Port``

var asmname = new AssemblyName(AssemblyName);
var asm = AssemblyBuilder.DefineDynamicAssembly(asmname, AssemblyBuilderAccess.Run);
var module = asm.DefineDynamicModule(AssemblyName);

TypeBuilder type;
EmitContext ctx;
{
    var word = WordLength switch
    {
        8 => typeof(byte),
        16 => typeof(ushort),
        32 => typeof(uint),
        64 => typeof(ulong),
        _ => throw ErrorAndQuit("Invalid Word Length, this shouldn't be possible"), // unreachable
    };
    var arrWord = word.MakeArrayType();

    var port = module.DefineType("Port", TypeAttributes.Interface | TypeAttributes.Abstract | TypeAttributes.Public);
    var writePort = port.DefineMethod("Write", MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.NewSlot, typeof(void), new[] { typeof(string), word });
    var readPort = port.DefineMethod("Read", MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.NewSlot, word, new[] { typeof(string) });
    writePort.GetILGenerator().Emit(OpCodes.Ret);
    readPort.GetILGenerator().Emit(OpCodes.Ret);
    port.CreateType();

    type = module.DefineType(TypeName, TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract);
    var arrWord2 = typeof(Tuple<,>).MakeGenericType(arrWord, arrWord);
    var method = type.DefineMethod(MethodName, MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.Final, arrWord2, new[] { port });
    var il = method.GetILGenerator();
    var Memory = il.DeclareLocal(arrWord);
    for (var i = 0; i < Registers; i++) il.DeclareLocal(word);
    foreach (var label in InstructionLabels.Values) label.EmitLabel = il.DefineLabel();
    var SP = il.DeclareLocal(word);
    var JmpTable = il.DefineLabel();
    var JmpLocation = il.DeclareLocal(typeof(int));
    ctx = new(il, WordLength, JmpTable, JmpLocation, SP, Memory, Registers, word, arrWord, arrWord2, readPort, writePort);
}
var FuncStart = ctx.IL.DefineLabel();
ctx.IL.Emit(OpCodes.Br, FuncStart);

ctx.IL.MarkLabel(ctx.JmpTable);
ctx.IL.Emit(OpCodes.Ldloc, ctx.JmpLocation);
ctx.IL.Emit(OpCodes.Switch, Instructions.Select(i => i.Label!.EmitLabel!.Value).ToArray());
ctx.IL.Emit(OpCodes.Ldstr, "Invalid branch address");
ctx.IL.Emit(OpCodes.Throw);

ctx.IL.MarkLabel(FuncStart);
ctx.IL.Emit(OpCodes.Ldc_I4, DefineWords.Count + HeapMax);
ctx.IL.Emit(OpCodes.Newarr, ctx.Word);
for (var i = 0; i < DefineWords.Count; i++)
{
    var def = DefineWords[i];
    ctx.IL.Emit(OpCodes.Dup);
    ctx.IL.Emit(OpCodes.Ldc_I4, i);
    ctx.EmitLdc(def);
    ctx.EmitStelem();
}
ctx.IL.Emit(OpCodes.Dup);
ctx.IL.Emit(OpCodes.Ldlen);
if (ctx.WordLength == 64) ctx.IL.Emit(OpCodes.Conv_I8);
ctx.IL.Emit(OpCodes.Stloc, ctx.StackPointer);
ctx.IL.Emit(OpCodes.Stloc, ctx.Memory);

for (ctx.Index = 0; ctx.Index < Instructions.Count; ctx.Index++)
{
    if (Instructions[ctx.Index].Label is Label { EmitLabel: EmitLabel label })
    {
        ctx.IL.MarkLabel(label);
    }
    Instructions[ctx.Index].Emit(ctx);
}

type.CreateType();

var generator = new AssemblyGenerator();
generator.GenerateAssembly(asm, args[1]);
Environment.ExitCode = (int)WordLength;

static string GenerateLabel(Dictionary<string, Label> labels, List<Instruction> instructions, int attachToInstruction)
{
    if (instructions[attachToInstruction].Label is Label lbl) return lbl.Name;
    var name = $"<compiler_generated>0";
    for (var i = 0; labels.ContainsKey(name); name = $"<compiler_generated>{++i}") ;
    var label = new Label(name, attachToInstruction);
    labels[name] = label;
    instructions[attachToInstruction].Label = label;
    return name;
}

static IEnumerable<Operand> ParseOperands(string input)
{
    while (!string.IsNullOrWhiteSpace(input))
    {
        if (TryParseImmediate(ref input, out var imm))
        {
            yield return new Immediate(imm);
            continue;
        }
        switch (input[0])
        {
            case '#':
            case 'M':
                {
                    var i = NextWhitespace(input);
                    yield return new MemoryLocation(ParseInt(input[1..i]));
                    input = input[i..].TrimStart();
                    continue;
                }
            case '$':
            case 'R':
                {
                    var i = NextWhitespace(input);
                    yield return new GeneralRegister(ParseInt(input[1..i]));
                    input = input[i..].TrimStart();
                    continue;
                }
            case '~':
                {
                    var negative = input[1] == '-';
                    var i = NextWhitespace(input);
                    int val = (int)ParseInt(input[2..i]);
                    if (negative) val = -val;
                    yield return new RelativeNum(val);
                    input = input[i..].TrimStart();
                    continue;
                }
            case '&':
                {
                    var i = NextWhitespace(input);
                    yield return new ImmediateDef(input[1..i]);
                    input = input[i..].TrimStart();
                    continue;
                }
            case '%':
                {
                    var i = NextWhitespace(input);
                    yield return new Port(input[1..i]);
                    input = input[i..].TrimStart();
                    continue;
                }
            case '.':
                {
                    var i = NextWhitespace(input);
                    yield return new LabelPtr(input[1..i]);
                    input = input[i..].TrimStart();
                    continue;
                }
        }
        switch (input)
        {
            case "PC":
                yield return new ProgramCounter();
                break;
            case "SP":
                yield return new StackPointer();
                break;
        }
        throw ErrorAndQuit("Couldn't parse operand, and it's a fucking miracle it even got to this point due to the complete lack of error handling and shits to give.");
    }
}

static Exception ErrorAndQuit(string message)
{
    Console.Error.WriteLine(message);
    Environment.Exit(1);
    return null;
}

static bool TryParseImmediate(ref string input, out ulong result)
{
    result = default;
    if (string.IsNullOrWhiteSpace(input)) return false;
    if ("0123456789".Contains(input[0]))
    {
        var i = NextWhitespace(input);
        result = ParseLong(input[..i]);
        input = input[i..].TrimStart();
        return true;
    }
    if (input.Length < 3) return false;
    if (input[0] == '\'' && char.IsAscii(input[1]) && input[2] == '\'')
    {
        result = input[1];
        input = input[3..].TrimStart();
        return true;
    }
    return false;
}

static int NextWhitespace(string input)
{
    var i = input.IndexOf(" ");
    return i == -1 ? input.Length : i;
}

// defs that take a number that can span a word size (max 64 bits)
static ulong ParseLong(string input)
{
    var radix = Radix(ref input);
    return Convert.ToUInt64(input, radix);
}

static bool TryParseInt(string input, out uint result)
{
    try
    {
        result = ParseInt(input);
        return true;
    }
    catch
    {
        result = default;
        return false;
    }
}

// defs that take a number that shouldn't be too big (headers mainly)
static uint ParseInt(string input)
{
    var radix = Radix(ref input);
    return Convert.ToUInt32(input, radix);
}

static int Radix(ref string input)
{
    if (input.Length < 2) return 10;
    switch (input[..2])
    {
        case "0x":
            input = input[2..];
            return 16;
        case "0b":
            input = input[2..];
            return 2;
        case "0o":
            input = input[2..];
            return 8;
        default: return 10;
    }
}

// unbalanced comments? not my problem lmao
static string RemoveMultiLineComments(string code)
{
    int i;
    while ((i = code.LastIndexOf("/*")) >= 0)
    {
        var j = (i + 2) + code[(i + 2)..].IndexOf("*/");
        var nline = "";
        if (code[(i + 2)..j].Contains(Environment.NewLine))
        {
            nline = Environment.NewLine;
        }
        code = code[..i] + nline + code[(j + 2)..];
    }
    return code;
}