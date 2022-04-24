using System.Reflection.Emit;

abstract record Operand() { }
abstract record Value() : Operand
{
    public abstract void EmitRead(EmitContext ctx);
}
abstract record Register() : Value
{
    public abstract void EmitWrite(EmitContext ctx);
}
record GeneralRegister(uint Index) : Register
{
    public override string ToString() => $"${Index}";
    public override void EmitRead(EmitContext ctx)
    {
        if (Index == 0) ctx.EmitLdc(0);
        else ctx.IL.Emit(OpCodes.Ldloc, Index);
    }
    public override void EmitWrite(EmitContext ctx)
    {
        if (Index == 0) ctx.IL.Emit(OpCodes.Pop);
        else ctx.IL.Emit(OpCodes.Stloc, Index);
    }
}
record MemoryLocation(uint Address) : Operand
{
    public override string ToString() => $"#{Address}";
}
record RelativeNum(int Value) : Operand
{
    public override string ToString() => $"~{(Value > 0 ? "+" : "")}{Value}";
}
record LabelPtr(string Name) : Operand
{
    public override string ToString() => $"*{Name}";
}
record Label(string Name, int Address, EmitLabel? EmitLabel = null) : Immediate((ulong)Address)
{
    public EmitLabel? EmitLabel { get; set; } = EmitLabel;
    public override string ToString() => $".{Name}";
}
record Immediate(ulong Value) : Value
{
    public override string ToString() => $"{Value}";
    public override void EmitRead(EmitContext ctx) => ctx.EmitLdc(Value);
}
record ImmediateDef(string Name) : Operand
{
    public override string ToString() => $"&{Name}";
}
record Port(string Name) : Operand
{
    public override string ToString() => $"%{Name}";
}
record StackPointer() : Register
{
    public override string ToString() => "SP";
    public override void EmitRead(EmitContext ctx) => ctx.IL.Emit(OpCodes.Ldloc, ctx.StackPointer);
    public override void EmitWrite(EmitContext ctx) => ctx.IL.Emit(OpCodes.Stloc, ctx.StackPointer);
}
record ProgramCounter() : Register
{
    public override string ToString() => "PC";
    public override void EmitRead(EmitContext ctx) => ctx.EmitLdc(ctx.Index);
    public override void EmitWrite(EmitContext ctx)
    {
        ctx.IL.Emit(OpCodes.Conv_I4);
        ctx.IL.Emit(OpCodes.Stloc, ctx.JmpLocation);
        ctx.IL.Emit(OpCodes.Br, ctx.JmpTable);
    }
}