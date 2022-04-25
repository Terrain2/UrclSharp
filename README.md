# URCL# compiler

Technically a transpiler(?) maybe?

This repo has two C# projects: The compiler and a "Runner" which runs the output of the compiler. The compiler is at the root, and the runner is at ``Runner/``

To simply run your code from a URCL file, use ``run.ps1``/``run.sh`` with the single argument of the filename

The compiler supports three macros: ``@ASSEMBLY`` for setting the assembly name, ``@TYPE`` for setting the name of the type it will emit, and ``@METHOD`` for setting the name of the method containing the output code. Changing any of these will break the runner, and is only good if you plan on using the output for anything other than running directly.

All of the instructions are in ``Instructions.cs``, operand types in ``Operands.cs`` and the rest of the compiler is in ``Program.cs``. All of the ports are individual strings that are passed to the caller at runtime, which the Runner handles. That means all of the ports are implemented in ``Runner/Program.cs`` (this file will contain compile errors because it is built against the compiler output, which is absent from this repo)

The compiler is allegedly complete with all the instructions as implemented in [URCL v1.3.1](https://github.com/ModPunchtree/URCL/blob/main/Release/URCL%20V1.3.1.pdf). I have not tested all the instructions, and i've mostly only tested it with 64 bit word size.

A ``HLT`` instruction is automatically inserted at the end of the file if it doesn't already have one there. This does not mean it is ever reachable, but you should just know this. That's mainly so you don't have to manually insert one.

The only supported word sizes are ``8``, ``16``, ``32``, ``64``. If your ``BITS`` constraint does not match one of these, the compiler will reject your program.

There is mostly no error handling, so if your program is invalid, the result is anywhere from an error message detailing the issue, to an unhandled mystery exception in the compiler, and could even result in compile success, and only erroring when saving the output to disk (this will have a huge call stack and come from ILPack) or even successfully compiling and getting a verification exception at runtime.

The runner requires a conditional compilation symbol of ``I1``, ``I2``, ``I4`` or ``I8`` for the word size. It also optionally takes a ``-d`` argument to print debug output of the program's internal state at the point of reaching a ``HLT`` instruction. ``run.ps1``/``run.sh`` will automatically pass both these parameters to the runner.