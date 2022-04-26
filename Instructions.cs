global using EmitLabel = System.Reflection.Emit.Label;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
record EmitContext(ILGenerator IL, uint WordLength, EmitLabel JmpTable, LocalBuilder JmpLocation, LocalBuilder StackPointer, LocalBuilder Memory, uint RegCount, Type Word, Type WordArray, Type RetTuple, MethodInfo ReadPort, MethodInfo WritePort)
{
    public int Index { get; set; }

    public void EmitRead(Value Source) => Source.EmitRead(this);
    public void EmitWrite(Register Destination) => Destination.EmitWrite(this);
    public void EmitLdc(int value)
    {
        IL.Emit(OpCodes.Ldc_I4, value);
        if (WordLength == 64) IL.Emit(OpCodes.Conv_I8);
    }
    public void EmitLdc(ulong value)
    {
        switch (WordLength)
        {
            case 8:
                // ``byte`` overload
                IL.Emit(OpCodes.Ldc_I4_S, (byte)value);
                break;
            case 16:
                // ushort is implicitly using ``int`` overload (cast for truncation)
                IL.Emit(OpCodes.Ldc_I4, (ushort)value);
                break;
            case 32:
                // uint will implicitly use ``ulong`` overload
                IL.Emit(OpCodes.Ldc_I4, (int)value);
                break;
            case 64:
                // ulong will implicitly use ``float`` overload
                IL.Emit(OpCodes.Ldc_I8, (long)value);
                break;
        }
    }
    public void EmitStelem()
    {
        switch (WordLength)
        {
            case 8:
                {
                    IL.Emit(OpCodes.Stelem_I1);
                    break;
                }
            case 16:
                {
                    IL.Emit(OpCodes.Stelem_I2);
                    break;
                }
            case 32:
                {
                    IL.Emit(OpCodes.Stelem_I4);
                    break;
                }
            case 64:
                {
                    IL.Emit(OpCodes.Stelem_I8);
                    break;
                }
        }
    }
    public void EmitLdelem()
    {
        switch (WordLength)
        {
            case 8:
                {
                    IL.Emit(OpCodes.Ldelem_I1);
                    break;
                }
            case 16:
                {
                    IL.Emit(OpCodes.Ldelem_I2);
                    break;
                }
            case 32:
                {
                    IL.Emit(OpCodes.Ldelem_I4);
                    break;
                }
            case 64:
                {
                    IL.Emit(OpCodes.Ldelem_I8);
                    break;
                }
        }
    }

    public void EmitBr(OpCode opCode, Value address)
    {
        if (address is Label label) // Optimization: If branching to a label, we can just go directly there without the jump table
        {
            IL.Emit(opCode, label.EmitLabel!.Value);
        }
        else // Otherwise, we need to set the jump location for the jump table to read from, and then go to the jump table. This is not significantly slower maybe? at least for small programs idk
        {
            EmitRead(address);
            IL.Emit(OpCodes.Conv_I4);
            IL.Emit(OpCodes.Stloc, JmpLocation);
            IL.Emit(opCode, JmpTable);
        }
    }
}

record Instruction(Label? Label, string OpCode, Operand[] Operands)
{
    public Label? Label { get; set; } = Label;
    public override string ToString()
    {
        var builder = new StringBuilder();
        if (Label is not null)
        {
            builder.Append('.');
            builder.Append(Label.Name);
            builder.Append(": ");
        }
        builder.Append(OpCode);
        foreach (var operand in Operands)
        {
            builder.Append(' ');
            builder.Append(operand);
        }
        return builder.ToString();
    }

    public void Emit(EmitContext ctx)
    {
        switch ((OpCode, Operands))
        {
            // Fundamental Instructions
            case ("NOP", []):
                ctx.IL.Emit(OpCodes.Nop);
                break;
            case ("HLT", []):
                ctx.IL.Emit(OpCodes.Ldc_I4, ctx.RegCount);
                ctx.IL.Emit(OpCodes.Newarr, ctx.Word);
                for (var i = 1; i <= ctx.RegCount; i++)
                {
                    ctx.IL.Emit(OpCodes.Dup);
                    ctx.IL.Emit(OpCodes.Ldc_I4, i - 1);
                    ctx.IL.Emit(OpCodes.Ldloc, i);
                    ctx.EmitStelem();
                }
                ctx.IL.Emit(OpCodes.Ldloc, ctx.Memory);
                ctx.IL.Emit(OpCodes.Ldloc, ctx.StackPointer);
                ctx.IL.Emit(OpCodes.Newobj, ctx.RetTuple.GetConstructor(new Type[] { ctx.WordArray, ctx.WordArray, ctx.Word })!);
                ctx.IL.Emit(OpCodes.Ret);
                break;
            case ("IN", [Register Destination, Port Source]):
                ctx.IL.Emit(OpCodes.Ldarg_0);
                ctx.IL.Emit(OpCodes.Ldstr, Source.Name);
                ctx.IL.Emit(OpCodes.Callvirt, ctx.ReadPort);
                ctx.EmitWrite(Destination);
                break;
            case ("OUT", [Port Destination, Value Source]):
                ctx.IL.Emit(OpCodes.Ldarg_0);
                ctx.IL.Emit(OpCodes.Ldstr, Destination.Name);
                ctx.EmitRead(Source);
                ctx.IL.Emit(OpCodes.Callvirt, ctx.WritePort);
                break;
            case ("MOV", [Register Destination, Value Source]):
                ctx.EmitRead(Source);
                ctx.EmitWrite(Destination);
                break;
            case ("IMM", [Register Destination, Immediate Source]):
                ctx.EmitRead(Source);
                ctx.EmitWrite(Destination);
                break;

            // Stack Operations
            case ("PSH", [Value Source]):
                ctx.IL.Emit(OpCodes.Ldloc, ctx.Memory);
                ctx.IL.Emit(OpCodes.Ldloc, ctx.StackPointer);
                ctx.EmitLdc(1);
                ctx.IL.Emit(OpCodes.Sub);
                ctx.IL.Emit(OpCodes.Dup);
                ctx.IL.Emit(OpCodes.Stloc, ctx.StackPointer);
                ctx.EmitRead(Source);
                ctx.EmitStelem();
                break;
            case ("POP", [Register Destination]):
                ctx.IL.Emit(OpCodes.Ldloc, ctx.Memory);
                ctx.IL.Emit(OpCodes.Ldloc, ctx.StackPointer);
                ctx.IL.Emit(OpCodes.Dup);
                ctx.EmitLdc(1);
                ctx.IL.Emit(OpCodes.Add);
                ctx.IL.Emit(OpCodes.Stloc, ctx.StackPointer);
                ctx.EmitLdelem();
                ctx.EmitWrite(Destination);
                break;
            case ("RET", []):
                ctx.IL.Emit(OpCodes.Ldloc, ctx.Memory);
                ctx.IL.Emit(OpCodes.Ldloc, ctx.StackPointer);
                ctx.IL.Emit(OpCodes.Dup);
                ctx.EmitLdc(1);
                ctx.IL.Emit(OpCodes.Add);
                ctx.IL.Emit(OpCodes.Stloc, ctx.StackPointer);
                ctx.EmitLdelem();
                ctx.IL.Emit(OpCodes.Conv_I4);
                ctx.IL.Emit(OpCodes.Stloc, ctx.JmpLocation);
                ctx.IL.Emit(OpCodes.Br, ctx.JmpTable);
                break;

            // Memory Operations
            case ("LOD", [Register Destination, Value Source]):
                ctx.IL.Emit(OpCodes.Ldloc, ctx.Memory);
                ctx.EmitRead(Source);
                ctx.IL.Emit(OpCodes.Conv_I);
                ctx.EmitLdelem();
                ctx.EmitWrite(Destination);
                break;
            case ("LLOD", [Register Destination, Value Source1, Value Source2]):
                ctx.IL.Emit(OpCodes.Ldloc, ctx.Memory);
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.Add);
                ctx.IL.Emit(OpCodes.Conv_I);
                ctx.EmitLdelem();
                ctx.EmitWrite(Destination);
                break;
            case ("STR", [Value Destination, Value Source]):
                ctx.IL.Emit(OpCodes.Ldloc, ctx.Memory);
                ctx.EmitRead(Destination);
                ctx.IL.Emit(OpCodes.Conv_I);
                ctx.EmitRead(Source);
                ctx.EmitStelem();
                break;
            case ("LSTR", [Value Destination, Value Source1, Value Source2]):
                ctx.IL.Emit(OpCodes.Ldloc, ctx.Memory);
                ctx.EmitRead(Destination);
                ctx.EmitRead(Source1);
                ctx.IL.Emit(OpCodes.Add);
                ctx.IL.Emit(OpCodes.Conv_I);
                ctx.EmitRead(Source2);
                ctx.EmitStelem();
                break;
            case ("CPY", [Value Destination, Value Source]):
                ctx.IL.Emit(OpCodes.Ldloc, ctx.Memory);
                ctx.EmitRead(Destination);
                ctx.IL.Emit(OpCodes.Conv_I);
                ctx.IL.Emit(OpCodes.Ldloc, ctx.Memory);
                ctx.EmitRead(Source);
                ctx.IL.Emit(OpCodes.Conv_I);
                ctx.EmitLdelem();
                ctx.EmitStelem();
                break;

            // Arithmetic Operations
            case ("ADD", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.Add);
                ctx.EmitWrite(Destination);
                break;
            case ("SUB", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.Sub);
                ctx.EmitWrite(Destination);
                break;
            case ("INC", [Register Destination, Value Source]):
                ctx.EmitRead(Source);
                ctx.EmitLdc(1);
                ctx.IL.Emit(OpCodes.Add);
                ctx.EmitWrite(Destination);
                break;
            case ("DEC", [Register Destination, Value Source]):
                ctx.EmitRead(Source);
                ctx.EmitLdc(1);
                ctx.IL.Emit(OpCodes.Sub);
                ctx.EmitWrite(Destination);
                break;
            case ("NEG", [Register Destination, Value Source]):
                ctx.EmitRead(Source);
                ctx.IL.Emit(OpCodes.Neg);
                ctx.EmitWrite(Destination);
                break;
            case ("MLT", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.Mul);
                ctx.EmitWrite(Destination);
                break;
            case ("DIV", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.Div_Un);
                ctx.EmitWrite(Destination);
                break;
            case ("MOD", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.Rem_Un);
                ctx.EmitWrite(Destination);
                break;

            // Logical Operations
            case ("NOT", [Register Destination, Value Source]):
                ctx.EmitRead(Source);
                ctx.IL.Emit(OpCodes.Not);
                ctx.EmitWrite(Destination);
                break;
            case ("OR", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.Or);
                ctx.EmitWrite(Destination);
                break;
            case ("NOR", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.Or);
                ctx.IL.Emit(OpCodes.Not);
                ctx.EmitWrite(Destination);
                break;
            case ("AND", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.And);
                ctx.EmitWrite(Destination);
                break;
            case ("NAND", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.And);
                ctx.IL.Emit(OpCodes.Not);
                ctx.EmitWrite(Destination);
                break;
            case ("XOR", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.Xor);
                ctx.EmitWrite(Destination);
                break;
            case ("XNOR", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.Xor);
                ctx.IL.Emit(OpCodes.Not);
                ctx.EmitWrite(Destination);
                break;
            case ("RSH", [Register Destination, Value Source]):
                ctx.EmitRead(Source);
                ctx.IL.Emit(OpCodes.Ldc_I4_1);
                ctx.IL.Emit(OpCodes.Shr_Un);
                ctx.EmitWrite(Destination);
                break;
            case ("SRS", [Register Destination, Value Source]):
                ctx.EmitRead(Source);
                ctx.IL.Emit(OpCodes.Ldc_I4_1);
                ctx.IL.Emit(OpCodes.Shr);
                ctx.EmitWrite(Destination);
                break;
            case ("LSH", [Register Destination, Value Source]):
                ctx.EmitRead(Source);
                ctx.IL.Emit(OpCodes.Ldc_I4_1);
                ctx.IL.Emit(OpCodes.Shl);
                ctx.EmitWrite(Destination);
                break;
            case ("BSR", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.Conv_I);
                ctx.IL.Emit(OpCodes.Shr_Un);
                ctx.EmitWrite(Destination);
                break;
            case ("BSS", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.Conv_I);
                ctx.IL.Emit(OpCodes.Shr);
                ctx.EmitWrite(Destination);
                break;
            case ("BSL", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.Conv_I);
                ctx.IL.Emit(OpCodes.Shl);
                ctx.EmitWrite(Destination);
                break;

            // Branches
            case ("JMP", [Value Destination]):
                ctx.EmitBr(OpCodes.Br, Destination);
                break;
            case ("CAL", [Value Destination]):
                ctx.IL.Emit(OpCodes.Ldloc, ctx.Memory);
                ctx.IL.Emit(OpCodes.Ldloc, ctx.StackPointer);
                ctx.EmitLdc(1);
                ctx.IL.Emit(OpCodes.Sub);
                ctx.IL.Emit(OpCodes.Dup);
                ctx.IL.Emit(OpCodes.Stloc, ctx.StackPointer);
                ctx.EmitLdc(ctx.Index + 1);
                ctx.EmitStelem();
                ctx.EmitBr(OpCodes.Br, Destination);
                break;
            case ("BRE", [Value Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.EmitBr(OpCodes.Beq, Destination);
                break;
            case ("BNE", [Value Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.EmitBr(OpCodes.Bne_Un, Destination);
                break;
            case ("BRG", [Value Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.EmitBr(OpCodes.Bgt_Un, Destination);
                break;
            case ("BGE", [Value Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.EmitBr(OpCodes.Bge_Un, Destination);
                break;
            case ("BRL", [Value Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.EmitBr(OpCodes.Blt_Un, Destination);
                break;
            case ("BLE", [Value Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.EmitBr(OpCodes.Ble_Un, Destination);
                break;
            case ("BRC", [Value Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.BeginExceptionBlock();
                ctx.IL.Emit(OpCodes.Add_Ovf_Un);
                ctx.IL.Emit(OpCodes.Pop);
                ctx.IL.BeginFaultBlock();
                ctx.EmitBr(OpCodes.Br, Destination);
                ctx.IL.EndExceptionBlock();
                break;
            case ("BNC", [Value Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.BeginExceptionBlock();
                ctx.IL.Emit(OpCodes.Add_Ovf_Un);
                ctx.IL.Emit(OpCodes.Pop);
                ctx.EmitBr(OpCodes.Br, Destination);
                ctx.IL.BeginFaultBlock();
                ctx.IL.EndExceptionBlock();
                break;
            case ("BEV", [Value Destination, Value Source]):
                ctx.EmitRead(Source);
                ctx.EmitLdc(1);
                ctx.IL.Emit(OpCodes.And);
                ctx.EmitBr(OpCodes.Brfalse, Destination);
                break;
            case ("BOD", [Value Destination, Value Source]):
                ctx.EmitRead(Source);
                ctx.EmitLdc(1);
                ctx.IL.Emit(OpCodes.And);
                ctx.EmitBr(OpCodes.Brtrue, Destination);
                break;
            case ("BRZ", [Value Destination, Value Source]):
                ctx.EmitRead(Source);
                ctx.EmitBr(OpCodes.Brfalse, Destination);
                break;
            case ("BNZ", [Value Destination, Value Source]):
                ctx.EmitRead(Source);
                ctx.EmitBr(OpCodes.Brtrue, Destination);
                break;
            case ("BRP", [Value Destination, Value Source]):
                ctx.EmitRead(Source);
                ctx.EmitLdc(1uL << (int)ctx.WordLength >> 1);
                ctx.IL.Emit(OpCodes.And);
                ctx.EmitBr(OpCodes.Brfalse, Destination);
                break;
            case ("BRN", [Value Destination, Value Source]):
                ctx.EmitRead(Source);
                ctx.EmitLdc(1uL << (int)ctx.WordLength >> 1);
                ctx.IL.Emit(OpCodes.And);
                ctx.EmitBr(OpCodes.Brtrue, Destination);
                break;

            // Non-Branching Comparisons
            case ("SETE", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitLdc(unchecked(0uL - 1));
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.Ceq);
                ctx.IL.Emit(OpCodes.Mul);
                ctx.EmitWrite(Destination);
                break;
            case ("SETNE", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitLdc(unchecked(0uL - 1));
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.Ceq);
                ctx.IL.Emit(OpCodes.Ldc_I4_0);
                ctx.IL.Emit(OpCodes.Ceq);
                ctx.IL.Emit(OpCodes.Mul);
                ctx.EmitWrite(Destination);
                break;
            case ("SETLT", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitLdc(unchecked(0uL - 1));
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.Clt_Un);
                ctx.IL.Emit(OpCodes.Mul);
                ctx.EmitWrite(Destination);
                break;
            case ("SETLE", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitLdc(unchecked(0uL - 1));
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.Cgt_Un);
                ctx.IL.Emit(OpCodes.Ldc_I4_0);
                ctx.IL.Emit(OpCodes.Ceq);
                ctx.IL.Emit(OpCodes.Mul);
                ctx.EmitWrite(Destination);
                break;
            case ("SETGT", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitLdc(unchecked(0uL - 1));
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.Cgt_Un);
                ctx.IL.Emit(OpCodes.Mul);
                ctx.EmitWrite(Destination);
                break;
            case ("SETGE", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitLdc(unchecked(0uL - 1));
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.Emit(OpCodes.Clt_Un);
                ctx.IL.Emit(OpCodes.Ldc_I4_0);
                ctx.IL.Emit(OpCodes.Ceq);
                ctx.IL.Emit(OpCodes.Mul);
                ctx.EmitWrite(Destination);
                break;
            case ("SETC", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.BeginExceptionBlock();
                ctx.IL.Emit(OpCodes.Add_Ovf_Un);
                ctx.IL.Emit(OpCodes.Pop);
                ctx.EmitLdc(0);
                ctx.EmitWrite(Destination);
                ctx.IL.BeginFaultBlock();
                ctx.EmitLdc(unchecked(0uL - 1));
                ctx.EmitWrite(Destination);
                ctx.IL.EndExceptionBlock();
                break;
            case ("SETNC", [Register Destination, Value Source1, Value Source2]):
                ctx.EmitRead(Source1);
                ctx.EmitRead(Source2);
                ctx.IL.BeginExceptionBlock();
                ctx.IL.Emit(OpCodes.Add_Ovf_Un);
                ctx.IL.Emit(OpCodes.Pop);
                ctx.EmitLdc(unchecked(0uL - 1));
                ctx.EmitWrite(Destination);
                ctx.IL.BeginFaultBlock();
                ctx.EmitLdc(0);
                ctx.EmitWrite(Destination);
                ctx.IL.EndExceptionBlock();
                break;
            default:
                throw new NotImplementedException($"Unsupported instruction: {base.ToString()}");
        }
    }
}